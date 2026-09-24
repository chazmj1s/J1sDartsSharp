using J1sDartSharp.Api.Data;
using J1sDartSharp.Api.Services;
using J1sDartSharp.Core.Models;
using J1sDartSharp.Core.Services;
using J1sDartSharp.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace J1sDartSharp.Api.Controllers;

/// <summary>
/// Port of app/controllers/absences_controller.rb.
///
/// Rails → API:
///   create   POST   /api/matches/{matchId}/absences              → MatchDetailDto
///   destroy  DELETE /api/matches/{matchId}/absences/{absenceId}  → MatchDetailDto
///
/// Like Rails, marking a player absent/present reassigns pairings unless the
/// match is finalized (a locked lineup is left alone), and the absence change
/// is kept even if the reassign then fails — the 422 says so explicitly.
/// </summary>
[ApiController]
[Route("api/matches/{matchId:int}/absences")]
public sealed class AbsencesController(LeagueDbContext db, LineupService lineup) : ControllerBase
{
    // POST /api/matches/{matchId}/absences
    [HttpPost]
    public async Task<ActionResult<MatchDetailDto>> Create(
        int matchId, CreateAbsenceRequest request, CancellationToken ct)
    {
        var match = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null)
            return NotFound();

        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PlayerId, ct);
        if (player is null)
            return RuleViolation("Player not found.");

        if (await db.Absences.AnyAsync(a => a.MatchId == matchId && a.PlayerId == player.Id, ct))
            return RuleViolation($"{player.Name} is already marked absent for this match.");

        db.Absences.Add(new Absence
        {
            MatchId = matchId,
            PlayerId = player.Id,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return RuleViolation($"{player.Name} is already marked absent for this match.");
        }

        return await ReassignUnlessLockedAsync(match, $"{player.Name} marked absent", ct);
    }

    // DELETE /api/matches/{matchId}/absences/{absenceId}
    // Rails: "marked present".
    [HttpDelete("{absenceId:int}")]
    public async Task<ActionResult<MatchDetailDto>> Delete(int matchId, int absenceId, CancellationToken ct)
    {
        var absence = await db.Absences
            .Include(a => a.Player)
            .Include(a => a.Match)
            .FirstOrDefaultAsync(a => a.Id == absenceId && a.MatchId == matchId, ct);

        if (absence is null)
            return NotFound();

        var match = absence.Match;
        var playerName = absence.Player.Name;

        db.Absences.Remove(absence);
        await db.SaveChangesAsync(ct);

        return await ReassignUnlessLockedAsync(match, $"{playerName} marked present", ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Rails: unless @match.finalized? then PairingService.new(@match).assign!
    /// (also skipped for completed matches).
    /// </summary>
    private async Task<ActionResult<MatchDetailDto>> ReassignUnlessLockedAsync(
        Match match, string whatHappened, CancellationToken ct)
    {
        if (!match.IsFinalized && !match.IsCompleted)
        {
            try
            {
                await lineup.ReassignAsync(match.Id, ct);
            }
            catch (PairingException ex)
            {
                // Rails: redirect with alert "Could not reassign: ..." — the
                // absence change itself was already saved.
                return Problem(
                    title: "Could not reassign pairings",
                    detail: $"{whatHappened}, but pairings could not be reassigned: {ex.Message}",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        }

        return (await db.GetMatchDetailAsync(match.Id, ct))!;
    }

    private ObjectResult RuleViolation(string detail) =>
        Problem(title: "Invalid absence", detail: detail, statusCode: StatusCodes.Status422UnprocessableEntity);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true;
}
