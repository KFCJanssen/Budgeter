using System;
using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models
{
    /// <summary>
    /// Represents a single income or expense entry entered by the user.
    /// This is the row-level record that both the balance sheet and the CSV export are built from.
    /// </summary>
    public class Transaction
    {
        [Key]
        public int Id { get; set; }

        public DateTime Date { get; set; } = DateTime.Now;

        public TransactionType Type { get; set; }

        /// <summary>
        /// Free-text category, e.g. "Fixed Expenses", "Discretionary", "Savings", "Utilities", "Salary".
        /// Kept as a string (not an enum) so the user can type new categories without a code change.
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        [MaxLength(250)]
        public string Description { get; set; } = string.Empty;
    }
}
