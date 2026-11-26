// =============================================================================
// Vighnesh Payroll Systems - Super User Password Reset Console (V14)
// -----------------------------------------------------------------------------
// STANDALONE, OFFLINE recovery tool. It is NOT part of the web application and
// must NOT be uploaded to the web server.
//
// Run it only on a trusted machine by someone who already holds database
// access. It writes a PBKDF2 hash in the exact format used by App_Code/Auth.cs
// ("PBKDF2$210000$<salt>$<hash>"), so the normal Login page accepts it.
//
// V14 additions: clears login lockouts (LoginAttempts), optional --disable-2fa
// for a lost authenticator, safer handling when optional columns are missing.
//
// Build:  build.bat      (needs .NET Framework 4.8 csc.exe, no other SDK)
// Usage:  SuperAdminReset.exe --help
// =============================================================================
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace VighneshPayroll.Recovery
{
    internal static class Program
    {
        // Must match App_Code/Auth.cs
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 210000;
        private const int MinPasswordLength = 12;

        private static int Main(string[] args)
        {
            try
            {
                Options o = Options.Parse(args);
                if (o.ShowHelp) { PrintHelp(); return 0; }

                Banner();
                string conn = ResolveConnectionString(o);

                using (SqlConnection con = new SqlConnection(conn))
                {
                    con.Open();
                    Console.WriteLine("Connected to database: " + con.Database + " on " + con.DataSource);
                    Console.WriteLine();

                    List<SuperUser> users = LoadSuperUsers(con);
                    if (users.Count == 0)
                    {
                        Console.WriteLine("No Super User accounts (IsAdmin = 1) exist in this database.");
                        Console.WriteLine("Nothing was changed. Create one through your normal provisioning process.");
                        return 3;
                    }

                    SuperUser target = PickUser(users, o);
                    if (target == null) return 2;

                    string newPassword = o.GeneratePassword
                        ? GeneratePassword(20)
                        : PromptNewPassword();
                    if (newPassword == null) return 2;

                    Console.WriteLine();
                    Console.WriteLine("About to reset password for Super User: " + target.Username + " (id " + target.Id + ")");
                    if (!target.IsActive)
                        Console.WriteLine("  - Account is currently INACTIVE." + (o.Reactivate ? " It will be re-activated." : " It stays inactive unless you pass --reactivate."));
                    if (o.Disable2fa)
                        Console.WriteLine("  - Two-step sign-in will be turned off for this account.");
                    Console.WriteLine("  - Any login lockout for this account will be cleared.");

                    if (o.DryRun)
                    {
                        Console.WriteLine();
                        Console.WriteLine("[DRY RUN] No changes were made.");
                        return 0;
                    }

                    if (!o.AssumeYes && !ConfirmByTyping(target.Username))
                    {
                        Console.WriteLine("Cancelled. No changes were made.");
                        return 2;
                    }

                    ApplyReset(con, target, newPassword, o.Reactivate, o.Disable2fa);

                    Console.WriteLine();
                    Console.WriteLine("SUCCESS: password updated for '" + target.Username + "'.");
                    if (o.GeneratePassword)
                    {
                        Console.WriteLine();
                        Console.WriteLine("  Temporary password (shown once, not stored anywhere):");
                        Console.WriteLine("  " + newPassword);
                    }
                    Console.WriteLine();
                    Console.WriteLine("Next steps:");
                    Console.WriteLine("  1. Log in and change this password immediately.");
                    Console.WriteLine("  2. Recycle the IIS app pool if you need to end existing sessions");
                    Console.WriteLine("     (sessions are InProc; the app has no password-changed marker).");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        // ---------------------------------------------------------------- data

        private sealed class SuperUser
        {
            public int Id; public string Username; public bool IsActive; public string Email;
        }

        private static List<SuperUser> LoadSuperUsers(SqlConnection con)
        {
            List<SuperUser> list = new List<SuperUser>();
            bool hasEmail = ColumnExists(con, "AdminEmail");
            bool hasActive = ColumnExists(con, "IsActive");
            string sql = "SELECT id, username, " +
                         (hasActive ? "ISNULL(IsActive,1)" : "CAST(1 AS BIT)") + " AS IsActive, " +
                         (hasEmail ? "AdminEmail" : "CAST(NULL AS NVARCHAR(254))") + " AS AdminEmail" +
                         " FROM dbo.Users WHERE ISNULL(IsAdmin,0)=1 ORDER BY id";
            using (SqlCommand cmd = new SqlCommand(sql, con))
            using (SqlDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    list.Add(new SuperUser
                    {
                        Id = Convert.ToInt32(r["id"]),
                        Username = Convert.ToString(r["username"]),
                        IsActive = Convert.ToBoolean(r["IsActive"]),
                        Email = r["AdminEmail"] == DBNull.Value ? "" : Convert.ToString(r["AdminEmail"])
                    });
                }
            }
            return list;
        }

        private static bool ColumnExists(SqlConnection con, string column)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT COL_LENGTH('dbo.Users', @c)", con))
            {
                cmd.Parameters.AddWithValue("@c", column);
                object v = cmd.ExecuteScalar();
                return v != null && v != DBNull.Value;
            }
        }

        private static bool TableExists(SqlConnection con, SqlTransaction tx, string table)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT OBJECT_ID(@t,'U')", con, tx))
            {
                cmd.Parameters.AddWithValue("@t", "dbo." + table);
                object v = cmd.ExecuteScalar();
                return v != null && v != DBNull.Value;
            }
        }

        private static bool UsersColumnExists(SqlConnection con, SqlTransaction tx, string column)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT COL_LENGTH('dbo.Users', @c)", con, tx))
            {
                cmd.Parameters.AddWithValue("@c", column);
                object v = cmd.ExecuteScalar();
                return v != null && v != DBNull.Value;
            }
        }

        private static void ApplyReset(SqlConnection con, SuperUser target, string newPassword, bool reactivate, bool disable2fa)
        {
            string hash = HashPassword(newPassword);

            using (SqlTransaction tx = con.BeginTransaction())
            {
                try
                {
                    // Re-check IsAdmin inside the same statement: this tool must never touch
                    // an organisation user, even if the id was tampered with.
                    string sql = "UPDATE dbo.Users SET password_hash=@h" +
                                 (reactivate ? ", IsActive=1" : "") +
                                 " WHERE id=@id AND ISNULL(IsAdmin,0)=1";
                    int rows;
                    using (SqlCommand cmd = new SqlCommand(sql, con, tx))
                    {
                        cmd.Parameters.Add("@h", SqlDbType.NVarChar, 500).Value = hash;
                        cmd.Parameters.Add("@id", SqlDbType.Int).Value = target.Id;
                        rows = cmd.ExecuteNonQuery();
                    }
                    if (rows != 1) throw new InvalidOperationException("Update affected " + rows + " rows; rolled back.");

                    bool twoStepOff = false;
                    if (disable2fa && UsersColumnExists(con, tx, "TotpEnabled"))
                    {
                        using (SqlCommand cmd = new SqlCommand(
                            "UPDATE dbo.Users SET TotpEnabled=0 WHERE id=@id AND ISNULL(IsAdmin,0)=1", con, tx))
                        {
                            cmd.Parameters.Add("@id", SqlDbType.Int).Value = target.Id;
                            cmd.ExecuteNonQuery();
                        }
                        twoStepOff = true;
                    }

                    // Clear login lockout (table exists once V14 login throttling has run).
                    if (TableExists(con, tx, "LoginAttempts"))
                    {
                        using (SqlCommand cmd = new SqlCommand("DELETE FROM dbo.LoginAttempts WHERE Username=@u", con, tx))
                        {
                            cmd.Parameters.Add("@u", SqlDbType.NVarChar, 150).Value = target.Username.ToLowerInvariant();
                            cmd.ExecuteNonQuery();
                        }
                    }

                    WriteAudit(con, tx, target, reactivate, twoStepOff);
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        private static void WriteAudit(SqlConnection con, SqlTransaction tx, SuperUser target, bool reactivated, bool twoStepOff)
        {
            // OrganisationAudit is created by Auth.EnsureSecuritySchema(); skip quietly if absent.
            if (!TableExists(con, tx, "OrganisationAudit")) return;

            string details = "Offline reset by " + Environment.UserDomainName + "\\" + Environment.UserName +
                             " on " + Environment.MachineName +
                             (reactivated ? "; account re-activated" : "") +
                             (twoStepOff ? "; two-step sign-in turned off" : "");
            if (details.Length > 500) details = details.Substring(0, 500);

            using (SqlCommand cmd = new SqlCommand(
                "INSERT INTO dbo.OrganisationAudit (TenantID, UserID, ActionName, Details, CreatedOn) " +
                "VALUES (NULL, @u, 'SuperUserPasswordReset', @d, GETDATE())", con, tx))
            {
                cmd.Parameters.Add("@u", SqlDbType.Int).Value = target.Id;
                cmd.Parameters.Add("@d", SqlDbType.NVarChar, 500).Value = details;
                cmd.ExecuteNonQuery();
            }
        }

        // ------------------------------------------------------------- hashing

        // Identical format to Auth.HashPassword in the web app.
        internal static string HashPassword(string password)
        {
            byte[] salt = new byte[SaltSize];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) { rng.GetBytes(salt); }

            byte[] hash;
            using (Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
            {
                hash = pbkdf2.GetBytes(HashSize);
            }
            return "PBKDF2$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }

        // ------------------------------------------------------------ password

        private static string GeneratePassword(int length)
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string lower = "abcdefghijkmnopqrstuvwxyz";
            const string digits = "23456789";
            const string symbols = "!@#$%^&*-_=+?";
            string all = upper + lower + digits + symbols;

            List<char> chars = new List<char>
            {
                Pick(upper), Pick(lower), Pick(digits), Pick(symbols)
            };
            while (chars.Count < length) chars.Add(Pick(all));

            // Fisher-Yates shuffle with a CSPRNG
            for (int i = chars.Count - 1; i > 0; i--)
            {
                int j = RandomInt(i + 1);
                char t = chars[i]; chars[i] = chars[j]; chars[j] = t;
            }
            return new string(chars.ToArray());
        }

        private static char Pick(string set) { return set[RandomInt(set.Length)]; }

        // Unbiased random int in [0, max)
        private static int RandomInt(int max)
        {
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                byte[] b = new byte[4];
                uint limit = uint.MaxValue - (uint.MaxValue % (uint)max);
                uint v;
                do { rng.GetBytes(b); v = BitConverter.ToUInt32(b, 0); } while (v >= limit);
                return (int)(v % (uint)max);
            }
        }

        private static string PromptNewPassword()
        {
            Console.WriteLine("Enter a new password (min " + MinPasswordLength + " chars, upper + lower + digit + symbol).");
            string p1 = ReadHidden("New password: ");
            string reason = Validate(p1);
            if (reason != null) { Console.WriteLine("Rejected: " + reason); return null; }
            string p2 = ReadHidden("Repeat password: ");
            if (!String.Equals(p1, p2, StringComparison.Ordinal)) { Console.WriteLine("Passwords do not match."); return null; }
            return p1;
        }

        private static string Validate(string p)
        {
            if (String.IsNullOrEmpty(p) || p.Length < MinPasswordLength) return "too short (minimum " + MinPasswordLength + ").";
            if (!p.Any(char.IsUpper)) return "needs an uppercase letter.";
            if (!p.Any(char.IsLower)) return "needs a lowercase letter.";
            if (!p.Any(char.IsDigit)) return "needs a digit.";
            if (p.All(char.IsLetterOrDigit)) return "needs a symbol.";
            return null;
        }

        private static string ReadHidden(string prompt)
        {
            Console.Write(prompt);
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
                if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
                if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
            }
            return sb.ToString();
        }

        // ----------------------------------------------------------------- UI

        private static SuperUser PickUser(List<SuperUser> users, Options o)
        {
            if (!String.IsNullOrEmpty(o.Username))
            {
                SuperUser m = users.FirstOrDefault(u => String.Equals(u.Username, o.Username, StringComparison.OrdinalIgnoreCase));
                if (m == null)
                {
                    Console.WriteLine("'" + o.Username + "' is not a Super User account. Super Users found:");
                    foreach (SuperUser u in users) Console.WriteLine("  - " + u.Username);
                    return null;
                }
                return m;
            }

            Console.WriteLine("Super User accounts:");
            for (int i = 0; i < users.Count; i++)
            {
                SuperUser u = users[i];
                Console.WriteLine("  [" + (i + 1) + "] " + u.Username + (u.IsActive ? "" : "  (inactive)") +
                                  (u.Email.Length > 0 ? "  <" + u.Email + ">" : ""));
            }
            if (users.Count == 1) { Console.WriteLine("Only one account exists; selecting it."); return users[0]; }

            Console.Write("Select number: ");
            int n;
            if (!Int32.TryParse((Console.ReadLine() ?? "").Trim(), out n) || n < 1 || n > users.Count)
            {
                Console.WriteLine("Invalid selection.");
                return null;
            }
            return users[n - 1];
        }

        private static bool ConfirmByTyping(string username)
        {
            Console.Write("Type the username '" + username + "' to confirm: ");
            return String.Equals((Console.ReadLine() ?? "").Trim(), username, StringComparison.OrdinalIgnoreCase);
        }

        private static void Banner()
        {
            Console.WriteLine("=====================================================");
            Console.WriteLine(" Vighnesh Payroll Systems - Super User Reset Console");
            Console.WriteLine(" Offline recovery tool. Authorised personnel only.");
            Console.WriteLine("=====================================================");
            Console.WriteLine();
        }

        private static void PrintHelp()
        {
            Console.WriteLine("SuperAdminReset.exe [options]");
            Console.WriteLine();
            Console.WriteLine("Connection (first match wins):");
            Console.WriteLine("  --conn \"<connection string>\"   explicit SQL Server connection string");
            Console.WriteLine("                                 (visible in shell history - prefer --config or the prompt)");
            Console.WriteLine("  --config <path>                Database.config (supports ENC: values made on THIS machine)");
            Console.WriteLine("  (none)                         prompts for the connection string (hidden input)");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --user <name>     Super User to reset (otherwise pick from a list)");
            Console.WriteLine("  --generate        create a random 20-char temporary password and show it once");
            Console.WriteLine("  --reactivate      also set IsActive = 1");
            Console.WriteLine("  --disable-2fa     also turn off two-step sign-in (lost authenticator)");
            Console.WriteLine("  --dry-run         show what would happen; change nothing");
            Console.WriteLine("  --yes             skip the type-the-username confirmation");
            Console.WriteLine("  --help            this text");
            Console.WriteLine();
            Console.WriteLine("Only accounts with IsAdmin = 1 can be reset. Organisation users are never touched.");
            Console.WriteLine("Login lockouts (table LoginAttempts) for the account are cleared automatically.");
        }

        // -------------------------------------------------------- connection

        private static string ResolveConnectionString(Options o)
        {
            if (!String.IsNullOrWhiteSpace(o.Conn)) return o.Conn;

            if (!String.IsNullOrWhiteSpace(o.ConfigPath))
                return ReadFromConfig(o.ConfigPath);

            Console.WriteLine("No --conn or --config supplied.");
            Console.WriteLine("Paste the SQL Server connection string (input is hidden):");
            string s = ReadHidden("> ");
            if (String.IsNullOrWhiteSpace(s)) throw new InvalidOperationException("No connection string provided.");
            return s.Trim();
        }

        private static string ReadFromConfig(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Config file not found: " + path);
            XDocument doc = XDocument.Load(path);
            XElement item = doc.Descendants("add").FirstOrDefault(x => (string)x.Attribute("name") == "PayrollDb");
            string value = item == null ? null : (string)item.Attribute("connectionString");
            if (String.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("PayrollDb not found in " + path);

            if (value.StartsWith("ENC:", StringComparison.Ordinal))
            {
                try
                {
                    byte[] cipher = Convert.FromBase64String(value.Substring(4));
                    byte[] plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.LocalMachine);
                    return Encoding.UTF8.GetString(plain);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Database.config is encrypted for another Windows machine. Use the prompt (or --conn) with the plain connection string instead.", ex);
                }
            }
            return value;
        }

        // ------------------------------------------------------------ options

        private sealed class Options
        {
            public string Conn, ConfigPath, Username;
            public bool GeneratePassword, Reactivate, Disable2fa, DryRun, AssumeYes, ShowHelp;

            public static Options Parse(string[] a)
            {
                Options o = new Options();
                for (int i = 0; i < a.Length; i++)
                {
                    switch (a[i].ToLowerInvariant())
                    {
                        case "--conn": o.Conn = Next(a, ref i); break;
                        case "--config": o.ConfigPath = Next(a, ref i); break;
                        case "--user": o.Username = Next(a, ref i); break;
                        case "--generate": o.GeneratePassword = true; break;
                        case "--reactivate": o.Reactivate = true; break;
                        case "--disable-2fa": o.Disable2fa = true; break;
                        case "--dry-run": o.DryRun = true; break;
                        case "--yes": o.AssumeYes = true; break;
                        case "--help": case "-h": case "/?": o.ShowHelp = true; break;
                        default: throw new ArgumentException("Unknown option: " + a[i] + " (try --help)");
                    }
                }
                return o;
            }

            private static string Next(string[] a, ref int i)
            {
                if (i + 1 >= a.Length) throw new ArgumentException("Missing value after " + a[i]);
                return a[++i];
            }
        }
    }
}
