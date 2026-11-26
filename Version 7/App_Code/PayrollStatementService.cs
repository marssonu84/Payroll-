using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PayrollWebApp
{
    // One staff line read from an uploaded Staff Salary Disbursement statement.
    public sealed class PayrollStatementLine
    {
        public int RowNumber;
        public string Name;
        public decimal Lates;
        public decimal Leaves;
        public decimal Permissions;
        public decimal DeductionDays;
    }

    // Reads the Staff Salary Disbursement statement (.xlsx) that the site
    // itself downloads, and keeps its Amount-to-be-Deducted figures per
    // employee/month so Payroll can use them instead of the attendance snapshot.
    public static class PayrollStatementService
    {
        private const string TableName = "PayrollStatementDeductions";

        // ------------------------------------------------------------
        // Rule printed in the statement's own note:
        // Deducted = Leaves + whole groups of 3 Lates
        //          + (whole pairs of 2 Permissions x 0.5) - 1 free day, min 0
        // ------------------------------------------------------------
        public static decimal CalculateDeduction(
            decimal lates,
            decimal leaves,
            decimal permissions)
        {
            decimal d =
                leaves
                + Math.Floor(lates / 3m)
                + Math.Floor(permissions / 2m) * 0.5m
                - 1m;

            return d < 0m ? 0m : d;
        }

        // The statement is paid on the 4th of the month after the attendance
        // month, so a statement dated 04-11-2026 belongs to October 2026.
        public static DateTime AttendanceMonthFor(DateTime disbursement)
        {
            DateTime prev = disbursement.AddMonths(-1);
            return new DateTime(prev.Year, prev.Month, 1);
        }

        public static string NormalizeName(string s)
        {
            if (s == null)
                return "";

            string t = Regex.Replace(s.ToUpperInvariant(), "[^A-Z0-9]+", " ");
            return t.Trim();
        }

        public static void EnsureTable()
        {
            Db.Execute(
                @"IF OBJECT_ID('" + TableName + @"','U') IS NULL
                  CREATE TABLE " + TableName + @"
                  (
                      StatementDeductionID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                      TenantID INT NOT NULL,
                      BranchCode NVARCHAR(100) NOT NULL,
                      EmployeeID INT NOT NULL,
                      AttendanceMonth DATE NOT NULL,
                      Lates DECIMAL(8,2) NOT NULL DEFAULT (0),
                      Leaves DECIMAL(8,2) NOT NULL DEFAULT (0),
                      Permissions DECIMAL(8,2) NOT NULL DEFAULT (0),
                      DeductionDays DECIMAL(8,2) NOT NULL DEFAULT (0),
                      UploadedBy NVARCHAR(150) NULL,
                      UploadedOn DATETIME NOT NULL DEFAULT (GETDATE())
                  )");
        }

        public static bool Exists(int tenantId, string branchCode, DateTime month)
        {
            EnsureTable();

            object n = Db.Scalar(
                @"SELECT COUNT(*)
                  FROM " + TableName + @"
                  WHERE TenantID=?
                  AND BranchCode=?
                  AND AttendanceMonth=?",
                Prm(tenantId),
                Prm(branchCode),
                Prm(month));

            return Convert.ToInt32(n) > 0;
        }

        public static decimal? GetDeductionDays(
            int tenantId,
            int employeeId,
            DateTime month)
        {
            EnsureTable();

            object v = Db.Scalar(
                @"SELECT DeductionDays
                  FROM " + TableName + @"
                  WHERE TenantID=?
                  AND EmployeeID=?
                  AND AttendanceMonth=?",
                Prm(tenantId),
                Prm(employeeId),
                Prm(month));

            if (v == null || v == DBNull.Value)
                return null;

            return Convert.ToDecimal(v);
        }

        // Matches the statement lines to active employees of the branch by
        // name and stores them. Nothing is saved unless every line matches
        // exactly one employee.
        public static int Apply(
            int tenantId,
            string branchCode,
            DateTime month,
            List<PayrollStatementLine> lines,
            string uploadedBy,
            List<string> problems)
        {
            EnsureTable();

            DataTable emps = Db.Query(
                @"SELECT EmployeeID, EmployeeName
                  FROM Employees
                  WHERE IsActive=True
                  AND TenantID=?
                  AND BranchCode=?",
                Prm(tenantId),
                Prm(branchCode));

            Dictionary<string, int> byName = new Dictionary<string, int>();
            HashSet<string> duplicateNames = new HashSet<string>();

            foreach (DataRow r in emps.Rows)
            {
                string key = NormalizeName(Convert.ToString(r["EmployeeName"]));

                if (key.Length == 0)
                    continue;

                if (byName.ContainsKey(key))
                    duplicateNames.Add(key);
                else
                    byName[key] = Convert.ToInt32(r["EmployeeID"]);
            }

            Dictionary<int, PayrollStatementLine> resolved =
                new Dictionary<int, PayrollStatementLine>();

            foreach (PayrollStatementLine line in lines)
            {
                string key = NormalizeName(line.Name);

                if (duplicateNames.Contains(key))
                {
                    problems.Add(
                        "Row " + line.RowNumber + ": '" + line.Name +
                        "' matches more than one active employee in branch " +
                        branchCode + ".");
                    continue;
                }

                int empId;

                if (!byName.TryGetValue(key, out empId))
                {
                    problems.Add(
                        "Row " + line.RowNumber + ": '" + line.Name +
                        "' was not found among active employees of branch " +
                        branchCode + ". The name must match the Employee Name in Employees.");
                    continue;
                }

                if (resolved.ContainsKey(empId))
                {
                    problems.Add(
                        "Row " + line.RowNumber + ": '" + line.Name +
                        "' appears more than once in the statement.");
                    continue;
                }

                resolved[empId] = line;
            }

            if (problems.Count > 0)
                return 0;

            Db.Execute(
                @"DELETE FROM " + TableName + @"
                  WHERE TenantID=?
                  AND BranchCode=?
                  AND AttendanceMonth=?",
                Prm(tenantId),
                Prm(branchCode),
                Prm(month));

            try
            {
                foreach (KeyValuePair<int, PayrollStatementLine> kv in resolved)
                {
                    PayrollStatementLine l = kv.Value;

                    Db.Execute(
                        @"INSERT INTO " + TableName + @"
                          (TenantID, BranchCode, EmployeeID, AttendanceMonth,
                           Lates, Leaves, Permissions, DeductionDays,
                           UploadedBy, UploadedOn)
                          VALUES (?,?,?,?,?,?,?,?,?,?)",
                        Prm(tenantId),
                        Prm(branchCode),
                        Prm(kv.Key),
                        Prm(month),
                        Prm(l.Lates),
                        Prm(l.Leaves),
                        Prm(l.Permissions),
                        Prm(l.DeductionDays),
                        Prm(uploadedBy),
                        Prm(DateTime.Now));
                }
            }
            catch
            {
                // Never leave a half-saved statement behind.
                Db.Execute(
                    @"DELETE FROM " + TableName + @"
                      WHERE TenantID=?
                      AND BranchCode=?
                      AND AttendanceMonth=?",
                    Prm(tenantId),
                    Prm(branchCode),
                    Prm(month));
                throw;
            }

            return resolved.Count;
        }

        // ------------------------------------------------------------
        // .xlsx reading (no third-party library)
        // ------------------------------------------------------------
        private sealed class SheetCell
        {
            public string Text = "";
            public bool HasFormula;
        }

        public static List<PayrollStatementLine> Parse(
            Stream stream,
            out DateTime? disbursementDate)
        {
            disbursementDate = null;

            List<Dictionary<int, SheetCell>> rows = ReadFirstSheet(stream);

            // Disbursement date, e.g. "Staff Salary Disbursement Dt: 04-11-2026 (WEDNESDAY)"
            foreach (Dictionary<int, SheetCell> row in rows)
            {
                foreach (SheetCell c in row.Values)
                {
                    Match m = Regex.Match(
                        c.Text,
                        @"Dt\s*:\s*(\d{1,2})-(\d{1,2})-(\d{4})");

                    if (m.Success)
                    {
                        DateTime d;

                        if (DateTime.TryParseExact(
                                m.Groups[1].Value + "-" + m.Groups[2].Value + "-" + m.Groups[3].Value,
                                new[] { "d-M-yyyy", "dd-MM-yyyy" },
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out d))
                        {
                            disbursementDate = d;
                        }
                    }
                }

                if (disbursementDate.HasValue)
                    break;
            }

            // Header row
            int headerIndex = -1;
            int nameCol = -1, latesCol = -1, leavesCol = -1, permCol = -1, dedCol = -1;

            for (int i = 0; i < rows.Count && headerIndex < 0; i++)
            {
                foreach (KeyValuePair<int, SheetCell> kv in rows[i])
                {
                    string h = kv.Value.Text.Trim().ToLowerInvariant();

                    if (h.StartsWith("name of the staff"))
                        nameCol = kv.Key;
                }

                if (nameCol >= 0)
                {
                    headerIndex = i;

                    foreach (KeyValuePair<int, SheetCell> kv in rows[i])
                    {
                        string h = kv.Value.Text.Trim().ToLowerInvariant();

                        if (h == "lates") latesCol = kv.Key;
                        else if (h == "leaves") leavesCol = kv.Key;
                        else if (h == "permissions") permCol = kv.Key;
                        else if (h.StartsWith("amount to be deducted")) dedCol = kv.Key;
                    }
                }
            }

            if (headerIndex < 0 || latesCol < 0 || leavesCol < 0 || permCol < 0)
            {
                throw new Exception(
                    "This does not look like a Staff Salary Disbursement statement. " +
                    "Expected the columns: Name of the Staff, Lates, Leaves, Permissions, " +
                    "Amount to be Deducted. Please upload the file downloaded from Payroll.");
            }

            List<PayrollStatementLine> result = new List<PayrollStatementLine>();

            for (int i = headerIndex + 1; i < rows.Count; i++)
            {
                Dictionary<int, SheetCell> row = rows[i];

                string name = Get(row, nameCol).Text.Trim();

                if (name.Length == 0)
                    continue;

                if (name.StartsWith("Note:", StringComparison.OrdinalIgnoreCase))
                    break;

                PayrollStatementLine line = new PayrollStatementLine();
                line.RowNumber = i + 1;
                line.Name = name;
                line.Lates = Num(Get(row, latesCol).Text, "Lates", line);
                line.Leaves = Num(Get(row, leavesCol).Text, "Leaves", line);
                line.Permissions = Num(Get(row, permCol).Text, "Permissions", line);

                SheetCell ded = dedCol >= 0 ? Get(row, dedCol) : new SheetCell();

                if (dedCol < 0 || ded.HasFormula)
                {
                    // Formula column: recalculate with the statement's own rule so a
                    // stale saved result can never be applied.
                    line.DeductionDays =
                        CalculateDeduction(line.Lates, line.Leaves, line.Permissions);
                }
                else
                {
                    // Typed value: the sheet's figure is final.
                    line.DeductionDays = Num(ded.Text, "Amount to be Deducted", line);
                }

                result.Add(line);
            }

            if (result.Count == 0)
                throw new Exception("The statement has no staff rows.");

            return result;
        }

        private static SheetCell Get(Dictionary<int, SheetCell> row, int col)
        {
            SheetCell c;
            return row.TryGetValue(col, out c) ? c : new SheetCell();
        }

        private static decimal Num(string text, string field, PayrollStatementLine line)
        {
            text = (text ?? "").Trim();

            if (text.Length == 0)
                return 0m;

            decimal d;

            if (!Decimal.TryParse(
                    text,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out d)
                || d < 0m)
            {
                throw new Exception(
                    "Row " + line.RowNumber + " (" + line.Name + "): '" + text +
                    "' is not a valid number for " + field + ".");
            }

            return d;
        }

        private static List<Dictionary<int, SheetCell>> ReadFirstSheet(Stream stream)
        {
            List<Dictionary<int, SheetCell>> rows =
                new List<Dictionary<int, SheetCell>>();

            using (MemoryStream copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                copy.Position = 0;

                ZipArchive archive;

                try
                {
                    archive = new ZipArchive(copy, ZipArchiveMode.Read, true);
                }
                catch
                {
                    throw new Exception("The file is not a valid .xlsx workbook.");
                }

                using (archive)
                {
                    XNamespace main =
                        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

                    List<string> shared = new List<string>();
                    ZipArchiveEntry sst = archive.GetEntry("xl/sharedStrings.xml");

                    if (sst != null)
                    {
                        using (Stream s = sst.Open())
                        {
                            XDocument d = XDocument.Load(s);

                            foreach (XElement si in d.Descendants(main + "si"))
                            {
                                StringBuilder sb = new StringBuilder();

                                foreach (XElement t in si.Descendants(main + "t"))
                                    sb.Append(t.Value);

                                shared.Add(sb.ToString());
                            }
                        }
                    }

                    ZipArchiveEntry sheet = archive.GetEntry("xl/worksheets/sheet1.xml");

                    if (sheet == null)
                    {
                        List<string> names = new List<string>();

                        foreach (ZipArchiveEntry e in archive.Entries)
                        {
                            if (e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                                && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                                names.Add(e.FullName);
                        }

                        names.Sort(StringComparer.OrdinalIgnoreCase);

                        if (names.Count > 0)
                            sheet = archive.GetEntry(names[0]);
                    }

                    if (sheet == null)
                        throw new Exception("The Excel file does not contain a worksheet.");

                    XDocument doc;

                    using (Stream s = sheet.Open())
                        doc = XDocument.Load(s);

                    foreach (XElement rowEl in doc.Descendants(main + "sheetData").Elements(main + "row"))
                    {
                        int rowNo = 0;
                        string rAttr = (string)rowEl.Attribute("r");

                        Dictionary<int, SheetCell> row = new Dictionary<int, SheetCell>();

                        foreach (XElement c in rowEl.Elements(main + "c"))
                        {
                            string reference = (string)c.Attribute("r");
                            int col = ColumnIndex(reference);

                            if (col < 0)
                                continue;

                            string type = (string)c.Attribute("t");
                            string text = "";

                            XElement v = c.Element(main + "v");

                            if (type == "s")
                            {
                                int idx;

                                if (v != null
                                    && Int32.TryParse(v.Value, out idx)
                                    && idx >= 0
                                    && idx < shared.Count)
                                    text = shared[idx];
                            }
                            else if (type == "inlineStr")
                            {
                                XElement isEl = c.Element(main + "is");

                                if (isEl != null)
                                {
                                    StringBuilder sb = new StringBuilder();

                                    foreach (XElement t in isEl.Descendants(main + "t"))
                                        sb.Append(t.Value);

                                    text = sb.ToString();
                                }
                            }
                            else if (v != null)
                            {
                                text = v.Value;
                            }

                            SheetCell cell = new SheetCell();
                            cell.Text = text ?? "";
                            cell.HasFormula = c.Element(main + "f") != null;
                            row[col] = cell;
                        }

                        // Keep the sheet's own row number for messages.
                        if (Int32.TryParse(rAttr, out rowNo))
                        {
                            while (rows.Count < rowNo - 1)
                                rows.Add(new Dictionary<int, SheetCell>());
                        }

                        rows.Add(row);
                    }
                }
            }

            return rows;
        }

        private static int ColumnIndex(string reference)
        {
            if (String.IsNullOrEmpty(reference))
                return -1;

            int col = 0;
            int i = 0;

            while (i < reference.Length && Char.IsLetter(reference[i]))
            {
                col = col * 26 + (Char.ToUpperInvariant(reference[i]) - 'A' + 1);
                i++;
            }

            return i == 0 ? -1 : col - 1;
        }

        private static OleDbParameter Prm(object v)
        {
            return new OleDbParameter("?", v ?? DBNull.Value);
        }
    }
}
