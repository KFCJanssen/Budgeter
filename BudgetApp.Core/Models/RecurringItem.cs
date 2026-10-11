using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Models
{
    public enum RecurringFrequency
    {
        Weekly = 0,
        Monthly = 1,
        Yearly = 2
    }

    /// <summary>
    /// A "programmed" income or cost entered once - salary, rent, a subscription, insurance.
    /// It counts in every month from its start date while active, as a monthly equivalent.
    /// Stored in the "RecurringExpenses" table (its name from version 1.0, when only costs existed).
    /// </summary>
    public class RecurringItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        /// <summary>Amount per week, month or year (see Frequency).</summary>
        public decimal Amount { get; set; }

        public RecurringFrequency Frequency { get; set; } = RecurringFrequency.Monthly;

        /// <summary>Income or Expense. Existing 1.0 data is all Expense.</summary>
        public TransactionType Type { get; set; } = TransactionType.Expense;

        public DateTime StartDate { get; set; } = DateTime.Today;

        /// <summary>Paused items stop counting but stay in the list.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Weekly/yearly amounts converted to a per-month figure.</summary>
        [NotMapped]
        public decimal MonthlyAmount => Frequency switch
        {
            RecurringFrequency.Weekly => Amount * 52 / 12,
            RecurringFrequency.Yearly => Amount / 12,
            _ => Amount
        };

        [NotMapped]
        public bool IsIncome => Type == TransactionType.Income;
    }
}
