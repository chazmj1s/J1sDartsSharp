using J1sDartSharp.Services;
using J1sDartSharp.Shared.Contracts;
using J1sDartSharp.Shared.Enums;

namespace J1sDartSharp.ViewModels.League;

/// <summary>One tile in the Roster &amp; Attendance grid.</summary>
public sealed record AttendanceRow(PlayerRefDto Player, int Games, AbsenceDto? Absence)
{
    public bool IsAbsent => Absence is not null;
}

/// <summary>Games of one type (e.g. both Doubles Cricket games), in sequence order.</summary>
public sealed record GameGroup(string DisplayName, GameFormat Format, IReadOnlyList<GameDto> Games);

/// <summary>
/// Match-night screen state. Port of matches#show plus the actions it posts
/// to: matches#reassign, matches#finalize, absences#create/#destroy and
/// games#update. Scope is match-night roster management only; scoring is
/// intentionally not exposed (scores will come from DartConnect later).
///
/// Every action returns the full MatchDetailDto, so the screen simply
/// re-renders from the result. Only one inline panel (edit players) is
/// open at a time. Errors from an inline panel are shown
/// inside that game's card (<see cref="ErrorGameId"/>) so they aren't
/// scrolled off the top of the screen.
/// </summary>
public sealed class MatchNightViewModel(ILeagueApiClient api)
{
    private readonly HashSet<int> _selectedPlayerIds = [];

    public int MatchId { get; private set; }

    public MatchDetailDto? Match { get; private set; }

    public IReadOnlyList<AttendanceRow> Attendance { get; private set; } = [];

    /// <summary>Players not marked absent — the only ones a game can use.</summary>
    public IReadOnlyList<PlayerRefDto> AvailablePlayers { get; private set; } = [];

    public IReadOnlyList<GameGroup> GameGroups { get; private set; } = [];

    // ── State ───────────────────────────────────────────────────────────

    public bool IsLoading { get; private set; }

    /// <summary>True while any action is in flight; every action button is disabled.</summary>
    public bool IsBusy { get; private set; }

    public string? Error { get; private set; }

    /// <summary>When set, <see cref="Error"/> belongs to this game's inline panel.</summary>
    public int? ErrorGameId { get; private set; }

    /// <summary>Rails flash notice equivalent.</summary>
    public string? Notice { get; private set; }

    public bool ConfirmingFinalize { get; private set; }

    // ── Rails view logic ────────────────────────────────────────────────

    /// <summary>Rails hid Re-Assign / Finalize / Edit players unless the match was a draft.</summary>
    public bool CanChangeLineup => Match?.Status == MatchStatus.Draft;

    public bool IsFinalized => Match?.Status == MatchStatus.Finalized;

    public int TotalSlots => Match?.Games.Sum(g => g.PlayerCount) ?? 0;

    // ── Loading ─────────────────────────────────────────────────────────

    public async Task LoadAsync(int matchId, CancellationToken ct = default)
    {
        if (matchId != MatchId)
        {
            Match = null;
            Notice = null;
            CloseAllPanels();
        }

        MatchId = matchId;
        IsLoading = true;
        ClearError();
        try
        {
            Apply(await api.GetMatchAsync(matchId, ct));
        }
        catch (LeagueApiException ex)
        {
            Error = ex.IsNotFound ? "That match no longer exists." : ex.UserMessage;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Pull the latest from the server (another device may have changed it).</summary>
    public Task RefreshAsync(CancellationToken ct = default)
    {
        Notice = null;
        return LoadAsync(MatchId, ct);
    }

    // ── Lineup actions ──────────────────────────────────────────────────

    public Task ReassignAsync(CancellationToken ct = default) =>
        RunAsync(c => api.ReassignPairingsAsync(MatchId, c), "Pairings updated.", ct);

    public void AskFinalize()
    {
        CloseAllPanels();
        ClearError();
        ConfirmingFinalize = true;
    }

    public void CancelFinalize() => ConfirmingFinalize = false;

    public Task FinalizeAsync(CancellationToken ct = default) =>
        RunAsync(c => api.FinalizeMatchAsync(MatchId, c), "Lineup locked in!", ct);

    // ── Attendance ──────────────────────────────────────────────────────

    // The server keeps an absence change even when the follow-on reassign
    // fails (Rails behavior), so reload after any failure to show the truth.

    public Task MarkAbsentAsync(PlayerRefDto player, CancellationToken ct = default) =>
        RunAsync(c => api.MarkAbsentAsync(MatchId, player.Id, null, c),
                 CanChangeLineup ? $"{player.Name} marked absent. Pairings updated." : $"{player.Name} marked absent.",
                 ct, reloadOnError: true);

    public Task MarkPresentAsync(AttendanceRow row, CancellationToken ct = default) =>
        row.Absence is not { } absence
            ? Task.CompletedTask
            : RunAsync(c => api.MarkPresentAsync(MatchId, absence.Id, c),
                       CanChangeLineup ? $"{row.Player.Name} marked present. Pairings updated." : $"{row.Player.Name} marked present.",
                       ct, reloadOnError: true);

    // ── Edit players (games#update) ─────────────────────────────────────

    public int? EditingGameId { get; private set; }

    public int SelectedCount => _selectedPlayerIds.Count;

    public bool IsSelected(int playerId) => _selectedPlayerIds.Contains(playerId);

    public void StartEdit(GameDto game)
    {
        CloseAllPanels();
        ClearError();
        EditingGameId = game.Id;
        foreach (var player in game.Players)
            _selectedPlayerIds.Add(player.Id);
    }

    public void TogglePlayer(int playerId)
    {
        if (!_selectedPlayerIds.Remove(playerId))
            _selectedPlayerIds.Add(playerId);
    }

    public async Task SaveGamePlayersAsync(GameDto game, CancellationToken ct = default)
    {
        if (_selectedPlayerIds.Count != game.PlayerCount)
        {
            SetError($"This game needs {game.PlayerCount} player(s); you selected {_selectedPlayerIds.Count}.", game.Id);
            return;
        }

        var playerIds = _selectedPlayerIds.ToList();
        await RunAsync(c => api.UpdateGamePlayersAsync(MatchId, game.Id, playerIds, c),
                       $"Game {game.Sequence} updated.", ct, gameId: game.Id);
    }

    // ── Panels ──────────────────────────────────────────────────────────

    public void CloseAllPanels()
    {
        EditingGameId = null;
        _selectedPlayerIds.Clear();
        ConfirmingFinalize = false;
        if (ErrorGameId is not null)
            ClearError();
    }

    // ── Display helpers (kept here so the page needs no enum usings) ────

    public static string FormatIcon(GameFormat format) => format switch
    {
        GameFormat.Singles => "🎯",
        GameFormat.Doubles => "👥",
        GameFormat.Team => "🏆",
        _ => "•"
    };

    public static string StatusClass(MatchStatus status) => status switch
    {
        MatchStatus.Finalized => "status-finalized",
        MatchStatus.Completed => "status-completed",
        _ => "status-draft"
    };

    public static string GenderLabel(Gender gender) => gender == Gender.Female ? "F" : "M";

    public static string PlayerNames(GameDto game) => string.Join(" & ", game.Players.Select(p => p.Name));

    // ── Plumbing ────────────────────────────────────────────────────────

    private async Task RunAsync(
        Func<CancellationToken, Task<MatchDetailDto>> call,
        string notice,
        CancellationToken ct,
        bool reloadOnError = false,
        int? gameId = null)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ClearError();
        Notice = null;
        try
        {
            Apply(await call(ct));
            CloseAllPanels();
            Notice = notice;
        }
        catch (LeagueApiException ex)
        {
            SetError(ex.UserMessage, gameId);

            // 409 = someone locked the lineup (maybe on another device).
            if (reloadOnError || ex.IsLocked || ex.IsNotFound)
                await TryReloadAsync(ct);
        }
        finally
        {
            IsBusy = false;
            ConfirmingFinalize = false;
        }
    }

    private async Task TryReloadAsync(CancellationToken ct)
    {
        try
        {
            Apply(await api.GetMatchAsync(MatchId, ct));
        }
        catch (LeagueApiException)
        {
            // Keep the original error on screen; the stale view is still better than none.
        }
    }

    private void Apply(MatchDetailDto match)
    {
        Match = match;

        var present = match.Load.Select(l => new AttendanceRow(l.Player, l.Games, null));
        var absent = match.Absences.Select(a => new AttendanceRow(a.Player, 0, a));
        Attendance = present.Concat(absent)
                            .OrderBy(r => r.Player.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();

        AvailablePlayers = match.Load.Select(l => l.Player)
                                     .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                                     .ToList();

        // GroupBy keeps first-appearance order, so groups follow the slate.
        GameGroups = match.Games.OrderBy(g => g.Sequence)
                                .GroupBy(g => g.DisplayName)
                                .Select(g => new GameGroup(g.Key, g.First().Format, g.ToList()))
                                .ToList();

        // Lineup got locked (here or elsewhere): editing players no longer applies.
        if (!CanChangeLineup && EditingGameId is not null)
        {
            EditingGameId = null;
            _selectedPlayerIds.Clear();
        }
    }

    private void SetError(string message, int? gameId = null)
    {
        Error = message;
        ErrorGameId = gameId;
    }

    private void ClearError()
    {
        Error = null;
        ErrorGameId = null;
    }
}
