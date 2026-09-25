using J1sDartSharp.Models;
using SQLite;
using J1sDartSharp.Data;

namespace J1sDartSharp.Services;

/// <summary>
/// Persistent SQLite store for all practice sessions and dart-level logs.
/// Thread-safe via SQLiteAsyncConnection. New columns (e.g. X01InMode) are
/// added automatically by CreateTableAsync on first run.
/// </summary>
public class DartsDatabase
{
    private SQLiteAsyncConnection? _db;
    private bool _initialised;

    private async Task InitAsync()
    {
        if (_initialised) return;
        _initialised = true;

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "j1sdartsharp.db3");
        _db = new SQLiteAsyncConnection(dbPath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

        await _db.CreateTableAsync<SessionRow>();
        await _db.CreateTableAsync<DartLogRow>();
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    public async Task SaveCricketSessionAsync(CricketSession s)
    {
        await InitAsync();

        var id = s.Id.ToString();
        var row = new SessionRow
        {
            Id                = id,
            Game              = "Cricket",
            StartedAt         = s.StartedAt,
            CompletedAt       = s.CompletedAt,
            IsComplete        = s.IsComplete,
            CricketOches        = s.OcheCount,
            CricketTotalMarks   = s.TotalMarks,
            CricketVariant      = s.Variant.ToString(),
            CricketDarts        = s.DartsThrown
        };

        await _db!.InsertOrReplaceAsync(row);
        await SaveDartLogAsync(id, s.OcheLog);
    }

    public async Task SaveX01SessionAsync(X01Session s)
    {
        await InitAsync();

        var id = s.Id.ToString();
        var row = new SessionRow
        {
            Id                    = id,
            Game                  = s.StartScore.ToString(),   // "301" | "501"
            StartedAt             = s.StartedAt,
            CompletedAt           = s.CompletedAt,
            IsComplete            = s.IsComplete,
            X01StartScore         = s.StartScore,
            X01InMode             = s.InMode.ToString(),
            X01OutMode            = s.OutMode.ToString(),
            X01DartsToDoubleIn    = s.DartsToDoubleIn,
            X01DoubleInAt         = s.DoubleInAt,
            X01AvgPerOche         = s.AveragePerOche,
            X01OcheTrips          = s.OcheTrips,
            X01DoubleOutAchieved  = s.DoubleOutAchieved,
            X01DoubleOutAt        = s.DoubleOutAt,
            X01FinishingDart      = s.FinishingDart,
            X01TotalDarts         = s.DartsThrown,
            X01BustCount          = s.BustCount
        };

        await _db!.InsertOrReplaceAsync(row);

        // Per-trip totals (not per dart): Number holds the trip's score, 0 for a bust.
        await _db!.Table<DartLogRow>().DeleteAsync(r => r.SessionId == id);
        var tripRows = s.OcheLog.Select(e => new DartLogRow
        {
            SessionId  = id,
            OcheNumber = e.OcheNumber,
            DartInOche = 1,
            Number     = e.Score,
            Multiplier = 1
        }).ToList();

        if (tripRows.Count > 0)
            await _db!.InsertAllAsync(tripRows);
    }

    private async Task SaveDartLogAsync(string sessionId, List<CricketOcheEntry> log)
    {
        await _db!.Table<DartLogRow>().DeleteAsync(r => r.SessionId == sessionId);

        var rows = log.Select(e => new DartLogRow
        {
            SessionId     = sessionId,
            OcheNumber    = e.OcheNumber,
            DartInOche    = 1,
            Number        = e.MarksScored,
            Multiplier    = 1,
            CricketTarget = e.Target
        }).ToList();

        if (rows.Count > 0)
            await _db!.InsertAllAsync(rows);
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    public async Task<List<SessionRow>> GetAllSessionsAsync()
    {
        await InitAsync();
        return await _db!.Table<SessionRow>()
                         .OrderByDescending(r => r.StartedAt)
                         .ToListAsync();
    }

    public async Task<List<SessionRow>> GetSessionsByGameAsync(string game)
    {
        await InitAsync();
        return await _db!.Table<SessionRow>()
                         .Where(r => r.Game == game && r.IsComplete)
                         .OrderByDescending(r => r.StartedAt)
                         .ToListAsync();
    }

    public async Task<List<DartLogRow>> GetDartLogAsync(string sessionId)
    {
        await InitAsync();
        return await _db!.Table<DartLogRow>()
                         .Where(r => r.SessionId == sessionId)
                         .OrderBy(r => r.OcheNumber).ThenBy(r => r.DartInOche)
                         .ToListAsync();
    }

    // ── Aggregate stats ──────────────────────────────────────────────────────

    public async Task<CricketStats> GetCricketStatsAsync()
    {
        var rows = await GetSessionsByGameAsync("Cricket");
        if (!rows.Any()) return new CricketStats();

        return new CricketStats
        {
            SessionsPlayed = rows.Count,
            AvgOches       = rows.Average(r => r.CricketOches ?? 0),
            BestOches      = rows.Min(r => r.CricketOches ?? int.MaxValue),
            AvgTotalMarks  = rows.Average(r => r.CricketTotalMarks ?? 0),
            BestTotalMarks = rows.Min(r => r.CricketTotalMarks ?? int.MaxValue)
        };
    }

    public async Task<X01Stats> GetX01StatsAsync(string game)
    {
        var rows = await GetSessionsByGameAsync(game);
        if (!rows.Any()) return new X01Stats();

        var withDI  = rows.Where(r => r.X01InMode != nameof(InMode.SingleIn) && r.X01DartsToDoubleIn > 0).ToList();
        var withDO  = rows.Where(r => r.X01DoubleOutAchieved == true && r.X01DoubleOutAt.HasValue && r.X01DoubleInAt.HasValue).ToList();

        TimeSpan? AvgSpan(IEnumerable<double> secs)
        {
            var list = secs.ToList();
            return list.Any() ? TimeSpan.FromSeconds(list.Average()) : null;
        }

        TimeSpan? BestSpan(IEnumerable<double> secs)
        {
            var list = secs.ToList();
            return list.Any() ? TimeSpan.FromSeconds(list.Min()) : null;
        }

        return new X01Stats
        {
            SessionsPlayed       = rows.Count,
            AvgDartsToDoubleIn   = withDI.Any() ? withDI.Average(r => r.X01DartsToDoubleIn!.Value) : 0,
            BestDartsToDoubleIn  = withDI.Any() ? withDI.Min(r => r.X01DartsToDoubleIn!.Value) : 0,
            AvgTimeToDoubleIn    = AvgSpan(withDI.Where(r => r.X01DoubleInAt.HasValue)
                                                  .Select(r => (r.X01DoubleInAt!.Value - r.StartedAt).TotalSeconds)),
            AvgScorePerOche      = rows.Average(r => r.X01AvgPerOche ?? 0),
            BestScorePerOche     = rows.Max(r => r.X01AvgPerOche ?? 0),
            AvgTimeToDoubleOut   = AvgSpan(withDO.Select(r => (r.X01DoubleOutAt!.Value - r.X01DoubleInAt!.Value).TotalSeconds)),
            BestTimeToDoubleOut  = BestSpan(withDO.Select(r => (r.X01DoubleOutAt!.Value - r.X01DoubleInAt!.Value).TotalSeconds)),
            AvgBusts             = rows.Average(r => r.X01BustCount ?? 0)
        };
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    public async Task DeleteSessionAsync(string id)
    {
        await InitAsync();
        await _db!.DeleteAsync<SessionRow>(id);
        await _db!.Table<DartLogRow>().DeleteAsync(r => r.SessionId == id);
    }

    public async Task ClearAllAsync()
    {
        await InitAsync();
        await _db!.DeleteAllAsync<SessionRow>();
        await _db!.DeleteAllAsync<DartLogRow>();
    }
}
