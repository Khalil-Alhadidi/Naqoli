using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Data.Entities;

namespace Wallet.MockApi.Data;

public class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("Users");
            user.HasIndex(u => u.Phone).IsUnique();
            user.Property(u => u.Phone).HasMaxLength(15);
            user.Property(u => u.DisplayName).HasMaxLength(100);
        });

        modelBuilder.Entity<Account>(account =>
        {
            account.Property(a => a.BalanceIqd).HasColumnType("bigint");
            account.HasOne(a => a.User)
                .WithMany(u => u.Accounts)
                .HasForeignKey(a => a.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
