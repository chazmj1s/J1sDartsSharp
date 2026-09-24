using System.ComponentModel.DataAnnotations;
using J1sDartSharp.Shared.Enums;

namespace J1sDartSharp.Shared.Contracts;

// ── Responses ───────────────────────────────────────────────────────────────

/// <summary>
/// One game on the match-night screen. Format, DisplayName and PlayerCount
/// are computed on the server so the client never needs Core.
/// </summary>
public sealed record GameDto(
    int Id,
    int Sequence,
    GameType GameType,
    GameFormat Format,
    string DisplayName,
    int PlayerCount,
    GameStatus Status,
    int? HomeScore,
    int? AwayScore,
    IReadOnlyList<PlayerRefDto> Players);

/// <summary>A player marked absent for a match.</summary>
public sealed record AbsenceDto(int Id, PlayerRefDto Player, string? Reason);

// ── Requests ────────────────────────────────────────────────────────────────

/// <summary>
/// Rails: games#update — manual override of a game's players. The server
/// checks the count matches the game format and the female-coverage rule.
/// </summary>
public sealed record UpdateGamePlayersRequest
{
    [Required, MinLength(1)]
    public IReadOnlyList<int> PlayerIds { get; init; } = [];
}

/// <summary>Rails: games#complete — record the final score.</summary>
public sealed record CompleteGameRequest
{
    [Range(0, int.MaxValue)]
    public int HomeScore { get; init; }

    [Range(0, int.MaxValue)]
    public int AwayScore { get; init; }
}

/// <summary>Rails: absences#create — mark a player absent for a match.</summary>
public sealed record CreateAbsenceRequest
{
    [Range(1, int.MaxValue)]
    public int PlayerId { get; init; }

    [StringLength(250)]
    public string? Reason { get; init; }
}
