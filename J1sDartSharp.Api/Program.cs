using J1sDartSharp.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Data ────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<LeagueDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("League")
        ?? throw new InvalidOperationException("Connection string 'League' is not configured.")));

// Phase 2 will add: controllers, PairingService, JSON options, CORS/auth as needed.

var app = builder.Build();

// POC convenience: apply pending migrations at startup so deploying to the
// Ubuntu box is just "copy + restart". Swap for a deploy-time step later.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LeagueDbContext>();
    db.Database.Migrate();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

app.Run();
