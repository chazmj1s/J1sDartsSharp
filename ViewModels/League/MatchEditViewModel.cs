using J1sDartSharp.Services;
using J1sDartSharp.Shared.Contracts;

namespace J1sDartSharp.ViewModels.League;

/// <summary>
/// New / edit match form state. Port of matches#new, #create, #edit, #update,
/// #destroy and matches/_form.
///
/// Location stays free text (a handoff decision: the seeds use venue names
/// like "Top Spin", not just home/away); the form offers Home / Away as
/// quick picks. Creating a match builds the standard 11-game slate on the
/// server; pairings are assigned from the match-night screen.
/// </summary>
public sealed class MatchEditViewModel(ILeagueApiClient api)
{
    public const string OpponentField = "Opponent";
    public const string MatchDateField = "MatchDate";
    public const string LocationField = "Location";

    private static readonly HashSet<string> KnownFields =
        new([OpponentField, MatchDateField, LocationField], StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string[]> _fieldErrors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Null when creating a new match.</summary>
    public int? MatchId { get; private set; }

    public bool IsNew => MatchId is null;

    // ── Form fields ─────────────────────────────────────────────────────

    public string Opponent { get; set; } = string.Empty;

    /// <summary>Rails defaulted new matches to today.</summary>
    public DateOnly? MatchDate { get; set; }

    public string Location { get; set; } = "home";

    // ── State ───────────────────────────────────────────────────────────

    public bool IsLoading { get; private set; }
    public bool IsSaving { get; private set; }
    public bool IsDeleting { get; private set; }
    public bool IsBusy => IsSaving || IsDeleting;

    /// <summary>True when an edit couldn't load its match; the form is hidden.</summary>
    public bool LoadFailed { get; private set; }

    /// <summary>True while the inline "Delete this match?" confirm is showing.</summary>
    public bool ConfirmingDelete { get; private set; }

    public string? Error { get; private set; }

    public IReadOnlyList<string> OtherErrors =>
        _fieldErrors.Where(kv => !KnownFields.Contains(kv.Key))
                    .SelectMany(kv => kv.Value)
                    .ToList();

    public string? FieldError(string field) =>
        _fieldErrors.TryGetValue(field, out var messages) && messages.Length > 0 ? messages[0] : null;

    // ── Actions ─────────────────────────────────────────────────────────

    /// <summary>Resets the form, then loads the match when editing.</summary>
    public async Task LoadAsync(int? matchId, CancellationToken ct = default)
    {
        MatchId = matchId;
        Opponent = string.Empty;
        MatchDate = DateOnly.FromDateTime(DateTime.Today);
        Location = "home";
        LoadFailed = false;
        ConfirmingDelete = false;
        ClearErrors();

        if (matchId is not int id)
            return;

        IsLoading = true;
        try
        {
            var match = await api.GetMatchAsync(id, ct);
            Opponent = match.Opponent;
            MatchDate = match.MatchDate;
            Location = match.Location;
        }
        catch (LeagueApiException ex)
        {
            LoadFailed = true;
            Error = ex.IsNotFound ? "That match no longer exists." : ex.UserMessage;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Returns the saved match's id, or null when the save failed.</summary>
    public async Task<int?> SaveAsync(CancellationToken ct = default)
    {
        if (IsBusy)
            return null;

        ClearErrors();
        ConfirmingDelete = false;
        if (!ValidateLocally())
            return null;

        IsSaving = true;
        try
        {
            var request = new SaveMatchRequest
            {
                Opponent = Opponent.Trim(),
                MatchDate = MatchDate,
                Location = Location.Trim()
            };

            var saved = MatchId is int id
                ? await api.UpdateMatchAsync(id, request, ct)
                : await api.CreateMatchAsync(request, ct);

            return saved.Id;
        }
        catch (LeagueApiException ex)
        {
            Error = ex.UserMessage;
            foreach (var (key, messages) in ex.ValidationErrors)
                _fieldErrors[NormalizeKey(key)] = messages;
            return null;
        }
        finally
        {
            IsSaving = false;
        }
    }

    public void AskDelete()
    {
        ClearErrors();
        ConfirmingDelete = true;
    }

    public void CancelDelete() => ConfirmingDelete = false;

    /// <summary>Returns true when deleted; the page then goes back to the list.</summary>
    public async Task<bool> DeleteAsync(CancellationToken ct = default)
    {
        if (MatchId is not int id || IsBusy)
            return false;

        IsDeleting = true;
        Error = null;
        try
        {
            await api.DeleteMatchAsync(id, ct);
            return true;
        }
        catch (LeagueApiException ex)
        {
            // Already gone counts as success: the user wanted it deleted.
            if (ex.IsNotFound)
                return true;

            Error = ex.UserMessage;
            ConfirmingDelete = false;
            return false;
        }
        finally
        {
            IsDeleting = false;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private bool ValidateLocally()
    {
        if (string.IsNullOrWhiteSpace(Opponent))
            _fieldErrors[OpponentField] = ["Opponent is required."];

        if (MatchDate is null)
            _fieldErrors[MatchDateField] = ["Pick a date."];

        if (string.IsNullOrWhiteSpace(Location))
            _fieldErrors[LocationField] = ["Location is required."];

        return _fieldErrors.Count == 0;
    }

    private void ClearErrors()
    {
        Error = null;
        _fieldErrors.Clear();
    }

    /// <summary>ProblemDetails keys can arrive as "Opponent" or "$.opponent".</summary>
    private static string NormalizeKey(string key) => key.TrimStart('$', '.');
}
