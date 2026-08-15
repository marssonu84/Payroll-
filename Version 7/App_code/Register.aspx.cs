using System;
using System.Data;
using System.Data.OleDb;
using System.Text.RegularExpressions;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace PayrollWebApp
{
    public class Register : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            Auth.RequireAdmin();
            Auth.EnsureSecuritySchema();
            if (!IsPostBack) { ClearForm(); BindUsers(); }
        }

        protected void btnSave_Click(object sender, EventArgs e)
        {
            Label msg = F<Label>("lblMessage");
            try
            {
                HiddenField hf = F<HiddenField>("hfUserID");
                int userId; bool editing = Int32.TryParse(hf.Value, out userId) && userId > 0;
                string username = (F<TextBox>("txtUsername")).Text.Trim();
                string password = (F<TextBox>("txtPassword")).Text;
                string confirm = (F<TextBox>("txtConfirmPassword")).Text;
                bool isAdmin = (F<DropDownList>("ddlRole")).SelectedValue == "Admin";
                bool isActive = (F<CheckBox>("chkActive")).Checked;

                string error = ValidateInput(username, password, confirm, editing);
                if (!String.IsNullOrEmpty(error)) { ShowError(msg, error); return; }

                DataTable duplicate = Db.Query("SELECT id FROM Users WHERE username=? AND id<>?", new OleDbParameter("?", username), new OleDbParameter("?", editing ? userId : 0));
                if (duplicate.Rows.Count > 0) { ShowError(msg, "This username already exists."); return; }

                if (editing)
                {
                    if (userId == Auth.CurrentUserId() && (!isAdmin || !isActive)) { ShowError(msg, "You cannot remove administrator rights from, or deactivate, your own logged-in account."); return; }
                    if (WouldRemoveLastAdmin(userId, isAdmin, isActive)) { ShowError(msg, "The last active administrator cannot be downgraded or deactivated."); return; }
                    Db.Execute("UPDATE Users SET username=?, IsAdmin=?, IsActive=? WHERE id=?", P(username), P(isAdmin), P(isActive), P(userId));
                    if (!String.IsNullOrEmpty(password)) Db.Execute("UPDATE Users SET password_hash=? WHERE id=?", P(Auth.HashPassword(password)), P(userId));
                }
                else
                {
                    object newIdObj = Db.Scalar("INSERT INTO Users(username,password_hash,IsAdmin,IsActive) OUTPUT INSERTED.id VALUES(?,?,?,?)", P(username), P(Auth.HashPassword(password)), P(isAdmin), P(isActive));
                    userId = Convert.ToInt32(newIdObj);
                }

                SavePermissions(userId, isAdmin);
                ShowSuccess(msg, editing ? "User account updated successfully." : "User account created successfully.");
                ClearForm(); BindUsers();
            }
            catch { ShowError(msg, "The user account could not be saved. Please try again."); }
        }

        protected void btnCancel_Click(object sender, EventArgs e) { ClearForm(); F<Label>("lblMessage").Text = ""; }

        protected void gvUsers_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            int userId; if (!Int32.TryParse(Convert.ToString(e.CommandArgument), out userId)) return;
            Label msg = F<Label>("lblMessage");
            try
            {
                if (e.CommandName == "EditUser") { LoadUser(userId); return; }
                if (e.CommandName == "ToggleUser")
                {
                    if (userId == Auth.CurrentUserId()) { ShowError(msg, "You cannot deactivate your own logged-in account."); return; }
                    DataTable dt = Db.Query("SELECT IsAdmin,IsActive FROM Users WHERE id=?", P(userId)); if (dt.Rows.Count == 0) return;
                    bool admin = Convert.ToBoolean(dt.Rows[0]["IsAdmin"]); bool active = Convert.ToBoolean(dt.Rows[0]["IsActive"]);
                    if (active && admin && WouldRemoveLastAdmin(userId, true, false)) { ShowError(msg, "The last active administrator cannot be deactivated."); return; }
                    Db.Execute("UPDATE Users SET IsActive=? WHERE id=?", P(!active), P(userId));
                    ShowSuccess(msg, active ? "User account deactivated." : "User account activated."); BindUsers(); return;
                }
                if (e.CommandName == "DeleteUser")
                {
                    if (userId == Auth.CurrentUserId()) { ShowError(msg, "You cannot delete your own logged-in account."); return; }
                    DataTable dt = Db.Query("SELECT IsAdmin,IsActive FROM Users WHERE id=?", P(userId)); if (dt.Rows.Count == 0) return;
                    bool admin = Convert.ToBoolean(dt.Rows[0]["IsAdmin"]); bool active = Convert.ToBoolean(dt.Rows[0]["IsActive"]);
                    if (admin && active && WouldRemoveLastAdmin(userId, false, false)) { ShowError(msg, "The last active administrator cannot be deleted."); return; }
                    Db.Execute("DELETE FROM UserPermissions WHERE UserID=?", P(userId));
                    Db.Execute("DELETE FROM Users WHERE id=?", P(userId));
                    ShowSuccess(msg, "User account deleted."); ClearForm(); BindUsers();
                }
            }
            catch { ShowError(msg, "The requested user action could not be completed."); }
        }

        private void BindUsers()
        {
            GridView gv = F<GridView>("gvUsers");
            gv.DataSource = Db.Query(@"SELECT id,username,CASE WHEN IsAdmin=1 THEN 'Administrator' ELSE 'Standard User' END AS RoleName,CASE WHEN IsActive=1 THEN 'Active' ELSE 'Inactive' END AS StatusName,IsActive FROM Users ORDER BY username");
            gv.DataBind();
        }

        private void LoadUser(int userId)
        {
            DataTable u = Db.Query("SELECT id,username,IsAdmin,IsActive FROM Users WHERE id=?", P(userId)); if (u.Rows.Count == 0) return;
            DataRow r = u.Rows[0];
            (F<HiddenField>("hfUserID")).Value = Convert.ToString(userId);
            (F<TextBox>("txtUsername")).Text = Convert.ToString(r["username"]);
            (F<DropDownList>("ddlRole")).SelectedValue = Convert.ToBoolean(r["IsAdmin"]) ? "Admin" : "User";
            (F<CheckBox>("chkActive")).Checked = Convert.ToBoolean(r["IsActive"]);
            (F<TextBox>("txtPassword")).Text = ""; (F<TextBox>("txtConfirmPassword")).Text = "";
            DataTable p = Db.Query("SELECT * FROM UserPermissions WHERE UserID=?", P(userId));
            SetChecks(p.Rows.Count == 0 ? null : p.Rows[0], Convert.ToBoolean(r["IsAdmin"]));
            (F<Button>("btnSave")).Text = "Save User Changes";
            (F<Button>("btnCancel")).Visible = true;
            (F<Literal>("litMode")).Text = "Edit User";
        }

        private void SavePermissions(int userId, bool admin)
        {
            bool ev = admin || C("chkEmployeesView"); bool ee = admin || C("chkEmployeesEdit");
            bool pv = admin || C("chkPayrollView"); bool pp = admin || C("chkPayrollProcess");
            bool lv = admin || C("chkLoansView"); bool le = admin || C("chkLoansEdit"); bool rv = admin || C("chkReportsView");
            Db.Execute(@"IF EXISTS(SELECT 1 FROM UserPermissions WHERE UserID=?) UPDATE UserPermissions SET EmployeesView=?,EmployeesEdit=?,PayrollView=?,PayrollProcess=?,LoansView=?,LoansEdit=?,ReportsView=? WHERE UserID=? ELSE INSERT INTO UserPermissions(UserID,EmployeesView,EmployeesEdit,PayrollView,PayrollProcess,LoansView,LoansEdit,ReportsView) VALUES(?,?,?,?,?,?,?,?)",
                P(userId),P(ev),P(ee),P(pv),P(pp),P(lv),P(le),P(rv),P(userId),P(userId),P(ev),P(ee),P(pv),P(pp),P(lv),P(le),P(rv));
        }

        private T F<T>(string id) where T : System.Web.UI.Control
        {
            T found = FindRecursive<T>(this, id);
            if (found == null) throw new InvalidOperationException("Required control not found: " + id);
            return found;
        }

        private static T FindRecursive<T>(System.Web.UI.Control root, string id) where T : System.Web.UI.Control
        {
            foreach (System.Web.UI.Control child in root.Controls)
            {
                if (child.ID == id && child is T) return (T)child;
                T nested = FindRecursive<T>(child, id);
                if (nested != null) return nested;
            }
            return null;
        }

        private bool C(string id) { return F<CheckBox>(id).Checked; }
        private void SetChecks(DataRow r, bool admin)
        {
            string[] ids={"chkEmployeesView","chkEmployeesEdit","chkPayrollView","chkPayrollProcess","chkLoansView","chkLoansEdit","chkReportsView"};
            string[] cols={"EmployeesView","EmployeesEdit","PayrollView","PayrollProcess","LoansView","LoansEdit","ReportsView"};
            for(int i=0;i<ids.Length;i++) F<CheckBox>(ids[i]).Checked = admin || (r!=null && Convert.ToBoolean(r[cols[i]]));
        }

        private void ClearForm()
        {
            (F<HiddenField>("hfUserID")).Value=""; (F<TextBox>("txtUsername")).Text=""; (F<TextBox>("txtPassword")).Text=""; (F<TextBox>("txtConfirmPassword")).Text="";
            (F<DropDownList>("ddlRole")).SelectedValue="User"; (F<CheckBox>("chkActive")).Checked=true;
            string[] ids={"chkEmployeesView","chkEmployeesEdit","chkPayrollView","chkPayrollProcess","chkLoansView","chkLoansEdit","chkReportsView"}; foreach(string id in ids) F<CheckBox>(id).Checked=false;
            (F<Button>("btnSave")).Text="Create Account"; (F<Button>("btnCancel")).Visible=false; (F<Literal>("litMode")).Text="Create User";
        }

        private bool WouldRemoveLastAdmin(int userId, bool newIsAdmin, bool newIsActive)
        {
            if (newIsAdmin && newIsActive) return false;
            object count = Db.Scalar("SELECT COUNT(*) FROM Users WHERE IsAdmin=1 AND IsActive=1 AND id<>?", P(userId));
            return Convert.ToInt32(count) == 0;
        }

        private static string ValidateInput(string username,string password,string confirm,bool editing)
        {
            if (String.IsNullOrWhiteSpace(username)) return "Username is required.";
            if (!Regex.IsMatch(username,@"^[A-Za-z0-9._-]{3,50}$")) return "Username must be 3 to 50 characters and may contain only letters, numbers, dot, underscore or hyphen.";
            if (!editing || !String.IsNullOrEmpty(password))
            {
                if (password.Length<8 || !Regex.IsMatch(password,@"[A-Z]") || !Regex.IsMatch(password,@"[a-z]") || !Regex.IsMatch(password,@"[0-9]") || !Regex.IsMatch(password,@"[^A-Za-z0-9]")) return "Password must contain at least 8 characters, including uppercase, lowercase, a number and a special character.";
                if (!String.Equals(password,confirm,StringComparison.Ordinal)) return "Password and confirm password do not match.";
            }
            return "";
        }
        private static OleDbParameter P(object value) { return new OleDbParameter("?", value ?? DBNull.Value); }
        private static void ShowError(Label l,string m){ l.CssClass="message error-message"; l.Text=m; }
        private static void ShowSuccess(Label l,string m){ l.CssClass="message success-message"; l.Text=m; }
    }
}
