using System;
using System.Data;
using System.Data.OleDb;
using System.Web.UI.WebControls;

namespace PayrollWebApp
{
    public partial class LoansPage : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            Auth.RequirePermission("LoansView");

            btnSave.Visible =
                Auth.HasPermission("LoansEdit");

            if (!IsPostBack)
            {
                txtDate.Text =
                    DateTime.Today.ToString("yyyy-MM-dd");

                BindEmployees();
                LoadOpeningBalance();
                BindLoans();
            }
        }

        private void BindEmployees()
        {
            DataTable dt = Db.Query(
                @"SELECT
                    EmployeeID,
                    EmployeeName + ' - ' + BranchCode AS Emp
                  FROM Employees
                  WHERE IsActive=True
                  ORDER BY EmployeeName"
            );

            ddlEmployee.DataSource = dt;
            ddlEmployee.DataTextField = "Emp";
            ddlEmployee.DataValueField = "EmployeeID";
            ddlEmployee.DataBind();

            ddlEmployee.AutoPostBack = true;

            ddlEmployee.SelectedIndexChanged -=
                ddlEmployee_SelectedIndexChanged;

            ddlEmployee.SelectedIndexChanged +=
                ddlEmployee_SelectedIndexChanged;
        }

        protected void ddlEmployee_SelectedIndexChanged(
            object sender,
            EventArgs e)
        {
            LoadOpeningBalance();

            txtAvailed.Text = "0";
            txtDeduction.Text = "0";
            txtRemarks.Text = "";
        }

        private void LoadOpeningBalance()
        {
            if (ddlEmployee.Items.Count == 0 ||
                String.IsNullOrEmpty(
                    ddlEmployee.SelectedValue))
            {
                txtOpening.Text = "0.00";
                return;
            }

            try
            {
                int employeeId =
                    Convert.ToInt32(
                        ddlEmployee.SelectedValue
                    );

                DataTable dt = Db.Query(
                    @"SELECT TOP 1
                        NetBalance
                      FROM Loans
                      WHERE EmployeeID=?
                      ORDER BY
                        TxnDate DESC,
                        LoanID DESC",
                    P(employeeId)
                );

                if (dt.Rows.Count > 0 &&
                    dt.Rows[0]["NetBalance"] != DBNull.Value)
                {
                    decimal balance =
                        Convert.ToDecimal(
                            dt.Rows[0]["NetBalance"]
                        );

                    txtOpening.Text =
                        balance.ToString("0.00");
                }
                else
                {
                    txtOpening.Text = "0.00";
                }
            }
            catch
            {
                txtOpening.Text = "0.00";
            }
        }

        protected void btnSave_Click(
            object sender,
            EventArgs e)
        {
            Auth.RequirePermission("LoansEdit");

            try
            {
                if (ddlEmployee.Items.Count == 0)
                {
                    ShowError(
                        "No active employees found."
                    );

                    return;
                }

                DateTime txnDate;

                if (!DateTime.TryParse(
                    txtDate.Text,
                    out txnDate))
                {
                    ShowError(
                        "Please enter a valid transaction date."
                    );

                    return;
                }

                int employeeId =
                    Convert.ToInt32(
                        ddlEmployee.SelectedValue
                    );

                decimal opening =
                    GetLatestBalance(
                        employeeId
                    );

                decimal availed =
                    M(txtAvailed.Text);

                decimal deduction =
                    M(txtDeduction.Text);

                if (availed < 0)
                {
                    ShowError(
                        "Loan availed cannot be negative."
                    );

                    return;
                }

                if (deduction < 0)
                {
                    ShowError(
                        "Deduction cannot be negative."
                    );

                    return;
                }

                decimal availableBalance =
                    opening + availed;

                if (deduction > availableBalance)
                {
                    ShowError(
                        "Deduction cannot be greater than the available loan balance."
                    );

                    return;
                }

                decimal net =
                    availableBalance -
                    deduction;

                Db.Execute(
                    @"INSERT INTO Loans
                    (
                        EmployeeID,
                        TxnDate,
                        OpeningBalance,
                        LoanAvailed,
                        Deduction,
                        NetBalance,
                        Remarks
                    )
                    VALUES
                    (
                        ?,?,?,?,?,?,?
                    )",
                    P(employeeId),
                    P(txnDate),
                    P(opening),
                    P(availed),
                    P(deduction),
                    P(net),
                    P(txtRemarks.Text.Trim())
                );

                ShowSuccess(
                    "Loan entry saved. Closing balance: ₹" +
                    net.ToString("N2")
                );

                txtOpening.Text =
                    net.ToString("0.00");

                txtAvailed.Text = "0";
                txtDeduction.Text = "0";
                txtRemarks.Text = "";

                BindLoans();
            }
            catch (Exception ex)
            {
                ShowError(
                    ex.Message
                );
            }
        }

        private decimal GetLatestBalance(
            int employeeId)
        {
            DataTable dt = Db.Query(
                @"SELECT TOP 1
                    NetBalance
                  FROM Loans
                  WHERE EmployeeID=?
                  ORDER BY
                    TxnDate DESC,
                    LoanID DESC",
                P(employeeId)
            );

            if (dt.Rows.Count == 0)
            {
                return 0m;
            }

            if (dt.Rows[0]["NetBalance"] ==
                DBNull.Value)
            {
                return 0m;
            }

            return Convert.ToDecimal(
                dt.Rows[0]["NetBalance"]
            );
        }

        private void BindLoans()
        {
            gvLoans.DataSource = Db.Query(
                @"SELECT
                    L.LoanID,
                    E.EmployeeName,
                    E.BranchCode,
                    L.TxnDate,
                    L.OpeningBalance,
                    L.LoanAvailed,
                    L.Deduction,
                    L.NetBalance,
                    L.Remarks
                  FROM Loans L
                  INNER JOIN Employees E
                    ON L.EmployeeID =
                       E.EmployeeID
                  ORDER BY
                    L.TxnDate DESC,
                    L.LoanID DESC"
            );

            gvLoans.DataBind();
        }

        private void ShowSuccess(
            string message)
        {
            lblMsg.CssClass =
                "message";

            lblMsg.Text =
                message;
        }

        private void ShowError(
            string message)
        {
            lblMsg.CssClass =
                "message error";

            lblMsg.Text =
                message;
        }

        private OleDbParameter P(
            object value)
        {
            return new OleDbParameter(
                "?",
                value ?? DBNull.Value
            );
        }

        private decimal M(
            string text)
        {
            decimal value;

            return Decimal.TryParse(
                text,
                out value
            )
                ? value
                : 0m;
        }
    }
}
