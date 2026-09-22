using J1sDartSharp.Models;
using SQLite;
using J1sDartSharp.Data;

namespace J1sDartSharp.Services;

/// <summary>
/// Persistent SQLite store for all practice sessions and dart-level logs.
/// Thread-safe via SQLiteAsyncConnection.
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

        var row = new SessionRow
        {
            Id                = s.Id.ToString(),
            Game              = "Cricket",
            StartedAt         = s.StartedAt,
            CompletedAt       = s.CompletedAt,
            IsComplete        = s.IsComplete,
            CricketOchres     = s.OchreCount,
            CricketTotalMarks = s.TotalMarks
        };

        await _db!.InsertOrReplaceAsync(row);
        await SaveDartLogAsync(s.Id.ToString(), s.OchreLog);
    }

    public async Task SaveX01SessionAsync(X01Session s)
    {
        await InitAsync();

        var row = new SessionRow
        {
            Id                    = s.Id.ToString(),
            Game                  = s.Game == GameType.ThreeOhOne ? "301" : "501",
            StartedAt             = s.StartedAt,
            CompletedAt           = s.CompletedAt,
            IsComplete            = s.IsComplete,
            X01StartScore         = s.StartScore,
            X01DartsToDoubleIn    = s.DartsToDoubleIn,
            X01DoubleInAt         = s.DoubleInAt,
            X01AvgPerOchre        = s.AveragePerOchre,
            X01ScoringOchres      = s.ScoringOchres,
            X01DoubleOutAchieved  = s.DoubleOutAchieved,
            X01DoubleOutAt        = s.DoubleOutAt,
            X01FinishingDart      = s.FinishingDart,
            X01TotalDarts         = s.TotalDarts,
            X01BustCount          = s.BustCount
        };

        await _db!.InsertOrReplaceAsync(row);

        // Persist per-ochre log as dart rows (score-per-ochre — full dart detail only
        // available if the caller also passes dart log; extend as needed)
        var dartRows = s.OchreLog.Select((e, i) => new DartLogRow
        {
            SessionId    = s.Id.ToString(),
            OchreNumber  = e.OchreNumber,
            DartInOchre  = 1,       // ochre-level only for now; full dart detail is an extension
            Number       = e.Score, // using Score field as aggregate per ochre
            Multiplier   = 1
        }).ToList();

        await _db!.InsertAllAsync(dartRows);
    }

    private async Task SaveDartLogAsync(string sessionId, List<CricketOchreEntry> log)
    {
        var rows = log.Select((e, i) => new DartLogRow
        {
            SessionId     = sessionId,
            OchreNumber   = e.OchreNumber,
            DartInOchre   = 1,
            Number        = e.MarksScored,
            Multiplier    = 1,
            CricketTarget = e.Target
        }).ToList();

        if (rows.Any())
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
                         .OrderBy(r => r.OchreNumber).ThenBy(r => r.DartInOchre)
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
            AvgOchres      = rows.Average(r => r.CricketOchres ?? 0),
            BestOchres     = rows.Min(r => r.CricketOchres ?? int.MaxValue),
            AvgTotalMarks  = rows.Average(r => r.CricketTotalMarks ?? 0),
            BestTotalMarks = rows.Min(r => r.CricketTotalMarks ?? int.MaxValue)
        };
    }

    public async Task<X01Stats> GetX01StatsAsync(string game)
    {
        var rows = await GetSessionsByGameAsync(game);
        if (!rows.Any()) return new X01Stats();

        var withDI  = rows.Where(r => r.X01DartsToDoubleIn.HasValue).ToList();
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
            AvgScorePerOchre     = rows.Average(r => r.X01AvgPerOchre ?? 0),
            BestScorePerOchre    = rows.Max(r => r.X01AvgPerOchre ?? 0),
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
