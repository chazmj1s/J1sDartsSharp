namespace J1sDartSharp.Core.Models;

/// <summary>
/// Rails: matches table / Match model. One match night against an opponent,
/// always carrying the standard 11-game slate.
/// </summary>
public class Match : ITimestamped
{
    public int Id { get; set; }

    public string Opponent { get; set; } = string.Empty;

    public DateOnly MatchDate { get; set; }

    /// <summary>
    /// Free text. Rails declared LOCATIONS = home/away but never validated it,
    /// and the seed data uses venue names ("Rags", "Top Spin") — so this stays
    /// a string rather than an enum.
    /// </summary>
    public string Location { get; set; } = "home";

    public MatchStatus Status { get; set; } = MatchStatus.Draft;

    // Reserved for a future DartConnect sync/import job.
    public string? ExternalId { get; set; }
    public DateTime? SyncedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Game> Games { get; set; } = new List<Game>();
    public ICollection<Absence> Absences { get; set; } = new List<Absence>();

    // Computed (not mapped)
    public bool IsFinalized => Status == MatchStatus.Finalized;
    public bool IsCompleted => Status == MatchStatus.Completed;

    /// <summary>
    /// Rails: Match.build_standard_slate. Builds a new match with the full
    /// 11-game slate (sequence 1..11), no players assigned.
    /// </summary>
    public static Match CreateWithStandardSlate(string opponent, DateOnly matchDate, string location = "home")
    {
        var match = new Match
        {
            Opponent = opponent,
            MatchDate = matchDate,
            Location = location
        };

        var sequence = 1;
        foreach (var type in LeagueRules.StandardGames)
        {
            match.Games.Add(new Game
            {
                GameType = type,
                Sequence = sequence++,
                Status = GameStatus.Unassigned
            });
        }

        return match;
    }
}
