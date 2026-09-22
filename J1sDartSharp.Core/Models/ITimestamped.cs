namespace J1sDartSharp.Core.Models;

/// <summary>
/// Replaces Rails' automatic created_at / updated_at.
/// LeagueDbContext stamps these (UTC) in SaveChanges.
/// </summary>
public interface ITimestamped
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
