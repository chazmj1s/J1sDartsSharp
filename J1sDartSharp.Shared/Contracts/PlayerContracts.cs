using System.ComponentModel.DataAnnotations;
using J1sDartSharp.Shared.Enums;

namespace J1sDartSharp.Shared.Contracts;

// ── Responses ───────────────────────────────────────────────────────────────

/// <summary>A roster player. Rails: players#index row / players#edit.</summary>
public sealed record PlayerDto(int Id, string Name, Gender Gender, int Rank, bool Active);

/// <summary>
/// Minimal player reference used inside games, absences and load lists.
/// </summary>
public sealed record PlayerRefDto(int Id, string Name, Gender Gender);

/// <summary>
/// Rails: players#index — the active roster plus the stats row
/// (Players / Women / Men / Open).
/// </summary>
public sealed record RosterDto(
    IReadOnlyList<PlayerDto> Players,
    int Total,
    int Female,
    int Male,
    int OpenSlots);

// ── Requests ────────────────────────────────────────────────────────────────

/// <summary>
/// Create or update a player (Rails: player_params — name, gender, rank).
/// [ApiController] validates the attributes automatically and returns a 400
/// ProblemDetails on failure.
/// </summary>
public sealed record SavePlayerRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public Gender? Gender { get; init; }

    [Range(1, 3)]
    public int Rank { get; init; } = 1;
}
