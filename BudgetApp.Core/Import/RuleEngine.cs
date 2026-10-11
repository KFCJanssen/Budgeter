using System;
using System.Collections.Generic;
using System.Linq;
using BudgetApp.Models;

namespace BudgetApp.Import
{
    /// <summary>
    /// Picks a category for a bank row. Your own rules always win; after that, a built-in list of
    /// well-known Dutch shops and companies (can be switched off in Settings).
    /// When several rules match, the longest pattern wins ("albert heijn to go" beats "albert heijn").
    /// </summary>
    public static class RuleEngine
    {
        public static CategoryRule? Match(IEnumerable<CategoryRule> userRules, bool useBuiltIn, string? payee, string? description)
        {
            var user = BestMatch(userRules, payee, description);
            if (user != null) return user;
            return useBuiltIn ? BestMatch(BuiltInRules, payee, description) : null;
        }

        public static CategoryRule? BestMatch(IEnumerable<CategoryRule> rules, string? payee, string? description)
        {
            var p = (payee ?? "").ToLowerInvariant();
            var d = (description ?? "").ToLowerInvariant();
            CategoryRule? best = null;
            int bestLength = 0;
            foreach (var r in rules)
            {
                var pattern = (r.Pattern ?? "").Trim().ToLowerInvariant();
                if (pattern.Length == 0) continue;
                bool hit = r.Field switch
                {
                    RuleField.Payee => ContainsWordStart(p, pattern),
                    RuleField.Description => ContainsWordStart(d, pattern),
                    _ => ContainsWordStart(p, pattern) || ContainsWordStart(d, pattern)
                };
                if (hit && pattern.Length > bestLength) { best = r; bestLength = pattern.Length; }
            }
            return best;
        }

        /// <summary>
        /// True when <paramref name="pattern"/> occurs at the start of a word, so "esso" matches
        /// "Esso Maastricht" but not "Espresso bar".
        /// </summary>
        internal static bool ContainsWordStart(string text, string pattern)
        {
            int i = 0;
            while ((i = text.IndexOf(pattern, i, StringComparison.Ordinal)) >= 0)
            {
                if (i == 0 || !char.IsLetterOrDigit(text[i - 1])) return true;
                i++;
            }
            return false;
        }

        private static IEnumerable<CategoryRule> Rules(string category, params string[] patterns) =>
            patterns.Select(p => new CategoryRule { Pattern = p, Category = category });

        private static IEnumerable<CategoryRule> Transfers(params string[] patterns) =>
            patterns.Select(p => new CategoryRule { Pattern = p, Category = "Savings transfer", SetType = TransactionType.Transfer });

        /// <summary>Common Dutch shops, services and institutions.</summary>
        public static readonly IReadOnlyList<CategoryRule> BuiltInRules = new List<CategoryRule>()
            .Concat(Rules("Groceries", "albert heijn", "ah to go", "jumbo", "lidl", "aldi", "dirk van den broek", "vomar",
                "hoogvliet", "plus supermarkt", "picnic", "ekoplaza", "poiesz", "coop supermarkt", "spar city", "crisp"))
            .Concat(Rules("Eating out", "thuisbezorgd", "uber eats", "deliveroo", "mcdonald", "burger king", "kfc",
                "domino", "new york pizza", "starbucks", "subway", "febo", "flink", "getir"))
            .Concat(Rules("Transport", "ns groep", "ns reizigers", "ov-chipkaart", "translink", "arriva", "qbuzz", "connexxion",
                "keolis", "ret rotterdam", "gvb", "htm ", "swapfiets", "q-park", "parkmobile", "yellowbrick", "easypark",
                "shell", "esso", "tinq", "tango", "texaco", "total energies station", "anwb wegenwacht", "ov-fiets", "uber"))
            .Concat(Rules("Subscriptions", "netflix", "spotify", "disney plus", "disney+", "videoland", "hbo max", "max.com",
                "prime video", "amazon prime", "youtube premium", "apple.com/bill", "icloud", "google play", "google one",
                "playstation", "xbox", "nintendo", "openai", "chatgpt", "anthropic", "adobe", "dropbox", "nlziet", "viaplay"))
            .Concat(Rules("Sport", "basic-fit", "basic fit", "sportcity", "anytime fitness", "trainmore", "fit for free",
                "decathlon", "knltb", "knvb"))
            .Concat(Rules("Utilities", "ziggo", "kpn", "vodafone", "odido", "t-mobile", "tele2", "simyo", "lebara", "youfone",
                "eneco", "vattenfall", "essent", "greenchoice", "budget energie", "vandebron", "oxxio", "energiedirect",
                "pure energie", "frank energie", "tibber", "waternet", "vitens", "evides", "brabant water", "dunea", "oasen",
                "waterleiding maatschappij limburg", "wml"))
            .Concat(Rules("Insurance", "zilveren kruis", "cz groep", "cz zorgverzekering", "vgz", "menzis", "dsw zorg",
                "zorg en zekerheid", "ohra", "centraal beheer", "interpolis", "nationale-nederlanden", "fbto", "univé", "unive",
                "a.s.r.", "ditzo", "inshared", "aegon", "allianz", "anwb verzekeren", "independer", "ik kies zelf"))
            .Concat(Rules("Shopping", "bol.com", "amazon", "coolblue", "mediamarkt", "zalando", "hema", "action", "ikea",
                "kruidvat", "etos", "primark", "h&m", "zara", "blokker", "wehkamp", "gamma", "praxis", "karwei", "temu",
                "aliexpress", "shein", "vinted", "marktplaats", "intertoys", "bijenkorf"))
            .Concat(Rules("Health", "apotheek", "tandarts", "huisarts", "fysio", "mondzorg", "ziekenhuis"))
            .Concat(Rules("Student finance", "dienst uitvoering onderwijs", "duo hoofdkantoor", "duo groningen"))
            .Concat(Rules("Taxes & allowances", "belastingdienst", "toeslagen", "gemeentelijke belasting", "waterschap", "cjib"))
            .Concat(Rules("Cash", "geldautomaat", "geldopname", "atm "))
            .Concat(Rules("Gifts & donations", "kwf", "unicef", "rode kruis", "artsen zonder grenzen", "greenpeace",
                "wakker dier", "amnesty"))
            .Concat(Transfers("oranje spaarrekening", "naar spaarrekening", "van spaarrekening", "rabo spaarrekening",
                "spaarrekening", "direct sparen", "kwartaalsparen", "bonusrente rekening", "vermogen sparen"))
            .ToList();
    }
}
