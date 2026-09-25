namespace J1sDartSharp.Models;

public record CricketStats
{
    public int SessionsPlayed { get; init; }
    public double AvgOches { get; init; }
    public int BestOches { get; init; }
    public double AvgTotalMarks { get; init; }
    public int BestTotalMarks { get; init; }
}

public record X01Stats
{
    public int SessionsPlayed { get; init; }
    public double AvgDartsToDoubleIn { get; init; }
    public int BestDartsToDoubleIn { get; init; }
    public TimeSpan? AvgTimeToDoubleIn { get; init; }
    public double AvgScorePerOche { get; init; }
    public double BestScorePerOche { get; init; }
    public TimeSpan? AvgTimeToDoubleOut { get; init; }
    public TimeSpan? BestTimeToDoubleOut { get; init; }
    public double AvgBusts { get; init; }
}
