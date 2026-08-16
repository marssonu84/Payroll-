using System;
using System.Data;
using System.Data.OleDb;
using System.Web.UI;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;

namespace PayrollWebApp
{
    public class Login : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            Auth.EnsureSecuritySchema();

            /*
             * IMPORTANT:
             * Do NOT clear the session here.
             *
             * If the user is already logged in and
             * manually opens Login.aspx, send them
             * back to the dashboard.
             */
            if (!IsPostBack)
            {
                if (Auth.IsLoggedIn())
                {
                    Response.Redirect(
                        "~/Default.aspx",
                        false
                    );

                    Context.ApplicationInstance
                        .CompleteRequest();

                    return;
                }

                Label lblSuccess =
                    FindControl("lblSuccess")
                    as Label;

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
                FindControl("lblError")
                as Label;

            try
            {
                HtmlInputText txtUsername =
                    FindControl("txtUsername")
                    as HtmlInputText;

                HtmlInputPassword txtPassword =
                    FindControl("txtPassword")
                    as HtmlInputPassword;

                string username =
                    txtUsername != null
                    ? (txtUsername.Value ?? "").Trim()
                    : "";

                string password =
                    txtPassword != null
                    ? (txtPassword.Value ?? "")
                    : "";

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

                DataTable dt =
                    Db.Query(
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
                            username
                        )
                    );

                if (dt == null ||
                    dt.Rows.Count == 0)
                {
                    ShowInvalidLogin(lblError);
                    return;
                }

                DataRow row =
                    dt.Rows[0];

                if (row["IsActive"] != DBNull.Value &&
                    !Convert.ToBoolean(
                        row["IsActive"]))
                {
                    if (lblError != null)
                    {
                        lblError.Text =
                            "This user account is inactive. Please contact the administrator.";
                    }

                    return;
                }

                string storedHash =
                    Convert.ToString(
                        row["password_hash"]
                    );

                bool passwordCorrect =
                    Auth.VerifyPassword(
                        password,
                        storedHash
                    );

                if (!passwordCorrect)
                {
                    ShowInvalidLogin(lblError);
                    return;
                }

                int userId =
                    Convert.ToInt32(
                        row["id"]
                    );

                /*
                 * Upgrade legacy password hash
                 * after successful login.
                 */
                if (Auth.NeedsPasswordUpgrade(
                    storedHash))
                {
                    string newHash =
                        Auth.HashPassword(
                            password
                        );

                    Db.Execute(
                        @"UPDATE Users
                          SET password_hash=?
                          WHERE id=?",
                        new OleDbParameter(
                            "?",
                            newHash
                        ),
                        new OleDbParameter(
                            "?",
                            userId
                        )
                    );
                }

                /*
                 * Start clean authenticated session.
                 * Clearing here is correct because
                 * authentication has succeeded.
                 */
                Session.Clear();

                Session["UserID"] =
                    userId;

                Session["Username"] =
                    Convert.ToString(
                        row["username"]
                    );

                Session["IsAdmin"] =
                    row["IsAdmin"] != DBNull.Value &&
                    Convert.ToBoolean(
                        row["IsAdmin"]
                    );

                Session.Timeout = 30;

                Response.Redirect(
                    "~/Default.aspx",
                    false
                );

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
