using J1sDartSharp.Shared.Contracts;

namespace J1sDartSharp.Services;

/// <summary>
/// Client-side access to the league/match-night Api. The app never touches
/// league data any other way (practice sessions stay in DartsDatabase).
///
/// Every call throws <see cref="LeagueApiException"/> on failure — HTTP errors
/// and "can't reach the server" alike — with a UserMessage ready to show.
///
/// Match-night calls return the full <see cref="MatchDetailDto"/>; re-render
/// from it. Exception: a failed absence call may still have saved the
/// absence (see Api AbsencesController), so reload the match after one.
/// </summary>
public interface ILeagueApiClient
{
    // ── Players ─────────────────────────────────────────────────────────
    Task<RosterDto> GetRosterAsync(CancellationToken ct = default);
    Task<PlayerDto> GetPlayerAsync(int playerId, CancellationToken ct = default);
    Task<PlayerDto> CreatePlayerAsync(SavePlayerRequest request, CancellationToken ct = default);
    Task<PlayerDto> UpdatePlayerAsync(int playerId, SavePlayerRequest request, CancellationToken ct = default);
    Task RemovePlayerAsync(int playerId, CancellationToken ct = default);

    // ── Matches ─────────────────────────────────────────────────────────
    Task<IReadOnlyList<MatchSummaryDto>> GetMatchesAsync(CancellationToken ct = default);
    Task<MatchDetailDto> GetMatchAsync(int matchId, CancellationToken ct = default);
    Task<MatchDetailDto> CreateMatchAsync(SaveMatchRequest request, CancellationToken ct = default);
    Task<MatchDetailDto> UpdateMatchAsync(int matchId, SaveMatchRequest request, CancellationToken ct = default);
    Task DeleteMatchAsync(int matchId, CancellationToken ct = default);
    Task<MatchDetailDto> FinalizeMatchAsync(int matchId, CancellationToken ct = default);
    Task<MatchDetailDto> ReassignPairingsAsync(int matchId, CancellationToken ct = default);

    // ── Games ───────────────────────────────────────────────────────────
    Task<MatchDetailDto> UpdateGamePlayersAsync(int matchId, int gameId, IReadOnlyList<int> playerIds, CancellationToken ct = default);
    Task<MatchDetailDto> CompleteGameAsync(int matchId, int gameId, int homeScore, int awayScore, CancellationToken ct = default);

    // ── Absences ────────────────────────────────────────────────────────
    Task<MatchDetailDto> MarkAbsentAsync(int matchId, int playerId, string? reason = null, CancellationToken ct = default);
    Task<MatchDetailDto> MarkPresentAsync(int matchId, int absenceId, CancellationToken ct = default);
}
