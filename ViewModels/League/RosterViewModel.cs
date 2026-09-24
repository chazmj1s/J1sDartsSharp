using J1sDartSharp.Services;
using J1sDartSharp.Shared.Constants;
using J1sDartSharp.Shared.Contracts;

namespace J1sDartSharp.ViewModels.League;

/// <summary>
/// Roster screen state. Port of players#index (list + stats row + warnings)
/// and players#destroy (soft remove).
///
/// Plain C#, no Blazor types: the page owns one instance, calls these methods
/// from its event handlers, and Blazor re-renders when each awaited handler
/// completes. Only LeagueApiException is caught; anything else is a bug and
/// is left to surface in the Blazor error bar.
/// </summary>
public sealed class RosterViewModel(ILeagueApiClient api)
{
    public RosterDto? Roster { get; private set; }

    public bool IsLoading { get; private set; }

    /// <summary>True while a remove is in flight (disables the confirm button).</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Last failure, ready to show. Null when the last call succeeded.</summary>
    public string? Error { get; private set; }

    /// <summary>Rails flash notice equivalent ("X removed from the roster.").</summary>
    public string? Notice { get; private set; }

    /// <summary>Player whose row is showing the inline "Remove?" confirm.</summary>
    public int? PendingRemoveId { get; private set; }

    // ── Rails view logic ────────────────────────────────────────────────

    public bool CanAddPlayer => Roster is { OpenSlots: > 0 };

    /// <summary>Rails: "Need at least 4 players to run a match."</summary>
    public bool BelowMinimum => Roster is not null && Roster.Total < LeagueRules.MinRoster;

    /// <summary>Rails: "At least one female player is required per league rules."</summary>
    public bool NoWomen => Roster is { Female: 0 };

    // ── Actions ─────────────────────────────────────────────────────────

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        Error = null;
        try
        {
            Roster = await api.GetRosterAsync(ct);
        }
        catch (LeagueApiException ex)
        {
            Error = ex.UserMessage;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void AskRemove(int playerId)
    {
        PendingRemoveId = playerId;
        Notice = null;
        Error = null;
    }

    public void CancelRemove() => PendingRemoveId = null;

    public async Task ConfirmRemoveAsync(CancellationToken ct = default)
    {
        if (PendingRemoveId is not int playerId || IsBusy)
            return;

        var name = Roster?.Players.FirstOrDefault(p => p.Id == playerId)?.Name ?? "Player";

        IsBusy = true;
        Error = null;
        Notice = null;
        try
        {
            await api.RemovePlayerAsync(playerId, ct);
            PendingRemoveId = null;
            Notice = $"{name} removed from the roster.";

            // Reload rather than patch locally: the stats row (Women/Men/Open)
            // comes from the server.
            Roster = await api.GetRosterAsync(ct);
        }
        catch (LeagueApiException ex)
        {
            Error = ex.UserMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
