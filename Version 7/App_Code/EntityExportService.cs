using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PayrollWebApp
{
    /// <summary>
    /// V10.27: Super User export of ONE entity's data as a ZIP of CSV files.
    /// Uses plain SELECT queries only (no BACKUP/RESTORE permission needed), so it works on shared hosting.
    /// This is a data safety copy, not a full database backup. Password hashes and two-step secrets are never exported.
    /// </summary>
    public static class EntityExportService
    {
        // file name inside the ZIP, then a SELECT with exactly one "?" parameter (the TenantID).
        private static readonly string[][] Exports = new string[][]
        {
            new[] { "1_Entity.csv",
                "SELECT TenantID, OrganizationName, OrganizationType, ContactPerson, Mobile, Email, AddressLine, City, StateName, Pincode, GSTIN, PAN, ApprovalStatus, CreatedOn, ApprovedOn FROM Tenants WHERE TenantID=?" },
            new[] { "2_Branches.csv",
                "SELECT * FROM Branches WHERE TenantID=?" },
            new[] { "3_Users.csv",
                "SELECT id, username, IsActive, IsOrgAdmin, AdminEmail FROM Users WHERE TenantID=?" },
            new[] { "4_UserPermissions.csv",
                "SELECT P.* FROM UserPermissions P INNER JOIN Users U ON U.id=P.UserID WHERE U.TenantID=?" },
            new[] { "5_Employees.csv",
                "SELECT * FROM Employees WHERE TenantID=?" },
            new[] { "6_Attendance.csv",
                "SELECT A.* FROM Attendance A INNER JOIN Employees E ON E.EmployeeID=A.EmployeeID WHERE E.TenantID=?" },
            new[] { "7_AttendanceMonthlyFinalization.csv",
                "SELECT * FROM AttendanceMonthlyFinalization WHERE TenantID=?" },
            new[] { "8_AttendanceMonthlyEmployeeSummary.csv",
                "SELECT S.* FROM AttendanceMonthlyEmployeeSummary S INNER JOIN Employees E ON E.EmployeeID=S.EmployeeID WHERE E.TenantID=?" },
            new[] { "9_Payroll.csv",
                "SELECT * FROM Payroll WHERE TenantID=?" },
            new[] { "10_Loans.csv",
                "SELECT * FROM Loans WHERE TenantID=?" }
        };

        public static string GetEntityName(int tenantId)
        {
            return Convert.ToString(Db.Scalar("SELECT OrganizationName FROM Tenants WHERE TenantID=?", new OleDbParameter("?", tenantId)));
        }

        /// <summary>Builds the ZIP in memory. Returns the bytes; entityName is the organisation name.</summary>
        public static byte[] BuildZip(int tenantId, out string entityName)
        {
            entityName = GetEntityName(tenantId);
            if (String.IsNullOrWhiteSpace(entityName))
                throw new InvalidOperationException("Entity not found.");

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("Vighnesh Payroll Systems - entity data export");
            summary.AppendLine("Entity: " + entityName + " (ID " + tenantId + ")");
            summary.AppendLine("Exported (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            summary.AppendLine();
            summary.AppendLine("This is a data safety copy of ONE entity, as CSV files. It is NOT a full database backup.");
            summary.AppendLine("It contains personal and bank details. Store it securely and do not share it.");
            summary.AppendLine("Password hashes and two-step secrets are not included.");
            summary.AppendLine();
            summary.AppendLine("Files and row counts:");

            using (MemoryStream ms = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (string[] item in Exports)
                    {
                        string line;
                        try
                        {
                            DataTable dt = Db.Query(item[1], new OleDbParameter("?", tenantId));
                            AddEntry(zip, item[0], ToCsv(dt));
                            line = item[0] + ": " + dt.Rows.Count + " row(s)";
                        }
                        catch (Exception)
                        {
                            line = item[0] + ": NOT EXPORTED (table or column not available)";
                        }
                        summary.AppendLine(line);
                    }

                    AddEntry(zip, "README.txt", summary.ToString());
                }

                return ms.ToArray();
            }
        }

        public static string SafeFileName(string name)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in name ?? "")
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) sb.Append(c);
                else sb.Append('_');
            }
            string s = sb.ToString().Trim('_');
            if (s.Length == 0) s = "entity";
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }

        private static void AddEntry(ZipArchive zip, string name, string text)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream s = entry.Open())
            {
                // UTF-8 with BOM so Excel shows names correctly
                byte[] bom = new UTF8Encoding(true).GetPreamble();
                s.Write(bom, 0, bom.Length);
                byte[] body = new UTF8Encoding(false).GetBytes(text);
                s.Write(body, 0, body.Length);
            }
        }

        private static string ToCsv(DataTable dt)
        {
            StringBuilder sb = new StringBuilder();
            List<string> header = new List<string>();
            foreach (DataColumn c in dt.Columns) header.Add(Cell(c.ColumnName, false));
            sb.Append(String.Join(",", header.ToArray())).Append("\r\n");

            foreach (DataRow row in dt.Rows)
            {
                List<string> cells = new List<string>();
                foreach (DataColumn c in dt.Columns)
                {
                    object v = row[c];
                    if (v == null || v == DBNull.Value) { cells.Add(""); continue; }

                    if (v is DateTime) cells.Add(((DateTime)v).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    else if (v is bool) cells.Add(((bool)v) ? "1" : "0");
                    else if (v is IFormattable && !(v is string)) cells.Add(((IFormattable)v).ToString(null, CultureInfo.InvariantCulture));
                    else if (v is byte[]) cells.Add("");
                    else cells.Add(Cell(Convert.ToString(v), true));
                }
                sb.Append(String.Join(",", cells.ToArray())).Append("\r\n");
            }
            return sb.ToString();
        }

        // Quotes a text cell. Text that starts with = + - @ is prefixed with an apostrophe so Excel cannot run it as a formula.
        private static string Cell(string text, bool guardFormula)
        {
            text = text ?? "";
            if (guardFormula && text.Length > 0 && "=+-@\t\r".IndexOf(text[0]) >= 0) text = "'" + text;
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0) text = "\"" + text.Replace("\"", "\"\"") + "\"";
            return text;
        }
    }
}
