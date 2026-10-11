using System.Text;
using BudgetApp.Import;
using BudgetApp.Models;
using static T;

public static class ImportTests
{
    private static (DetectedFormat Fmt, List<ImportRow> Rows) Read(string text)
    {
        var table = CsvTable.Parse(text);
        var fmt = BankFormatDetector.Detect(table);
        return (fmt, ImportParser.Parse(table, fmt.Mapping));
    }

    public static void Run()
    {
        // ---------------- CSV reading ----------------
        Test("csv: quoted fields with separators, quotes and newlines", () =>
        {
            var t = CsvTable.Parse("a,b,c\n\"x, y\",\"say \"\"hi\"\"\",\"line1\nline2\"\n");
            Eq(',', t.Delimiter);
            Eq(2, t.Rows.Count);
            Eq("x, y", t.Rows[1][0]);
            Eq("say \"hi\"", t.Rows[1][1]);
            Eq("line1\nline2", t.Rows[1][2]);
        });

        Test("csv: semicolon and tab detection", () =>
        {
            Eq(';', CsvTable.Parse("a;b;c\n1,5;2;3\n4;5;6\n").Delimiter);
            Eq('\t', CsvTable.Parse("a\tb\tc,d\n1\t2\t3\n").Delimiter);
        });

        Test("csv: UTF-8 with BOM and Windows-1252 bytes both decode", () =>
        {
            var utf8 = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("Café;€ 5")).ToArray();
            Eq("Café;€ 5", CsvTable.Decode(utf8));
            var cp1252 = new byte[] { (byte)'C', (byte)'a', (byte)'f', 0xE9, (byte)';', 0x80 };   // é and € in Windows-1252
            var s = CsvTable.Decode(cp1252);
            True(s.StartsWith("Caf") && s.Contains('é'), $"got '{s}'");
        });

        // ---------------- ING ----------------
        Test("ING: detected and read (Af/Bij, yyyyMMdd, comma decimals)", () =>
        {
            var (fmt, rows) = Read(ImportSamples.Ing);
            Eq(BankFormat.Ing, fmt.Format);
            Eq(6, rows.Count);
            True(rows.All(r => r.Error == null), "no errors: " + rows.FirstOrDefault(r => r.Error != null)?.Error);
            Eq(new DateTime(2026, 10, 1), rows[0].Date);
            Eq(-23.45m, rows[0].Amount, "Af = out");
            Eq("Albert Heijn 1234", rows[0].Payee);
            Eq(2150m, rows[1].Amount, "Bij = in");
            Eq("NL22RABO0123456789", rows[1].CounterAccount);
            Eq("NL11INGB0001234567", rows[1].OwnAccount);
        });

        Test("ING: English headers with semicolons", () =>
        {
            var (fmt, rows) = Read(ImportSamples.IngSemicolonEnglish);
            Eq(BankFormat.Ing, fmt.Format);
            Eq(-41.20m, rows[0].Amount);
            Eq(15m, rows[1].Amount);
        });

        // ---------------- Rabobank ----------------
        Test("Rabobank: detected and read (signed amounts, yyyy-MM-dd, accents)", () =>
        {
            var (fmt, rows) = Read(ImportSamples.Rabobank);
            Eq(BankFormat.Rabobank, fmt.Format);
            Eq(3, rows.Count);
            Eq(442.98m, rows[0].Amount);
            Eq("Café Één", rows[0].Payee);
            Eq(-1435m, rows[2].Amount);
            Eq("Huur september Woning 12", rows[2].Description);
            Eq("000000000000006409", rows[1].ExternalId);
        });

        Test("Rabobank: Windows-1252 file still reads accents", () =>
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var bytes = Encoding.GetEncoding(1252).GetBytes(ImportSamples.Rabobank);
            var table = CsvTable.FromBytes(bytes);
            var rows = ImportParser.Parse(table, BankFormatDetector.Detect(table).Mapping);
            Eq("Café Één", rows[0].Payee);
        });

        // ---------------- ABN AMRO ----------------
        Test("ABN AMRO TXT: detected, read, payee taken from description", () =>
        {
            var (fmt, rows) = Read(ImportSamples.AbnAmro);
            Eq(BankFormat.AbnAmro, fmt.Format);
            Eq(4, rows.Count);
            Eq(-25m, rows[0].Amount);
            Eq(new DateTime(2026, 10, 12), rows[0].Date);
            Eq("Hema EV123", rows[0].Payee);
            Eq("Spotify AB", rows[2].Payee);
            Eq("J de Boer", rows[3].Payee);
        });

        // ---------------- Other banks ----------------
        Test("generic: bunq-style CSV with headers is guessed", () =>
        {
            var text = "\"Date\";\"Interest Date\";\"Amount\";\"Account\";\"Counterparty\";\"Name\";\"Description\"\n" +
                       "\"2026-10-01\";\"2026-10-01\";\"-12,50\";\"NL01BUNQ0000000001\";\"NL02INGB0000000002\";\"Jumbo\";\"Boodschappen\"\n" +
                       "\"2026-10-02\";\"2026-10-02\";\"1.200,00\";\"NL01BUNQ0000000001\";\"NL03RABO0000000003\";\"Werkgever\";\"Salaris\"\n";
            var (fmt, rows) = Read(text);
            Eq(BankFormat.Generic, fmt.Format);
            Eq(0, fmt.Mapping.Date, "date column");
            Eq(2, fmt.Mapping.Amount, "amount column");
            Eq(5, fmt.Mapping.Payee, "name column");
            Eq(-12.5m, rows[0].Amount);
            Eq("Jumbo", rows[0].Payee);
            Eq(1200m, rows[1].Amount);
        });

        Test("generic: headerless Triodos-style file with Debet/Credit column", () =>
        {
            var text = "01-10-2026,NL01TRIO0000000001,\"12,50\",Debet,Bakker Bart,NL02INGB0000000002,BA,Brood\n" +
                       "02-10-2026,NL01TRIO0000000001,\"100,00\",Credit,Oma,NL03RABO0000000003,OV,Cadeau\n";
            var table = CsvTable.Parse(text);
            var m = BankFormatDetector.GuessMapping(table);
            True(!m.HasHeader, "no header");
            Eq(0, m.Date);
            Eq("dd-MM-yyyy", m.DateFormat);
            m.DebitCredit = 3;   // the user picks this in the mapping step
            m.Payee = 4;
            var rows = ImportParser.Parse(table, m);
            Eq(-12.5m, rows[0].Amount, "Debet = out");
            Eq(100m, rows[1].Amount, "Credit = in");
            Eq(new DateTime(2026, 10, 2), rows[1].Date);
        });

        Test("unreadable lines are reported, not silently dropped", () =>
        {
            var text = "Datum;Bedrag;Naam\n2026-10-01;-5,00;A\nnot a date;-5,00;B\n2026-10-03;abc;C\n";
            var (fmt, rows) = Read(text);
            Eq(3, rows.Count);
            True(rows[1].Error!.Contains("date"), rows[1].Error!);
            True(rows[2].Error!.Contains("amount"), rows[2].Error!);
            Eq(3, rows[1].RowNumber, "line number in file");
        });

        Test("date formats are detected", () =>
        {
            Eq("yyyyMMdd", BankFormatDetector.DetectDateFormat(new[] { "20261001", "20261231" }));
            Eq("dd-MM-yyyy", BankFormatDetector.DetectDateFormat(new[] { "01-10-2026", "31-12-2026" }));
            Eq("dd/MM/yyyy", BankFormatDetector.DetectDateFormat(new[] { "13/10/2026", "01/02/2026" }));
            Eq("yyyy-MM-dd", BankFormatDetector.DetectDateFormat(new[] { "2026-10-01 12:00:00" }));
        });

        // ---------------- Payees and rules ----------------
        Test("payee extraction from bank descriptions", () =>
        {
            Eq("Albert Heijn 1234", PayeeExtractor.FromDescription("BEA, Betaalpas Albert Heijn 1234,PAS123 NR:ABC, 01.02.24/12:34 MAASTRICHT"));
            Eq("Netflix", PayeeExtractor.FromDescription("/TRTP/SEPA Incasso/NAME/Netflix/REMI/abc/"));
            Eq("Huisbaas", PayeeExtractor.FromDescription("SEPA Overboeking IBAN: NL01 Naam: Huisbaas Omschrijving: huur"));
        });

        Test("rule pattern from payee drops branch numbers", () =>
        {
            Eq("Albert Heijn", PayeeExtractor.ToPattern("Albert Heijn 1234 MAASTRICHT"));
            Eq("Netflix International B.V", PayeeExtractor.ToPattern("Netflix International B.V."));
        });

        Test("rules: word-start matching, longest pattern wins, user rules first", () =>
        {
            Eq("Transport", RuleEngine.Match(Array.Empty<CategoryRule>(), true, "Esso Maastricht", "")!.Category);
            Eq(null, RuleEngine.Match(Array.Empty<CategoryRule>(), true, "Espresso Bar Centraal", "")?.Category);
            Eq("Eating out", RuleEngine.Match(Array.Empty<CategoryRule>(), true, "Uber Eats", "")!.Category);
            var mine = new[] { new CategoryRule { Id = 5, Pattern = "albert heijn", Category = "Food" } };
            Eq("Food", RuleEngine.Match(mine, true, "Albert Heijn 1234", "")!.Category);
            Eq(null, RuleEngine.Match(Array.Empty<CategoryRule>(), false, "Albert Heijn 1234", ""), "built-in off");
        });

        // ---------------- Planning an import ----------------
        Test("plan: types, rules, transfer to own savings, programmed match", () =>
        {
            var (fmt, rows) = Read(ImportSamples.Ing);
            var recurring = new[]
            {
                Rec("Netflix", "Subscriptions", 13.99m, RecurringFrequency.Monthly, "2026-01-01", id: 7),
                Rec("Huur", "Housing", 650m, RecurringFrequency.Monthly, "2026-01-01", id: 8),
            };
            var plan = ImportPlanner.Plan(rows, fmt.Format, new List<Transaction>(), recurring, new List<CategoryRule>());
            var ah = plan[0];
            Eq(TransactionType.Expense, ah.Type); Eq("Groceries", ah.Category); True(ah.Include, "include");
            Eq(TransactionType.Income, plan[1].Type);
            Eq(BudgetApp.Services.BudgetCalculator.Uncategorized, plan[1].Category, "salary unknown");
            Eq(7, plan[2].RecurringId, "netflix linked to programmed item");
            Eq(TransactionType.Transfer, plan[3].Type, "Oranje Spaarrekening is a transfer");
            Eq(8, plan[4].RecurringId, "rent linked via 'Huur' in description");
            Eq(BudgetApp.Services.BudgetCalculator.Uncategorized, plan[5].Category, "espresso not 'Transport'");
        });

        Test("plan: re-importing the same file marks everything as already imported", () =>
        {
            var (fmt, rows) = Read(ImportSamples.Ing);
            var first = ImportPlanner.Plan(rows, fmt.Format, new List<Transaction>(), Array.Empty<RecurringItem>(), new List<CategoryRule>());
            var saved = ImportPlanner.ToTransactions(first, 1);
            Eq(6, saved.Count);
            var again = ImportPlanner.Plan(rows, fmt.Format, saved, Array.Empty<RecurringItem>(), new List<CategoryRule>());
            True(again.All(c => c.Status == CandidateStatus.AlreadyImported && !c.Include), "all duplicates");
        });

        Test("plan: identical rows in one file are both kept", () =>
        {
            var text = "Datum;Bedrag;Naam\n2026-10-01;-2,50;Koffie\n2026-10-01;-2,50;Koffie\n";
            var (fmt, rows) = Read(text);
            var plan = ImportPlanner.Plan(rows, fmt.Format, new List<Transaction>(), Array.Empty<RecurringItem>(), new List<CategoryRule>());
            Eq(2, plan.Count(c => c.Include));
            True(plan[0].ImportKey != plan[1].ImportKey, "distinct keys");
        });

        Test("plan: a manual entry with the same amount and date is flagged, not double-imported", () =>
        {
            var (fmt, rows) = Read(ImportSamples.Ing);
            var manual = new List<Transaction> { Tx("2026-10-02", TransactionType.Expense, "Groceries", 23.45m) };
            var plan = ImportPlanner.Plan(rows, fmt.Format, manual, Array.Empty<RecurringItem>(), new List<CategoryRule>());
            Eq(CandidateStatus.PossibleDuplicate, plan[0].Status);
            True(!plan[0].Include, "off by default");
            True(plan[0].CanInclude, "user can still tick it");
        });

        Test("plan: transfer detection via own accounts in the same file", () =>
        {
            var text = "Datum;Bedrag;Naam;Rekening;Tegenrekening\n2026-10-01;-100,00;Spaar;NL01;NL02\n2026-10-01;100,00;Betaal;NL02;NL01\n";
            var table = CsvTable.Parse(text);
            var m = BankFormatDetector.GuessMapping(table);
            m.OwnAccount = 3; m.CounterAccount = 4;
            var rows = ImportParser.Parse(table, m);
            var plan = ImportPlanner.Plan(rows, BankFormat.Generic, new List<Transaction>(), Array.Empty<RecurringItem>(), new List<CategoryRule>());
            True(plan.All(c => c.Type == TransactionType.Transfer), "both legs are transfers");
        });

        Test("plan: changing one row's category updates same-payee rows and creates a rule", () =>
        {
            var text = "Datum;Bedrag;Naam\n2026-10-01;-4,00;Bakkerij Bart 12\n2026-10-08;-6,00;Bakkerij Bart 12\n2026-10-09;-6,00;Slager\n";
            var (fmt, rows) = Read(text);
            var plan = ImportPlanner.Plan(rows, fmt.Format, new List<Transaction>(), Array.Empty<RecurringItem>(), new List<CategoryRule>());
            plan[0].Category = "Groceries";
            Eq(1, ImportPlanner.PropagateCategory(plan, plan[0]));
            Eq("Groceries", plan[1].Category);
            Eq(BudgetApp.Services.BudgetCalculator.Uncategorized, plan[2].Category);
            var rules = ImportPlanner.NewRules(plan, Array.Empty<CategoryRule>());
            Eq(1, rules.Count);
            Eq("Bakkerij Bart", rules[0].Pattern);
        });

        Test("plan: unreadable rows can't be included", () =>
        {
            var (fmt, rows) = Read("Datum;Bedrag;Naam\nxx;-5,00;A\n");
            var plan = ImportPlanner.Plan(rows, fmt.Format, new List<Transaction>(), Array.Empty<RecurringItem>(), new List<CategoryRule>());
            Eq(CandidateStatus.Unreadable, plan[0].Status);
            plan[0].Include = true;
            Eq(0, ImportPlanner.ToTransactions(plan, 1).Count);
        });
    }
}
