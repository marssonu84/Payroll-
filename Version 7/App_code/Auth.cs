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

                Db.Execute(
                    @"IF COL_LENGTH(
                        'Users',
                        'IsActive'
                      ) IS NULL
                      ALTER TABLE Users
                      ADD IsActive BIT
                      NOT NULL
                      CONSTRAINT DF_Users_IsActive
                      DEFAULT 1;"
                );

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
                                NOT NULL DEFAULT 0
                        );
                      END"
                );

                /*
                 * Preserve Version 4 behaviour
                 * for existing Standard Users
                 * during the first upgrade.
                 */
                Db.Execute(
                    @"INSERT INTO UserPermissions
                    (
                        UserID,
                        EmployeesView,
                        EmployeesEdit,
                        PayrollView,
                        PayrollProcess,
                        LoansView,
                        LoansEdit,
                        ReportsView
                    )
                    SELECT
                        U.id,
                        1,
                        1,
                        1,
                        1,
                        1,
                        1,
                        1
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
            return
                HttpContext.Current != null &&
                HttpContext.Current.Session != null &&
                HttpContext.Current.Session[
                    "UserID"
                ] != null;
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

        public static bool HasPermission(
            string permission)
        {
            if (!IsLoggedIn())
            {
                return false;
            }

            if (IsAdmin())
            {
                return true;
            }

            EnsureSecuritySchema();

            string[] allowed =
            {
                "EmployeesView",
                "EmployeesEdit",
                "PayrollView",
                "PayrollProcess",
                "LoansView",
                "LoansEdit",
                "ReportsView"
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

        public static void RequireAdmin()
        {
            if (!IsLoggedIn())
            {
                RedirectSafely(
                    "~/Login.aspx"
                );

                return;
            }

            if (!IsAdmin())
            {
                RedirectSafely(
                    "~/Default.aspx?access=denied"
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
                    "~/Default.aspx?access=denied"
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
