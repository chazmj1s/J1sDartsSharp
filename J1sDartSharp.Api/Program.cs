using J1sDartSharp.Api.Data;
using J1sDartSharp.Api.Services;
using J1sDartSharp.Core.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Data ────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<LeagueDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("League")
        ?? throw new InvalidOperationException("Connection string 'League' is not configured.")));

// ── Domain services ─────────────────────────────────────────────────────
// Pure and stateless → one shared instance.
builder.Services.AddSingleton<PairingService>();

// Uses the DbContext → one per request.
builder.Services.AddScoped<LineupService>();

// ── Web ─────────────────────────────────────────────────────────────────
// Enum/DateOnly JSON handling comes from attributes on the Shared types,
// so no JsonOptions configuration is needed here.
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

var app = builder.Build();

// POC convenience: apply pending migrations at startup so deploying to the
// Ubuntu box is just "copy + restart". Swap for a deploy-time step later.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LeagueDbContext>();
    db.Database.Migrate();
}

// Unhandled exceptions → RFC 7807 ProblemDetails (outside Development,
// where the developer exception page takes over).
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));
app.MapControllers();

app.Run();
