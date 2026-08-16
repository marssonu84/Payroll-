using System;
using System.Data;
using System.Data.OleDb;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace PayrollWebApp
{
    public class Login : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            Auth.EnsureSecuritySchema();

            if (!IsPostBack)
            {
                Session.Clear();

                Label lblSuccess =
                    (Label)FindControl("lblSuccess");

                if (lblSuccess != null &&
                    String.Equals(
                        Request.QueryString["registered"],
                        "1",
                        StringComparison.Ordinal))
                {
                    lblSuccess.Text =
                        "Account created successfully. Please login.";
                }
            }
        }

        protected void btnLogin_Click(
            object sender,
            EventArgs e)
        {
            Label lblError =
                (Label)FindControl("lblError");

            try
            {
                string username =
                    (Request.Form["txtUsername"] ?? "")
                    .Trim();

                string password =
                    (Request.Form["txtPassword"] ?? "");

                if (String.IsNullOrWhiteSpace(username) ||
                    String.IsNullOrWhiteSpace(password))
                {
                    if (lblError != null)
                    {
                        lblError.Text =
                            "Please enter both username and password.";
                    }

                    return;
                }

                /*
                 * IMPORTANT:
                 * Your actual Users table currently contains:
                 *
                 * id
                 * username
                 * password_hash
                 * IsAdmin
                 */

                DataTable dt = Db.Query(
                    @"SELECT
                        id,
                        username,
                        password_hash,
                        IsAdmin,
                        IsActive
                      FROM Users
                      WHERE username=?",
                    new OleDbParameter(
                        "?",
                        username)
                );

                if (dt == null ||
                    dt.Rows.Count == 0)
                {
                    ShowInvalidLogin(lblError);
                    return;
                }

                DataRow row = dt.Rows[0];

                if (row["IsActive"] != DBNull.Value && !Convert.ToBoolean(row["IsActive"]))
                {
                    if (lblError != null) lblError.Text = "This user account is inactive. Please contact the administrator.";
                    return;
                }

                string storedHash =
                    Convert.ToString(
                        row["password_hash"]);

                bool passwordCorrect =
                    Auth.VerifyPassword(
                        password,
                        storedHash);

                if (!passwordCorrect)
                {
                    ShowInvalidLogin(lblError);
                    return;
                }

                int userId =
                    Convert.ToInt32(
                        row["id"]);

                /*
                 * Upgrade old password hash to
                 * PBKDF2 after successful login.
                 */
                if (Auth.NeedsPasswordUpgrade(
                    storedHash))
                {
                    string newHash =
                        Auth.HashPassword(password);

                    Db.Execute(
                        @"UPDATE Users
                          SET password_hash=?
                          WHERE id=?",
                        new OleDbParameter(
                            "?",
                            newHash),
                        new OleDbParameter(
                            "?",
                            userId)
                    );
                }

                /*
                 * Create authenticated session.
                 *
                 * TenantID = 1 temporarily because
                 * the existing Users table currently
                 * has no TenantID column.
                 */

                Session.Clear();

                Session["UserID"] = userId;
               
                Session["Username"] =
                    Convert.ToString(
                        row["username"]);

                Session["IsAdmin"] =
                    row["IsAdmin"] != DBNull.Value &&
                    Convert.ToBoolean(row["IsAdmin"]);

                Session.Timeout = 30;

                Response.Redirect(
                    "~/Default.aspx",
                    false);

                Context.ApplicationInstance
                    .CompleteRequest();
            }
            catch (Exception)
{
    if (lblError != null)
    {
        lblError.Text =
            "Login could not be completed. Please try again.";
    }
}
        }

        private void ShowInvalidLogin(
            Label lblError)
        {
            if (lblError != null)
            {
                lblError.Text =
                    "Invalid username or password.";
            }
        }
    }
}


