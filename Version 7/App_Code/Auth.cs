using System;
using System.Data;
using System.Data.OleDb;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace PayrollWebApp
{
    public static class Auth
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 210000;

        private static readonly object SchemaLock = new object();
        private static bool _schemaReady = false;

        public static string HashPassword(string password)
        {
            byte[] salt = new byte[SaltSize];

            using (RandomNumberGenerator rng =
                RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            byte[] hash;

            using (var pbkdf2 =
                new Rfc2898DeriveBytes(
                    password,
                    salt,
                    Iterations,
                    HashAlgorithmName.SHA256))
            {
                hash = pbkdf2.GetBytes(HashSize);
            }

            return
                "PBKDF2$" +
                Iterations +
                "$" +
                Convert.ToBase64String(salt) +
                "$" +
                Convert.ToBase64String(hash);
        }

        public static bool VerifyPassword(
            string password,
            string storedHash)
        {
            if (String.IsNullOrWhiteSpace(storedHash))
            {
                return false;
            }

            if (storedHash.StartsWith(
                "PBKDF2$",
                StringComparison.Ordinal))
            {
                string[] parts =
                    storedHash.Split('$');

                if (parts.Length != 4)
                {
                    return false;
                }

                int iterations;

                if (!Int32.TryParse(
                    parts[1],
                    out iterations))
                {
                    return false;
                }

                try
                {
                    byte[] salt =
                        Convert.FromBase64String(
                            parts[2]);

                    byte[] expected =
                        Convert.FromBase64String(
                            parts[3]);

                    byte[] actual;

                    using (var pbkdf2 =
                        new Rfc2898DeriveBytes(
                            password,
                            salt,
                            iterations,
                            HashAlgorithmName.SHA256))
                    {
                        actual =
                            pbkdf2.GetBytes(
                                expected.Length);
                    }

                    return FixedTimeEquals(
                        actual,
                        expected);
                }
                catch
                {
                    return false;
                }
            }

            return FixedTimeEquals(
                Encoding.UTF8.GetBytes(
                    LegacyHashPassword(password)),
                Encoding.UTF8.GetBytes(
                    storedHash));
        }

        public static bool NeedsPasswordUpgrade(
            string storedHash)
        {
            return
                String.IsNullOrWhiteSpace(
                    storedHash) ||
                !storedHash.StartsWith(
                    "PBKDF2$",
                    StringComparison.Ordinal);
        }

        private static string LegacyHashPassword(
            string password)
        {
            using (SHA256 sha256 =
                SHA256.Create())
            {
                return Convert.ToBase64String(
                    sha256.ComputeHash(
                        Encoding.UTF8.GetBytes(
                            password)));
            }
        }

        private static bool FixedTimeEquals(
            byte[] a,
            byte[] b)
        {
            if (a == null ||
                b == null ||
                a.Length != b.Length)
            {
                return false;
            }

            int difference = 0;

            for (int i = 0;
                 i < a.Length;
                 i++)
            {
                difference |=
                    a[i] ^ b[i];
            }

            return difference == 0;
        }

        public static void EnsureSecuritySchema()
        {
            if (_schemaReady)
            {
                return;
            }

            lock (SchemaLock)
            {
                if (_schemaReady)
                {
                    return;
                }

                /*
                 * Ensure Users table has
                 * IsActive column.
                 */
                Db.Execute(
                    @"IF COL_LENGTH(
                        'Users',
                        'IsActive'
                      ) IS NULL
                      BEGIN
                        ALTER TABLE Users
                        ADD IsActive BIT
                        NOT NULL
                        CONSTRAINT DF_Users_IsActive
                        DEFAULT 1;
                      END"
                );

                /*
                 * Track self-registered accounts separately so they can
                 * use a safe onboarding dashboard until an administrator
                 * grants normal application permissions.
                 */
                Db.Execute(
                    @"IF COL_LENGTH(
                        'Users',
                        'IsNewUser'
                      ) IS NULL
                      BEGIN
                        ALTER TABLE Users
                        ADD IsNewUser BIT
                        NOT NULL
                        CONSTRAINT DF_Users_IsNewUser
                        DEFAULT 0;
                      END"
                );


                // Version 8.8 organisation registration and approval schema.
                Db.Execute(@"IF OBJECT_ID('Tenants','U') IS NULL BEGIN CREATE TABLE Tenants (TenantID INT IDENTITY(1,1) PRIMARY KEY, OrganizationName NVARCHAR(150) NOT NULL, IsActive BIT NOT NULL DEFAULT 1) END");
                string[] tenantColumns = { "OrganizationType NVARCHAR(30) NULL", "ContactPerson NVARCHAR(120) NULL", "Mobile NVARCHAR(20) NULL", "Email NVARCHAR(150) NULL", "AddressLine NVARCHAR(250) NULL", "City NVARCHAR(80) NULL", "StateName NVARCHAR(80) NULL", "Pincode NVARCHAR(10) NULL", "BoardType NVARCHAR(50) NULL", "AffiliationNumber NVARCHAR(80) NULL", "UdiseNumber NVARCHAR(30) NULL", "BusinessType NVARCHAR(50) NULL", "GSTIN NVARCHAR(20) NULL", "PAN NVARCHAR(15) NULL", "ApprovalStatus NVARCHAR(20) NULL", "RejectionReason NVARCHAR(500) NULL", "CreatedOn DATETIME NULL", "ApprovedOn DATETIME NULL", "ApprovedByUserID INT NULL", "OriginalLocationName NVARCHAR(150) NULL" };
                foreach (string definition in tenantColumns)
                {
                    string name = definition.Split(' ')[0];
                    Db.Execute("IF COL_LENGTH('Tenants','" + name + "') IS NULL ALTER TABLE Tenants ADD " + definition);
                }
                Db.Execute(@"UPDATE Tenants SET ApprovalStatus='Active' WHERE ApprovalStatus IS NULL; UPDATE Tenants SET CreatedOn=GETDATE() WHERE CreatedOn IS NULL;");
                // Ensure TenantID exists and is nullable. Vighnesh Payroll Systems Super Users are platform-level
                // accounts, so they must be allowed to have TenantID = NULL.
                Db.Execute(
                    @"IF COL_LENGTH('Users','TenantID') IS NULL
                      BEGIN
                        ALTER TABLE Users ADD TenantID INT NULL;
                      END
                      ELSE IF EXISTS
                      (
                        SELECT 1
                        FROM sys.columns
                        WHERE object_id = OBJECT_ID('Users')
                          AND name = 'TenantID'
                          AND is_nullable = 0
                      )
                      BEGIN
                        ALTER TABLE Users ALTER COLUMN TenantID INT NULL;
                      END"
                );

                Db.Execute(
                    @"IF COL_LENGTH('Users','IsOrgAdmin') IS NULL
                      BEGIN
                        ALTER TABLE Users
                        ADD IsOrgAdmin BIT NOT NULL
                        CONSTRAINT DF_Users_IsOrgAdmin DEFAULT 0;
                      END"
                );

                // V10.14+: Vighnesh Payroll Systems Super Users are platform-level accounts and must never belong to an entity.
                Db.Execute(
                    @"UPDATE Users
                      SET TenantID = NULL,
                          IsOrgAdmin = 0
                      WHERE ISNULL(IsAdmin,0) = 1;"
                );

                Db.Execute(@"IF OBJECT_ID('OrganisationAudit','U') IS NULL CREATE TABLE OrganisationAudit (AuditID INT IDENTITY(1,1) PRIMARY KEY, TenantID INT NULL, UserID INT NULL, ActionName NVARCHAR(100) NOT NULL, Details NVARCHAR(500) NULL, CreatedOn DATETIME NOT NULL DEFAULT GETDATE());");

                /*
                 * Create UserPermissions table
                 * when it does not already exist.
                 */
                Db.Execute(
                    @"IF OBJECT_ID(
                        'UserPermissions',
                        'U'
                      ) IS NULL
                      BEGIN
                        CREATE TABLE UserPermissions
                        (
                            UserID INT NOT NULL
                                PRIMARY KEY,

                            DashboardView BIT
                                NOT NULL DEFAULT 0,

                            EmployeesView BIT
                                NOT NULL DEFAULT 0,

                            EmployeesEdit BIT
                                NOT NULL DEFAULT 0,

                            PayrollView BIT
                                NOT NULL DEFAULT 0,

                            PayrollProcess BIT
                                NOT NULL DEFAULT 0,

                            LoansView BIT
                                NOT NULL DEFAULT 0,

                            LoansEdit BIT
                                NOT NULL DEFAULT 0,

                            ReportsView BIT
                                NOT NULL DEFAULT 0,

                            AttendanceView BIT
                                NOT NULL DEFAULT 0,

                            AttendanceImport BIT
                                NOT NULL DEFAULT 0
                        );
                      END"
                );

                /*
                 * Add DashboardView to existing databases.
                 * Default 1 preserves the access every existing user
                 * had before Dashboard became a configurable permission.
                 */
                Db.Execute(
                    @"IF COL_LENGTH(
                        'UserPermissions',
                        'DashboardView'
                      ) IS NULL
                      BEGIN
                        ALTER TABLE UserPermissions
                        ADD DashboardView BIT
                        NOT NULL
                        CONSTRAINT DF_UserPermissions_DashboardView
                        DEFAULT 1;
                      END"
                );

                /*
                 * Upgrade existing databases.
                 *
                 * If UserPermissions already existed
                 * before Attendance was introduced,
                 * add AttendanceView safely.
                 */
                Db.Execute(
                    @"IF COL_LENGTH(
                        'UserPermissions',
                        'AttendanceView'
                      ) IS NULL
                      BEGIN
                        ALTER TABLE UserPermissions
                        ADD AttendanceView BIT
                        NOT NULL
                        CONSTRAINT DF_UserPermissions_AttendanceView
                        DEFAULT 0;
                      END"
                );

                /*
                 * Add AttendanceImport safely
                 * to existing databases.
                 */
                Db.Execute(
                    @"IF COL_LENGTH(
                        'UserPermissions',
                        'AttendanceImport'
                      ) IS NULL
                      BEGIN
                        ALTER TABLE UserPermissions
                        ADD AttendanceImport BIT
                        NOT NULL
                        CONSTRAINT DF_UserPermissions_AttendanceImport
                        DEFAULT 0;
                      END"
                );

                /*
                 * Keep Administrator permission rows
                 * explicitly synchronized to full access.
                 * HasPermission() already grants Admins
                 * automatic full access, but this keeps
                 * database permission records consistent.
                 */
                Db.Execute(
                    @"INSERT INTO UserPermissions
                    (
                        UserID,
                        DashboardView,
                        EmployeesView, EmployeesEdit,
                        PayrollView, PayrollProcess,
                        LoansView, LoansEdit,
                        ReportsView,
                        AttendanceView, AttendanceImport
                    )
                    SELECT
                        U.id,
                        1,1,1,1,1,1,1,1,1,1
                    FROM Users U
                    WHERE ISNULL(U.IsAdmin,0)=1
                      AND NOT EXISTS
                      (
                        SELECT 1
                        FROM UserPermissions P
                        WHERE P.UserID=U.id
                      );

                    UPDATE P
                    SET
                        DashboardView=1,
                        EmployeesView=1,
                        EmployeesEdit=1,
                        PayrollView=1,
                        PayrollProcess=1,
                        LoansView=1,
                        LoansEdit=1,
                        ReportsView=1,
                        AttendanceView=1,
                        AttendanceImport=1
                    FROM UserPermissions P
                    INNER JOIN Users U
                        ON U.id=P.UserID
                    WHERE ISNULL(U.IsAdmin,0)=1;"
                );

                /*
                 * Preserve Version 4 behaviour
                 * for existing Standard Users.
                 *
                 * Attendance permissions remain OFF
                 * by default for security.
                 */
                Db.Execute(
                    @"INSERT INTO UserPermissions
                    (
                        UserID,
                        DashboardView,
                        EmployeesView,
                        EmployeesEdit,
                        PayrollView,
                        PayrollProcess,
                        LoansView,
                        LoansEdit,
                        ReportsView,
                        AttendanceView,
                        AttendanceImport
                    )
                    SELECT
                        U.id,
                        1,
                        1,
                        1,
                        1,
                        1,
                        1,
                        1,
                        1,
                        0,
                        0
                    FROM Users U
                    WHERE
                        ISNULL(
                            U.IsAdmin,
                            0
                        ) = 0
                        AND NOT EXISTS
                        (
                            SELECT 1
                            FROM UserPermissions P
                            WHERE P.UserID = U.id
                        );"
                );

                _schemaReady = true;
            }
        }

        public static bool IsLoggedIn()
        {
            HttpContext ctx = HttpContext.Current;

            if (ctx == null ||
                ctx.Session == null ||
                ctx.Session["UserID"] == null)
            {
                return false;
            }

            // V10.25: a signed-in session ends as soon as the client's entity
            // is no longer Active (for example Pending or Rejected).
            return TenantStillAllowed(ctx);
        }

        /// <summary>
        /// Returns null when the entity (client) may sign in, otherwise the message to show.
        /// Only an entity whose ApprovalStatus is Active may sign in. Pending, Rejected,
        /// unknown or missing entities are locked out. tenantId 0 (Super User) is never blocked.
        /// </summary>
        public static string TenantBlockMessage(int tenantId)
        {
            if (tenantId <= 0) return null;

            string status = Convert.ToString(
                Db.Scalar(
                    "SELECT ApprovalStatus FROM Tenants WHERE TenantID=?",
                    new OleDbParameter("?", tenantId))) ?? "";

            if (String.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)) return null;

            if (String.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
                return "Your entity is pending. You can sign in once the Super User approves it.";

            if (String.Equals(status, "Suspended", StringComparison.OrdinalIgnoreCase))
                return "Your entity has been suspended. Please contact the system administrator.";

            if (String.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
                return "Entity registration was rejected. Please contact the system administrator.";

            return "This entity is not active. Please contact the system administrator.";
        }

        // Checked at most once per request (cached in HttpContext.Items).
        private static bool TenantStillAllowed(HttpContext ctx)
        {
            object cached = ctx.Items["TenantAllowed"];
            if (cached != null) return (bool)cached;

            bool allowed = true;

            try
            {
                bool isSuper;
                Boolean.TryParse(Convert.ToString(ctx.Session["IsAdmin"]), out isSuper);

                int tenantId;
                Int32.TryParse(Convert.ToString(ctx.Session["TenantID"]), out tenantId);

                if (!isSuper && tenantId > 0)
                {
                    allowed = TenantBlockMessage(tenantId) == null;
                }
            }
            catch (Exception)
            {
                allowed = false; // fail closed
            }

            ctx.Items["TenantAllowed"] = allowed;

            if (!allowed) ctx.Session.Clear();

            return allowed;
        }

        public static int CurrentUserId()
        {
            if (!IsLoggedIn())
            {
                return 0;
            }

            return Convert.ToInt32(
                HttpContext.Current.Session[
                    "UserID"
                ]);
        }

        public static bool IsAdmin()
        {
            if (!IsLoggedIn())
            {
                return false;
            }

            bool value;

            return
                Boolean.TryParse(
                    Convert.ToString(
                        HttpContext.Current.Session[
                            "IsAdmin"
                        ]),
                    out value
                ) &&
                value;
        }

        public static int CurrentTenantId()
        {
            if (!IsLoggedIn() || HttpContext.Current.Session["TenantID"] == null) return 0;
            int value; return Int32.TryParse(Convert.ToString(HttpContext.Current.Session["TenantID"]), out value) ? value : 0;
        }

        public static bool IsOrgAdmin()
        {
            if (!IsLoggedIn()) return false;
            bool value; return Boolean.TryParse(Convert.ToString(HttpContext.Current.Session["IsOrgAdmin"]), out value) && value;
        }

        public static bool IsSuperUser() { return IsAdmin(); }

        public static string CurrentUserName()
        {
            if (!IsLoggedIn())
            {
                return "";
            }

            object value = Db.Scalar(
                "SELECT username FROM Users WHERE id=?",
                new OleDbParameter("?", CurrentUserId())
            );

            if (value == null || value == DBNull.Value)
            {
                return "User";
            }

            return Convert.ToString(value);
        }

        public static string CurrentOrganizationName()
        {
            if (!IsLoggedIn()) return "Vighnesh Payroll Systems";
            int tenantId = CurrentTenantId();
            if (tenantId <= 0) return "Vighnesh Payroll Systems";
            string name = TenantLookup("Name", "OrganizationName", tenantId);
            return String.IsNullOrWhiteSpace(name) ? "Vighnesh Payroll Systems" : name;
        }

        public static string CurrentOrganizationType()
        {
            if (!IsLoggedIn()) return "";
            int tenantId = CurrentTenantId();
            if (tenantId <= 0) return "";
            return TenantLookup("Type", "OrganizationType", tenantId);
        }

        // V10.30: the page header asks for the entity name/type on every page; keep the answer for 2 minutes
        // per signed-in session to save database round trips. "column" is always a fixed value from this file.
        private static string TenantLookup(string kind, string column, int tenantId)
        {
            HttpContext ctx = HttpContext.Current;
            string key = "TenantInfo_" + kind;
            if (ctx != null && ctx.Session != null)
            {
                object[] hit = ctx.Session[key] as object[];
                if (hit != null && hit.Length == 3 && (int)hit[0] == tenantId && DateTime.UtcNow < (DateTime)hit[2])
                {
                    return (string)hit[1];
                }
            }

            string value = Convert.ToString(Db.Scalar("SELECT " + column + " FROM Tenants WHERE TenantID=?", new OleDbParameter("?", tenantId)));
            if (ctx != null && ctx.Session != null)
            {
                ctx.Session[key] = new object[] { tenantId, value, DateTime.UtcNow.AddMinutes(2) };
            }
            return value;
        }

        public static bool HasPermission(
            string permission)
        {
            if (!IsLoggedIn())
            {
                return false;
            }

            /*
             * Administrator automatically
             * gets every permission.
             */
            if (IsAdmin() || IsOrgAdmin())
            {
                return true;
            }

            EnsureSecuritySchema();

            /*
             * Only explicitly approved
             * permission column names
             * can be queried.
             *
             * This also prevents SQL injection
             * through the permission parameter.
             */
            string[] allowed =
            {
                "DashboardView",
                "EmployeesView",
                "EmployeesEdit",
                "PayrollView",
                "PayrollProcess",
                "LoansView",
                "LoansEdit",
                "ReportsView",
                "AttendanceView",
                "AttendanceImport"
            };

            bool valid = false;

            foreach (string p in allowed)
            {
                if (String.Equals(
                    p,
                    permission,
                    StringComparison.OrdinalIgnoreCase))
                {
                    valid = true;
                    permission = p;
                    break;
                }
            }

            if (!valid)
            {
                return false;
            }

            object result =
                Db.Scalar(
                    "SELECT " +
                    permission +
                    @" FROM UserPermissions
                       WHERE UserID=?",
                    new OleDbParameter(
                        "?",
                        CurrentUserId()
                    )
                );

            return
                result != null &&
                result != DBNull.Value &&
                Convert.ToBoolean(result);
        }

        public static string GetLandingPage()
        {
            if (!IsLoggedIn())
            {
                return "~/Login.aspx";
            }

            if (!IsAdmin())
            {
                object newUser =
                    Db.Scalar(
                        "SELECT IsNewUser FROM Users WHERE id=?",
                        new OleDbParameter(
                            "?",
                            CurrentUserId()
                        )
                    );

                if (newUser != null &&
                    newUser != DBNull.Value &&
                    Convert.ToBoolean(newUser))
                {
                    return "~/NewUserDashboard.aspx";
                }
            }

            if (IsAdmin() || HasPermission("DashboardView"))
            {
                return "~/Dashboard.aspx";
            }

            if (HasPermission("EmployeesView")) return "~/Employees.aspx";
            if (HasPermission("LoansView")) return "~/Loans.aspx";
            if (HasPermission("PayrollView")) return "~/Payroll.aspx";
            if (HasPermission("AttendanceView")) return "~/Attendance.aspx";
            if (HasPermission("ReportsView")) return "~/Reports.aspx";

            // A valid user can exist with no section permissions.
            // Keep them on the dashboard shell so an access-denied message
            // can be shown without exposing dashboard data.
            return "~/Dashboard.aspx?access=denied";
        }

        public static void RequireAuth()
        {
            if (IsLoggedIn())
            {
                return;
            }

            RedirectSafely(
                "~/Login.aspx"
            );
        }

        // V14.1: strict global Super User gate. Unlike RequireAdmin(), this never
        // grants access to Entity Admin accounts and returns false after redirect.
        public static bool RequireSuperUser()
        {
            if (!IsLoggedIn())
            {
                RedirectSafely("~/Login.aspx");
                return false;
            }

            if (!IsSuperUser())
            {
                RedirectSafely("~/Dashboard.aspx?access=denied");
                return false;
            }

            return true;
        }

        public static void RequireAdmin()
        {
            if (!IsLoggedIn())
            {
                RedirectSafely(
                    "~/Login.aspx"
                );

                return;
            }

            if (!IsAdmin() && !IsOrgAdmin())
            {
                RedirectSafely(
                    "~/Dashboard.aspx?access=denied"
                );

                return;
            }
        }

        public static void RequirePermission(
            string permission)
        {
            if (!IsLoggedIn())
            {
                RedirectSafely(
                    "~/Login.aspx"
                );

                return;
            }

            if (!HasPermission(permission))
            {
                RedirectSafely(
                    "~/Dashboard.aspx?access=denied"
                );

                return;
            }
        }

        private static void RedirectSafely(
            string url)
        {
            if (HttpContext.Current == null)
            {
                return;
            }

            HttpContext.Current.Response.Redirect(
                url,
                false
            );

            if (HttpContext.Current.ApplicationInstance
                != null)
            {
                HttpContext.Current
                    .ApplicationInstance
                    .CompleteRequest();
            }
        }
    }
}
