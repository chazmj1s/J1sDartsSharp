using J1sDartSharp.Api.Mapping;
using J1sDartSharp.Core.Models;
using J1sDartSharp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace J1sDartSharp.Api.Data;

/// <summary>
/// Read queries shared by the match-night controllers (Matches, Games,
/// Absences). Every mutating match-night endpoint finishes by returning
/// GetMatchDetailAsync, so the client always re-renders from fresh data.
/// </summary>
public static class MatchQueries
{
    /// <summary>
    /// Rails: Player.available_for(match) — active players not marked absent,
    /// ordered by name.
    /// </summary>
    public static Task<List<Player>> GetAvailablePlayersAsync(
        this LeagueDbContext db, IEnumerable<int> absentPlayerIds, bool tracking, CancellationToken ct)
    {
        var absent = absentPlayerIds.ToList();
        var query = tracking ? db.Players : db.Players.AsNoTracking();

        return query
            .Where(p => p.Active && !absent.Contains(p.Id))
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }

    /// <summary>Everything the match-night screen needs; null if not found.</summary>
    public static async Task<MatchDetailDto?> GetMatchDetailAsync(
        this LeagueDbContext db, int matchId, CancellationToken ct)
    {
        var match = await db.Matches
            .AsNoTracking()
            .Include(m => m.Games).ThenInclude(g => g.GameParticipants).ThenInclude(gp => gp.Player)
            .Include(m => m.Absences).ThenInclude(a => a.Player)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);

        if (match is null)
            return null;

        var available = await db.GetAvailablePlayersAsync(
            match.Absences.Select(a => a.PlayerId), tracking: false, ct);

        return match.ToDetail(available);
    }
}
