using J1sDartSharp.Api.Data;
using J1sDartSharp.Api.Mapping;
using J1sDartSharp.Core.Models;
using J1sDartSharp.Shared.Constants;
using J1sDartSharp.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace J1sDartSharp.Api.Controllers;

/// <summary>
/// Port of app/controllers/players_controller.rb.
///
/// Rails → API:
///   index    GET    /api/players          → RosterDto (active players + stats)
///   (edit)   GET    /api/players/{id}     → PlayerDto
///   create   POST   /api/players          → 201 PlayerDto
///   update   PUT    /api/players/{id}     → PlayerDto
///   destroy  DELETE /api/players/{id}     → 204 (soft delete: Active = false)
///
/// Rails' model validations (presence, rank 1–3, gender) are enforced by the
/// DataAnnotations on SavePlayerRequest before these actions run. The two
/// that need the database — unique name and roster-not-full — live here and
/// return the same 400 ValidationProblem shape.
/// </summary>
[ApiController]
[Route("api/players")]
public sealed class PlayersController(LeagueDbContext db) : ControllerBase
{
    // GET /api/players
    [HttpGet]
    public async Task<ActionResult<RosterDto>> GetRoster(CancellationToken ct)
    {
        var players = await db.Players
            .AsNoTracking()
            .Where(p => p.Active)
            .OrderBy(p => p.Rank).ThenBy(p => p.Name)
            .ToListAsync(ct);

        var female = players.Count(p => p.IsFemale);

        return new RosterDto(
            Players: players.Select(p => p.ToDto()).ToList(),
            Total: players.Count,
            Female: female,
            Male: players.Count - female,
            OpenSlots: LeagueRules.MaxRoster - players.Count);
    }

    // GET /api/players/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PlayerDto>> Get(int id, CancellationToken ct)
    {
        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (player is null)
            return NotFound();

        return player.ToDto();
    }

    // POST /api/players
    [HttpPost]
    public async Task<ActionResult<PlayerDto>> Create(SavePlayerRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();

        // Rails: validate :roster_not_full, on: :create
        var activeCount = await db.Players.CountAsync(p => p.Active, ct);
        if (activeCount >= LeagueRules.MaxRoster)
            return RosterFull();

        // Rails: uniqueness { case_sensitive: false }. The Name column uses
        // NOCASE collation, so this comparison is case-insensitive in SQLite.
        // Like Rails, this includes removed (inactive) players.
        if (await db.Players.AnyAsync(p => p.Name == name, ct))
            return NameTaken();

        var player = new Player
        {
            Name = name,
            Gender = request.Gender!.Value,
            Rank = request.Rank,
            Active = true
        };

        db.Players.Add(player);

        if (!await TrySaveAsync(ct))
            return NameTaken();   // lost a race to another request with the same name

        return CreatedAtAction(nameof(Get), new { id = player.Id }, player.ToDto());
    }

    // PUT /api/players/{id}
    [HttpPut("{id:int}")]
    public async Task<ActionResult<PlayerDto>> Update(int id, SavePlayerRequest request, CancellationToken ct)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (player is null)
            return NotFound();

        var name = request.Name.Trim();

        if (await db.Players.AnyAsync(p => p.Id != id && p.Name == name, ct))
            return NameTaken();

        player.Name = name;
        player.Gender = request.Gender!.Value;
        player.Rank = request.Rank;

        if (!await TrySaveAsync(ct))
            return NameTaken();

        return player.ToDto();
    }

    // DELETE /api/players/{id}
    // Rails: destroy → update!(active: false). Game history is kept.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (player is null)
            return NotFound();

        if (player.Active)
        {
            player.Active = false;
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private ActionResult NameTaken()
    {
        ModelState.AddModelError(nameof(SavePlayerRequest.Name), "Name has already been taken.");
        return ValidationProblem(ModelState);
    }

    private ActionResult RosterFull()
    {
        ModelState.AddModelError(string.Empty, $"Roster is full (max {LeagueRules.MaxRoster} active players).");
        return ValidationProblem(ModelState);
    }

    /// <summary>
    /// Saves, returning false if the unique name index rejected the write.
    /// </summary>
    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }
    }
}
