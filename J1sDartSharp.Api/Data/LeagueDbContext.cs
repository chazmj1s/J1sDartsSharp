using J1sDartSharp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace J1sDartSharp.Api.Data;

/// <summary>
/// Shared league/match-night data (players, matches, games, participants,
/// absences). Server-side only — no client touches this via local SQLite.
///
/// Recreates the Rails schema's unique indexes:
///   players(name), games(match_id, sequence),
///   game_participants(game_id, player_id), absences(match_id, player_id)
/// </summary>
public class LeagueDbContext(DbContextOptions<LeagueDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameParticipant> GameParticipants => Set<GameParticipant>();
    public DbSet<Absence> Absences => Set<Absence>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums persist as their names ("SinglesCricket", "Female", ...).
        configurationBuilder.Properties<Gender>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<GameType>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<GameStatus>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<MatchStatus>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<ParticipantSide>().HaveConversion<string>().HaveMaxLength(8);
        configurationBuilder.Properties<ParticipantRole>().HaveConversion<string>().HaveMaxLength(16);

        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // NOTE: No DB-level HasDefaultValue on enums/bools. Their defaults
        // (Draft, Unassigned, Home, Player, Active = true, Rank = 1) are set
        // by the POCO initializers. DB defaults on CLR-default values trip
        // EF's sentinel warning and silently ignore explicit "false"/0 writes.

        // ── Players ──────────────────────────────────────────────────────
        modelBuilder.Entity<Player>(e =>
        {
            e.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(100)
                // Rails: uniqueness case_sensitive: false.
                // NOCASE is SQLite-specific — revisit if moving to Postgres
                // (citext) or SQL Server (a *_CI_* collation).
                .UseCollation("NOCASE");

            e.HasIndex(p => p.Name).IsUnique();
            e.HasIndex(p => p.Rank);

            e.ToTable(t => t.HasCheckConstraint("CK_Players_Rank", "\"Rank\" BETWEEN 1 AND 3"));

            e.Property(p => p.ExternalId).HasMaxLength(64);
            e.HasIndex(p => p.ExternalId)
                .IsUnique()
                .HasFilter("\"ExternalId\" IS NOT NULL");
        });

        // ── Matches ──────────────────────────────────────────────────────
        modelBuilder.Entity<Match>(e =>
        {
            e.Property(m => m.Opponent).IsRequired().HasMaxLength(100);
            e.Property(m => m.Location).IsRequired().HasMaxLength(100);

            e.HasIndex(m => m.MatchDate);

            e.Property(m => m.ExternalId).HasMaxLength(64);
            e.HasIndex(m => m.ExternalId)
                .IsUnique()
                .HasFilter("\"ExternalId\" IS NOT NULL");

            // Rails: has_many :games / :absences, dependent: :destroy
            e.HasMany(m => m.Games)
                .WithOne(g => g.Match)
                .HasForeignKey(g => g.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(m => m.Absences)
                .WithOne(a => a.Match)
                .HasForeignKey(a => a.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Games ────────────────────────────────────────────────────────
        modelBuilder.Entity<Game>(e =>
        {
            e.HasIndex(g => new { g.MatchId, g.Sequence }).IsUnique();

            e.ToTable(t => t.HasCheckConstraint("CK_Games_Sequence", "\"Sequence\" BETWEEN 1 AND 11"));

            // Rails: has_many :game_participants, dependent: :destroy
            e.HasMany(g => g.GameParticipants)
                .WithOne(gp => gp.Game)
                .HasForeignKey(gp => gp.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Game participants ────────────────────────────────────────────
        modelBuilder.Entity<GameParticipant>(e =>
        {
            e.HasIndex(gp => new { gp.GameId, gp.PlayerId }).IsUnique();

            // Deliberate deviation from Rails (which cascaded on player
            // destroy): players are soft-deleted, so a hard delete that would
            // wipe game history should fail loudly instead.
            e.HasOne(gp => gp.Player)
                .WithMany(p => p.GameParticipants)
                .HasForeignKey(gp => gp.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Absences ─────────────────────────────────────────────────────
        modelBuilder.Entity<Absence>(e =>
        {
            e.HasIndex(a => new { a.MatchId, a.PlayerId }).IsUnique();

            e.Property(a => a.Reason).HasMaxLength(250);

            e.HasOne(a => a.Player)
                .WithMany(p => p.Absences)
                .HasForeignKey(a => a.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    // ── Timestamps (Rails created_at / updated_at) ───────────────────────

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
                                               CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<ITimestamped>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Modified:
                    entry.Property(x => x.CreatedAt).IsModified = false;
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
