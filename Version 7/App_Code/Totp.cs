using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.Security.Cryptography;
using System.Text;

namespace PayrollWebApp
{
    /// <summary>
    /// V10.24 Two-step sign-in (TOTP, RFC 6238) for authenticator apps.
    /// Switch off without redeploying code: Web.config appSettings TwoStepEnabled = false.
    /// </summary>
    public static class Totp
    {
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        private const string BackupAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int StepSeconds = 30;
        private const int MaxFailures = 10;
        private const int FailureWindowMinutes = 15;

        private static bool _schemaReady;
        private static readonly object SchemaLock = new object();
        private static readonly Dictionary<int, List<DateTime>> Failures = new Dictionary<int, List<DateTime>>();
        private static readonly object FailureLock = new object();

        public static bool FeatureEnabled()
        {
            string v = ConfigurationManager.AppSettings["TwoStepEnabled"];
            return !String.Equals((v ?? "").Trim(), "false", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Adds the 2-step columns. Returns false (and login keeps working) if they cannot be created.</summary>
        public static bool EnsureSchema()
        {
            if (_schemaReady) return true;
            lock (SchemaLock)
            {
                if (_schemaReady) return true;
                try
                {
                    Db.Execute("IF COL_LENGTH('Users','TotpSecret') IS NULL ALTER TABLE Users ADD TotpSecret NVARCHAR(64) NULL");
                    Db.Execute("IF COL_LENGTH('Users','TotpEnabled') IS NULL ALTER TABLE Users ADD TotpEnabled BIT NOT NULL CONSTRAINT DF_Users_TotpEnabled DEFAULT 0");
                    Db.Execute("IF COL_LENGTH('Users','TotpBackupCodes') IS NULL ALTER TABLE Users ADD TotpBackupCodes NVARCHAR(MAX) NULL");
                    Db.Execute("IF COL_LENGTH('Users','TotpLastStep') IS NULL ALTER TABLE Users ADD TotpLastStep BIGINT NULL");
                    object n = Db.Scalar("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name IN ('TotpSecret','TotpEnabled','TotpBackupCodes','TotpLastStep')");
                    _schemaReady = Convert.ToInt32(n) == 4;
                }
                catch (Exception)
                {
                    _schemaReady = false;
                }
                return _schemaReady;
            }
        }

        public static bool IsEnabledForUser(int userId)
        {
            object r = Db.Scalar("SELECT TotpEnabled FROM Users WHERE id=?", new OleDbParameter("?", userId));
            return r != null && r != DBNull.Value && Convert.ToBoolean(r);
        }

        // ---------- secret / codes ----------

        public static string GenerateSecret()
        {
            byte[] b = new byte[20];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) { rng.GetBytes(b); }
            return Base32Encode(b);
        }

        public static string FormatSecret(string secret)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < secret.Length; i++)
            {
                if (i > 0 && i % 4 == 0) sb.Append(' ');
                sb.Append(secret[i]);
            }
            return sb.ToString();
        }

        public static string BuildOtpAuthUri(string username, string secret)
        {
            const string issuer = "Vighnesh Payroll Systems";
            return "otpauth://totp/" + Uri.EscapeDataString(issuer + ":" + username)
                + "?secret=" + secret
                + "&issuer=" + Uri.EscapeDataString(issuer)
                + "&algorithm=SHA1&digits=6&period=30";
        }

        public static string[] GenerateBackupCodes()
        {
            string[] codes = new string[8];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                for (int c = 0; c < codes.Length; c++)
                {
                    byte[] b = new byte[10];
                    rng.GetBytes(b);
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < 10; i++)
                    {
                        if (i == 5) sb.Append('-');
                        sb.Append(BackupAlphabet[b[i] & 31]);
                    }
                    codes[c] = sb.ToString();
                }
            }
            return codes;
        }

        // ---------- verification ----------

        public static bool Verify(string secret, string code, long lastStep, out long matchedStep)
        {
            matchedStep = -1;
            byte[] key = Base32Decode(secret);
            if (key == null || code == null) return false;
            code = code.Replace(" ", "").Trim();
            if (code.Length != 6 || !AllDigits(code)) return false;

            long now = CurrentStep();
            for (long s = now - 1; s <= now + 1; s++)
            {
                if (s <= lastStep) continue;
                if (ConstantEquals(ComputeCode(key, s), code))
                {
                    matchedStep = s;
                    return true;
                }
            }
            return false;
        }

        public static bool IsLockedOut(int userId)
        {
            lock (FailureLock)
            {
                List<DateTime> list;
                if (!Failures.TryGetValue(userId, out list)) return false;
                list.RemoveAll(delegate (DateTime t) { return t < DateTime.UtcNow.AddMinutes(-FailureWindowMinutes); });
                return list.Count >= MaxFailures;
            }
        }

        private static void RecordFailure(int userId)
        {
            lock (FailureLock)
            {
                List<DateTime> list;
                if (!Failures.TryGetValue(userId, out list))
                {
                    list = new List<DateTime>();
                    Failures[userId] = list;
                }
                list.Add(DateTime.UtcNow);
            }
        }

        private static void ClearFailures(int userId)
        {
            lock (FailureLock) { Failures.Remove(userId); }
        }

        /// <summary>Checks an authenticator code or a one-time backup code for a user who has 2-step enabled.</summary>
        public static bool VerifyLogin(int userId, string input)
        {
            if (IsLockedOut(userId)) return false;

            DataTable dt = Db.Query("SELECT TotpSecret, TotpLastStep FROM Users WHERE id=? AND TotpEnabled=1", new OleDbParameter("?", userId));
            if (dt == null || dt.Rows.Count == 0) return false;

            string secret = Convert.ToString(dt.Rows[0]["TotpSecret"]);
            long last = dt.Rows[0]["TotpLastStep"] == DBNull.Value ? -1 : Convert.ToInt64(dt.Rows[0]["TotpLastStep"]);
            string cleaned = (input ?? "").Replace(" ", "").Replace("-", "").Trim();

            bool ok = false;
            if (cleaned.Length == 6 && AllDigits(cleaned))
            {
                long matched;
                if (Verify(secret, cleaned, last, out matched))
                {
                    // Atomic: a code can be used only once, even with two simultaneous requests.
                    ok = Db.Execute(
                        "UPDATE Users SET TotpLastStep=? WHERE id=? AND (TotpLastStep IS NULL OR TotpLastStep < ?)",
                        new OleDbParameter("?", matched),
                        new OleDbParameter("?", userId),
                        new OleDbParameter("?", matched)) == 1;
                }
            }
            else
            {
                ok = TryConsumeBackupCode(userId, input);
            }

            if (ok) ClearFailures(userId); else RecordFailure(userId);
            return ok;
        }

        private static bool TryConsumeBackupCode(int userId, string input)
        {
            string normalized = NormalizeBackup(input);
            if (normalized.Length != 10) return false;

            object stored = Db.Scalar("SELECT TotpBackupCodes FROM Users WHERE id=?", new OleDbParameter("?", userId));
            string current = (stored == null || stored == DBNull.Value) ? "" : Convert.ToString(stored);
            if (current.Length == 0) return false;

            string target = HashBackup(userId, normalized);
            List<string> remaining = new List<string>();
            bool found = false;
            foreach (string h in current.Split(','))
            {
                if (h.Length == 0) continue;
                if (!found && ConstantEquals(h, target)) { found = true; continue; }
                remaining.Add(h);
            }
            if (!found) return false;

            return Db.Execute(
                "UPDATE Users SET TotpBackupCodes=? WHERE id=? AND TotpBackupCodes=?",
                new OleDbParameter("?", String.Join(",", remaining.ToArray())),
                new OleDbParameter("?", userId),
                new OleDbParameter("?", current)) == 1;
        }

        // ---------- storage ----------

        public static void SaveEnrollment(int userId, string secret, string[] backupCodes, long lastStep)
        {
            List<string> hashes = new List<string>();
            foreach (string c in backupCodes) hashes.Add(HashBackup(userId, NormalizeBackup(c)));

            Db.Execute(
                "UPDATE Users SET TotpSecret=?, TotpEnabled=1, TotpBackupCodes=?, TotpLastStep=? WHERE id=?",
                new OleDbParameter("?", secret),
                new OleDbParameter("?", String.Join(",", hashes.ToArray())),
                new OleDbParameter("?", lastStep),
                new OleDbParameter("?", userId));
            ClearFailures(userId);
        }

        public static void Disable(int userId)
        {
            Db.Execute("UPDATE Users SET TotpSecret=NULL, TotpEnabled=0, TotpBackupCodes=NULL, TotpLastStep=NULL WHERE id=?", new OleDbParameter("?", userId));
            ClearFailures(userId);
        }

        // ---------- helpers ----------

        private static string NormalizeBackup(string s)
        {
            return (s ?? "").ToUpperInvariant().Replace("-", "").Replace(" ", "").Trim();
        }

        private static string HashBackup(int userId, string normalized)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("TOTPBACKUP|" + userId + "|" + normalized));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static bool AllDigits(string s)
        {
            for (int i = 0; i < s.Length; i++) { if (s[i] < '0' || s[i] > '9') return false; }
            return true;
        }

        private static bool ConstantEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static long CurrentStep()
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(t.TotalSeconds / StepSeconds);
        }

        public static string ComputeCode(byte[] key, long counter)
        {
            byte[] msg = new byte[8];
            for (int i = 7; i >= 0; i--) { msg[i] = (byte)(counter & 0xFF); counter >>= 8; }

            byte[] hash;
            using (HMACSHA1 h = new HMACSHA1(key)) { hash = h.ComputeHash(msg); }

            int offset = hash[hash.Length - 1] & 0x0F;
            int bin = ((hash[offset] & 0x7F) << 24)
                    | ((hash[offset + 1] & 0xFF) << 16)
                    | ((hash[offset + 2] & 0xFF) << 8)
                    | (hash[offset + 3] & 0xFF);
            return (bin % 1000000).ToString("D6");
        }

        public static string Base32Encode(byte[] data)
        {
            StringBuilder sb = new StringBuilder();
            int buffer = 0, bits = 0;
            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
                buffer &= (1 << bits) - 1;
            }
            if (bits > 0) sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
            return sb.ToString();
        }

        public static byte[] Base32Decode(string s)
        {
            if (String.IsNullOrEmpty(s)) return null;
            s = s.ToUpperInvariant().Replace(" ", "").Replace("-", "").Replace("=", "");
            List<byte> output = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (char c in s)
            {
                int idx = Base32Alphabet.IndexOf(c);
                if (idx < 0) return null;
                buffer = (buffer << 5) | idx;
                bits += 5;
                if (bits >= 8)
                {
                    output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                    bits -= 8;
                }
                buffer &= (1 << bits) - 1;
            }
            return output.ToArray();
        }
    }
}
