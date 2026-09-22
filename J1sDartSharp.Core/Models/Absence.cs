namespace J1sDartSharp.Core.Models;

/// <summary>
/// Rails: absences table. Marks a player unavailable for one match.
/// A player can be marked absent at most once per match.
/// </summary>
public class Absence : ITimestamped
{
    public int Id { get; set; }

    public int MatchId { get; set; }
    public Match Match { get; set; } = null!;

    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
