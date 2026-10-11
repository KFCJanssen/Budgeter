using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BudgetApp.Data;
using BudgetApp.Services;

namespace BudgetApp
{
    public enum PageKey
    {
        Dashboard,
        Transactions,
        Programmed,
        Budgets,
        Trends,
        Import,
        Tools,
        Profile,
        Help,
        Settings
    }

    /// <summary>Implemented by every page so the window can tell it when it's shown.</summary>
    public interface IPage
    {
        /// <summary>Called each time the page becomes visible (and after data changes while visible).</summary>
        void OnShown(object? argument);
    }

    /// <summary>App-wide state shared by the pages.</summary>
    public static class AppState
    {
        public static BudgetStore Store { get; set; } = null!;

        /// <summary>The month (or all time) the Dashboard and Transactions pages show; shared so they stay in step.</summary>
        public static BudgetPeriod Period { get; set; } = BudgetPeriod.ForMonth(DateTime.Today);

        /// <summary>Set by the self-test so charts draw instantly for screenshots.</summary>
        public static bool NoAnimations { get; set; }

        /// <summary>Set by the self-test: never show message boxes (there's nobody to click them).</summary>
        public static bool Unattended { get; set; }

        internal static Action<PageKey, object?>? Navigator;
        internal static Action<string, string?, Action?>? Toaster;

        public static void Navigate(PageKey page, object? argument = null) => Navigator?.Invoke(page, argument);

        /// <summary>Short message at the bottom of the window, optionally with one action ("Undo").</summary>
        public static void Toast(string message, string? actionText = null, Action? action = null) =>
            Toaster?.Invoke(message, actionText, action);

        public static List<string> Categories(IEnumerable<string>? more = null) =>
            BudgetCalculator.CategorySuggestions(BudgetCalculator.DefaultCategories, Store.Transactions, Store.Recurring,
                Store.Budgets.Select(b => b.Category).Concat(more ?? Enumerable.Empty<string>()));
    }

    /// <summary>Message boxes in one place, so wording and the unattended self-test behave consistently.</summary>
    public static class Ui
    {
        public static bool Confirm(string text, string title)
        {
            if (AppState.Unattended) return true;
            return MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        public static void Warn(string text, string title = "Budgeter")
        {
            if (AppState.Unattended) throw new InvalidOperationException($"{title}: {text}");
            MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public static void Info(string text, string title = "Budgeter")
        {
            if (AppState.Unattended) return;
            MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public static void Error(string what, Exception ex)
        {
            if (AppState.Unattended) throw new InvalidOperationException(what, ex);
            MessageBox.Show($"{what}\n\n{ex.GetBaseException().Message}", "Budgeter", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
