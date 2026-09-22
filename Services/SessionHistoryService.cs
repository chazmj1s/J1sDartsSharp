using J1sDartSharp.Models;

namespace J1sDartSharp.Services;

/// <summary>
/// Thin facade over DartsDatabase.
/// Maintains an in-memory cache so pages don't need to be async-heavy for
/// simple stat lookups. Call LoadAsync() once on app start (from App.razor or MainLayout).
/// </summary>
public class SessionHistoryService
{
    private readonly DartsDatabase _db;

    // In-memory mirror for quick sync access by pages
    private List<SessionBase> _cache = new();

    public SessionHistoryService(DartsDatabase db) => _db = db;

    public IReadOnlyList<SessionBase> All => _cache.AsReadOnly();

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>Call once at startup to hydrate the in-memory cache from SQLite.</summary>
    public async Task LoadAsync()
    {
        _cache.Clear();
        var rows = await _db.GetAllSessionsAsync();
        foreach (var r in rows)
            _cache.Add(RowToSession(r));
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task SaveCricketSessionAsync(CricketSession s)
    {
        await _db.SaveCricketSessionAsync(s);
        UpsertCache(s);
    }

    public async Task SaveX01SessionAsync(X01Session s)
    {
        await _db.SaveX01SessionAsync(s);
        UpsertCache(s);
    }

    public async Task DeleteSessionAsync(Guid id)
    {
        await _db.DeleteSessionAsync(id.ToString());
        _cache.RemoveAll(s => s.Id == id);
    }

    public async Task ClearAllAsync()
    {
        await _db.ClearAllAsync();
        _cache.Clear();
    }

    // ── Stats (sync — read from in-memory cache) ──────────────────────────────

    public CricketStats GetCricketStats()
    {
        var completed = _cache.OfType<CricketSession>().Where(s => s.IsComplete).ToList();
        if (!completed.Any()) return new CricketStats();
        return new CricketStats
        {
            SessionsPlayed = completed.Count,
            AvgOchres      = completed.Average(s => s.OchreCount),
            BestOchres     = completed.Min(s => s.OchreCount),
            AvgTotalMarks  = completed.Average(s => s.TotalMarks),
            BestTotalMarks = completed.Min(s => s.TotalMarks)
        };
    }

    public X01Stats GetX01Stats(GameType game)
    {
        var sessions = _cache.OfType<X01Session>()
                             .Where(s => s.Game == game && s.IsComplete)
                             .ToList();
        if (!sessions.Any()) return new X01Stats();

        var withDI = sessions.Where(s => s.DoubleInAchieved).ToList();
        var withDO = sessions.Where(s => s.DoubleOutAchieved && s.TimeToDoubleOut.HasValue).ToList();

        return new X01Stats
        {
            SessionsPlayed      = sessions.Count,
            AvgDartsToDoubleIn  = withDI.Any() ? withDI.Average(s => s.DartsToDoubleIn) : 0,
            BestDartsToDoubleIn = withDI.Any() ? withDI.Min(s => s.DartsToDoubleIn) : 0,
            AvgTimeToDoubleIn   = withDI.Any(s => s.TimeToDoubleIn.HasValue)
                ? TimeSpan.FromSeconds(withDI.Where(s => s.TimeToDoubleIn.HasValue)
                                             .Average(s => s.TimeToDoubleIn!.Value.TotalSeconds)) : null,
            AvgScorePerOchre    = sessions.Average(s => s.AveragePerOchre),
            BestScorePerOchre   = sessions.Max(s => s.AveragePerOchre),
            AvgTimeToDoubleOut  = withDO.Any()
                ? TimeSpan.FromSeconds(withDO.Average(s => s.TimeToDoubleOut!.Value.TotalSeconds)) : null,
            BestTimeToDoubleOut = withDO.Any()
                ? withDO.Min(s => s.TimeToDoubleOut!.Value) : null,
            AvgBusts            = sessions.Average(s => s.BustCount)
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpsertCache(SessionBase s)
    {
        _cache.RemoveAll(c => c.Id == s.Id);
        _cache.Add(s);
    }

    private static SessionBase RowToSession(Data.SessionRow r)
    {
        if (r.Game == "Cricket")
        {
            return new CricketSession
            {
                StartedAt    = r.StartedAt,
                CompletedAt  = r.CompletedAt,
                IsComplete   = r.IsComplete,
                OchreCount   = r.CricketOchres ?? 0
                // Marks map not rehydrated from flat row — extend DartLogRow query if needed
            };
        }
        else
        {
            var game = r.Game == "301" ? GameType.ThreeOhOne : GameType.FiveOhOne;
            var s = new X01Session(game)
            {
                StartedAt           = r.StartedAt,
                CompletedAt         = r.CompletedAt,
                IsComplete          = r.IsComplete,
                DartsToDoubleIn     = r.X01DartsToDoubleIn ?? 0,
                DoubleInAchieved    = r.X01DoubleInAt.HasValue,
                DoubleInAt          = r.X01DoubleInAt,
                DoubleOutAchieved   = r.X01DoubleOutAchieved ?? false,
                DoubleOutAt         = r.X01DoubleOutAt,
                BustCount           = r.X01BustCount ?? 0
            };
            // Rehydrate summary ochre log entry for average calculation
            if (r.X01ScoringOchres > 0 && r.X01AvgPerOchre > 0)
            {
                var syntheticScore = (int)Math.Round(r.X01AvgPerOchre!.Value);
                for (int i = 1; i <= r.X01ScoringOchres; i++)
                    s.OchreLog.Add(new X01OchreEntry(i, syntheticScore, 0));
            }
            return s;
        }
    }
}
