using System;
using System.Data;
using System.Data.OleDb;
using System.Web.UI.WebControls;

namespace PayrollWebApp
{
    public partial class PayrollPage : System.Web.UI.Page
    {
        protected void Page_Load(object s, EventArgs e)
        {
            Auth.RequirePermission("PayrollView");

            btnProcess.Visible =
                Auth.HasPermission("PayrollProcess");

            if (!IsPostBack)
            {
                txtMonth.Text =
                    DateTime.Today.ToString("yyyy-MM");

                DataTable dt = Db.Query(
                    @"SELECT BranchCode
                      FROM Branches
                      WHERE IsActive=True
                      ORDER BY BranchCode"
                );

                ddlBranch.DataSource = dt;
                ddlBranch.DataTextField = "BranchCode";
                ddlBranch.DataValueField = "BranchCode";
                ddlBranch.DataBind();
            }
        }

        protected void btnLoad_Click(
            object s,
            EventArgs e)
        {
            Auth.RequirePermission("PayrollView");

            BindPayroll(
                BuildTableFromEmployees()
            );
        }

        protected void btnProcess_Click(
            object s,
            EventArgs e)
        {
            Auth.RequirePermission("PayrollProcess");

            try
            {
                DateTime month =
                    DateTime.Parse(
                        txtMonth.Text + "-01"
                    );

                int days = I(
                    txtDays.Text,
                    DateTime.DaysInMonth(
                        month.Year,
                        month.Month
                    )
                );

                foreach (GridViewRow row in gvPayroll.Rows)
                {
                    int empId =
                        Convert.ToInt32(
                            gvPayroll.DataKeys[
                                row.RowIndex
                            ].Value
                        );

                    DataTable empTable = Db.Query(
                        @"SELECT *
                          FROM Employees
                          WHERE EmployeeID=?",
                        P(empId)
                    );

                    if (empTable.Rows.Count == 0)
                        continue;

                    DataRow emp =
                        empTable.Rows[0];

                    TextBox leavesBox =
                        row.FindControl("txtLeaves")
                        as TextBox;

                    TextBox advanceBox =
                        row.FindControl("txtAdvance")
                        as TextBox;

                    TextBox otherBox =
                        row.FindControl("txtOther")
                        as TextBox;

                    decimal leaves =
                        leavesBox == null
                            ? 0m
                            : M(leavesBox.Text);

                    decimal adv =
                        advanceBox == null
                            ? 0m
                            : M(advanceBox.Text);

                    decimal other =
                        otherBox == null
                            ? 0m
                            : M(otherBox.Text);

                    var r =
                        PayrollCalculator.Calculate(
                            V(emp["StandardSalaryPrev"]),
                            V(emp["IncrementAmount"]),
                            V(emp["SalaryAdjustment"]),
                            days,
                            leaves,
                            B(emp["PFMember"]),
                            B(emp["ESIMember"]),
                            adv,
                            other
                        );

                    Db.Execute(
                        @"DELETE FROM Payroll
                          WHERE EmployeeID=?
                          AND SalaryMonth=?",
                        P(empId),
                        P(month)
                    );

                    Db.Execute(
                        @"INSERT INTO Payroll
                        (
                            EmployeeID,
                            SalaryMonth,
                            DaysInMonth,
                            Leaves,
                            StandardSalary,
                            LOP,
                            GrossSalary,
                            Basic,
                            HRA,
                            OvertimeAllowance,
                            PF,
                            ESI,
                            PT,
                            SalaryAdvance,
                            OtherDeductions,
                            TotalDeductions,
                            NetPay,
                            CreatedOn
                        )
                        VALUES
                        (
                            ?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?
                        )",
                        P(empId),
                        P(month),
                        P(days),
                        P(leaves),
                        P(r.StandardSalary),
                        P(r.Lop),
                        P(r.Gross),
                        P(r.Basic),
                        P(r.Hra),
                        P(r.OvertimeAllowance),
                        P(r.Pf),
                        P(r.Esi),
                        P(r.Pt),
                        P(adv),
                        P(other),
                        P(r.TotalDeductions),
                        P(r.NetPay),
                        P(DateTime.Now)
                    );
                }

                lblMsg.CssClass =
                    "message";

                lblMsg.Text =
                    "Payroll calculated and saved for " +
                    month.ToString("MMMM yyyy") +
                    ".";

                BindPayroll(
                    BuildTableFromDatabase(month)
                );
            }
            catch (Exception ex)
            {
                lblMsg.CssClass =
                    "message error";

                lblMsg.Text =
                    ex.Message;
            }
        }

        private DataTable BuildTableFromEmployees()
        {
            DataTable src = Db.Query(
                @"SELECT
                    EmployeeID,
                    EmployeeName,
                    Designation,
                    StandardSalaryPrev,
                    IncrementAmount,
                    SalaryAdjustment,
                    PFMember,
                    ESIMember
                  FROM Employees
                  WHERE IsActive=True
                  AND BranchCode=?
                  ORDER BY EmployeeName",
                P(ddlBranch.SelectedValue)
            );

            return Build(src, null);
        }

        private DataTable BuildTableFromDatabase(
            DateTime month)
        {
            DataTable src = Db.Query(
                @"SELECT
                    E.EmployeeID,
                    E.EmployeeName,
                    E.Designation,
                    E.StandardSalaryPrev,
                    E.IncrementAmount,
                    E.SalaryAdjustment,
                    P.Leaves,
                    P.LOP,
                    P.GrossSalary,
                    P.Basic,
                    P.HRA,
                    P.OvertimeAllowance,
                    P.PF,
                    P.ESI,
                    P.PT,
                    P.SalaryAdvance,
                    P.OtherDeductions,
                    P.TotalDeductions,
                    P.NetPay
                  FROM Employees E
                  INNER JOIN Payroll P
                    ON E.EmployeeID=P.EmployeeID
                  WHERE E.BranchCode=?
                  AND P.SalaryMonth=?
                  ORDER BY E.EmployeeName",
                P(ddlBranch.SelectedValue),
                P(month)
            );

            return Build(src, month);
        }

        private DataTable Build(
            DataTable src,
            DateTime? saved)
        {
            DataTable dt =
                new DataTable();

            string[] columns =
            {
                "EmployeeID",
                "EmployeeName",
                "Designation",
                "StandardSalary",
                "Leaves",
                "LOP",
                "GrossSalary",
                "Basic",
                "HRA",
                "OvertimeAllowance",
                "PF",
                "ESI",
                "PT",
                "SalaryAdvance",
                "OtherDeductions",
                "TotalDeductions",
                "NetPay"
            };

            foreach (string c in columns)
            {
                Type columnType;

                if (c == "EmployeeName" ||
                    c == "Designation")
                {
                    columnType =
                        typeof(string);
                }
                else if (c == "EmployeeID")
                {
                    columnType =
                        typeof(int);
                }
                else
                {
                    columnType =
                        typeof(decimal);
                }

                dt.Columns.Add(
                    c,
                    columnType
                );
            }

            DateTime selectedMonth =
                DateTime.Parse(
                    txtMonth.Text + "-01"
                );

            int days = I(
                txtDays.Text,
                DateTime.DaysInMonth(
                    selectedMonth.Year,
                    selectedMonth.Month
                )
            );

            foreach (DataRow e in src.Rows)
            {
                DataRow n =
                    dt.NewRow();

                n["EmployeeID"] =
                    Convert.ToInt32(
                        e["EmployeeID"]
                    );

                n["EmployeeName"] =
                    e["EmployeeName"];

                n["Designation"] =
                    e["Designation"];

                decimal leaves =
                    saved.HasValue
                        ? V(e["Leaves"])
                        : 0m;

                decimal adv =
                    saved.HasValue
                        ? V(e["SalaryAdvance"])
                        : 0m;

                decimal other =
                    saved.HasValue
                        ? V(e["OtherDeductions"])
                        : 0m;

                n["StandardSalary"] =
                    V(e["StandardSalaryPrev"]) +
                    V(e["IncrementAmount"]) +
                    V(e["SalaryAdjustment"]);

                n["Leaves"] =
                    leaves;

                n["SalaryAdvance"] =
                    adv;

                n["OtherDeductions"] =
                    other;

                if (saved.HasValue)
                {
                    n["LOP"] =
                        V(e["LOP"]);

                    n["GrossSalary"] =
                        V(e["GrossSalary"]);

                    n["Basic"] =
                        V(e["Basic"]);

                    n["HRA"] =
                        V(e["HRA"]);

                    n["OvertimeAllowance"] =
                        V(e["OvertimeAllowance"]);

                    n["PF"] =
                        V(e["PF"]);

                    n["ESI"] =
                        V(e["ESI"]);

                    n["PT"] =
                        V(e["PT"]);

                    n["TotalDeductions"] =
                        V(e["TotalDeductions"]);

                    n["NetPay"] =
                        V(e["NetPay"]);
                }
                else
                {
                    var r =
                        PayrollCalculator.Calculate(
                            V(e["StandardSalaryPrev"]),
                            V(e["IncrementAmount"]),
                            V(e["SalaryAdjustment"]),
                            days,
                            leaves,
                            B(e["PFMember"]),
                            B(e["ESIMember"]),
                            adv,
                            other
                        );

                    n["LOP"] =
                        r.Lop;

                    n["GrossSalary"] =
                        r.Gross;

                    n["Basic"] =
                        r.Basic;

                    n["HRA"] =
                        r.Hra;

                    n["OvertimeAllowance"] =
                        r.OvertimeAllowance;

                    n["PF"] =
                        r.Pf;

                    n["ESI"] =
                        r.Esi;

                    n["PT"] =
                        r.Pt;

                    n["TotalDeductions"] =
                        r.TotalDeductions;

                    n["NetPay"] =
                        r.NetPay;
                }

                dt.Rows.Add(n);
            }

            return dt;
        }

        private void BindPayroll(
            DataTable dt)
        {
            gvPayroll.DataSource = dt;
            gvPayroll.DataBind();
        }

        private OleDbParameter P(
            object v)
        {
            return new OleDbParameter(
                "?",
                v ?? DBNull.Value
            );
        }

        private decimal M(
            string s)
        {
            decimal x;

            return Decimal.TryParse(
                s,
                out x
            )
                ? x
                : 0m;
        }

        private int I(
            string s,
            int d)
        {
            int x;

            return Int32.TryParse(
                s,
                out x
            )
                ? x
                : d;
        }

        private decimal V(
            object o)
        {
            return
                o == null ||
                o == DBNull.Value
                    ? 0m
                    : Convert.ToDecimal(o);
        }

        private bool B(
            object o)
        {
            return
                o != null &&
                o != DBNull.Value &&
                Convert.ToBoolean(o);
        }
    }
}
