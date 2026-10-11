# Budgeter

A simple personal budget app for Windows (WPF, .NET 8). Track income and expenses, add recurring costs once, and see where your money goes.

## Features

- **Budget dashboard** — total income, total expenses and net balance at a glance (in €)
- **Add income / expense** entries with date, category and description
- **Programmed (recurring) expenses** — subscriptions, insurance, rent… entered once as weekly, monthly or yearly; converted to a monthly amount and included in your totals automatically
- **Pie chart** of expenses as a share of income (LiveCharts)
- **Transaction history** with delete
- **Export to CSV**

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

## Changelog

- **1.0** — First release: dashboard, income/expense entries, recurring expenses, pie chart, CSV export

## Releasing a new version

Bump the number in `VERSION` (and the changelog above) and push to `main`. GitHub Actions builds `BudgetApp.exe` and publishes the release automatically.
