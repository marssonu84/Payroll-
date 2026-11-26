using System;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Web;
using System.Web.UI.WebControls;

namespace PayrollWebApp
{
    public partial class ReportsPage : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            Auth.RequirePermission("ReportsView");
            if (!IsPostBack)
            {
                txtMonth.Text = DateTime.Today.ToString("yyyy-MM");
                LoadData();
            }
        }

        protected void btnShow_Click(object sender, EventArgs e)
        {
            LoadData();
        }

        // ------------------------------------------------------------------
        // Load grids + KPIs
        // ------------------------------------------------------------------

        private void LoadData()
        {
            HideMessage();
            DateTime month;
            if (!TryGetMonth(out month))
            {
                ShowMessage("Please choose a valid month (YYYY-MM).", true);
                ClearKpisAndDownloads();
                return;
            }

            litReportMonth.Text = HttpUtility.HtmlEncode(month.ToString("MMMM yyyy", CultureInfo.InvariantCulture));

            DataTable summary = GetSummaryData(month);
            DataTable bank = GetBankData(month);

            gvSummary.DataSource = summary;
            gvSummary.DataBind();
            gvBank.DataSource = bank;
            gvBank.DataBind();

            BindKpis(summary, bank);
            pnlKpis.Visible = true;
            pnlDownloads.Visible = true;

            if (summary.Rows.Count == 0 && bank.Rows.Count == 0)
                ShowMessage("No payroll data found for " + month.ToString("MMMM yyyy") + ".", false);
        }

        private void BindKpis(DataTable summary, DataTable bank)
        {
            int empCount = 0;
            int branchCount = summary.Rows.Count;
            decimal totalGross = 0m;
            decimal totalNet = 0m;

            foreach (DataRow r in summary.Rows)
            {
                empCount += ToInt(r, "TOTAL NO. OF EMPLOYEES");
                totalGross += ToDec(r, "Bank Gross Salaries");
                totalNet += ToDec(r, "Actual Net Pay");
            }

            // Fallback: count bank rows if summary is empty but bank has data
            if (empCount == 0 && bank.Rows.Count > 0)
                empCount = bank.Rows.Count;

            litEmpCount.Text = empCount.ToString("N0", CultureInfo.InvariantCulture);
            litBranchCount.Text = branchCount.ToString("N0", CultureInfo.InvariantCulture);
            litGross.Text = totalGross.ToString("N2", CultureInfo.InvariantCulture);
            litNet.Text = totalNet.ToString("N2", CultureInfo.InvariantCulture);
        }

        private void ClearKpisAndDownloads()
        {
            pnlKpis.Visible = false;
            pnlDownloads.Visible = false;
            gvSummary.DataSource = null;
            gvSummary.DataBind();
            gvBank.DataSource = null;
            gvBank.DataBind();
            litReportMonth.Text = "—";
        }

        // ------------------------------------------------------------------
        // Download handlers
        // ------------------------------------------------------------------

        protected void btnDownloadSummary_Click(object sender, EventArgs e)
        {
            DateTime month;
            if (!TryGetMonth(out month)) { ShowMessage("Please choose a valid month first.", true); return; }
            DataTable data = GetSummaryData(month);
            ReportExcelService.Send(
                Response,
                data,
                "Payroll_Summary_" + month.ToString("yyyy_MM") + ".xlsx",
                "PAYROLL SUMMARY",
                month);
        }

        protected void btnDownloadNEFT_Click(object sender, EventArgs e) { DownloadBank("NEFT", "NEFT_Salaries"); }
        protected void btnDownloadHouse_Click(object sender, EventArgs e) { DownloadBank("HOUSE", "House_Salaries"); }
        protected void btnDownloadIDBI_Click(object sender, EventArgs e) { DownloadBank("IDBI", "IDBI_Bank"); }
        protected void btnDownloadHDFC_Click(object sender, EventArgs e) { DownloadBank("HDFC", "HDFC_Bank"); }
        protected void btnDownloadKVB_Click(object sender, EventArgs e) { DownloadBank("KVB", "KVB_Bank_Statement"); }

        protected void btnDownloadPF_Click(object sender, EventArgs e) { DownloadStatutory(true); }
        protected void btnDownloadESI_Click(object sender, EventArgs e) { DownloadStatutory(false); }

        private void DownloadBank(string type, string filePrefix)
        {
            DateTime month;
            if (!TryGetMonth(out month)) { ShowMessage("Please choose a valid month first.", true); return; }

            DataTable source = GetBankData(month);
            DataTable result = source.Clone();

            foreach (DataRow r in source.Rows)
            {
                if (MatchesBank(r, type))
                    result.ImportRow(r);
            }

            string heading = "BANK PAYMENTS (" + type + ")";
            ReportExcelService.Send(
                Response,
                result,
                filePrefix + "_" + month.ToString("yyyy_MM") + ".xlsx",
                heading,
                month);
        }

        private void DownloadStatutory(bool isPf)
        {
            DateTime month;
            if (!TryGetMonth(out month)) { ShowMessage("Please choose a valid month first.", true); return; }

            string col = isPf ? "PF" : "ESI";
            // Column name is controlled by this method only (PF or ESI).
            string sql =
                @"SELECT E.BranchCode, E.EmployeeName, E.BankAccount, E.BankName,
                         P.GrossSalary, P." + col + @" AS EmployeeContribution, P.NetPay
                  FROM Employees E
                  INNER JOIN Payroll P ON E.EmployeeID = P.EmployeeID
                  WHERE P.SalaryMonth = ? AND E.TenantID = ? AND P.TenantID = ?
                    AND ISNULL(P." + col + @", 0) > 0
                  ORDER BY E.BranchCode, E.EmployeeName";

            DataTable data = Db.Query(sql, P(month), P(Auth.CurrentTenantId()), P(Auth.CurrentTenantId()));
            string fileName = (isPf ? "PF_Payroll_Report_" : "ESI_Payroll_Report_") + month.ToString("yyyy_MM") + ".xlsx";
            string heading = isPf ? "PROVIDENT FUND PAYROLL REPORT" : "ESI PAYROLL REPORT";
            ReportExcelService.Send(Response, data, fileName, heading, month);
        }

        // ------------------------------------------------------------------
        // Data access
        // ------------------------------------------------------------------

        private DataTable GetSummaryData(DateTime month)
        {
            return Db.Query(
                @"SELECT E.BranchCode AS [BRANCH],
                         COUNT(P.PayrollID) AS [TOTAL NO. OF EMPLOYEES],
                         SUM(CASE WHEN ISNULL(P.PF,0)>0 THEN 1 ELSE 0 END) AS [PF MEMBERS],
                         SUM(CASE WHEN ISNULL(P.ESI,0)>0 THEN 1 ELSE 0 END) AS [ESI MEMBERS],
                         SUM(P.GrossSalary) AS [Bank Gross Salaries],
                         SUM(P.LOP) AS [LOP],
                         SUM(P.PF) AS [PF],
                         SUM(P.ESI) AS [ESI],
                         SUM(P.PT) AS [PT],
                         SUM(P.SalaryAdvance) AS [Salary Advance],
                         SUM(P.OtherDeductions) AS [Other Deductions],
                         SUM(P.GrossSalary)-SUM(P.LOP)-SUM(P.PF)-SUM(P.ESI)-SUM(P.PT)-SUM(P.SalaryAdvance)-SUM(P.OtherDeductions) AS [Cheque Net Payable],
                         SUM(P.NetPay) AS [Actual Net Pay],
                         SUM(P.NetPay)-(SUM(P.GrossSalary)-SUM(P.LOP)-SUM(P.PF)-SUM(P.ESI)-SUM(P.PT)-SUM(P.SalaryAdvance)-SUM(P.OtherDeductions)) AS [Diff]
                  FROM Employees E
                  INNER JOIN Payroll P ON E.EmployeeID = P.EmployeeID
                  WHERE P.SalaryMonth = ? AND E.TenantID = ? AND P.TenantID = ?
                  GROUP BY E.BranchCode
                  ORDER BY E.BranchCode",
                P(month), P(Auth.CurrentTenantId()), P(Auth.CurrentTenantId()));
        }

        private DataTable GetBankData(DateTime month)
        {
            return Db.Query(
                @"SELECT E.BranchCode, E.EmployeeName, E.BankAccount, E.BankName,
                         P.GrossSalary, P.TotalDeductions, P.NetPay
                  FROM Employees E
                  INNER JOIN Payroll P ON E.EmployeeID = P.EmployeeID
                  WHERE P.SalaryMonth = ? AND E.TenantID = ? AND P.TenantID = ?
                  ORDER BY E.BankName, E.BranchCode, E.EmployeeName",
                P(month), P(Auth.CurrentTenantId()), P(Auth.CurrentTenantId()));
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private bool TryGetMonth(out DateTime month)
        {
            return DateTime.TryParseExact(
                (txtMonth.Text ?? "").Trim() + "-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out month);
        }

        private static bool MatchesBank(DataRow r, string type)
        {
            string bank = Convert.ToString(r["BankName"]) ?? "";
            string branch = Convert.ToString(r["BranchCode"]) ?? "";
            string t = (type ?? "").ToUpperInvariant();

            if (t == "HOUSE")
                return branch.Equals("HOUSE", StringComparison.OrdinalIgnoreCase);

            if (t == "NEFT")
            {
                return !ContainsBank(bank, "KVB")
                    && !ContainsBank(bank, "KARUR VYSYA")
                    && !ContainsBank(bank, "IDBI")
                    && !ContainsBank(bank, "HDFC")
                    && !branch.Equals("HOUSE", StringComparison.OrdinalIgnoreCase);
            }

            return ContainsBank(bank, type);
        }

        private static bool ContainsBank(string value, string bank)
        {
            return (value ?? "").IndexOf(bank ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ShowMessage(string text, bool isError)
        {
            pnlMessage.Visible = true;
            pnlMessage.CssClass = isError ? "message error" : "message";
            litMessage.Text = HttpUtility.HtmlEncode(text);
        }

        private void HideMessage()
        {
            pnlMessage.Visible = false;
            litMessage.Text = "";
        }

        private static int ToInt(DataRow r, string col)
        {
            if (r == null || !r.Table.Columns.Contains(col) || r[col] == DBNull.Value) return 0;
            int n;
            return int.TryParse(Convert.ToString(r[col], CultureInfo.InvariantCulture), out n) ? n : 0;
        }

        private static decimal ToDec(DataRow r, string col)
        {
            if (r == null || !r.Table.Columns.Contains(col) || r[col] == DBNull.Value) return 0m;
            decimal n;
            return decimal.TryParse(Convert.ToString(r[col], CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? n : 0m;
        }

        private static OleDbParameter P(object value)
        {
            return new OleDbParameter("?", value ?? DBNull.Value);
        }
    }
}
