namespace J1sDartSharp.Core.Models;

/// <summary>
/// Rails: game_participants table. Join entity between Game and Player,
/// with payload (side, role). A player can appear at most once per game.
/// </summary>
public class GameParticipant : ITimestamped
{
    public int Id { get; set; }

    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    public ParticipantRole Role { get; set; } = ParticipantRole.Player;
    public ParticipantSide Side { get; set; } = ParticipantSide.Home;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
