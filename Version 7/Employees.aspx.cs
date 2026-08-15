using System;
using System.Data;
using System.Data.OleDb;
using System.Web.UI.WebControls;

namespace PayrollWebApp
{
    public partial class Employees : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            Auth.RequirePermission("EmployeesView");

            bool canEdit = Auth.HasPermission("EmployeesEdit");

            btnSave.Visible = canEdit;

            if (gvEmployees.Columns.Count > 0)
            {
                gvEmployees.Columns[
                    gvEmployees.Columns.Count - 1
                ].Visible = canEdit;
            }

            if (!IsPostBack)
            {
                BindBranches();
                ddlStatus.SelectedValue = "1";
                chkActive.Checked = true;
                BindGrid();
            }
        }

        private void BindBranches()
        {
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

            ddlFilter.Items.Clear();
            ddlFilter.Items.Add(
                new ListItem("All Branches", "")
            );

            foreach (DataRow row in dt.Rows)
            {
                string branch =
                    Convert.ToString(row["BranchCode"]);

                ddlFilter.Items.Add(
                    new ListItem(branch, branch)
                );
            }
        }

        protected void btnSave_Click(
            object sender,
            EventArgs e)
        {
            Auth.RequirePermission("EmployeesEdit");

            try
            {
                string employeeCode =
                    txtEmployeeCode.Text.Trim();

                string employeeName =
                    txtName.Text.Trim();

                if (String.IsNullOrWhiteSpace(employeeCode))
                {
                    ShowError("Employee Code is required.");
                    return;
                }

                if (String.IsNullOrWhiteSpace(employeeName))
                {
                    ShowError("Employee Name is required.");
                    return;
                }

                if (String.IsNullOrEmpty(
                    ddlBranch.SelectedValue))
                {
                    ShowError("Please select a branch.");
                    return;
                }

                int? editingEmployeeId = null;

                if (!String.IsNullOrEmpty(
                    hfEmployeeID.Value))
                {
                    editingEmployeeId =
                        Convert.ToInt32(
                            hfEmployeeID.Value
                        );
                }

                if (EmployeeCodeExists(
                    employeeCode,
                    editingEmployeeId))
                {
                    ShowError(
                        "Employee Code already exists. Please use a unique code."
                    );
                    return;
                }

                DateTime? doj = null;
                DateTime parsedDate;

                if (!String.IsNullOrWhiteSpace(
                    txtDOJ.Text))
                {
                    if (!DateTime.TryParse(
                        txtDOJ.Text,
                        out parsedDate))
                    {
                        ShowError(
                            "Please enter a valid Date of Joining."
                        );
                        return;
                    }

                    doj = parsedDate;
                }

                decimal previousSalary =
                    M(txtPrevSalary.Text);

                decimal increment =
                    M(txtIncrement.Text);

                decimal adjustment =
                    M(txtAdjustment.Text);

                if (previousSalary < 0 ||
                    increment < 0)
                {
                    ShowError(
                        "Salary and Increment cannot be negative."
                    );
                    return;
                }

                if (chkPF.Checked &&
                    String.IsNullOrWhiteSpace(
                        txtUAN.Text))
                {
                    ShowError(
                        "UAN Number is required for PF members."
                    );
                    return;
                }

                if (chkESI.Checked &&
                    String.IsNullOrWhiteSpace(
                        txtESINo.Text))
                {
                    ShowError(
                        "ESI Number is required for ESI members."
                    );
                    return;
                }

                if (editingEmployeeId == null)
                {
                    InsertEmployee(
                        employeeCode,
                        employeeName,
                        doj,
                        previousSalary,
                        increment,
                        adjustment
                    );

                    ShowSuccess(
                        "Employee saved successfully."
                    );
                }
                else
                {
                    UpdateEmployee(
                        editingEmployeeId.Value,
                        employeeCode,
                        employeeName,
                        doj,
                        previousSalary,
                        increment,
                        adjustment
                    );

                    ShowSuccess(
                        "Employee updated successfully."
                    );
                }

                ClearForm();
                BindGrid();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void InsertEmployee(
            string employeeCode,
            string employeeName,
            DateTime? doj,
            decimal previousSalary,
            decimal increment,
            decimal adjustment)
        {
            Db.Execute(
                @"INSERT INTO Employees
                (
                    EmployeeCode,
                    EmployeeName,
                    Designation,
                    BranchCode,
                    DateOfJoining,
                    PFMember,
                    ESIMember,
                    UANNo,
                    ESINo,
                    BankAccount,
                    BankName,
                    IFSCCode,
                    StandardSalaryPrev,
                    IncrementAmount,
                    SalaryAdjustment,
                    IsActive
                )
                VALUES
                (
                    ?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?
                )",

                P(employeeCode),
                P(employeeName),
                P(txtDesignation.Text.Trim()),
                P(ddlBranch.SelectedValue),

                P(doj.HasValue
                    ? (object)doj.Value
                    : DBNull.Value),

                P(chkPF.Checked),
                P(chkESI.Checked),
                P(txtUAN.Text.Trim()),
                P(txtESINo.Text.Trim()),
                P(txtAccount.Text.Trim()),
                P(txtBank.Text.Trim()),
                P(txtIFSC.Text.Trim().ToUpper()),
                P(previousSalary),
                P(increment),
                P(adjustment),
                P(chkActive.Checked)
            );
        }

        private void UpdateEmployee(
            int employeeId,
            string employeeCode,
            string employeeName,
            DateTime? doj,
            decimal previousSalary,
            decimal increment,
            decimal adjustment)
        {
            Db.Execute(
                @"UPDATE Employees
                  SET
                    EmployeeCode=?,
                    EmployeeName=?,
                    Designation=?,
                    BranchCode=?,
                    DateOfJoining=?,
                    PFMember=?,
                    ESIMember=?,
                    UANNo=?,
                    ESINo=?,
                    BankAccount=?,
                    BankName=?,
                    IFSCCode=?,
                    StandardSalaryPrev=?,
                    IncrementAmount=?,
                    SalaryAdjustment=?,
                    IsActive=?
                  WHERE EmployeeID=?",

                P(employeeCode),
                P(employeeName),
                P(txtDesignation.Text.Trim()),
                P(ddlBranch.SelectedValue),

                P(doj.HasValue
                    ? (object)doj.Value
                    : DBNull.Value),

                P(chkPF.Checked),
                P(chkESI.Checked),
                P(txtUAN.Text.Trim()),
                P(txtESINo.Text.Trim()),
                P(txtAccount.Text.Trim()),
                P(txtBank.Text.Trim()),
                P(txtIFSC.Text.Trim().ToUpper()),
                P(previousSalary),
                P(increment),
                P(adjustment),
                P(chkActive.Checked),
                P(employeeId)
            );
        }

        private bool EmployeeCodeExists(
            string employeeCode,
            int? excludeEmployeeId)
        {
            DataTable dt;

            if (excludeEmployeeId.HasValue)
            {
                dt = Db.Query(
                    @"SELECT EmployeeID
                      FROM Employees
                      WHERE EmployeeCode=?
                      AND EmployeeID<>?",
                    P(employeeCode),
                    P(excludeEmployeeId.Value)
                );
            }
            else
            {
                dt = Db.Query(
                    @"SELECT EmployeeID
                      FROM Employees
                      WHERE EmployeeCode=?",
                    P(employeeCode)
                );
            }

            return dt.Rows.Count > 0;
        }

        protected void gvEmployees_RowCommand(
            object sender,
            GridViewCommandEventArgs e)
        {
            Auth.RequirePermission("EmployeesEdit");

            if (e.CommandName != "EditEmployee")
                return;

            try
            {
                int employeeId =
                    Convert.ToInt32(
                        e.CommandArgument
                    );

                DataTable dt = Db.Query(
                    @"SELECT
                        EmployeeID,
                        EmployeeCode,
                        EmployeeName,
                        Designation,
                        BranchCode,
                        DateOfJoining,
                        PFMember,
                        ESIMember,
                        UANNo,
                        ESINo,
                        BankAccount,
                        BankName,
                        IFSCCode,
                        StandardSalaryPrev,
                        IncrementAmount,
                        SalaryAdjustment,
                        IsActive
                      FROM Employees
                      WHERE EmployeeID=?",
                    P(employeeId)
                );

                if (dt.Rows.Count == 0)
                {
                    ShowError("Employee not found.");
                    return;
                }

                DataRow row = dt.Rows[0];

                hfEmployeeID.Value =
                    Convert.ToString(
                        row["EmployeeID"]
                    );

                txtEmployeeCode.Text =
                    Convert.ToString(
                        row["EmployeeCode"]
                    );

                txtName.Text =
                    Convert.ToString(
                        row["EmployeeName"]
                    );

                txtDesignation.Text =
                    Convert.ToString(
                        row["Designation"]
                    );

                string branch =
                    Convert.ToString(
                        row["BranchCode"]
                    );

                if (ddlBranch.Items.FindByValue(
                    branch) != null)
                {
                    ddlBranch.SelectedValue =
                        branch;
                }

                if (row["DateOfJoining"] != DBNull.Value)
                {
                    txtDOJ.Text =
                        Convert.ToDateTime(
                            row["DateOfJoining"]
                        ).ToString("yyyy-MM-dd");
                }
                else
                {
                    txtDOJ.Text = "";
                }

                chkPF.Checked =
                    B(row["PFMember"]);

                chkESI.Checked =
                    B(row["ESIMember"]);

                txtUAN.Text =
                    Convert.ToString(
                        row["UANNo"]
                    );

                txtESINo.Text =
                    Convert.ToString(
                        row["ESINo"]
                    );

                txtAccount.Text =
                    Convert.ToString(
                        row["BankAccount"]
                    );

                txtBank.Text =
                    Convert.ToString(
                        row["BankName"]
                    );

                txtIFSC.Text =
                    Convert.ToString(
                        row["IFSCCode"]
                    );

                txtPrevSalary.Text =
                    V(row["StandardSalaryPrev"])
                    .ToString("0.##");

                txtIncrement.Text =
                    V(row["IncrementAmount"])
                    .ToString("0.##");

                txtAdjustment.Text =
                    V(row["SalaryAdjustment"])
                    .ToString("0.##");

                chkActive.Checked =
                    B(row["IsActive"]);

                lblFormTitle.Text =
                    "Edit Employee";

                btnSave.Text =
                    "Update Employee";

                btnCancel.Visible = true;

                ShowSuccess(
                    "Editing " +
                    txtEmployeeCode.Text +
                    " - " +
                    txtName.Text
                );
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        protected void btnCancel_Click(
            object sender,
            EventArgs e)
        {
            ClearForm();

            lblMsg.Text = "";
            lblMsg.CssClass = "";
        }

        protected void ddlFilter_SelectedIndexChanged(
            object sender,
            EventArgs e)
        {
            BindGrid();
        }

        protected void ddlStatus_SelectedIndexChanged(
            object sender,
            EventArgs e)
        {
            BindGrid();
        }

        protected void txtSearch_TextChanged(
            object sender,
            EventArgs e)
        {
            BindGrid();
        }

        private void BindGrid()
        {
            string sql =
                @"SELECT
                    EmployeeID,
                    EmployeeCode,
                    EmployeeName,
                    Designation,
                    BranchCode,
                    DateOfJoining,
                    PFMember,
                    ESIMember,
                    BankAccount,
                    IFSCCode,
                    StandardSalaryPrev,
                    IncrementAmount,
                    SalaryAdjustment,
                    IsActive
                  FROM Employees
                  WHERE 1=1";

            System.Collections.Generic.List<OleDbParameter>
                parameters =
                new System.Collections.Generic.List<OleDbParameter>();

            if (!String.IsNullOrEmpty(
                ddlFilter.SelectedValue))
            {
                sql += " AND BranchCode=?";

                parameters.Add(
                    P(ddlFilter.SelectedValue)
                );
            }

            if (!String.IsNullOrEmpty(
                ddlStatus.SelectedValue))
            {
                sql += " AND IsActive=?";

                parameters.Add(
                    P(ddlStatus.SelectedValue == "1")
                );
            }

            if (!String.IsNullOrWhiteSpace(
                txtSearch.Text))
            {
                string search =
                    "%" +
                    txtSearch.Text.Trim() +
                    "%";

                sql +=
                    @" AND
                    (
                        EmployeeCode LIKE ?
                        OR EmployeeName LIKE ?
                        OR Designation LIKE ?
                    )";

                parameters.Add(P(search));
                parameters.Add(P(search));
                parameters.Add(P(search));
            }

            sql +=
                " ORDER BY BranchCode, EmployeeName";

            gvEmployees.DataSource =
                Db.Query(
                    sql,
                    parameters.ToArray()
                );

            gvEmployees.DataBind();
        }

        private void ClearForm()
        {
            hfEmployeeID.Value = "";

            txtEmployeeCode.Text = "";
            txtName.Text = "";
            txtDesignation.Text = "";
            txtDOJ.Text = "";

            txtBank.Text = "";
            txtAccount.Text = "";
            txtIFSC.Text = "";

            txtPrevSalary.Text = "0";
            txtIncrement.Text = "0";
            txtAdjustment.Text = "0";

            chkPF.Checked = false;
            chkESI.Checked = false;

            txtUAN.Text = "";
            txtESINo.Text = "";

            chkActive.Checked = true;

            lblFormTitle.Text =
                "Add Employee";

            btnSave.Text =
                "Save Employee";

            btnCancel.Visible = false;
        }

        private void ShowSuccess(string message)
        {
            lblMsg.CssClass =
                "message";

            lblMsg.Text =
                message;
        }

        private void ShowError(string message)
        {
            lblMsg.CssClass =
                "message error";

            lblMsg.Text =
                message;
        }

        private OleDbParameter P(object value)
        {
            return new OleDbParameter(
                "?",
                value ?? DBNull.Value
            );
        }

        private decimal M(string text)
        {
            decimal value;

            return Decimal.TryParse(
                text,
                out value
            )
                ? value
                : 0m;
        }

        private decimal V(object value)
        {
            return value == DBNull.Value
                ? 0m
                : Convert.ToDecimal(value);
        }

        private bool B(object value)
        {
            return
                value != DBNull.Value &&
                Convert.ToBoolean(value);
        }
    }
}

