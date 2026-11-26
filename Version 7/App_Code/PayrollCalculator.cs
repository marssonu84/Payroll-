using System;

namespace PayrollWebApp
{
    public class PayrollResult
    {
        public decimal StandardSalary { get; set; }
        public decimal Lop { get; set; }
        public decimal Gross { get; set; }
        public decimal Basic { get; set; }
        public decimal Da { get; set; }
        public decimal Pf { get; set; }
        public decimal Esi { get; set; }
        public decimal Pt { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal NetPay { get; set; }
    }

    public static class PayrollCalculator
    {
        // Same signature as V13, so existing callers (Payroll.aspx.cs) need no change.
        // Rates now come from StatutoryRules (database-backed, effective-dated, per entity).
        public static PayrollResult Calculate(decimal previousSalary, decimal increment, decimal adjustment,
            int monthDays, decimal leaves, bool pfMember, bool esiMember,
            decimal salaryAdvance, decimal otherDeductions)
        {
            return Calculate(previousSalary, increment, adjustment, monthDays, leaves, pfMember, esiMember,
                salaryAdvance, otherDeductions, StatutoryRules.Resolver(DateTime.Today));
        }

        // Use this overload when the payroll month is known so the correct effective-dated rules apply:
        //   StatutoryRules.Resolver(new DateTime(year, month, 1))
        public static PayrollResult Calculate(decimal previousSalary, decimal increment, decimal adjustment,
            int monthDays, decimal leaves, bool pfMember, bool esiMember,
            decimal salaryAdvance, decimal otherDeductions, StatutoryRules rules)
        {
            if (rules == null) rules = StatutoryRules.CreateDefault();

            var standard = previousSalary + increment + adjustment;
            var lop = monthDays <= 0 ? 0 : standard / monthDays * leaves;
            var gross = standard - lop;
            var basic = gross * rules.BasicPct;
            var da = gross * (1m - rules.BasicPct);

            var pf = pfMember ? Math.Round(basic > rules.PfCapBasic ? rules.PfCapAmount : basic * rules.PfPct, 0) : 0m;
            // ESI is calculated on standard Basic, so LOP/leaves do not reduce ESI.
            // ESI applies when standard Basic is up to and including the ceiling.
            var esiBasic = standard * rules.BasicPct;
            var esi = esiMember ? Math.Round(esiBasic > rules.EsiMaxBasic ? 0m : esiBasic * rules.EsiPct, 0) : 0m;
            var pt = gross <= rules.Pt1Upto ? rules.Pt1Amt : gross <= rules.Pt2Upto ? rules.Pt2Amt : rules.Pt3Amt;
            var total = pf + esi + pt + salaryAdvance + otherDeductions;
            var net = Math.Round(gross - total, 0);

            return new PayrollResult
            {
                StandardSalary = standard,
                Lop = lop,
                Gross = gross,
                Basic = basic,
                Da = da,
                Pf = pf,
                Esi = esi,
                Pt = pt,
                TotalDeductions = total,
                NetPay = net
            };
        }
    }
}
