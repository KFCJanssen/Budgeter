using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Import
{
    public enum CandidateStatus
    {
        New,
        AlreadyImported,
        PossibleDuplicate,
        Unreadable,
        ZeroAmount
    }

    /// <summary>
    /// One bank row as it will be imported, with the choices the user can still change in the
    /// review step (include it or not, category, type, link to a programmed item, remember as rule).
    /// </summary>
    public sealed class ImportCandidate : INotifyPropertyChanged
    {
        public ImportRow Row { get; }
        public CandidateStatus Status { get; }
        public string ImportKey { get; }
        /// <summary>For possible duplicates: the manual entry this row looks like.</summary>
        public Transaction? LooksLike { get; }

        public ImportCandidate(ImportRow row, CandidateStatus status, string importKey, Transaction? looksLike = null)
        {
            Row = row;
            Status = status;
            ImportKey = importKey;
            LooksLike = looksLike;
            _include = status == CandidateStatus.New;
        }

        private bool _include;
        public bool Include { get => _include; set => Set(ref _include, value); }

        private string _category = BudgetCalculator.Uncategorized;
        public string Category
        {
            get => _category;
            set { if (Set(ref _category, value?.Trim() ?? "")) { OnPropertyChanged(nameof(IsUncategorized)); Explain(); } }
        }

        private TransactionType _type;
        public TransactionType Type
        {
            get => _type;
            set { if (Set(ref _type, value)) { OnPropertyChanged(nameof(TypeLabel)); Explain(); } }
        }

        private int? _recurringId;
        public int? RecurringId
        {
            get => _recurringId;
            set { if (Set(ref _recurringId, value)) { OnPropertyChanged(nameof(RecurringName)); Explain(); } }
        }

        private string? _recurringName;
        public string? RecurringName { get => _recurringId == null ? null : _recurringName; set => Set(ref _recurringName, value); }

        /// <summary>Create a rule from this row's payee when importing.</summary>
        private bool _remember;
        public bool Remember { get => _remember; set => Set(ref _remember, value); }

        /// <summary>Set once the user picked the category by hand, so auto-filling won't overwrite it.</summary>
        private bool _userEdited;
        public bool UserEdited { get => _userEdited; set { _userEdited = value; Explain(); } }

        /// <summary>The rule that chose the category, for display ("rule: albert heijn").</summary>
        private string? _ruleSource;
        public string? RuleSource { get => _ruleSource; set { if (Set(ref _ruleSource, value)) Explain(); } }

        /// <summary>One line explaining the row's status or where its category came from.</summary>
        public string Why => Status switch
        {
            CandidateStatus.New when Type == TransactionType.Transfer => $"Transfer, not counted ({RuleSource ?? "set by you"})",
            CandidateStatus.New when RecurringId != null => $"Paid by programmed item \"{RecurringName}\" - not counted twice",
            CandidateStatus.New when IsUncategorized => "Needs a category",
            CandidateStatus.New when UserEdited => "Category set by you",
            CandidateStatus.New => RuleSource != null ? $"From {RuleSource}" : "Category set by you",
            _ => StatusText
        };

        /// <summary>For colouring: 0 = fine, 1 = needs a category, 2 = can't be read, 3 = skipped (duplicate).</summary>
        public int Attention => Status switch
        {
            CandidateStatus.Unreadable => 2,
            CandidateStatus.AlreadyImported or CandidateStatus.ZeroAmount => 3,
            CandidateStatus.PossibleDuplicate => 1,
            _ => IsUncategorized && Type != TransactionType.Transfer ? 1 : 0
        };

        /// <summary>Rows worth a look: not readable, possible duplicates, or still without a category.</summary>
        public bool NeedsAttention => Attention is 1 or 2;

        private void Explain()
        {
            OnPropertyChanged(nameof(Why));
            OnPropertyChanged(nameof(Attention));
        }

        public bool IsUncategorized => string.Equals(Category, BudgetCalculator.Uncategorized, StringComparison.OrdinalIgnoreCase) || Category.Length == 0;

        public DateTime Date => Row.Date;
        public decimal Amount => Math.Abs(Row.Amount);
        public decimal SignedAmount => Row.Amount;
        public string Payee => Row.Payee;
        public string Description => Row.Description;
        public string TypeLabel => Type switch { TransactionType.Income => "In", TransactionType.Expense => "Out", _ => "Transfer" };

        public string StatusText => Status switch
        {
            CandidateStatus.New => "New",
            CandidateStatus.AlreadyImported => "Already imported",
            CandidateStatus.PossibleDuplicate => LooksLike != null
                ? $"Looks like your entry of {LooksLike.Date:dd-MM} ({LooksLike.Category})"
                : "Possible duplicate",
            CandidateStatus.Unreadable => $"Line {Row.RowNumber}: {Row.Error}",
            CandidateStatus.ZeroAmount => "Zero amount",
            _ => ""
        };

        public bool CanInclude => Status is CandidateStatus.New or CandidateStatus.PossibleDuplicate;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }

    public sealed record ImportOptions(bool UseBuiltInRules = true, IReadOnlyCollection<string>? MyAccounts = null);

    /// <summary>
    /// Turns parsed bank rows into import candidates: decides income/expense/transfer, picks a category
    /// from your rules, links payments of programmed items (so they don't count twice), and spots rows
    /// that were imported before or that you already entered by hand.
    /// </summary>
    public static class ImportPlanner
    {
        public static List<ImportCandidate> Plan(
            IReadOnlyList<ImportRow> rows,
            BankFormat format,
            IEnumerable<Transaction> existing,
            IEnumerable<RecurringItem> recurring,
            IEnumerable<CategoryRule> rules,
            ImportOptions? options = null)
        {
            options ??= new ImportOptions();
            var existingList = existing.ToList();
            var existingKeys = new HashSet<string>(existingList.Where(t => t.ImportKey != null).Select(t => t.ImportKey!));
            var manual = existingList.Where(t => t.ImportKey == null).ToList();
            var claimedManual = new HashSet<Transaction>();
            var recurringList = recurring.Where(r => r.IsActive).ToList();
            var ruleList = rules.ToList();

            // Accounts that belong to the user: the "own account" column of this file plus saved ones.
            var own = new HashSet<string>(rows.Select(r => r.OwnAccount).Where(a => a.Length > 0), StringComparer.OrdinalIgnoreCase);
            foreach (var a in options.MyAccounts ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(a)) own.Add(ImportParser.NormalizeAccount(a));

            var keys = MakeKeys(rows, format);
            var result = new List<ImportCandidate>();

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var key = keys[i];

                if (row.Error != null)
                {
                    result.Add(new ImportCandidate(row, CandidateStatus.Unreadable, key));
                    continue;
                }
                if (row.Amount == 0)
                {
                    result.Add(new ImportCandidate(row, CandidateStatus.ZeroAmount, key));
                    continue;
                }

                var type = row.Amount < 0 ? TransactionType.Expense : TransactionType.Income;
                CandidateStatus status = CandidateStatus.New;
                Transaction? looksLike = null;

                if (existingKeys.Contains(key))
                {
                    status = CandidateStatus.AlreadyImported;
                }
                else
                {
                    var amount = Math.Abs(row.Amount);
                    looksLike = manual.FirstOrDefault(t =>
                        !claimedManual.Contains(t) && t.Type == type && t.Amount == amount && Math.Abs((t.Date - row.Date).TotalDays) <= 3);
                    if (looksLike != null)
                    {
                        status = CandidateStatus.PossibleDuplicate;
                        claimedManual.Add(looksLike);
                    }
                }

                var c = new ImportCandidate(row, status, key, looksLike) { Type = type };

                if (row.CounterAccount.Length > 0 && own.Contains(row.CounterAccount))
                {
                    c.Type = TransactionType.Transfer;
                    c.Category = "Transfer";
                    c.RuleSource = "between your own accounts";
                }
                else
                {
                    var rule = RuleEngine.Match(ruleList, options.UseBuiltInRules, row.Payee, row.Description);
                    if (rule != null)
                    {
                        c.Category = rule.Category;
                        if (rule.SetType is TransactionType t) c.Type = t;
                        c.RuleSource = rule.Id > 0 ? $"your rule \"{rule.Pattern}\"" : $"built-in: {rule.Pattern}";
                    }

                    if (c.Type != TransactionType.Transfer)
                    {
                        var r = MatchRecurring(recurringList, c.Type, Math.Abs(row.Amount), row.Payee, row.Description);
                        if (r != null)
                        {
                            c.RecurringName = r.Name;
                            c.RecurringId = r.Id;
                            c.Category = r.Category;
                            c.RuleSource = $"programmed: {r.Name}";
                        }
                    }
                }

                result.Add(c);
            }

            return result;
        }

        /// <summary>
        /// A programmed item matches when the amount is (almost) the same and the item's name
        /// appears in the payee or description, e.g. "Netflix" in "NETFLIX.COM AMSTERDAM".
        /// </summary>
        public static RecurringItem? MatchRecurring(IEnumerable<RecurringItem> items, TransactionType type, decimal amount, string payee, string description)
        {
            var text = ((payee ?? "") + " " + (description ?? "")).ToLowerInvariant();
            foreach (var r in items)
            {
                if (r.Type != type || !r.IsActive) continue;
                var tolerance = Math.Max(0.50m, r.Amount * 0.03m);
                if (Math.Abs(amount - r.Amount) > tolerance) continue;
                var words = r.Name.ToLowerInvariant()
                    .Split(new[] { ' ', '-', '/', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length >= 3)
                    .ToList();
                if (words.Count > 0 && words.Any(w => RuleEngine.ContainsWordStart(text, w))) return r;
            }
            return null;
        }

        /// <summary>
        /// Fingerprints that stay the same if you import an overlapping file later. Rabobank rows have a
        /// unique number; for other banks the date, amount, accounts, text and balance are combined.
        /// Identical rows within one file get #2, #3… so genuinely repeated payments are kept.
        /// </summary>
        public static List<string> MakeKeys(IReadOnlyList<ImportRow> rows, BankFormat format)
        {
            var seen = new Dictionary<string, int>();
            var keys = new List<string>(rows.Count);
            foreach (var r in rows)
            {
                string key;
                if (r.ExternalId.Length > 0)
                {
                    key = $"{format}:{r.OwnAccount}:{r.ExternalId}";
                }
                else
                {
                    var raw = string.Join("|", r.Date.ToString("yyyyMMdd"), r.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.OwnAccount, r.CounterAccount, r.Payee.ToLowerInvariant(), r.Description.ToLowerInvariant(), r.Balance);
                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..24];
                    key = $"h1:{hash}";
                }
                int n = seen.GetValueOrDefault(key) + 1;
                seen[key] = n;
                keys.Add(n == 1 ? key : $"{key}#{n}");
            }
            return keys;
        }

        /// <summary>
        /// After the user picks a category for one row, give the same category to other rows with the
        /// same payee that they haven't touched, and remember it as a rule.
        /// Returns how many other rows changed.
        /// </summary>
        public static int PropagateCategory(IEnumerable<ImportCandidate> all, ImportCandidate source)
        {
            source.UserEdited = true;
            var pattern = PayeeExtractor.ToPattern(source.Payee);
            if (pattern.Length < 3) return 0;
            source.Remember = true;
            int changed = 0;
            foreach (var c in all)
            {
                if (ReferenceEquals(c, source) || c.UserEdited || c.RecurringId != null) continue;
                if (!RuleEngine.ContainsWordStart(c.Payee.ToLowerInvariant(), pattern.ToLowerInvariant())) continue;
                if (c.Category != source.Category)
                {
                    c.Category = source.Category;
                    c.RuleSource = "same payee";
                    changed++;
                }
                if (source.Type == TransactionType.Transfer) c.Type = TransactionType.Transfer;
            }
            return changed;
        }

        public static List<Transaction> ToTransactions(IEnumerable<ImportCandidate> candidates, int? batchId)
        {
            return candidates
                .Where(c => c.Include && c.CanInclude)
                .Select(c => new Transaction
                {
                    Date = c.Date.Date,
                    Type = c.Type,
                    Category = string.IsNullOrWhiteSpace(c.Category) ? BudgetCalculator.Uncategorized : c.Category,
                    Amount = c.Amount,
                    Payee = string.IsNullOrWhiteSpace(c.Payee) ? null : c.Payee,
                    Description = c.Description,
                    CounterAccount = string.IsNullOrWhiteSpace(c.Row.CounterAccount) ? null : c.Row.CounterAccount,
                    ImportKey = c.ImportKey,
                    ImportBatchId = batchId,
                    RecurringId = c.RecurringId
                })
                .ToList();
        }

        /// <summary>New rules for rows marked "remember", skipping ones that already exist.</summary>
        public static List<CategoryRule> NewRules(IEnumerable<ImportCandidate> candidates, IEnumerable<CategoryRule> existing)
        {
            var have = new HashSet<string>(existing.Select(r => r.Pattern.Trim().ToLowerInvariant()));
            var rules = new List<CategoryRule>();
            foreach (var c in candidates.Where(c => c.Remember && c.Include && !c.IsUncategorized))
            {
                var pattern = PayeeExtractor.ToPattern(c.Payee);
                if (pattern.Length < 3 || !have.Add(pattern.ToLowerInvariant())) continue;
                rules.Add(new CategoryRule
                {
                    Pattern = pattern,
                    Field = RuleField.Payee,
                    Category = c.Category,
                    SetType = c.Type == TransactionType.Transfer ? TransactionType.Transfer : null
                });
            }
            return rules;
        }
    }
}
