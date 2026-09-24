using System.ComponentModel.DataAnnotations;
using J1sDartSharp.Shared.Enums;

namespace J1sDartSharp.Shared.Contracts;

// ── Responses ───────────────────────────────────────────────────────────────

/// <summary>
/// Rails: matches#index card — opponent, date, location, status, and the
/// "N completed · N assigned · N pending" line.
/// </summary>
public sealed record MatchSummaryDto(
    int Id,
    string Opponent,
    DateOnly MatchDate,
    string Location,
    MatchStatus Status,
    int CompletedGames,
    int AssignedGames,
    int PendingGames);

/// <summary>
/// Rails: matches#show — everything the match-night screen needs in one call.
/// Every mutating match-night endpoint (reassign, finalize, absences, game
/// edits, scores) returns this, so the client just re-renders from the result.
/// </summary>
public sealed record MatchDetailDto(
    int Id,
    string Opponent,
    DateOnly MatchDate,
    string Location,
    MatchStatus Status,
    IReadOnlyList<GameDto> Games,
    IReadOnlyList<AbsenceDto> Absences,
    IReadOnlyList<PlayerLoadDto> Load);

/// <summary>
/// Rails: match.load_report — games assigned to each available player.
/// </summary>
public sealed record PlayerLoadDto(PlayerRefDto Player, int Games);

// ── Requests ────────────────────────────────────────────────────────────────

/// <summary>
/// Create or edit a match (Rails: match_params — opponent, match_date,
/// location). Status changes go through the finalize endpoint instead.
/// Creating a match always builds the standard 11-game slate.
/// </summary>
public sealed record SaveMatchRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Opponent { get; init; } = string.Empty;

    [Required]
    public DateOnly? MatchDate { get; init; }

    [Required, StringLength(100, MinimumLength = 1)]
    public string Location { get; init; } = "home";
}
