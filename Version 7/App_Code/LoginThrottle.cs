using System;
using System.Data.OleDb;

namespace PayrollWebApp
{
    /// <summary>
    /// DB-backed login throttling (survives new sessions / app-pool recycles).
    /// Locks a username OR an IP after MaxFailures failures inside WindowMinutes
    /// for LockMinutes. Uses the existing Db helper (? placeholders).
    /// </summary>
    public static class LoginThrottle
    {
        public const int MaxFailures = 5;
        public const int WindowMinutes = 15;
        public const int LockMinutes = 15;

        private static readonly object _lock = new object();
        private static bool _ready;

        public static void EnsureSchema()
        {
            if (_ready) return;
            lock (_lock)
            {
                if (_ready) return;
                Db.Execute(@"IF OBJECT_ID('LoginAttempts','U') IS NULL
                  BEGIN
                    CREATE TABLE LoginAttempts (
                      Id BIGINT IDENTITY(1,1) PRIMARY KEY,
                      Username NVARCHAR(150) NOT NULL,
                      IpAddress NVARCHAR(45) NOT NULL,
                      AttemptedOn DATETIME NOT NULL DEFAULT GETUTCDATE(),
                      Success BIT NOT NULL);
                    CREATE INDEX IX_LoginAttempts_User ON LoginAttempts(Username, AttemptedOn);
                    CREATE INDEX IX_LoginAttempts_Ip ON LoginAttempts(IpAddress, AttemptedOn);
                  END");
                _ready = true;
            }
        }

        /// <summary>True if this username or IP is currently locked out.</summary>
        public static bool IsLocked(string username, string ip, out int minutesLeft)
        {
            EnsureSchema();
            minutesLeft = 0;
            DateTime since = DateTime.UtcNow.AddMinutes(-WindowMinutes);

            // Failures since the last success, inside the window.
            object byUser = Db.Scalar(
                @"SELECT COUNT(*) FROM LoginAttempts
                  WHERE Username=? AND Success=0 AND AttemptedOn>=?
                    AND AttemptedOn > ISNULL((SELECT MAX(AttemptedOn) FROM LoginAttempts WHERE Username=? AND Success=1), '1900-01-01')",
                new OleDbParameter("?", username.ToLowerInvariant()),
                new OleDbParameter("?", since),
                new OleDbParameter("?", username.ToLowerInvariant()));
            object byIp = Db.Scalar(
                "SELECT COUNT(*) FROM LoginAttempts WHERE IpAddress=? AND Success=0 AND AttemptedOn>=?",
                new OleDbParameter("?", ip),
                new OleDbParameter("?", since));

            // IP limit is looser so one office NAT doesn't lock everyone.
            bool locked = Convert.ToInt32(byUser) >= MaxFailures || Convert.ToInt32(byIp) >= MaxFailures * 4;
            if (!locked) return false;

            object last = Db.Scalar(
                "SELECT MAX(AttemptedOn) FROM LoginAttempts WHERE (Username=? OR IpAddress=?) AND Success=0",
                new OleDbParameter("?", username.ToLowerInvariant()),
                new OleDbParameter("?", ip));
            DateTime lastFail = last == null || last == DBNull.Value ? DateTime.UtcNow : (DateTime)last;
            double left = LockMinutes - (DateTime.UtcNow - lastFail).TotalMinutes;
            if (left <= 0) return false;
            minutesLeft = (int)Math.Ceiling(left);
            return true;
        }

        public static void Record(string username, string ip, bool success)
        {
            EnsureSchema();
            Db.Execute("INSERT INTO LoginAttempts (Username, IpAddress, Success) VALUES (?,?,?)",
                new OleDbParameter("?", (username ?? "").ToLowerInvariant()),
                new OleDbParameter("?", ip ?? ""),
                new OleDbParameter("?", success));
            // Housekeeping: keep 30 days.
            Db.Execute("DELETE FROM LoginAttempts WHERE AttemptedOn < DATEADD(day,-30,GETUTCDATE())");
        }
    }
}
