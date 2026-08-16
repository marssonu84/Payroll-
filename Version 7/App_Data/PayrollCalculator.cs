using System;

namespace PayrollWebApp
{
    public class PayrollResult
    {
        public decimal StandardSalary { get; set; }
        public decimal Lop { get; set; }
        public decimal Gross { get; set; }
        public decimal Basic { get; set; }
        public decimal Hra { get; set; }
        public decimal OvertimeAllowance { get; set; }
        public decimal Pf { get; set; }
        public decimal Esi { get; set; }
        public decimal Pt { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal NetPay { get; set; }
    }

    public static class PayrollCalculator
    {
        // Mirrors the formulas in JUL Salaries - 2026.xlsm.
        public static PayrollResult Calculate(decimal previousSalary, decimal increment, decimal adjustment,
            int monthDays, decimal leaves, bool pfMember, bool esiMember,
            decimal salaryAdvance, decimal otherDeductions)
        {
            var standard = previousSalary + increment + adjustment;
            var lop = monthDays <= 0 ? 0 : standard / monthDays * leaves;
            var gross = standard - lop;
            var basic = gross * 0.60m;
            var hra = gross * 0.30m;
            var overtime = gross * 0.10m;

            var pf = pfMember ? Math.Round(basic > 15000m ? 1800m : basic * 0.12m, 0) : 0m;
            // Workbook formula uses Basic (not Gross) for ESI eligibility/contribution.
            var esi = esiMember ? Math.Round(basic >= 21001m ? 0m : basic * 0.0075m, 0) : 0m;
            var pt = gross <= 15000m ? 0m : gross <= 20000m ? 150m : 200m;
            var total = pf + esi + pt + salaryAdvance + otherDeductions;
            var net = Math.Round(gross - total, 0);

            return new PayrollResult
            {
                StandardSalary = standard,
                Lop = lop,
                Gross = gross,
                Basic = basic,
                Hra = hra,
                OvertimeAllowance = overtime,
                Pf = pf,
                Esi = esi,
                Pt = pt,
                TotalDeductions = total,
                NetPay = net
            };
        }
    }
}
