using J1sDartSharp.Api.Data;
using J1sDartSharp.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace J1sDartSharp.Api.Services;

/// <summary>
/// The database half of Rails' PairingService#assign! — loads the match,
/// runs the pure Core PairingService, and persists the result atomically.
/// Used by MatchesController (reassign) and AbsencesController (absent /
/// present triggers a reassign).
/// </summary>
public sealed class LineupService(LeagueDbContext db, PairingService pairing)
{
    /// <summary>
    /// Rebuilds every non-completed game's lineup.
    ///
    /// Mirrors Rails: validate → clear assigned/unassigned games → schedule →
    /// save. If validation or scheduling fails, nothing is written.
    ///
    /// One deliberate difference (PairingService RULES TO REVISIT #3):
    /// completed games are still passed to the scheduler, so the schedule for
    /// the other games is identical to Rails — but their participants are
    /// never touched. Rails appended new participants onto completed games.
    /// </summary>
    /// <exception cref="PairingException">Too few players, no women, or a game can't be filled.</exception>
    public async Task ReassignAsync(int matchId, CancellationToken ct)
    {
        var match = await db.Matches
            .Include(m => m.Games).ThenInclude(g => g.GameParticipants)
            .Include(m => m.Absences)
            .AsSplitQuery()
            .FirstAsync(m => m.Id == matchId, ct);

        var available = await db.GetAvailablePlayersAsync(
            match.Absences.Select(a => a.PlayerId), tracking: true, ct);

        var reassignable = match.Games
            .Where(g => g.Status != GameStatus.Completed)
            .ToList();

        // Rails' clear_reassignable_games! reset these to "unassigned" before
        // scheduling; the scheduler reads statuses (RULES TO REVISIT #1),
        // so reset them in memory first to keep the same behavior.
        foreach (var game in reassignable)
            game.Status = GameStatus.Unassigned;

        // Pure — throws PairingException before anything is saved.
        var schedule = pairing.BuildSchedule(available, match.Games);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Pass 1: remove old participants. Saved separately so the deletes
        // hit the database before any re-inserts of the same (game, player)
        // pair can trip the unique index.
        foreach (var game in reassignable)
            db.GameParticipants.RemoveRange(game.GameParticipants);

        await db.SaveChangesAsync(ct);

        // Pass 2: write the new lineup.
        foreach (var assignment in schedule.Assignments)
        {
            var game = assignment.Game;
            if (game.Status == GameStatus.Completed)
                continue;

            foreach (var player in assignment.Players)
            {
                db.GameParticipants.Add(new()
                {
                    GameId = game.Id,
                    PlayerId = player.Id,
                    Side = ParticipantSide.Home,
                    Role = ParticipantRole.Player
                });
            }

            game.Status = GameStatus.Assigned;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
