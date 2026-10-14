using Microsoft.EntityFrameworkCore;
using Wallet.MockApi.Data.Entities;

namespace Wallet.MockApi.Data;

public class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    private const int NameLength = 100;
    private const int PhoneLength = 15;
    private const int AliasLength = 50;
    private const int KeyLength = 64;
    private const int ReferenceLength = 16;
    private const int CodeLength = 32;
    private const int EnumLength = 16;
    private const int PinHashLength = 128;

    public DbSet<User> Users => Set<User>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Alias> Aliases => Set<Alias>();

    public DbSet<Biller> Billers => Set<Biller>();

    public DbSet<BillAccount> BillAccounts => Set<BillAccount>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Rows a user owns (accounts, contacts, aliases, bill accounts) cascade from Users. Every other
        // relationship is Restrict, so SQL Server never sees two cascade paths; the reset deletes
        // tables in dependency order instead.
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("Users");
            user.HasIndex(u => u.Phone).IsUnique();
            user.Property(u => u.Phone).HasMaxLength(PhoneLength);
            user.Property(u => u.DisplayName).HasMaxLength(NameLength);
            user.Property(u => u.PinHash).HasMaxLength(PinHashLength);
        });

        modelBuilder.Entity<Account>(account =>
        {
            account.ToTable("Accounts");
            account.Property(a => a.BalanceIqd).HasColumnType("bigint");
            account.HasOne(a => a.User)
                .WithMany(u => u.Accounts)
                .HasForeignKey(a => a.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Contact>(contact =>
        {
            contact.ToTable("Contacts");
            contact.Property(c => c.SavedName).HasMaxLength(NameLength);
            contact.Property(c => c.Phone).HasMaxLength(PhoneLength);
            contact.HasIndex(c => new { c.OwnerUserId, c.SavedName, c.Phone }).IsUnique();
            contact.HasOne(c => c.Owner)
                .WithMany(u => u.Contacts)
                .HasForeignKey(c => c.OwnerUserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Alias>(alias =>
        {
            alias.ToTable("Aliases");
            alias.Property(a => a.Text).HasMaxLength(AliasLength);
            alias.HasIndex(a => new { a.OwnerUserId, a.Text }).IsUnique();
            alias.HasOne(a => a.Owner)
                .WithMany(u => u.Aliases)
                .HasForeignKey(a => a.OwnerUserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            alias.HasOne(a => a.TargetAccount)
                .WithMany()
                .HasForeignKey(a => a.TargetAccountId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Biller>(biller =>
        {
            biller.ToTable("Billers");
            biller.Property(b => b.Code).HasMaxLength(CodeLength);
            biller.Property(b => b.Name).HasMaxLength(NameLength);
            biller.HasIndex(b => b.Code).IsUnique();
        });

        modelBuilder.Entity<BillAccount>(bill =>
        {
            bill.ToTable("BillAccounts");
            bill.Property(b => b.Label).HasMaxLength(AliasLength);
            bill.Property(b => b.AccountNumber).HasMaxLength(CodeLength);
            bill.Property(b => b.DueDate).HasColumnType("date");
            bill.HasOne(b => b.User)
                .WithMany(u => u.BillAccounts)
                .HasForeignKey(b => b.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            bill.HasOne(b => b.Biller)
                .WithMany()
                .HasForeignKey(b => b.BillerId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("Products");
            product.Property(p => p.Provider).HasMaxLength(CodeLength);
            product.Property(p => p.Name).HasMaxLength(NameLength);
            product.Property(p => p.Type).HasConversion<string>().HasMaxLength(EnumLength);
        });

        modelBuilder.Entity<Transaction>(transaction =>
        {
            transaction.ToTable("Transactions");
            transaction.Property(t => t.IdempotencyKey).HasMaxLength(KeyLength);
            transaction.HasIndex(t => t.IdempotencyKey).IsUnique();
            transaction.Property(t => t.Reference).HasMaxLength(ReferenceLength);
            transaction.HasIndex(t => t.Reference).IsUnique().HasFilter("[Reference] IS NOT NULL");
            transaction.Property(t => t.Type).HasConversion<string>().HasMaxLength(EnumLength);
            transaction.Property(t => t.Status).HasConversion<string>().HasMaxLength(EnumLength);
            transaction.Property(t => t.ErrorCode).HasMaxLength(CodeLength);
            transaction.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
            transaction.HasOne(t => t.FromAccount)
                .WithMany()
                .HasForeignKey(t => t.FromAccountId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
            transaction.HasOne(t => t.ToAccount)
                .WithMany()
                .HasForeignKey(t => t.ToAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            transaction.HasOne(t => t.BillAccount)
                .WithMany()
                .HasForeignKey(t => t.BillAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            transaction.HasOne(t => t.Product)
                .WithMany()
                .HasForeignKey(t => t.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("Orders");
            order.Property(o => o.Code).HasMaxLength(CodeLength);
            order.HasIndex(o => o.TransactionId).IsUnique();
            order.HasOne(o => o.User)
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
            order.HasOne(o => o.Product)
                .WithMany()
                .HasForeignKey(o => o.ProductId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
            order.HasOne(o => o.Transaction)
                .WithMany()
                .HasForeignKey(o => o.TransactionId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
