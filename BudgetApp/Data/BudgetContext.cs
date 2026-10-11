using System;
using System.IO;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Data
{
    /// <summary>
    /// EF Core database context backed by a local SQLite file.
    /// The database file is created automatically (via EnsureCreated) the first time the
    /// app runs, so there's no manual database setup required by the user.
    /// </summary>
    public class BudgetContext : DbContext
    {
        public DbSet<Transaction> Transactions { get; set; } = null!;
        public DbSet<RecurringExpense> RecurringExpenses { get; set; } = null!;

        // Stored under %AppData%/BudgetApp/budget.db so data survives rebuilds of the project
        // (unlike putting it in the bin/Debug output folder, which gets wiped on rebuild).
        private static readonly string DbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BudgetApp",
            "budget.db");

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            optionsBuilder.UseSqlite($"Data Source={DbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Transaction>()
                .Property(t => t.Amount)
                .HasColumnType("TEXT"); // SQLite has no native decimal type; EF Core stores it as text/real under the hood.

            modelBuilder.Entity<RecurringExpense>()
                .Property(r => r.Amount)
                .HasColumnType("TEXT");
        }
    }
}
