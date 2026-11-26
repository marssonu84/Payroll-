using System;

namespace PayrollWebApp
{
    /// <summary>
    /// Statutory / policy parameters used by PayrollCalculator.
    /// No dependencies, so it can be unit-tested on its own.
    /// Defaults reproduce the V13 hard-coded behaviour exactly.
    /// </summary>
    public class StatutoryRules
    {
        // Rule keys (stored in table StatutoryRules.RuleKey)
        public const string BasicPercent = "BASIC_PERCENT";
        public const string PfRate = "PF_RATE";
        public const string PfBasicCap = "PF_BASIC_CAP";
        public const string PfCappedAmount = "PF_CAPPED_AMOUNT";
        public const string EsiRate = "ESI_RATE";
        public const string EsiCeiling = "ESI_CEILING";
        public const string PtSlab1Upto = "PT_SLAB1_UPTO";
        public const string PtSlab1Amount = "PT_SLAB1_AMOUNT";
        public const string PtSlab2Upto = "PT_SLAB2_UPTO";
        public const string PtSlab2Amount = "PT_SLAB2_AMOUNT";
        public const string PtSlab3Amount = "PT_SLAB3_AMOUNT";

        public static readonly string[] AllKeys =
        {
            BasicPercent, PfRate, PfBasicCap, PfCappedAmount, EsiRate, EsiCeiling,
            PtSlab1Upto, PtSlab1Amount, PtSlab2Upto, PtSlab2Amount, PtSlab3Amount
        };

        public decimal BasicPct { get; set; }          // share of gross treated as Basic (rest is DA)
        public decimal PfPct { get; set; }             // employee PF rate on Basic
        public decimal PfCapBasic { get; set; }        // Basic above this uses the capped amount
        public decimal PfCapAmount { get; set; }       // flat PF when Basic exceeds the cap
        public decimal EsiPct { get; set; }            // employee ESI rate
        public decimal EsiMaxBasic { get; set; }       // ESI not applicable above this standard Basic
        public decimal Pt1Upto { get; set; }           // gross up to this: slab 1 amount
        public decimal Pt1Amt { get; set; }
        public decimal Pt2Upto { get; set; }           // gross up to this: slab 2 amount
        public decimal Pt2Amt { get; set; }
        public decimal Pt3Amt { get; set; }            // above slab 2

        public static StatutoryRules CreateDefault()
        {
            return new StatutoryRules
            {
                BasicPct = 0.80m,
                PfPct = 0.12m,
                PfCapBasic = 25000m,
                PfCapAmount = 3000m,
                EsiPct = 0.0075m,
                EsiMaxBasic = 21000m,
                Pt1Upto = 15000m,
                Pt1Amt = 0m,
                Pt2Upto = 20000m,
                Pt2Amt = 150m,
                Pt3Amt = 200m
            };
        }

        /// <summary>Default value for a key (used to seed the table).</summary>
        public static decimal DefaultFor(string key)
        {
            StatutoryRules d = CreateDefault();
            decimal v;
            d.TryGet(key, out v);
            return v;
        }

        public bool TryGet(string key, out decimal value)
        {
            switch (key)
            {
                case BasicPercent: value = BasicPct; return true;
                case PfRate: value = PfPct; return true;
                case PfBasicCap: value = PfCapBasic; return true;
                case PfCappedAmount: value = PfCapAmount; return true;
                case EsiRate: value = EsiPct; return true;
                case EsiCeiling: value = EsiMaxBasic; return true;
                case PtSlab1Upto: value = Pt1Upto; return true;
                case PtSlab1Amount: value = Pt1Amt; return true;
                case PtSlab2Upto: value = Pt2Upto; return true;
                case PtSlab2Amount: value = Pt2Amt; return true;
                case PtSlab3Amount: value = Pt3Amt; return true;
            }
            value = 0m;
            return false;
        }

        /// <summary>Sets a value by key. Returns false for unknown keys.</summary>
        public bool Apply(string key, decimal value)
        {
            switch (key)
            {
                case BasicPercent: BasicPct = value; return true;
                case PfRate: PfPct = value; return true;
                case PfBasicCap: PfCapBasic = value; return true;
                case PfCappedAmount: PfCapAmount = value; return true;
                case EsiRate: EsiPct = value; return true;
                case EsiCeiling: EsiMaxBasic = value; return true;
                case PtSlab1Upto: Pt1Upto = value; return true;
                case PtSlab1Amount: Pt1Amt = value; return true;
                case PtSlab2Upto: Pt2Upto = value; return true;
                case PtSlab2Amount: Pt2Amt = value; return true;
                case PtSlab3Amount: Pt3Amt = value; return true;
            }
            return false;
        }

        /// <summary>Basic validation for values entered on the admin page.</summary>
        public static string Validate(string key, decimal value)
        {
            if (Array.IndexOf(AllKeys, key) < 0) return "Unknown rule key.";
            if (value < 0m) return "Value cannot be negative.";
            if ((key == BasicPercent || key == PfRate || key == EsiRate) && value > 1m)
                return "Rates must be entered as a fraction between 0 and 1 (for example 0.12 for 12%).";
            if (key == BasicPercent && value == 0m) return "Basic percent cannot be zero.";
            return null;
        }

        /// <summary>
        /// Hook that returns the rules in force on a date. Defaults to the built-in values;
        /// Global.asax Application_Start replaces it with the database-backed resolver.
        /// </summary>
        public static Func<DateTime, StatutoryRules> Resolver = delegate(DateTime d) { return CreateDefault(); };
    }
}
