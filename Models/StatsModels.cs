namespace J1sDartSharp.Models;

public record CricketStats
{
    public int SessionsPlayed { get; init; }
    public double AvgOchres { get; init; }
    public int BestOchres { get; init; }
    public double AvgTotalMarks { get; init; }
    public int BestTotalMarks { get; init; }
}

public record X01Stats
{
    public int SessionsPlayed { get; init; }
    public double AvgDartsToDoubleIn { get; init; }
    public int BestDartsToDoubleIn { get; init; }
    public TimeSpan? AvgTimeToDoubleIn { get; init; }
    public double AvgScorePerOchre { get; init; }
    public double BestScorePerOchre { get; init; }
    public TimeSpan? AvgTimeToDoubleOut { get; init; }
    public TimeSpan? BestTimeToDoubleOut { get; init; }
    public double AvgBusts { get; init; }
}
