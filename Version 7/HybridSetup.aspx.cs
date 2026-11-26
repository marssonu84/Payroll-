using System;
using System.Data.SqlClient;
using System.Web.UI;
using PayrollWebApp;

public partial class HybridSetup : Page
{
    // Non-secret defaults only. Passwords are always typed by the administrator.
    private const string SomeeServer = "empsalary.mssql.somee.com";
    private const string SomeeDatabase = "empsalary";
    private const string SomeeUserId = "marssonu84_SQLLogin_2";

    private const string BsiteServer = "sql.bsite.net\\MSSQL2016";
    private const string BsiteDatabase = "empsalary_";
    private const string BsiteUserId = "empsalary_";

    private const string LocalDefaultDatabase = "empsalary";

    // First-time setup is allowed only while Database.config is still the shipped placeholder
    // and the request comes from the server itself. Otherwise an administrator is required.
    private bool BootstrapAllowed()
    {
        return SecureConfig.IsPlaceholder() && Request.IsLocal;
    }

    private bool EnsureAuthorized()
    {
        if (BootstrapAllowed()) return true;

        Auth.RequireAdmin();
        if (!Auth.IsAdmin())
        {
            Response.Redirect("~/Dashboard.aspx?access=denied", false);
            Context.ApplicationInstance.CompleteRequest();
            return false;
        }
        return true;
    }

    protected void Page_Load(object sender, EventArgs e)
    {
        if (!EnsureAuthorized()) return;

        if (!IsPostBack)
        {
            ShowCurrentConnection();
            ApplyModeDefaults();
            // Clear any previous test result when the page is first loaded.
            ViewState["LastTestOk"] = false;
            ViewState["LastTestServer"] = null;
            ViewState["LastTestDatabase"] = null;
        }
    }

    protected void Mode_Changed(object sender, EventArgs e)
    {
        ApplyModeDefaults();
        // Changing mode invalidates any previous successful test.
        ViewState["LastTestOk"] = false;
        lblMsg.Text = "";
    }

    protected void Integrated_Changed(object sender, EventArgs e)
    {
        pnlSqlLogin.Visible = !(rbLocal.Checked && chkIntegrated.Checked);
        ViewState["LastTestOk"] = false;
        lblMsg.Text = "";
    }

    private void ApplyModeDefaults()
    {
        bool local = rbLocal.Checked;
        pnlIntegrated.Visible = local;
        pnlCreate.Visible = local;

        if (rbSomee.Checked)
        {
            txtServer.Text = SomeeServer;
            txtDatabase.Text = SomeeDatabase;
            txtUser.Text = SomeeUserId;
        }
        else if (rbBsite.Checked)
        {
            txtServer.Text = BsiteServer;
            txtDatabase.Text = BsiteDatabase;
            txtUser.Text = BsiteUserId;
        }
        else
        {
            txtServer.Text = "";
            txtDatabase.Text = LocalDefaultDatabase;
            txtUser.Text = "";
        }

        pnlSqlLogin.Visible = !(local && chkIntegrated.Checked);
    }

    private void ShowCurrentConnection()
    {
        try
        {
            if (SecureConfig.IsPlaceholder())
            {
                lblCurrent.Text = "Not configured yet (placeholder Database.config).";
                return;
            }

            SqlConnectionStringBuilder b = new SqlConnectionStringBuilder(Db.ConnectionString);
            lblCurrent.Text = "Server: " + Server.HtmlEncode(b.DataSource)
                + " | Database: " + Server.HtmlEncode(b.InitialCatalog)
                + " | Auth: " + (b.IntegratedSecurity ? "Windows" : "SQL Login")
                + " [" + HostLabel(b) + "]"
                + (SecureConfig.IsEncrypted() ? " (encrypted)" : " (plain)");
        }
        catch (Exception ex)
        {
            lblCurrent.Text = "Unavailable: " + Server.HtmlEncode(ex.Message);
        }
    }

    private static string HostLabel(SqlConnectionStringBuilder b)
    {
        string ds = (b.DataSource ?? "").ToLowerInvariant();
        if (ds.Contains("somee.com")) return "Cloud A – somee.com";
        if (ds.Contains("bsite.net")) return "Cloud B – bsite.net";
        return "Local / other SQL Server";
    }

    /// <summary>
    /// Builds the connection string from the current form values.
    /// Does not open a connection and does not save anything.
    /// </summary>
    private SqlConnectionStringBuilder BuildConnectionString()
    {
        string server = (txtServer.Text ?? "").Trim();
        string database = (txtDatabase.Text ?? "").Trim();
        bool local = rbLocal.Checked;
        bool integrated = local && chkIntegrated.Checked;

        if (server.Length == 0 || database.Length == 0)
            throw new InvalidOperationException("Server and database are required.");

        SqlConnectionStringBuilder b = new SqlConnectionStringBuilder();
        b.DataSource = server;
        b.InitialCatalog = database;
        b.TrustServerCertificate = true;
        b.PersistSecurityInfo = false;
        b.ConnectTimeout = local ? 10 : 15;

        if (integrated)
        {
            b.IntegratedSecurity = true;
            b.Encrypt = true;
        }
        else
        {
            string user = (txtUser.Text ?? "").Trim();
            string pwd = txtPassword.Text ?? "";
            if (user.Length == 0 || pwd.Length == 0)
                throw new InvalidOperationException("SQL login and password are required.");
            b.UserID = user;
            b.Password = pwd;
            if (local) b.Encrypt = true;
        }

        if (rbSomee.Checked) b.PacketSize = 4096;
        return b;
    }

    /// <summary>
    /// TEST only. Verifies the connection. Never saves Database.config and never creates a database.
    /// A successful test is remembered in ViewState so Apply can proceed without re-testing
    /// unless the server/database fields change.
    /// </summary>
    protected void btnTest_Click(object sender, EventArgs e)
    {
        if (!EnsureAuthorized()) return;

        try
        {
            SqlConnectionStringBuilder b = BuildConnectionString();

            using (SqlConnection c = new SqlConnection(b.ConnectionString))
            {
                c.Open();
                using (SqlCommand cmd = new SqlCommand("SELECT DB_NAME()", c))
                {
                    string actual = Convert.ToString(cmd.ExecuteScalar());
                    if (!String.Equals(actual, b.InitialCatalog, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Connected to an unexpected database: " + actual);
                }
            }

            // Remember a successful test for this exact server + database combination.
            ViewState["LastTestOk"] = true;
            ViewState["LastTestServer"] = b.DataSource;
            ViewState["LastTestDatabase"] = b.InitialCatalog;

            lblMsg.Text = "Connection test succeeded. Server and database are reachable. "
                + "You may now click Apply / Save to store the encrypted connection string.";
            lblMsg.ForeColor = System.Drawing.Color.Green;
        }
        catch (Exception ex)
        {
            ViewState["LastTestOk"] = false;
            lblMsg.Text = "Connection test failed: " + Server.HtmlEncode(ex.Message);
            lblMsg.ForeColor = System.Drawing.Color.Red;
        }
    }

    /// <summary>
    /// APPLY / SAVE only. Saves the encrypted connection string.
    /// Requires either a prior successful Test for the same server/database, or the force checkbox.
    /// Optionally creates the database (local only). Schema initialization is left to Setup.aspx.
    /// </summary>
    protected void btnApply_Click(object sender, EventArgs e)
    {
        if (!EnsureAuthorized()) return;

        try
        {
            SqlConnectionStringBuilder b = BuildConnectionString();
            bool local = rbLocal.Checked;

            // Enforce the Test → Apply flow unless the administrator deliberately forces a save.
            bool priorTestOk = ViewState["LastTestOk"] is bool && (bool)ViewState["LastTestOk"];
            string lastServer = ViewState["LastTestServer"] as string;
            string lastDatabase = ViewState["LastTestDatabase"] as string;
            bool sameTarget = priorTestOk
                && String.Equals(lastServer, b.DataSource, StringComparison.OrdinalIgnoreCase)
                && String.Equals(lastDatabase, b.InitialCatalog, StringComparison.OrdinalIgnoreCase);

            if (!sameTarget && !chkForce.Checked)
            {
                lblMsg.Text = "Please click \"Test Connection\" first and obtain a successful result for these exact server/database values, "
                    + "or tick \"Save even if the connection test fails\" to force a save.";
                lblMsg.ForeColor = System.Drawing.Color.Red;
                return;
            }

            // Optional: create the database on a local SQL Server only.
            if (local && chkCreate.Checked)
                CreateDatabaseIfMissing(b, b.InitialCatalog);

            // Final live check (skipped only when force is used and no prior test exists).
            bool liveOk = false;
            string liveError = null;
            try
            {
                using (SqlConnection c = new SqlConnection(b.ConnectionString))
                {
                    c.Open();
                    using (SqlCommand cmd = new SqlCommand("SELECT DB_NAME()", c))
                    {
                        string actual = Convert.ToString(cmd.ExecuteScalar());
                        if (!String.Equals(actual, b.InitialCatalog, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Connected to an unexpected database: " + actual);
                    }
                }
                liveOk = true;
            }
            catch (Exception tx)
            {
                liveError = tx.Message;
            }

            if (!liveOk && !chkForce.Checked)
            {
                lblMsg.Text = "Live connection check failed, nothing was saved: " + Server.HtmlEncode(liveError);
                lblMsg.ForeColor = System.Drawing.Color.Red;
                return;
            }

            // Persist the encrypted connection string.
            SecureConfig.SaveEncryptedConnectionString(b.ConnectionString);

            // Clear the remembered test so a later change forces a new Test.
            ViewState["LastTestOk"] = false;

            if (!liveOk)
            {
                lblMsg.Text = "Saved (encrypted) but the live connection check failed: " + Server.HtmlEncode(liveError)
                    + ". Open Database Manager or Setup later to verify.";
                lblMsg.ForeColor = System.Drawing.Color.DarkOrange;
            }
            else
            {
                lblMsg.Text = "Connection saved (encrypted). "
                    + "Next step: open Setup.aspx to initialize the schema and create the first administrator if this is a new database.";
                lblMsg.ForeColor = System.Drawing.Color.Green;
            }

            ShowCurrentConnection();
        }
        catch (Exception ex)
        {
            lblMsg.Text = "Apply failed: " + Server.HtmlEncode(ex.Message);
            lblMsg.ForeColor = System.Drawing.Color.Red;
        }
    }

    private static void CreateDatabaseIfMissing(SqlConnectionStringBuilder target, string database)
    {
        SqlConnectionStringBuilder master = new SqlConnectionStringBuilder(target.ConnectionString);
        master.InitialCatalog = "master";

        using (SqlConnection c = new SqlConnection(master.ConnectionString))
        {
            c.Open();
            using (SqlCommand cmd = new SqlCommand(
                "IF DB_ID(@db) IS NULL BEGIN " +
                "DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@db); " +
                "EXEC (@sql); END", c))
            {
                cmd.Parameters.AddWithValue("@db", database);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
