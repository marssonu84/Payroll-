// Standalone console test runner (no test framework needed).
// Build from a Developer Command Prompt in this folder:
//   csc /out:CalculatorTests.exe ..\App_Code\StatutoryRules.cs ..\App_Code\PayrollCalculator.cs CalculatorTests.cs
// Then run CalculatorTests.exe  (exit code 0 = all passed).
using System;
using PayrollWebApp;

public static class CalculatorTests
{
    static int _fail, _pass;

    static void Check(string name, decimal expected, decimal actual)
    {
        if (expected == actual) { _pass++; return; }
        _fail++;
        Console.WriteLine("FAIL " + name + ": expected " + expected + " but got " + actual);
    }

    static void Case(string name, decimal prev, int days, decimal leaves, bool pf, bool esi,
        decimal adv, decimal oth, decimal pfX, decimal esiX, decimal ptX, decimal totalX, decimal netX)
    {
        PayrollResult r = PayrollCalculator.Calculate(prev, 0m, 0m, days, leaves, pf, esi, adv, oth);
        Check(name + " PF", pfX, r.Pf);
        Check(name + " ESI", esiX, r.Esi);
        Check(name + " PT", ptX, r.Pt);
        Check(name + " Total", totalX, r.TotalDeductions);
        Check(name + " Net", netX, r.NetPay);
    }

    // The V13 implementation, kept verbatim as a regression oracle.
    static PayrollResult Legacy(decimal previousSalary, decimal increment, decimal adjustment,
        int monthDays, decimal leaves, bool pfMember, bool esiMember, decimal salaryAdvance, decimal otherDeductions)
    {
        var standard = previousSalary + increment + adjustment;
        var lop = monthDays <= 0 ? 0 : standard / monthDays * leaves;
        var gross = standard - lop;
        var basic = gross * 0.80m;
        var da = gross * 0.20m;
        var pf = pfMember ? Math.Round(basic > 25000m ? 3000m : basic * 0.12m, 0) : 0m;
        var esiBasic = standard * 0.80m;
        var esi = esiMember ? Math.Round(esiBasic > 21000m ? 0m : esiBasic * 0.0075m, 0) : 0m;
        var pt = gross <= 15000m ? 0m : gross <= 20000m ? 150m : 200m;
        var total = pf + esi + pt + salaryAdvance + otherDeductions;
        var net = Math.Round(gross - total, 0);
        return new PayrollResult { StandardSalary = standard, Lop = lop, Gross = gross, Basic = basic, Da = da,
            Pf = pf, Esi = esi, Pt = pt, TotalDeductions = total, NetPay = net };
    }

    public static int Main()
    {
        // Hand-verified cases (values cross-checked independently).
        Case("A 30000",            30000m, 30, 0m, true, true, 0m, 0m,    2880m,   0m, 200m, 3080m, 26920m);
        Case("B 20000 PT slab2",   20000m, 30, 0m, true, true, 0m, 0m,    1920m, 120m, 150m, 2190m, 17810m);
        Case("C 15000 PT zero",    15000m, 30, 0m, true, true, 0m, 0m,    1440m,  90m,   0m, 1530m, 13470m);
        Case("D ESI ceiling 21000",26250m, 30, 0m, true, true, 0m, 0m,    2520m, 158m, 200m, 2878m, 23372m); // 157.5 rounds to even
        Case("E PF cap",           40000m, 30, 0m, true, true, 0m, 0m,    3000m,   0m, 200m, 3200m, 36800m);
        Case("F LOP 3 days",       30000m, 30, 3m, true, true, 0m, 0m,    2592m,   0m, 200m, 2792m, 24208m);
        Case("G zero month days",  30000m,  0, 5m, true, true, 0m, 0m,    2880m,   0m, 200m, 3080m, 26920m);
        Case("H advance+other",    30000m, 30, 0m, true, true, 1000m, 500m, 2880m, 0m, 200m, 4580m, 25420m);
        Case("I not PF/ESI member",30000m, 30, 0m, false,false,0m, 0m,       0m,   0m, 200m,  200m, 29800m);
        Case("J just over 15000",  15001m, 30, 0m, true, true, 0m, 0m,    1440m,  90m, 150m, 1680m, 13321m);
        Case("K just over 20000",  20001m, 30, 0m, true, true, 0m, 0m,    1920m, 120m, 200m, 2240m, 17761m);

        // Custom rules: ESI ceiling raised to 25,000 -> case D salary 30,000 now pays ESI.
        StatutoryRules custom = StatutoryRules.CreateDefault();
        custom.Apply(StatutoryRules.EsiCeiling, 25000m);
        PayrollResult c = PayrollCalculator.Calculate(30000m, 0m, 0m, 30, 0m, true, true, 0m, 0m, custom);
        Check("custom ESI", 180m, c.Esi);
        Check("custom Net", 26740m, c.NetPay);

        // Null rules fall back to defaults.
        Check("null rules", 26920m, PayrollCalculator.Calculate(30000m, 0m, 0m, 30, 0m, true, true, 0m, 0m, null).NetPay);

        // Regression: new calculator must equal V13 for a wide grid of inputs.
        int grid = 0;
        decimal[] salaries = { 0m, 9999m, 12000m, 15000m, 15000.5m, 17500m, 20000m, 20000.5m, 26250m, 26251m, 31250m, 31251m, 45000m, 120000m };
        decimal[] leaves = { 0m, 0.5m, 1m, 2.5m, 4m };
        int[] days = { 0, 28, 30, 31 };
        foreach (decimal s in salaries)
            foreach (decimal l in leaves)
                foreach (int d in days)
                    foreach (bool pf in new[] { true, false })
                        foreach (bool esi in new[] { true, false })
                        {
                            PayrollResult a = PayrollCalculator.Calculate(s, 500m, -200m, d, l, pf, esi, 100m, 50m);
                            PayrollResult b = Legacy(s, 500m, -200m, d, l, pf, esi, 100m, 50m);
                            string n = "grid s=" + s + " l=" + l + " d=" + d + " pf=" + pf + " esi=" + esi;
                            Check(n + " Gross", b.Gross, a.Gross);
                            Check(n + " DA", b.Da, a.Da);
                            Check(n + " PF", b.Pf, a.Pf);
                            Check(n + " ESI", b.Esi, a.Esi);
                            Check(n + " PT", b.Pt, a.Pt);
                            Check(n + " Net", b.NetPay, a.NetPay);
                            grid++;
                        }

        // Rule validation.
        if (StatutoryRules.Validate(StatutoryRules.PfRate, 12m) == null) { _fail++; Console.WriteLine("FAIL validate: rate 12 should be rejected"); } else _pass++;
        if (StatutoryRules.Validate(StatutoryRules.PfRate, 0.12m) != null) { _fail++; Console.WriteLine("FAIL validate: 0.12 should pass"); } else _pass++;
        if (StatutoryRules.Validate("NOPE", 1m) == null) { _fail++; Console.WriteLine("FAIL validate: unknown key"); } else _pass++;

        Console.WriteLine("Passed checks: " + _pass + ", failed: " + _fail + " (regression grid cases: " + grid + ")");
        return _fail == 0 ? 0 : 1;
    }
}
