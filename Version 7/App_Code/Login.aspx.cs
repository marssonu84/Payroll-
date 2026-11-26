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
                else if (lblSuccess != null &&
                    String.Equals(
                        Request.QueryString["pending"],
                        "1",
                        StringComparison.Ordinal))
                {
                    lblSuccess.Text =
                        "Registration submitted. Your entity is pending Super User approval. You can sign in once it is approved.";
                }
            }

            if (IsPostBack && Session["TwoStepUserID"] != null)
            {
                ShowTwoStepPanel(true);
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
                string clientIp = Request.UserHostAddress ?? "";
                int lockMinutesLeft;
                if (LoginThrottle.IsLocked(username, clientIp, out lockMinutesLeft))
                {
                    if (lblError != null)
                        lblError.Text = "Too many failed attempts. Please try again in " + lockMinutesLeft + " minute(s).";
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
                        IsActive,
                        TenantID,
                        IsOrgAdmin
                      FROM Users
                      WHERE username=?",
                    new OleDbParameter(
                        "?",
                        username)
                );

                if (dt == null ||
                    dt.Rows.Count == 0)
                {
                    LoginThrottle.Record(username, clientIp, false);
                    ShowInvalidLogin(lblError);
                    return;
                }

                DataRow row = dt.Rows[0];

                string storedHash =
                    Convert.ToString(
                        row["password_hash"]);

                bool passwordCorrect =
                    Auth.VerifyPassword(
                        password,
                        storedHash);

                if (!passwordCorrect)
                {
                    LoginThrottle.Record(username, clientIp, false);
                    ShowInvalidLogin(lblError);
                    return;
                }

                LoginThrottle.Record(username, clientIp, true);

                int userId =
                    Convert.ToInt32(
                        row["id"]);

                bool isSuperUser = row["IsAdmin"] != DBNull.Value && Convert.ToBoolean(row["IsAdmin"]);
                int tenantId = row["TenantID"] == DBNull.Value ? 0 : Convert.ToInt32(row["TenantID"]);

                // V10.14: Super Users always operate at Vighnesh Payroll Systems platform level.
                // Self-heal any legacy/mistaken entity assignment before session creation.
                if (isSuperUser && tenantId > 0)
                {
                    Db.Execute("UPDATE Users SET TenantID=NULL, IsOrgAdmin=0 WHERE id=? AND IsAdmin=1", new OleDbParameter("?", Convert.ToInt32(row["id"])));
                    tenantId = 0;
                }

                // V10.25: login is locked while the client's entity is Pending (or Rejected / not Active).
                // Checked only AFTER the password is correct, so status details are not shown to strangers,
                // and BEFORE the user-active check so a pending entity sees the right message.
                if (!isSuperUser)
                {
                    string blocked = Auth.TenantBlockMessage(tenantId);
                    if (blocked != null)
                    {
                        if (lblError != null) lblError.Text = blocked;
                        return;
                    }
                }

                if (row["IsActive"] != DBNull.Value && !Convert.ToBoolean(row["IsActive"]))
                {
                    if (lblError != null) lblError.Text = "This user account is inactive. Please contact the administrator.";
                    return;
                }


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

                string dbUsername = Convert.ToString(row["username"]);
                bool isOrgAdminFlag = isSuperUser ? false : (row["IsOrgAdmin"] != DBNull.Value && Convert.ToBoolean(row["IsOrgAdmin"]));

                // V10.24: optional two-step sign-in (only for users who enabled it).
                if (Totp.FeatureEnabled() &&
                    Totp.EnsureSchema() &&
                    Totp.IsEnabledForUser(userId))
                {
                    Session.Clear();
                    Session["TwoStepUserID"] = userId;
                    Session["TwoStepUsername"] = dbUsername;
                    Session["TwoStepIsAdmin"] = isSuperUser;
                    Session["TwoStepIsOrgAdmin"] = isOrgAdminFlag;
                    Session["TwoStepTenantID"] = tenantId;
                    Session["TwoStepExpires"] = DateTime.UtcNow.AddMinutes(5);
                    Session["TwoStepAttempts"] = 0;
                    ShowTwoStepPanel(true);
                    return;
                }

                CompleteLogin(userId, dbUsername, isSuperUser, isOrgAdminFlag, tenantId);
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

        private void CompleteLogin(
            int userId,
            string username,
            bool isSuperUser,
            bool isOrgAdmin,
            int tenantId)
        {
            Session.Clear();

            Session["UserID"] = userId;
            Session["Username"] = username;
            Session["IsAdmin"] = isSuperUser;
            Session["IsOrgAdmin"] = isOrgAdmin;
            Session["TenantID"] = tenantId;

            Session.Timeout = 30;

            Response.Redirect(
                Auth.GetLandingPage(),
                false);

            Context.ApplicationInstance
                .CompleteRequest();
        }

        protected void btnVerify_Click(
            object sender,
            EventArgs e)
        {
            Label lblError =
                (Label)FindControl("lblError");

            try
            {
                if (Session["TwoStepUserID"] == null ||
                    Session["TwoStepExpires"] == null ||
                    DateTime.UtcNow > (DateTime)Session["TwoStepExpires"])
                {
                    ResetToCredentials(lblError, "Sign-in timed out. Please sign in again.");
                    return;
                }

                int userId = Convert.ToInt32(Session["TwoStepUserID"]);
                int attempts = Convert.ToInt32(Session["TwoStepAttempts"]);

                if (attempts >= 5 || Totp.IsLockedOut(userId))
                {
                    ResetToCredentials(lblError, "Too many incorrect codes. Please wait 15 minutes and sign in again.");
                    return;
                }

                string code = (Request.Form["txtCode"] ?? "").Trim();

                if (!Totp.VerifyLogin(userId, code))
                {
                    Session["TwoStepAttempts"] = attempts + 1;
                    ShowTwoStepPanel(true);
                    if (lblError != null) lblError.Text = "Invalid verification code. Please try again.";
                    return;
                }

                string username = Convert.ToString(Session["TwoStepUsername"]);
                bool isAdmin = Convert.ToBoolean(Session["TwoStepIsAdmin"]);
                bool isOrgAdmin = Convert.ToBoolean(Session["TwoStepIsOrgAdmin"]);
                int tenantId = Convert.ToInt32(Session["TwoStepTenantID"]);

                CompleteLogin(userId, username, isAdmin, isOrgAdmin, tenantId);
            }
            catch (Exception)
            {
                ResetToCredentials(lblError, "Login could not be completed. Please try again.");
            }
        }

        protected void btnBack2FA_Click(
            object sender,
            EventArgs e)
        {
            ResetToCredentials(null, null);
        }

        private void ResetToCredentials(
            Label lblError,
            string message)
        {
            Session.Clear();
            ShowTwoStepPanel(false);

            if (lblError != null && message != null)
            {
                lblError.Text = message;
            }
        }

        private void ShowTwoStepPanel(bool show)
        {
            Panel credentials = FindControl("pnlCredentials") as Panel;
            Panel twoStep = FindControl("pnlTwoStep") as Panel;

            if (credentials != null) credentials.Visible = !show;
            if (twoStep != null) twoStep.Visible = show;
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


