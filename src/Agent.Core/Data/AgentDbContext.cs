using Agent.Core.Audit;
using Agent.Core.Pending;
using Microsoft.EntityFrameworkCore;

namespace Agent.Core.Data;

/// <summary>The agent's own database: pending payments and the audit trail. The wallet's data lives behind its API.</summary>
public class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public const int IdempotencyKeyLength = 32;
    public const int StateLength = 16;
    public const int TextLength = 100;

    public DbSet<PendingRecord> PendingTransactions => Set<PendingRecord>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PendingRecord>(e =>
        {
            e.ToTable("PendingTransactions");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.State).HasConversion<string>().HasMaxLength(StateLength);
            e.Property(p => p.IdempotencyKey).HasMaxLength(IdempotencyKeyLength).IsUnicode(false);
            e.HasIndex(p => p.IdempotencyKey).IsUnique();
            e.Property(p => p.Type).HasMaxLength(TextLength);
            e.Property(p => p.ToPhone).HasMaxLength(TextLength);
            e.Property(p => p.DisplayName).HasMaxLength(TextLength);
            e.Property(p => p.RegisteredName).HasMaxLength(TextLength);
            e.Property(p => p.BillCode).HasMaxLength(TextLength);
            e.Property(p => p.ProductCode).HasMaxLength(TextLength);
            e.Property(p => p.Warning).HasMaxLength(TextLength);
            e.Property(p => p.Reference).HasMaxLength(TextLength);
            e.Property(p => p.ResultCode).HasMaxLength(TextLength);
            e.Property(p => p.PurchaseCodeLast4).HasMaxLength(4);
            e.Property(p => p.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.ToTable("AuditEntries");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).UseIdentityColumn();
            e.Property(a => a.Event).HasMaxLength(TextLength);
            e.Property(a => a.FromState).HasMaxLength(StateLength);
            e.Property(a => a.ToState).HasMaxLength(StateLength);
            e.Property(a => a.Reason).HasMaxLength(TextLength);
            e.Property(a => a.Reference).HasMaxLength(TextLength);
            e.Property(a => a.Detail).HasMaxLength(TextLength);
            e.HasIndex(a => a.PendingId);
            e.HasIndex(a => a.At);
        });
    }
}
