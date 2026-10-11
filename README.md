# Budgeter

A simple personal budget app for Windows (WPF, .NET 8). Track income and expenses, add recurring costs once, and see where your money goes.

## Features

- **Monthly dashboard** — income, expenses and net balance for the month you pick (◀ ▶), or for all time, in €
- **Add, edit and delete** income / expense entries with date, category and description (double-click a row to edit)
- **Amounts the way you type them** — `12,50`, `12.50`, `€ 1.234,56` all work
- **Programmed (recurring) expenses** — subscriptions, insurance, rent… entered once as weekly, monthly or yearly with a start date; counted every month from then on. Pause one to stop it without losing past months
- **Pie chart** of where your income goes, including what's left over (LiveCharts)
- **Export to CSV** for the selected period, formatted so it opens as proper columns in Excel

Your data is stored locally in `%APPDATA%\BudgetApp\budget.db` (SQLite), so it survives rebuilds and updates.

## Download

Grab `BudgetApp.exe` from the [Releases](../../releases) page.

## Run from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project BudgetApp
```

Or open `BudgetApp/BudgetApp.csproj` in Visual Studio and press F5.

## Build the .exe yourself

Double-click `build_exe.bat`. The self-contained single-file app lands in `publish\BudgetApp.exe`.

## Tests

```bash
dotnet run --project BudgetApp.Tests
```

Every push is built and tested on Windows by GitHub Actions.

## Changelog

- **1.1** — Fixes: amounts like 12.50 were saved as 1250 on Dutch Windows; tables showed $ instead of €; totals mixed all-time income with one month of programmed expenses. New: month-by-month view, edit entries, pause programmed expenses, start dates, delete confirmations, Enter to submit, CSV that opens correctly in Dutch Excel

- **1.0** — First release: dashboard, income/expense entries, recurring expenses, pie chart, CSV export

## Releasing a new version

Bump the number in `VERSION` (and the changelog above) and push to `main`. GitHub Actions builds `BudgetApp.exe` and publishes the release automatically.
