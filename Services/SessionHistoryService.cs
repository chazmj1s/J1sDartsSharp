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
            AvgOches       = completed.Average(s => s.OcheCount),
            BestOches      = completed.Min(s => s.OcheCount),
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

        // Double-in stats only mean something for double-in legs.
        var withDI = sessions.Where(s => s.InMode == InMode.DoubleIn && s.DoubleInAchieved).ToList();
        var withDO = sessions.Where(s => s.DoubleOutAchieved && s.TimeToDoubleOut.HasValue).ToList();

        return new X01Stats
        {
            SessionsPlayed      = sessions.Count,
            AvgDartsToDoubleIn  = withDI.Any() ? withDI.Average(s => s.DartsToDoubleIn) : 0,
            BestDartsToDoubleIn = withDI.Any() ? withDI.Min(s => s.DartsToDoubleIn) : 0,
            AvgTimeToDoubleIn   = withDI.Any(s => s.TimeToDoubleIn.HasValue)
                ? TimeSpan.FromSeconds(withDI.Where(s => s.TimeToDoubleIn.HasValue)
                                             .Average(s => s.TimeToDoubleIn!.Value.TotalSeconds)) : null,
            AvgScorePerOche     = sessions.Average(s => s.AveragePerOche),
            BestScorePerOche    = sessions.Max(s => s.AveragePerOche),
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
        // Keep the row's id so Delete works on sessions loaded at startup.
        var id = Guid.TryParse(r.Id, out var parsed) ? parsed : Guid.NewGuid();

        if (r.Game == "Cricket")
        {
            var variant = Enum.TryParse<CricketVariant>(r.CricketVariant, out var v) ? v : CricketVariant.American;
            return new CricketSession(variant)
            {
                Id           = id,
                StartedAt    = r.StartedAt,
                CompletedAt  = r.CompletedAt,
                IsComplete   = r.IsComplete,
                OcheCount    = r.CricketOches ?? 0,
                MarksScored  = r.CricketTotalMarks ?? 0,
                DartsThrown  = r.CricketDarts ?? 0
                // Marks map not rehydrated from flat row — extend DartLogRow query if needed
            };
        }

        var startScore = r.X01StartScore ?? (r.Game == "301" ? 301 : 501);

        // Rows saved before in/out options existed: infer "in" from the double-in
        // count; they were all double out.
        var inMode = r.X01InMode switch
        {
            nameof(InMode.SingleIn) => InMode.SingleIn,
            nameof(InMode.DoubleIn) => InMode.DoubleIn,
            _ => (r.X01DartsToDoubleIn ?? 0) > 0 ? InMode.DoubleIn : InMode.SingleIn
        };
        var outMode = r.X01OutMode == nameof(OutMode.SingleOut) ? OutMode.SingleOut : OutMode.DoubleOut;
        var checkedOut = r.X01DoubleOutAchieved ?? false;

        return new X01Session(startScore, inMode, outMode)
        {
            Id                = id,
            StartedAt         = r.StartedAt,
            CompletedAt       = r.CompletedAt,
            IsComplete        = r.IsComplete,
            DartsToDoubleIn   = r.X01DartsToDoubleIn ?? 0,
            DoubleInAchieved  = inMode == InMode.SingleIn || r.X01DoubleInAt.HasValue,
            DoubleInAt        = r.X01DoubleInAt,
            RemainingScore    = checkedOut ? 0 : startScore,
            DartsThrown       = r.X01TotalDarts ?? 0,
            StoredAverage     = r.X01AvgPerOche,
            StoredOcheTrips   = r.X01OcheTrips,
            DoubleOutAchieved = checkedOut,
            DoubleOutAt       = r.X01DoubleOutAt,
            FinishingDart     = r.X01FinishingDart ?? 0,
            BustCount         = r.X01BustCount ?? 0
        };
    }
}
