namespace J1sDartSharp.Core.Models;

/// <summary>
/// Rails: players table / Player model.
/// Players are soft-deleted (Active = false), never hard-deleted.
/// </summary>
public class Player : ITimestamped
{
    public int Id { get; set; }

    /// <summary>Unique, case-insensitive (enforced by the DB collation).</summary>
    public string Name { get; set; } = string.Empty;

    public Gender Gender { get; set; }

    /// <summary>1–3. Rank 1 = highest priority for odd game slots.</summary>
    public int Rank { get; set; } = 1;

    public bool Active { get; set; } = true;

    // Reserved for a future DartConnect sync/import job.
    public string? ExternalId { get; set; }
    public DateTime? SyncedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<GameParticipant> GameParticipants { get; set; } = new List<GameParticipant>();
    public ICollection<Absence> Absences { get; set; } = new List<Absence>();

    // Computed (get-only, not mapped by EF)
    public bool IsFemale => Gender == Gender.Female;
    public bool IsMale => Gender == Gender.Male;
    public string GenderLabel => IsFemale ? "F" : "M";
}
