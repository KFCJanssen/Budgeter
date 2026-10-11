using System;
using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models
{
    /// <summary>
    /// A single income, expense or transfer, entered by hand or imported from a bank file.
    /// Amount is always positive; Type decides the direction.
    /// </summary>
    public class Transaction
    {
        [Key]
        public int Id { get; set; }

        public DateTime Date { get; set; } = DateTime.Today;

        public TransactionType Type { get; set; } = TransactionType.Expense;

        /// <summary>Free-text category, e.g. "Groceries" or "Salary".</summary>
        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        [MaxLength(250)]
        public string Description { get; set; } = string.Empty;

        /// <summary>Who was paid or who paid you (shop, employer…). Filled by bank imports.</summary>
        [MaxLength(120)]
        public string? Payee { get; set; }

        /// <summary>Counterparty IBAN from a bank import, if known.</summary>
        [MaxLength(40)]
        public string? CounterAccount { get; set; }

        /// <summary>Fingerprint of the bank row this came from; prevents importing the same row twice.</summary>
        [MaxLength(80)]
        public string? ImportKey { get; set; }

        /// <summary>The import this row came from, so a whole import can be undone.</summary>
        public int? ImportBatchId { get; set; }

        /// <summary>
        /// Set when this is the actual bank payment of a programmed item (rent, Netflix…).
        /// The programmed item already counts in the totals, so linked rows are not counted twice.
        /// </summary>
        public int? RecurringId { get; set; }

        public bool IsImported => ImportBatchId != null || ImportKey != null;
        public bool IsCoveredByRecurring => RecurringId != null;

        /// <summary>Counts towards income/expense totals (not a transfer, not covered by a programmed item).</summary>
        public bool CountsInTotals => Type != TransactionType.Transfer && RecurringId == null;
    }
}
