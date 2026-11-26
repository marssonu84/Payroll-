using System;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Web;
using System.Web.Caching;

namespace PayrollWebApp
{
    /// <summary>
    /// Database-backed statutory rules with effective dates and optional per-entity overrides.
    /// Precedence: entity-specific row beats a global row; within the same scope the latest
    /// EffectiveFrom on or before the payroll date wins.
    /// </summary>
    public static class StatutoryRulesService
    {
        private static readonly object _lock = new object();
        private static bool _ready;
        private static int _version;

        public static void EnsureSchema()
        {
            if (_ready) return;
            lock (_lock)
            {
                if (_ready) return;
                Db.Execute(@"IF OBJECT_ID('StatutoryRules','U') IS NULL
                  BEGIN
                    CREATE TABLE StatutoryRules (
                      Id INT IDENTITY(1,1) PRIMARY KEY,
                      TenantID INT NULL,
                      RuleKey NVARCHAR(40) NOT NULL,
                      RuleValue DECIMAL(18,6) NOT NULL,
                      EffectiveFrom DATE NOT NULL,
                      Notes NVARCHAR(200) NULL,
                      CreatedOn DATETIME NOT NULL DEFAULT GETUTCDATE(),
                      CreatedBy NVARCHAR(150) NULL);
                    CREATE INDEX IX_StatutoryRules_Key ON StatutoryRules(RuleKey, TenantID, EffectiveFrom);
                  END");

                // Seed global defaults once (identical to the V13 hard-coded values).
                object n = Db.Scalar("SELECT COUNT(*) FROM StatutoryRules WHERE TenantID IS NULL");
                if (Convert.ToInt32(n) == 0)
                {
                    foreach (string key in StatutoryRules.AllKeys)
                    {
                        Db.Execute(
                            "INSERT INTO StatutoryRules (TenantID, RuleKey, RuleValue, EffectiveFrom, Notes, CreatedBy) VALUES (NULL,?,?,'2000-01-01','Built-in default','system')",
                            new OleDbParameter("?", key),
                            new OleDbParameter("?", StatutoryRules.DefaultFor(key)));
                    }
                }
                _ready = true;
            }
        }

        /// <summary>Call after any change so cached rules are refreshed.</summary>
        public static void Invalidate()
        {
            System.Threading.Interlocked.Increment(ref _version);
        }

        /// <summary>
        /// Rules for the signed-in entity on a date. If the database is unreachable the built-in
        /// defaults are used (same numbers as V13) so payroll screens keep working.
        /// </summary>
        public static StatutoryRules Resolve(DateTime asOf)
        {
            int tenant = 0;
            try { tenant = Auth.CurrentTenantId(); } catch { }
            return ResolveFor(tenant, asOf);
        }

        public static StatutoryRules ResolveFor(int tenantId, DateTime asOf)
        {
            string cacheKey = "statrules:" + _version + ":" + tenantId + ":" + asOf.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            try
            {
                StatutoryRules cached = HttpRuntime.Cache == null ? null : HttpRuntime.Cache[cacheKey] as StatutoryRules;
                if (cached != null) return cached;

                EnsureSchema();
                DataTable dt = Db.Query(
                    @"SELECT RuleKey, RuleValue FROM StatutoryRules
                      WHERE EffectiveFrom<=? AND (TenantID IS NULL OR TenantID=?)
                      ORDER BY CASE WHEN TenantID IS NULL THEN 0 ELSE 1 END, EffectiveFrom, Id",
                    new OleDbParameter("?", asOf.Date),
                    new OleDbParameter("?", tenantId));

                StatutoryRules rules = StatutoryRules.CreateDefault();
                foreach (DataRow r in dt.Rows)
                    rules.Apply(Convert.ToString(r["RuleKey"]), Convert.ToDecimal(r["RuleValue"]));

                if (HttpRuntime.Cache != null)
                    HttpRuntime.Cache.Insert(cacheKey, rules, null, DateTime.UtcNow.AddMinutes(5), Cache.NoSlidingExpiration);
                return rules;
            }
            catch
            {
                return StatutoryRules.CreateDefault();
            }
        }

        public static DataTable ListRules(int? tenantId)
        {
            EnsureSchema();
            if (tenantId.HasValue)
                return Db.Query(
                    @"SELECT Id, TenantID, RuleKey, RuleValue, EffectiveFrom, Notes, CreatedBy, CreatedOn
                      FROM StatutoryRules WHERE TenantID=? ORDER BY RuleKey, EffectiveFrom DESC",
                    new OleDbParameter("?", tenantId.Value));
            return Db.Query(
                @"SELECT Id, TenantID, RuleKey, RuleValue, EffectiveFrom, Notes, CreatedBy, CreatedOn
                  FROM StatutoryRules WHERE TenantID IS NULL ORDER BY RuleKey, EffectiveFrom DESC");
        }

        public static void AddRule(int? tenantId, string key, decimal value, DateTime effectiveFrom, string notes, string user)
        {
            string err = StatutoryRules.Validate(key, value);
            if (err != null) throw new InvalidOperationException(err);
            EnsureSchema();
            Db.Execute(
                "INSERT INTO StatutoryRules (TenantID, RuleKey, RuleValue, EffectiveFrom, Notes, CreatedBy) VALUES (?,?,?,?,?,?)",
                new OleDbParameter("?", tenantId.HasValue ? (object)tenantId.Value : DBNull.Value),
                new OleDbParameter("?", key),
                new OleDbParameter("?", value),
                new OleDbParameter("?", effectiveFrom.Date),
                new OleDbParameter("?", String.IsNullOrEmpty(notes) ? (object)DBNull.Value : notes),
                new OleDbParameter("?", user ?? ""));
            Invalidate();
        }
    }
}
