using J1sDartSharp.Services;
using J1sDartSharp.Shared.Constants;
using J1sDartSharp.Shared.Contracts;
using J1sDartSharp.Shared.Enums;

namespace J1sDartSharp.ViewModels.League;

/// <summary>
/// Add / edit player form state. Port of players#new, #create, #edit, #update
/// and players/_form.
///
/// Does the cheap checks locally (name present, gender chosen) so the common
/// mistakes don't need a round trip. Everything else — unique name, roster
/// full, rank range — is the server's call; its 400 field errors are mapped
/// back onto the form.
/// </summary>
public sealed class PlayerEditViewModel(ILeagueApiClient api)
{
    public const string NameField = "Name";
    public const string GenderField = "Gender";
    public const string RankField = "Rank";

    private static readonly HashSet<string> KnownFields =
        new([NameField, GenderField, RankField], StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string[]> _fieldErrors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Null when adding a new player.</summary>
    public int? PlayerId { get; private set; }

    public bool IsNew => PlayerId is null;

    // ── Form fields ─────────────────────────────────────────────────────

    public string Name { get; set; } = string.Empty;

    /// <summary>No default, matching the Rails form: the user must pick one.</summary>
    public Gender? Gender { get; set; }

    public int Rank { get; set; } = LeagueRules.MinRank;

    // ── State ───────────────────────────────────────────────────────────

    public bool IsLoading { get; private set; }
    public bool IsSaving { get; private set; }

    /// <summary>True when an edit couldn't load its player; the form is hidden.</summary>
    public bool LoadFailed { get; private set; }

    /// <summary>Banner message for the last failure.</summary>
    public string? Error { get; private set; }

    /// <summary>Server messages that don't belong to a specific field (e.g. roster full).</summary>
    public IReadOnlyList<string> OtherErrors =>
        _fieldErrors.Where(kv => !KnownFields.Contains(kv.Key))
                    .SelectMany(kv => kv.Value)
                    .ToList();

    public string? FieldError(string field) =>
        _fieldErrors.TryGetValue(field, out var messages) && messages.Length > 0 ? messages[0] : null;

    // ── Actions ─────────────────────────────────────────────────────────

    /// <summary>Resets the form, then loads the player when editing.</summary>
    public async Task LoadAsync(int? playerId, CancellationToken ct = default)
    {
        PlayerId = playerId;
        Name = string.Empty;
        Gender = null;
        Rank = LeagueRules.MinRank;
        LoadFailed = false;
        ClearErrors();

        if (playerId is not int id)
            return;

        IsLoading = true;
        try
        {
            var player = await api.GetPlayerAsync(id, ct);
            Name = player.Name;
            Gender = player.Gender;
            Rank = player.Rank;
        }
        catch (LeagueApiException ex)
        {
            LoadFailed = true;
            Error = ex.IsNotFound ? "That player no longer exists." : ex.UserMessage;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Returns true when saved; the page then navigates back to the roster.</summary>
    public async Task<bool> SaveAsync(CancellationToken ct = default)
    {
        if (IsSaving)
            return false;

        ClearErrors();
        if (!ValidateLocally())
            return false;

        IsSaving = true;
        try
        {
            var request = new SavePlayerRequest
            {
                Name = Name.Trim(),
                Gender = Gender,
                Rank = Rank
            };

            if (PlayerId is int id)
                await api.UpdatePlayerAsync(id, request, ct);
            else
                await api.CreatePlayerAsync(request, ct);

            return true;
        }
        catch (LeagueApiException ex)
        {
            Error = ex.UserMessage;
            foreach (var (key, messages) in ex.ValidationErrors)
                _fieldErrors[NormalizeKey(key)] = messages;
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private bool ValidateLocally()
    {
        if (string.IsNullOrWhiteSpace(Name))
            _fieldErrors[NameField] = ["Name is required."];

        if (Gender is null)
            _fieldErrors[GenderField] = ["Choose a gender."];

        return _fieldErrors.Count == 0;
    }

    private void ClearErrors()
    {
        Error = null;
        _fieldErrors.Clear();
    }

    /// <summary>ProblemDetails keys can arrive as "Name" or "$.name".</summary>
    private static string NormalizeKey(string key) => key.TrimStart('$', '.');
}
