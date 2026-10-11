using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Data
{
    /// <summary>
    /// EF Core context on the local SQLite file. The tables themselves are created and upgraded by
    /// <see cref="SchemaMigrator"/>, never by EF, so existing databases are upgraded in place.
    /// </summary>
    public class BudgetContext : DbContext
    {
        private readonly string _dbPath;

        public BudgetContext(string dbPath) => _dbPath = dbPath;

        public DbSet<Transaction> Transactions { get; set; } = null!;
        public DbSet<RecurringItem> RecurringItems { get; set; } = null!;
        public DbSet<Budget> Budgets { get; set; } = null!;
        public DbSet<Goal> Goals { get; set; } = null!;
        public DbSet<CategoryRule> CategoryRules { get; set; } = null!;
        public DbSet<ImportBatch> ImportBatches { get; set; } = null!;
        public DbSet<BalanceItem> BalanceItems { get; set; } = null!;
        public DbSet<Setting> Settings { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");

        protected override void OnModelCreating(ModelBuilder b)
        {
            // Table names are fixed by the migrator (and "RecurringExpenses" by version 1.0).
            b.Entity<Transaction>().ToTable("Transactions");
            b.Entity<RecurringItem>().ToTable("RecurringExpenses");
            b.Entity<Budget>().ToTable("Budgets");
            b.Entity<Goal>().ToTable("Goals");
            b.Entity<CategoryRule>().ToTable("CategoryRules");
            b.Entity<ImportBatch>().ToTable("ImportBatches");
            b.Entity<BalanceItem>().ToTable("BalanceItems");
            b.Entity<Setting>().ToTable("Settings");

            // SQLite has no decimal type: amounts are stored as text, which keeps them exact.
            b.Entity<Transaction>().Property(t => t.Amount).HasColumnType("TEXT");
            b.Entity<RecurringItem>().Property(r => r.Amount).HasColumnType("TEXT");
            b.Entity<Budget>().Property(x => x.MonthlyLimit).HasColumnType("TEXT");
            b.Entity<Goal>().Property(x => x.TargetAmount).HasColumnType("TEXT");
            b.Entity<Goal>().Property(x => x.SavedAmount).HasColumnType("TEXT");
            b.Entity<BalanceItem>().Property(x => x.Amount).HasColumnType("TEXT");
            b.Entity<BalanceItem>().Property(x => x.InterestRate).HasColumnType("TEXT");
            b.Entity<BalanceItem>().Property(x => x.MonthlyPayment).HasColumnType("TEXT");

            b.Entity<Transaction>().Ignore(t => t.IsImported).Ignore(t => t.IsCoveredByRecurring).Ignore(t => t.CountsInTotals);
            b.Entity<BalanceItem>().Ignore(x => x.IsDebt).Ignore(x => x.IsLiquid);
        }
    }
}
