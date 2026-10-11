using System.Globalization;
using BudgetApp.Models;

/// <summary>Tiny test framework: no packages needed, so tests also run offline.</summary>
public static class T
{
    public static int Failures, Passed;

    public static void Test(string name, Action body)
    {
        try { body(); Passed++; Console.WriteLine($"PASS  {name}"); }
        catch (Exception ex) { Failures++; Console.WriteLine($"FAIL  {name}\n      {ex.Message}"); }
    }

    public static void Eq<TV>(TV expected, TV actual, string what = "")
    {
        if (!EqualityComparer<TV>.Default.Equals(expected, actual))
            throw new Exception($"{what} expected <{expected}> but got <{actual}>");
    }

    public static void Near(decimal expected, decimal actual, decimal tolerance, string what = "")
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new Exception($"{what} expected ~{expected} (±{tolerance}) but got {actual}");
    }

    public static void True(bool cond, string what) { if (!cond) throw new Exception(what); }

    public static readonly DateTime Today = new(2026, 10, 11);

    public static Transaction Tx(string date, TransactionType type, string cat, decimal amount, string desc = "") =>
        new() { Date = DateTime.Parse(date, CultureInfo.InvariantCulture), Type = type, Category = cat, Amount = amount, Description = desc };

    public static RecurringItem Rec(string name, string cat, decimal amount, RecurringFrequency f, string start, bool active = true,
        TransactionType type = TransactionType.Expense, int id = 0) =>
        new() { Id = id, Name = name, Category = cat, Amount = amount, Frequency = f, StartDate = DateTime.Parse(start, CultureInfo.InvariantCulture), IsActive = active, Type = type };
}
