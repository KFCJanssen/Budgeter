using System;
using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models
{
    public enum RecurringFrequency
    {
        Weekly,
        Monthly,
        Yearly
    }

    /// <summary>
    /// Represents a "programmed" recurring cost such as a subscription or insurance premium.
    /// These are stored separately from one-off Transactions, but their monthly-equivalent
    /// Amount is folded into the total-expenses calculation and the pie chart on every refresh,
    /// so the user only has to enter a subscription once instead of re-adding it every month.
    /// </summary>
    public class RecurringExpense
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public RecurringFrequency Frequency { get; set; } = RecurringFrequency.Monthly;

        public DateTime StartDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Lets the user "pause" a recurring cost (e.g. a cancelled subscription) without deleting
        /// its history. Only active items are added into totals and the chart.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Normalizes any frequency to a monthly figure so the dashboard always shows
        /// "cost per month" regardless of how the user entered it. Not mapped to the database;
        /// it's computed on the fly from Amount + Frequency.
        /// </summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public decimal MonthlyAmount => Frequency switch
        {
            RecurringFrequency.Weekly => Amount * 52 / 12,
            RecurringFrequency.Yearly => Amount / 12,
            _ => Amount
        };
    }
}
