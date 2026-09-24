using J1sDartSharp.Core.Models;

namespace J1sDartSharp.Core.Services;

/// <summary>One game from the slate and the players the scheduler put in it.</summary>
public sealed record GameAssignment(Game Game, IReadOnlyList<Player> Players);

/// <summary>
/// Output of <see cref="PairingService.BuildSchedule"/>: one assignment per game,
/// in sequence order, plus each player's resulting game count (keyed by Player.Id).
/// Nothing is persisted — the caller decides what to do with it.
/// </summary>
public sealed record PairingResult(
    IReadOnlyList<GameAssignment> Assignments,
    IReadOnlyDictionary<int, int> LoadByPlayerId);

/// <summary>
/// Base type so callers can catch both pairing failures in one place,
/// the way the Rails controllers rescued both error classes together.
/// </summary>
public abstract class PairingException(string message) : Exception(message);

/// <summary>Rails: PairingService::InsufficientPlayersError.</summary>
public sealed class InsufficientPlayersException(string message) : PairingException(message);

/// <summary>Rails: PairingService::PairingImpossibleError.</summary>
public sealed class PairingImpossibleException(string message) : PairingException(message);
