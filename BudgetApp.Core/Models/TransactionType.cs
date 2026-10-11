namespace BudgetApp.Models
{
    /// <summary>
    /// Whether a transaction adds money (Income), takes money out (Expense), or just moves it
    /// between your own accounts (Transfer, e.g. to a savings account). Transfers are left out of
    /// income and expense totals, so moving money to savings doesn't look like spending.
    /// The numbers are stored in the database, so never change existing values.
    /// </summary>
    public enum TransactionType
    {
        Income = 0,
        Expense = 1,
        Transfer = 2
    }
}
