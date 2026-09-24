using System.ComponentModel.DataAnnotations.Schema;

namespace J1sDartSharp.Core.Models;

/// <summary>
/// Rails: games table / Game model. One of the 11 games in a match.
/// </summary>
public class Game : ITimestamped
{
    public int Id { get; set; }

    public int MatchId { get; set; }
    public Match Match { get; set; } = null!;

    public GameType GameType { get; set; }

    /// <summary>1..11, unique within a match.</summary>
    public int Sequence { get; set; }

    public GameStatus Status { get; set; } = GameStatus.Unassigned;

    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<GameParticipant> GameParticipants { get; set; } = new List<GameParticipant>();

    // ── Computed (not mapped) ────────────────────────────────────────────
    // Rails stored `format` as its own column even though it is fully
    // determined by game_type. Deriving it removes a way to get out of sync.

    public GameFormat Format => LeagueRules.FormatOf(GameType);
    public int PlayerCount => LeagueRules.PlayerCountFor(Format);
    public string DisplayName => LeagueRules.DisplayNameOf(GameType);

    public bool IsAssigned => Status != GameStatus.Unassigned;
    public bool IsCompleted => Status == GameStatus.Completed;

    /// <summary>
    /// Rails: Game#home_players. Requires GameParticipants (and each
    /// participant's Player) to be loaded.
    /// </summary>
    /// <remarks>
    /// [NotMapped] is required. EF discovers get-only properties of an entity
    /// type (here IEnumerable&lt;Player&gt;) as collection navigations, so without
    /// it EF invents a Game→Player one-to-many with a shadow GameId column on
    /// Players, then crashes during change tracking trying to add to this
    /// LINQ iterator. The attribute lives in the BCL, so Core stays EF-free.
    /// </remarks>
    [NotMapped]
    public IEnumerable<Player> HomePlayers =>
        GameParticipants.Where(p => p.Side == ParticipantSide.Home).Select(p => p.Player);
}
