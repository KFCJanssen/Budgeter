using System;
using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models
{
    /// <summary>A monthly spending limit for one category.</summary>
    public class Budget
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        public decimal MonthlyLimit { get; set; }
    }

    /// <summary>A savings goal, e.g. "Laptop €1.200 by June".</summary>
    public class Goal
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public decimal TargetAmount { get; set; }

        public decimal SavedAmount { get; set; }

        public DateTime? TargetDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Today;
    }

    /// <summary>Where a rule looks for its text.</summary>
    public enum RuleField
    {
        Anywhere = 0,
        Payee = 1,
        Description = 2
    }

    /// <summary>
    /// "If a bank row contains <see cref="Pattern"/>, put it in <see cref="Category"/>."
    /// Optionally also sets the type, e.g. to Transfer for moves to your own savings account.
    /// </summary>
    public class CategoryRule
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Pattern { get; set; } = string.Empty;

        public RuleField Field { get; set; } = RuleField.Anywhere;

        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        public TransactionType? SetType { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Today;
    }

    /// <summary>One bank file import, so it can be listed and undone.</summary>
    public class ImportBatch
    {
        [Key]
        public int Id { get; set; }

        public DateTime ImportedAt { get; set; } = DateTime.Now;

        [MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        [MaxLength(40)]
        public string Format { get; set; } = string.Empty;

        public int Count { get; set; }
    }

    /// <summary>The kinds of things you own (assets) and owe (debts). Values are stored; don't renumber.</summary>
    public enum BalanceKind
    {
        Checking = 0,
        Savings = 1,
        Investments = 2,
        Pension = 3,
        Property = 4,
        OtherAsset = 5,

        StudentLoan = 10,
        Mortgage = 11,
        CreditCard = 12,
        PersonalLoan = 13,
        OtherDebt = 14
    }

    /// <summary>An account balance, investment, property or debt for the net-worth part of the profile.</summary>
    public class BalanceItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public BalanceKind Kind { get; set; }

        /// <summary>Always positive; Kind decides whether it's owned or owed.</summary>
        public decimal Amount { get; set; }

        /// <summary>Yearly interest in percent (e.g. 2.33), mostly for debts.</summary>
        public decimal? InterestRate { get; set; }

        /// <summary>What you pay per month on a debt.</summary>
        public decimal? MonthlyPayment { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.Today;

        public bool IsDebt => (int)Kind >= 10;

        /// <summary>Money you could use in an emergency without selling anything.</summary>
        public bool IsLiquid => Kind is BalanceKind.Checking or BalanceKind.Savings;
    }

    /// <summary>Simple key/value store for settings and the profile.</summary>
    public class Setting
    {
        [Key]
        [MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;
    }
}
