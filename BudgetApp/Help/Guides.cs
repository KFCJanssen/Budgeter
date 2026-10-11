using System.Collections.Generic;
using System.Linq;

namespace BudgetApp.Help
{
    public sealed record GuideStep(string Title, string Text);

    public sealed record Guide(
        string Id,
        string Title,
        string Summary,
        IReadOnlyList<GuideStep> Steps,
        IReadOnlyList<string> Tips,
        PageKey? GoTo = null,
        string? GoToText = null,
        string Group = "Guides");

    /// <summary>
    /// Step-by-step guides shown on the Help page (and the bank guides inside the import wizard).
    /// Bank menu names follow the banks' own help pages; banks rename menus now and then, so each
    /// guide also says what to look for if a name has changed.
    /// </summary>
    public static class Guides
    {
        public static readonly IReadOnlyList<Guide> All = new List<Guide>
        {
            new("start", "Getting started", "Set Budgeter up in about ten minutes.",
                new[]
                {
                    new GuideStep("Add what comes back every month",
                        "Go to Programmed. Add your income first (salary, allowance, student finance), then fixed costs: rent, insurance, phone, subscriptions. Choose 'Income' or 'Cost', the amount and how often it comes. These count automatically in every month from their start date."),
                    new GuideStep("Bring in your real spending",
                        "Easiest: download a file from your bank's website and use Import from bank (guides below). Or type single expenses with Quick add on the Dashboard."),
                    new GuideStep("Give everything a category",
                        "After an import, the Dashboard's 'To do' list shows how many entries have no category. Click 'Sort them', select rows (Ctrl+click), pick a category at the bottom and click Apply."),
                    new GuideStep("Set budgets",
                        "On Budgets & goals, click 'Suggest from my spending' to start from your real averages, then lower the ones you want to spend less on."),
                    new GuideStep("Check in once a month",
                        "Import the new month, look at the Dashboard and Trends, and fill in your Profile once to get a health check and a PDF report."),
                },
                new[] { "Everything is saved the moment you add or change it - there's no Save button.", "Your data stays on this computer in %APPDATA%\\BudgetApp, with automatic daily backups." },
                PageKey.Programmed, "Open Programmed"),

            new("bank-ing", "Download your transactions from ING", "Get a CSV file from Mijn ING and import it.",
                new[]
                {
                    new GuideStep("Log in on a computer", "Go to ing.nl and log in to Mijn ING in your web browser. The ING app can't make CSV files, so use a computer."),
                    new GuideStep("Open the download page", "Open 'Service' and choose 'Af- en bijschrijvingen downloaden' (download debits and credits). If the menu looks different, search Mijn ING for 'downloaden'."),
                    new GuideStep("Choose your accounts", "Tick your payment account. Also tick your savings account (Oranje Spaarrekening) if you have one: then money you move to savings is recognised as a transfer, not as spending."),
                    new GuideStep("Choose the period", "Pick the start and end date. Overlapping with an earlier download is fine - Budgeter skips rows it already has."),
                    new GuideStep("Choose CSV and download", "Choose 'Kommagescheiden CSV' (comma-separated). Semicolon-separated also works. Click Download; the file goes to your Downloads folder."),
                    new GuideStep("Import it", "In Budgeter, open Import from bank, click 'Choose file…' and pick the file. Don't open and save it in Excel first: Excel can change the dates and amounts."),
                },
                new[] { "ING doesn't put a minus sign on amounts; the 'Af Bij' column says whether money went out or came in. Budgeter reads that column for you." },
                PageKey.Import, "Go to Import", "Bank downloads"),

            new("bank-rabobank", "Download your transactions from Rabobank", "Get a CSV file from Rabo Online Bankieren and import it.",
                new[]
                {
                    new GuideStep("Log in on a computer", "Log in to Rabo Online Bankieren at rabobank.nl in your web browser."),
                    new GuideStep("Open the download page", "Click 'Downloads en documenten', then 'Transacties downloaden'. (In older menus: 'Betalen en sparen' > 'Downloaden transacties'.)"),
                    new GuideStep("Choose CSV", "Choose the CSV format (.csv). Not MT940 or CAMT.053 - those are for accounting software."),
                    new GuideStep("Choose accounts and period", "Select your payment account and savings accounts, and the period you want."),
                    new GuideStep("Download", "Click 'Bestand downloaden'. The file goes to your Downloads folder, with a name like CSV_A_NL..._EUR_....csv."),
                    new GuideStep("Import it", "In Budgeter, open Import from bank and choose the file. Don't open and save it in Excel first."),
                },
                new[] { "Rabobank gives every transaction a unique number, so importing an overlapping period never creates doubles." },
                PageKey.Import, "Go to Import", "Bank downloads"),

            new("bank-abnamro", "Download your transactions from ABN AMRO", "ABN AMRO has no CSV - Budgeter reads its TXT file instead.",
                new[]
                {
                    new GuideStep("Log in on a computer", "Log in to Internet Banking at abnamro.nl and find the page for downloading transactions (search for 'download bij- en afschrijvingen' or go to Payments > Download)."),
                    new GuideStep("Choose the account and period", "Select the account holder and the period. You can go back up to 18 months."),
                    new GuideStep("Mind the weekend", "If the end date is a Saturday or Sunday, weekend transactions are left out. Pick the next working day as end date instead."),
                    new GuideStep("Choose TXT", "As file format choose 'TXT' (tab-separated). Budgeter reads this format; it has no column names, but Budgeter knows the layout."),
                    new GuideStep("Download", "Click the download icon next to the account and save the file. Its name looks like TXT260131120000.TAB."),
                    new GuideStep("Or use the app", "In the ABN AMRO app: Profile > Accounts and payments > Bank statements and annual statement > 'Overview types' > Transactions. Choose account, period and TXT, then Download, and copy the file to your computer."),
                    new GuideStep("Import it", "In Budgeter, open Import from bank and choose the .TAB or .TXT file."),
                },
                new[] { "ABN AMRO files have no separate shop-name column. Budgeter reads the shop or person out of the description (e.g. 'BEA … Albert Heijn 1234,PAS123' becomes 'Albert Heijn 1234')." },
                PageKey.Import, "Go to Import", "Bank downloads"),

            new("bank-other", "Any other bank (bunq, ASN, SNS, Triodos, Knab…)", "Most banks can export a CSV file; Budgeter helps you match the columns.",
                new[]
                {
                    new GuideStep("Find the export", "In your online banking, look for 'export', 'download transactions' or 'afschriften downloaden'. Choose CSV (sometimes called 'Excel/CSV' or 'kommagescheiden')."),
                    new GuideStep("Choose the file in Budgeter", "Open Import from bank, pick 'Other bank' and choose the file. Budgeter guesses which column is which."),
                    new GuideStep("Check the columns", "On step 2, check Date and Amount. The preview underneath shows the first rows as Budgeter reads them - dates and amounts should look right."),
                    new GuideStep("Amounts without a minus?", "Some banks show every amount as positive and add a column like 'Af/Bij', 'Debet/Credit' or 'D/C'. Choose that column as 'In/out column'; the preview then shows outgoing amounts in red."),
                    new GuideStep("Name and description", "Pick the column with the shop or person as 'Payee' (this is what rules match on), and one or two columns as description."),
                    new GuideStep("Continue", "Click Next to review the rows and import them."),
                },
                new[] { "Lines Budgeter can't read are listed with their line number and reason, never silently skipped." },
                PageKey.Import, "Go to Import", "Bank downloads"),

            new("import-review", "Reviewing an import", "What the colours and labels on the review step mean.",
                new[]
                {
                    new GuideStep("New rows are ticked", "Everything that is new is ticked and will be imported. Untick a row to skip it."),
                    new GuideStep("Already imported", "Rows you imported before are recognised and not imported again, so overlapping downloads are safe."),
                    new GuideStep("Possible duplicates", "If you typed an expense by hand with the same amount around the same date, the bank row is shown as 'Looks like your entry…' and is NOT ticked. Tick it only if it's really a different payment."),
                    new GuideStep("Categories are filled in for you", "Your own rules come first, then a built-in list of Dutch shops (Albert Heijn, NS, Ziggo…). The 'Why' column says which rule was used."),
                    new GuideStep("Change a category once, the rest follows", "Change the category of one row and every other row with the same payee gets it too. Budgeter also remembers it as a rule for next time."),
                    new GuideStep("Transfers", "Money moved between your own accounts (e.g. to savings) is set to Transfer, which doesn't count as income or spending."),
                    new GuideStep("Programmed payments", "Payments that match a programmed item (same name and amount, e.g. Netflix) are linked to it, so they aren't counted twice."),
                    new GuideStep("Import and undo", "Click Import. Changed your mind? The last step and the import history both have 'Undo import', which removes exactly the rows of that import."),
                },
                new[] { "Use 'Only rows that need attention' to hide everything that's already sorted." },
                PageKey.Import, "Go to Import", "Bank imports"),

            new("rules", "Automatic categories (rules)", "Teach Budgeter where your money goes once.",
                new[]
                {
                    new GuideStep("What a rule is", "A rule says: 'if the payee contains Albert Heijn, the category is Groceries'. Rules are used for every import."),
                    new GuideStep("Rules are made while you import", "Change a category on the review step and Budgeter makes a rule from the payee. Nothing else to do."),
                    new GuideStep("Manage your rules", "On Import from bank, open 'Your rules' to see them all. Remove one with the bin icon, or add one by hand (text to look for + category)."),
                    new GuideStep("Built-in shops", "Budgeter knows many Dutch shops and companies. Your own rules always win. Turn the built-in list off in Settings if you prefer to start empty."),
                    new GuideStep("Which rule wins", "When several rules match, the longest text wins - so 'albert heijn to go' beats 'albert heijn'."),
                },
                new[] { "Rules match at the start of a word, so 'esso' matches 'Esso Maastricht' but not 'Espresso bar'." },
                PageKey.Import, "Go to Import", "Bank imports"),

            new("double-counting", "Programmed items and bank imports", "How Budgeter avoids counting rent or your salary twice.",
                new[]
                {
                    new GuideStep("The situation", "Rent is a programmed cost, so it counts every month. Your bank file also contains the rent payment. Counting both would double your rent."),
                    new GuideStep("Automatic linking", "When the payee or description contains the programmed item's name and the amount matches (within about 3%), the bank row is linked to the programmed item. Linked rows are shown in grey with 'Programmed' and don't count."),
                    new GuideStep("Give programmed items bank-like names", "Name programmed items like the bank shows them ('Netflix', 'Ziggo', 'DUO'), so linking works. For rent, the description often says 'huur' - a programmed item called 'Huur' then matches."),
                    new GuideStep("Link by hand", "On the review step, use the 'Programmed' column. For rows imported earlier: open the transaction (double-click) and choose it under 'Payment of a programmed item?'."),
                },
                new[] { "If you import everything from your bank anyway, you can also skip programmed costs entirely and let the imported rows count. Pick one approach per item." },
                PageKey.Programmed, "Open Programmed", "Bank imports"),

            new("budgets", "Budgets and savings goals", "Limits per category, and saving towards something.",
                new[]
                {
                    new GuideStep("Start from reality", "Click 'Suggest from my spending': Budgeter fills in a budget for each category, based on your average of the last 3 complete months, rounded up."),
                    new GuideStep("Adjust", "Click the pencil next to a budget to change it. Lower the categories you want to spend less on."),
                    new GuideStep("Follow along", "Bars turn orange above 85% and red when you go over. The Dashboard shows the same bars for the month you're viewing."),
                    new GuideStep("Add a goal", "Enter what it's for, the target, what you already saved and (optionally) a date. With a date, Budgeter tells you how much to save per month."),
                    new GuideStep("Add money as you go", "Click 'Add money' on the goal whenever you put money aside."),
                },
                new[] { "A good first goal is a buffer of one month of costs; the usual guideline is 3 to 6 months." },
                PageKey.Budgets, "Open Budgets & goals", "Planning"),

            new("report", "Your financial profile and PDF report", "A health check of your finances, as a PDF you can keep.",
                new[]
                {
                    new GuideStep("Fill in 'About you'", "On Profile & report: name, birth year, household, housing, work and your goals. This only adds context to the report."),
                    new GuideStep("Add what you own and owe", "Add your accounts (current, savings), investments, and debts (student loan, credit card, loans) with their balance. For debts, also the interest rate and monthly payment."),
                    new GuideStep("Read the health check", "Budgeter compares your numbers with common guidelines: savings rate, emergency buffer (3-6 months of costs), expensive debt, 50/30/20 split, budgets, subscriptions and goals. Every item shows the numbers it's based on."),
                    new GuideStep("Choose the period", "The report looks at your last 3, 6 or 12 complete months. Choose a longer period if your income varies."),
                    new GuideStep("Create the PDF", "Click 'Create PDF report' and choose where to save it. It opens automatically."),
                },
                new[] { "The health check is general information based on simple rules, not personal financial advice. For big decisions (a mortgage, investing) talk to a qualified adviser.", "Update your balances every few months so the net-worth part stays right." },
                PageKey.Profile, "Open Profile & report", "Planning"),

            new("backups", "Backups and restoring", "Your data is copied automatically; here's how to get it back.",
                new[]
                {
                    new GuideStep("Automatic backups", "Every day you use Budgeter, it first copies your data to %APPDATA%\\BudgetApp\\Backups. The 20 newest daily copies are kept, plus a copy before every update of the data format."),
                    new GuideStep("Back up now", "Settings > Backups > 'Back up now' makes an extra copy right away, e.g. before a big import."),
                    new GuideStep("Keep a copy elsewhere", "Click 'Open backups folder' and copy a recent file to a USB stick or cloud folder now and then."),
                    new GuideStep("Restore", "Settings > Backups > 'Restore from backup…', pick a file, confirm. Your current data is backed up first, then Budgeter restarts with the restored data."),
                },
                new[] { "A backup is a complete copy of your data in one .db file." },
                PageKey.Settings, "Open Settings", "Your data"),

            new("shortcut", "Updating Budgeter and fixing your shortcut", "Keep one BudgetApp.exe in one place, and let it update itself.",
                new[]
                {
                    new GuideStep("Why", "A shortcut to a Visual Studio folder like ...\\bin\\Debug\\net8.0-windows starts a frozen test build. It never updates, and after a data upgrade it can show wrong numbers. Your data is fine either way: every copy reads the same %APPDATA%\\BudgetApp folder."),
                    new GuideStep("Download the latest version", "Settings > Updates > 'Open download page', and download BudgetApp.exe from the newest release. If Windows SmartScreen warns that the app is unknown, click 'More info' > 'Run anyway' (the app isn't code-signed)."),
                    new GuideStep("Give it a permanent home", "Make the folder C:\\Git Projects\\Budgeter\\publish and move BudgetApp.exe into it. Git ignores this folder, so it is never uploaded or overwritten. Don't use Program Files: Budgeter can't update itself there."),
                    new GuideStep("Remove the old shortcut", "Right-click the old shortcut on your desktop > Delete. If it's pinned to the taskbar: right-click > 'Unpin from taskbar'. This only removes the shortcut, not your data."),
                    new GuideStep("Make the new shortcut", "Right-click the new BudgetApp.exe > 'Show more options' > 'Send to' > 'Desktop (create shortcut)'. To pin it: start Budgeter, right-click its icon in the taskbar > 'Pin to taskbar'."),
                    new GuideStep("Check", "Start Budgeter from the new shortcut. The sidebar no longer shows 'Old copy of Budgeter' and your data is all there."),
                    new GuideStep("From now on", "When a new version is out, the sidebar shows 'Install update'. Click it: Budgeter downloads the new version, replaces itself in the same place and restarts. The shortcut and taskbar pin keep working."),
                },
                new[]
                {
                    "Prefer building it yourself? In C:\\Git Projects\\Budgeter run 'git pull', then double-click build_exe.bat. It replaces publish\\BudgetApp.exe in place.",
                    "Running from Visual Studio (F5) is still fine for trying out changes - just don't use that build as your everyday shortcut."
                },
                PageKey.Settings, "Open Settings > Updates", "Your data"),

            new("export", "Exporting to Excel", "Get your numbers into a spreadsheet.",
                new[]
                {
                    new GuideStep("Pick the period", "On the Dashboard or Transactions page, choose the month (or All time) with the arrows."),
                    new GuideStep("Export", "Click 'Export to CSV' and save the file."),
                    new GuideStep("Open in Excel", "Double-click the file. Budgeter uses your Windows separators (';' and decimal comma on a Dutch system), so it opens as proper columns."),
                },
                new[] { "Transfers and payments of programmed items are in the export with a note, but not in the totals." },
                PageKey.Transactions, "Open Transactions", "Your data"),
        };

        public static Guide? Find(string id) => All.FirstOrDefault(g => g.Id == id);
    }
}
