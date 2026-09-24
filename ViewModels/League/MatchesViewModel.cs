using J1sDartSharp.Services;
using J1sDartSharp.Shared.Contracts;

namespace J1sDartSharp.ViewModels.League;

/// <summary>
/// Matches list state. Port of matches#index (ordered by date, oldest first,
/// like Rails).
/// </summary>
public sealed class MatchesViewModel(ILeagueApiClient api)
{
    public IReadOnlyList<MatchSummaryDto>? Matches { get; private set; }

    public bool IsLoading { get; private set; }

    public string? Error { get; private set; }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        Error = null;
        try
        {
            var matches = await api.GetMatchesAsync(ct);
            Matches = matches.OrderBy(m => m.MatchDate).ThenBy(m => m.Id).ToList();
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
}
