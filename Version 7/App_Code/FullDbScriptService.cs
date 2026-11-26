using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace PayrollWebApp
{
    /// <summary>
    /// V10.28: Super User full database script (.sql) - tables, indexes, keys and all rows.
    /// Uses only catalog and SELECT queries (no BACKUP/RESTORE permission), so it works on shared hosting
    /// where "Export Dump" or BACKUP DATABASE is unavailable. Rows are streamed, not held in memory.
    /// Run the file on an EMPTY database (SSMS or sqlcmd) to rebuild everything.
    /// </summary>
    public static class FullDbScriptService
    {
        private const int RowsPerInsert = 100;
        private static readonly Regex SensitiveName =
            new Regex("password|passwd|pwd|totp|secret", RegexOptions.IgnoreCase);

        public class ColumnInfo
        {
            public string Name;
            public string TypeName;
            public string SqlType;
            public bool Nullable;
            public bool IsIdentity;
            public long Seed;
            public long Increment;
            public string DefaultName;
            public string DefaultDef;
            public bool Sensitive;
        }

        public class IndexInfo
        {
            public string Name;
            public bool IsPrimaryKey;
            public bool IsUnique;
            public bool Clustered;
            public string Filter;
            public List<string> Keys = new List<string>();
            public List<string> KeyNames = new List<string>();
            public List<string> Included = new List<string>();
        }

        public class TableInfo
        {
            public int ObjectId;
            public string Name;
            public List<ColumnInfo> Columns = new List<ColumnInfo>();
            public List<IndexInfo> Indexes = new List<IndexInfo>();

            public bool HasIdentity
            {
                get { return Columns.Exists(delegate (ColumnInfo c) { return c.IsIdentity; }); }
            }
        }

        public class ForeignKeyInfo
        {
            public string Name;
            public string Table;
            public string RefTable;
            public int DeleteAction;
            public int UpdateAction;
            public List<string> Cols = new List<string>();
            public List<string> RefCols = new List<string>();
        }

        public class Plan
        {
            public string DatabaseName;
            public bool IncludeSignIn;
            public int SkippedTables;
            public List<TableInfo> Tables = new List<TableInfo>();
            public List<ForeignKeyInfo> ForeignKeys = new List<ForeignKeyInfo>();
            public List<string> Notes = new List<string>();

            /// <summary>Streams the whole script. Never throws to the caller for database problems:
            /// a failure is written into the file as "EXPORT FAILED" and the COMPLETE footer is missing.</summary>
            public void WriteTo(TextWriter w)
            {
                long totalRows = 0;
                string stage = "writing the header";
                try
                {
                    WriteHeader(w);

                    stage = "writing table definitions";
                    w.WriteLine("-- ===== TABLES =====");
                    foreach (TableInfo t in Tables) WriteCreateTable(w, t);

                    w.WriteLine("-- ===== DATA =====");
                    using (SqlConnection con = new SqlConnection(Db.ConnectionString))
                    {
                        con.Open();
                        foreach (TableInfo t in Tables)
                        {
                            stage = "reading data from table " + t.Name;
                            totalRows += WriteData(w, con, t, IncludeSignIn);
                            w.Flush();
                            if (!ClientAlive()) return;
                        }
                    }

                    stage = "writing indexes";
                    w.WriteLine("-- ===== INDEXES =====");
                    foreach (TableInfo t in Tables) WriteIndexes(w, t);

                    stage = "writing foreign keys";
                    w.WriteLine("-- ===== FOREIGN KEYS =====");
                    foreach (ForeignKeyInfo fk in ForeignKeys) WriteForeignKey(w, fk);

                    w.WriteLine();
                    w.WriteLine("-- COMPLETE: " + Tables.Count + " tables, " + totalRows + " rows.");
                }
                catch (Exception ex)
                {
                    try
                    {
                        w.WriteLine();
                        w.WriteLine("-- EXPORT FAILED - THIS FILE IS INCOMPLETE. Problem while " + stage + " (" + ex.GetType().Name + "): " + OneLine(ex.Message));
                    }
                    catch (Exception) { }
                }
            }

            private void WriteHeader(TextWriter w)
            {
                w.WriteLine("-- Vighnesh Payroll Systems - full database script");
                w.WriteLine("-- Generated (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                w.WriteLine("-- Source database: " + OneLine(DatabaseName));
                w.WriteLine("-- Sign-in data (password hashes, two-step keys): " + (IncludeSignIn ? "INCLUDED - keep this file private" : "NOT included - users must be given new passwords"));
                w.WriteLine("-- Tables: " + Tables.Count + (SkippedTables > 0 ? " (" + SkippedTables + " table(s) outside the dbo schema were not exported)" : ""));
                foreach (string n in Notes) w.WriteLine("-- NOTE: " + OneLine(n));
                w.WriteLine("--");
                w.WriteLine("-- HOW TO RESTORE");
                w.WriteLine("--  1. Create an EMPTY database (a spare local database is safest).");
                w.WriteLine("--  2. Open this file in SQL Server Management Studio, connected to that empty database, and run it.");
                w.WriteLine("--  3. The last line must say 'COMPLETE'. If it says 'EXPORT FAILED' or is missing, the file is incomplete.");
                w.WriteLine("--  4. Optional: back up that database to a .bak file, then use Import Dump in the hosting panel.");
                w.WriteLine();
                w.WriteLine("SET NOCOUNT ON;");
                w.WriteLine("GO");
            }

            private static void WriteCreateTable(TextWriter w, TableInfo t)
            {
                w.WriteLine("CREATE TABLE [dbo]." + Q(t.Name) + " (");

                List<string> lines = new List<string>();
                foreach (ColumnInfo c in t.Columns)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("    " + Q(c.Name) + " " + c.SqlType);
                    if (c.IsIdentity)
                    {
                        sb.Append(" IDENTITY(" + c.Seed.ToString(CultureInfo.InvariantCulture) + "," + c.Increment.ToString(CultureInfo.InvariantCulture) + ")");
                    }
                    sb.Append(c.Nullable ? " NULL" : " NOT NULL");
                    if (!String.IsNullOrEmpty(c.DefaultName) && !String.IsNullOrEmpty(c.DefaultDef))
                    {
                        sb.Append(" CONSTRAINT " + Q(c.DefaultName) + " DEFAULT " + c.DefaultDef);
                    }
                    lines.Add(sb.ToString());
                }

                foreach (IndexInfo ix in t.Indexes)
                {
                    if (!ix.IsPrimaryKey) continue;
                    lines.Add("    CONSTRAINT " + Q(ix.Name) + " PRIMARY KEY " + (ix.Clustered ? "CLUSTERED" : "NONCLUSTERED")
                        + " (" + String.Join(", ", ix.Keys.ToArray()) + ")");
                }

                w.WriteLine(String.Join("," + Environment.NewLine, lines.ToArray()));
                w.WriteLine(");");
                w.WriteLine("GO");
            }

            private static long WriteData(TextWriter w, SqlConnection con, TableInfo t, bool includeSignIn)
            {
                w.WriteLine("-- Data: " + Q(t.Name));
                if (t.Columns.Count == 0) return 0;

                List<string> names = new List<string>();
                foreach (ColumnInfo c in t.Columns) names.Add(Q(c.Name));
                string colList = String.Join(", ", names.ToArray());
                string insertHead = "INSERT INTO [dbo]." + Q(t.Name) + " (" + colList + ") VALUES";

                long count = 0;
                int inBatch = 0;
                bool identityOn = false;

                using (SqlCommand cmd = new SqlCommand("SELECT " + colList + " FROM [dbo]." + Q(t.Name), con))
                {
                    cmd.CommandTimeout = 600;
                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            if (inBatch == 0)
                            {
                                if (!identityOn && t.HasIdentity)
                                {
                                    w.WriteLine("SET IDENTITY_INSERT [dbo]." + Q(t.Name) + " ON;");
                                    identityOn = true;
                                }
                                w.WriteLine(insertHead);
                            }
                            else
                            {
                                w.WriteLine(",");
                            }

                            StringBuilder row = new StringBuilder();
                            row.Append("(");
                            for (int i = 0; i < t.Columns.Count; i++)
                            {
                                if (i > 0) row.Append(", ");
                                row.Append(Literal(r, i, t.Columns[i], includeSignIn));
                            }
                            row.Append(")");
                            w.Write(row.ToString());

                            inBatch++;
                            count++;
                            if (inBatch >= RowsPerInsert)
                            {
                                w.WriteLine(";");
                                w.WriteLine("GO");
                                inBatch = 0;
                                if (!ClientAlive()) return count;
                            }
                        }
                    }
                }

                if (inBatch > 0)
                {
                    w.WriteLine(";");
                    w.WriteLine("GO");
                }
                if (identityOn)
                {
                    w.WriteLine("SET IDENTITY_INSERT [dbo]." + Q(t.Name) + " OFF;");
                    w.WriteLine("GO");
                }
                w.WriteLine("-- " + Q(t.Name) + ": " + count.ToString(CultureInfo.InvariantCulture) + " row(s)");
                return count;
            }

            private static void WriteIndexes(TextWriter w, TableInfo t)
            {
                foreach (IndexInfo ix in t.Indexes)
                {
                    if (ix.IsPrimaryKey) continue;
                    StringBuilder sb = new StringBuilder();
                    sb.Append("CREATE ");
                    if (ix.IsUnique) sb.Append("UNIQUE ");
                    sb.Append(ix.Clustered ? "CLUSTERED " : "NONCLUSTERED ");
                    sb.Append("INDEX " + Q(ix.Name) + " ON [dbo]." + Q(t.Name) + " (" + String.Join(", ", ix.Keys.ToArray()) + ")");
                    if (ix.Included.Count > 0) sb.Append(" INCLUDE (" + String.Join(", ", ix.Included.ToArray()) + ")");
                    if (!String.IsNullOrEmpty(ix.Filter)) sb.Append(" WHERE " + ix.Filter);
                    sb.Append(";");
                    w.WriteLine(sb.ToString());
                    w.WriteLine("GO");
                }
            }

            private static void WriteForeignKey(TextWriter w, ForeignKeyInfo fk)
            {
                List<string> cols = new List<string>();
                foreach (string c in fk.Cols) cols.Add(Q(c));
                List<string> refs = new List<string>();
                foreach (string c in fk.RefCols) refs.Add(Q(c));

                w.WriteLine("ALTER TABLE [dbo]." + Q(fk.Table) + " WITH CHECK ADD CONSTRAINT " + Q(fk.Name)
                    + " FOREIGN KEY (" + String.Join(", ", cols.ToArray()) + ")"
                    + " REFERENCES [dbo]." + Q(fk.RefTable) + " (" + String.Join(", ", refs.ToArray()) + ")"
                    + " ON DELETE " + ActionText(fk.DeleteAction) + " ON UPDATE " + ActionText(fk.UpdateAction) + ";");
                w.WriteLine("GO");
            }
        }

        // ---------- reading the database structure ----------

        public static Plan Prepare(bool includeSignIn)
        {
            Plan plan = new Plan();
            plan.IncludeSignIn = includeSignIn;

            using (SqlConnection con = new SqlConnection(Db.ConnectionString))
            {
                con.Open();

                plan.DatabaseName = Convert.ToString(Scalar(con, "SELECT DB_NAME()"));
                plan.SkippedTables = Convert.ToInt32(Scalar(con,
                    "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0 AND SCHEMA_NAME(schema_id) <> 'dbo'"));

                DataTable tables = Fill(con,
                    "SELECT object_id, name FROM sys.tables WHERE is_ms_shipped = 0 AND SCHEMA_NAME(schema_id) = 'dbo' AND name <> 'sysdiagrams' ORDER BY name", 0);

                foreach (DataRow tr in tables.Rows)
                {
                    TableInfo t = new TableInfo();
                    t.ObjectId = Convert.ToInt32(tr["object_id"]);
                    t.Name = Convert.ToString(tr["name"]);
                    LoadColumns(con, t, plan);
                    LoadIndexes(con, t, plan);
                    plan.Tables.Add(t);
                }

                LoadForeignKeys(con, plan);
            }
            return plan;
        }

        private static void LoadColumns(SqlConnection con, TableInfo t, Plan plan)
        {
            DataTable dt = Fill(con,
                "SELECT c.name AS col_name, ISNULL(bt.name, ty.name) AS type_name, c.max_length, c.precision, c.scale, " +
                "       c.is_nullable, c.is_identity, c.is_computed, " +
                "       CAST(ic.seed_value AS BIGINT) AS seed_value, CAST(ic.increment_value AS BIGINT) AS inc_value, " +
                "       dc.name AS df_name, dc.definition AS df_def " +
                "FROM sys.columns c " +
                "LEFT JOIN sys.types ty ON ty.user_type_id = c.user_type_id " +
                "LEFT JOIN sys.types bt ON bt.user_type_id = c.system_type_id " +
                "LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id " +
                "LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id " +
                "WHERE c.object_id = @id ORDER BY c.column_id", t.ObjectId);

            foreach (DataRow r in dt.Rows)
            {
                string name = Convert.ToString(r["col_name"]);
                string type = Convert.ToString(r["type_name"]).ToLowerInvariant();

                if (Convert.ToBoolean(r["is_computed"]) || IsUnsupportedType(type))
                {
                    plan.Notes.Add("Column " + t.Name + "." + name + " (" + type + ") was not exported.");
                    continue;
                }

                ColumnInfo c = new ColumnInfo();
                c.Name = name;
                c.TypeName = type;
                c.SqlType = FormatType(type,
                    Convert.ToInt32(r["max_length"]),
                    Convert.ToInt32(r["precision"]),
                    Convert.ToInt32(r["scale"]));
                c.Nullable = Convert.ToBoolean(r["is_nullable"]);
                c.IsIdentity = Convert.ToBoolean(r["is_identity"]);
                if (c.IsIdentity && r["seed_value"] != DBNull.Value)
                {
                    c.Seed = Convert.ToInt64(r["seed_value"]);
                    c.Increment = Convert.ToInt64(r["inc_value"]);
                }
                if (r["df_name"] != DBNull.Value)
                {
                    c.DefaultName = Convert.ToString(r["df_name"]);
                    c.DefaultDef = Convert.ToString(r["df_def"]);
                }
                c.Sensitive = SensitiveName.IsMatch(name);
                t.Columns.Add(c);
            }
        }

        private static void LoadIndexes(SqlConnection con, TableInfo t, Plan plan)
        {
            DataTable dt = Fill(con,
                "SELECT i.index_id, i.name AS idx_name, i.type AS idx_type, i.is_primary_key, i.is_unique, i.has_filter, i.filter_definition, " +
                "       c.name AS col_name, ic.is_descending_key, ic.is_included_column " +
                "FROM sys.indexes i " +
                "INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id " +
                "INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
                "WHERE i.object_id = @id AND i.type IN (1, 2) AND i.is_hypothetical = 0 " +
                "ORDER BY i.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id", t.ObjectId);

            Dictionary<int, IndexInfo> byId = new Dictionary<int, IndexInfo>();
            List<IndexInfo> ordered = new List<IndexInfo>();

            foreach (DataRow r in dt.Rows)
            {
                int id = Convert.ToInt32(r["index_id"]);
                IndexInfo ix;
                if (!byId.TryGetValue(id, out ix))
                {
                    ix = new IndexInfo();
                    ix.Name = Convert.ToString(r["idx_name"]);
                    ix.IsPrimaryKey = Convert.ToBoolean(r["is_primary_key"]);
                    ix.IsUnique = Convert.ToBoolean(r["is_unique"]);
                    ix.Clustered = Convert.ToInt32(r["idx_type"]) == 1;
                    if (Convert.ToBoolean(r["has_filter"]) && r["filter_definition"] != DBNull.Value)
                    {
                        ix.Filter = Convert.ToString(r["filter_definition"]);
                    }
                    byId[id] = ix;
                    ordered.Add(ix);
                }

                string col = Convert.ToString(r["col_name"]);
                if (Convert.ToBoolean(r["is_included_column"]))
                {
                    ix.Included.Add(Q(col));
                }
                else
                {
                    ix.KeyNames.Add(col);
                    ix.Keys.Add(Q(col) + (Convert.ToBoolean(r["is_descending_key"]) ? " DESC" : " ASC"));
                }
            }

            foreach (IndexInfo ix in ordered)
            {
                bool ok = true;
                foreach (string k in ix.KeyNames) { if (!HasColumn(t, k)) ok = false; }
                if (!ok)
                {
                    plan.Notes.Add("Index " + ix.Name + " on " + t.Name + " was not exported (it uses a column that was skipped).");
                    continue;
                }
                t.Indexes.Add(ix);
            }
        }

        private static void LoadForeignKeys(SqlConnection con, Plan plan)
        {
            DataTable dt = Fill(con,
                "SELECT fk.name AS fk_name, pt.name AS parent_table, rt.name AS ref_table, " +
                "       fk.delete_referential_action AS del_act, fk.update_referential_action AS upd_act, " +
                "       pc.name AS parent_col, rc.name AS ref_col " +
                "FROM sys.foreign_keys fk " +
                "INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id " +
                "INNER JOIN sys.tables pt ON pt.object_id = fk.parent_object_id " +
                "INNER JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id " +
                "INNER JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id " +
                "INNER JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id " +
                "WHERE SCHEMA_NAME(pt.schema_id) = 'dbo' AND SCHEMA_NAME(rt.schema_id) = 'dbo' " +
                "  AND pt.name <> 'sysdiagrams' AND rt.name <> 'sysdiagrams' " +
                "ORDER BY pt.name, fk.name, fkc.constraint_column_id", 0);

            Dictionary<string, ForeignKeyInfo> byKey = new Dictionary<string, ForeignKeyInfo>();
            foreach (DataRow r in dt.Rows)
            {
                string table = Convert.ToString(r["parent_table"]);
                string name = Convert.ToString(r["fk_name"]);
                string key = table + "|" + name;
                ForeignKeyInfo fk;
                if (!byKey.TryGetValue(key, out fk))
                {
                    fk = new ForeignKeyInfo();
                    fk.Name = name;
                    fk.Table = table;
                    fk.RefTable = Convert.ToString(r["ref_table"]);
                    fk.DeleteAction = Convert.ToInt32(r["del_act"]);
                    fk.UpdateAction = Convert.ToInt32(r["upd_act"]);
                    byKey[key] = fk;
                    plan.ForeignKeys.Add(fk);
                }
                fk.Cols.Add(Convert.ToString(r["parent_col"]));
                fk.RefCols.Add(Convert.ToString(r["ref_col"]));
            }
        }

        // ---------- helpers ----------

        private static bool HasColumn(TableInfo t, string name)
        {
            foreach (ColumnInfo c in t.Columns)
            {
                if (String.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsUnsupportedType(string type)
        {
            return type == "timestamp" || type == "rowversion" || type == "sql_variant"
                || type == "geography" || type == "geometry" || type == "hierarchyid";
        }

        private static string FormatType(string type, int maxLen, int precision, int scale)
        {
            switch (type)
            {
                case "nvarchar":
                case "nchar":
                    return type + "(" + (maxLen == -1 ? "MAX" : (maxLen / 2).ToString(CultureInfo.InvariantCulture)) + ")";
                case "varchar":
                case "char":
                case "varbinary":
                case "binary":
                    return type + "(" + (maxLen == -1 ? "MAX" : maxLen.ToString(CultureInfo.InvariantCulture)) + ")";
                case "decimal":
                case "numeric":
                    return type + "(" + precision.ToString(CultureInfo.InvariantCulture) + "," + scale.ToString(CultureInfo.InvariantCulture) + ")";
                case "datetime2":
                case "time":
                case "datetimeoffset":
                    return type + "(" + scale.ToString(CultureInfo.InvariantCulture) + ")";
                case "float":
                    return precision == 53 ? "float" : "float(" + precision.ToString(CultureInfo.InvariantCulture) + ")";
                default:
                    return type;
            }
        }

        private static string ActionText(int code)
        {
            switch (code)
            {
                case 1: return "CASCADE";
                case 2: return "SET NULL";
                case 3: return "SET DEFAULT";
                default: return "NO ACTION";
            }
        }

        private static string Q(string name)
        {
            return "[" + name.Replace("]", "]]") + "]";
        }

        private static string OneLine(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 250 ? s.Substring(0, 250) : s;
        }

        private static bool ClientAlive()
        {
            HttpContext ctx = HttpContext.Current;
            return ctx == null || ctx.Response.IsClientConnected;
        }

        private static bool IsTextType(string type)
        {
            return type == "char" || type == "varchar" || type == "text" || type == "nchar"
                || type == "nvarchar" || type == "ntext" || type == "xml";
        }

        private static string Literal(SqlDataReader r, int i, ColumnInfo c, bool includeSignIn)
        {
            if (c.Sensitive && !includeSignIn)
            {
                if (c.Nullable) return "NULL";
                if (IsTextType(c.TypeName)) return "N''";
                if (c.TypeName == "binary" || c.TypeName == "varbinary" || c.TypeName == "image") return "0x";
                return "0";
            }

            if (r.IsDBNull(i)) return "NULL";
            object v = r.GetValue(i);
            CultureInfo inv = CultureInfo.InvariantCulture;

            switch (c.TypeName)
            {
                case "bit":
                    return Convert.ToBoolean(v) ? "1" : "0";
                case "tinyint":
                case "smallint":
                case "int":
                case "bigint":
                case "decimal":
                case "numeric":
                case "money":
                case "smallmoney":
                    return Convert.ToString(v, inv);
                case "float":
                    return Convert.ToDouble(v).ToString("R", inv);
                case "real":
                    return Convert.ToSingle(v).ToString("R", inv);
                case "datetime":
                case "smalldatetime":
                    return "'" + ((DateTime)v).ToString("yyyy-MM-dd'T'HH:mm:ss.fff", inv) + "'";
                case "datetime2":
                    return "'" + ((DateTime)v).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", inv) + "'";
                case "date":
                    return "'" + ((DateTime)v).ToString("yyyy-MM-dd", inv) + "'";
                case "time":
                    return "'" + ((TimeSpan)v).ToString(@"hh\:mm\:ss\.fffffff", inv) + "'";
                case "datetimeoffset":
                    return "'" + ((DateTimeOffset)v).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", inv) + "'";
                case "uniqueidentifier":
                    return "'" + ((Guid)v).ToString("D").ToUpperInvariant() + "'";
                case "binary":
                case "varbinary":
                case "image":
                    return "0x" + BitConverter.ToString((byte[])v).Replace("-", "");
                default:
                    return "N'" + Convert.ToString(v, inv).Replace("'", "''") + "'";
            }
        }

        private static DataTable Fill(SqlConnection con, string sql, int objectId)
        {
            DataTable dt = new DataTable();
            using (SqlCommand cmd = new SqlCommand(sql, con))
            {
                cmd.CommandTimeout = 120;
                if (sql.IndexOf("@id", StringComparison.Ordinal) >= 0)
                {
                    cmd.Parameters.AddWithValue("@id", objectId);
                }
                using (SqlDataAdapter da = new SqlDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }
            return dt;
        }

        private static object Scalar(SqlConnection con, string sql)
        {
            using (SqlCommand cmd = new SqlCommand(sql, con))
            {
                cmd.CommandTimeout = 120;
                return cmd.ExecuteScalar();
            }
        }
    }

    /// <summary>
    /// V10.29: delivers the full database script as a ZIP from a plain HTTP handler (no page HTML can be mixed in).
    /// Started from Backup / Export with a one-time token; Super User only.
    /// </summary>
    public class FullDbScriptHandler : IHttpHandler, System.Web.SessionState.IRequiresSessionState
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext ctx)
        {
            HttpResponse resp = ctx.Response;
            resp.Cache.SetCacheability(HttpCacheability.NoCache);
            resp.Cache.SetNoStore();

            string token = ctx.Request.QueryString["t"];
            string expected = ctx.Session["FullDbScriptToken"] as string;
            bool includeSignIn = ctx.Session["FullDbScriptSignIn"] is bool && (bool)ctx.Session["FullDbScriptSignIn"];
            // Token is valid for 10 minutes and may be reused, so a phone browser that retries a download
            // after a connection hiccup still works. It cannot be guessed, so other sites cannot trigger it.
            bool fresh = ctx.Session["FullDbScriptIssued"] is DateTime
                && DateTime.UtcNow - (DateTime)ctx.Session["FullDbScriptIssued"] <= TimeSpan.FromMinutes(10);

            if (!Auth.IsSuperUser())
            {
                Deny(resp, 403, "Access denied.");
                return;
            }
            if (String.IsNullOrEmpty(token) || expected == null || !fresh || !String.Equals(token, expected, StringComparison.Ordinal))
            {
                Deny(resp, 403, "This download link has expired. Please start it again from the Backup & Restore page.");
                return;
            }

            string step = "starting";
            try
            {
                ctx.Server.ScriptTimeout = 900;

                step = "reading the database structure";
                FullDbScriptService.Plan plan = FullDbScriptService.Prepare(includeSignIn);
                step = "building the script";
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);

                byte[] zipBytes;
                using (MemoryStream ms = new MemoryStream())
                {
                    using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    {
                        ZipArchiveEntry entry = zip.CreateEntry("FullDatabaseScript_" + stamp + ".sql", CompressionLevel.Optimal);
                        using (Stream es = entry.Open())
                        using (StreamWriter w = new StreamWriter(es, new UTF8Encoding(true), 65536))
                        {
                            plan.WriteTo(w);
                            w.Flush();
                        }
                    }
                    zipBytes = ms.ToArray();
                }

                try
                {
                    FutureScopeService.Audit("FullDatabaseScriptExport", includeSignIn ? "with sign-in data" : "without sign-in data");
                }
                catch (Exception) { }

                step = "sending the file";
                resp.Clear();
                resp.ContentType = "application/zip";
                resp.AddHeader("Content-Disposition", "attachment; filename=\"FullDatabaseScript_" + stamp + ".zip\"");
                resp.AddHeader("Content-Length", zipBytes.Length.ToString(CultureInfo.InvariantCulture));
                resp.BinaryWrite(zipBytes);
                resp.Flush();
                ctx.ApplicationInstance.CompleteRequest();
            }
            catch (Exception ex)
            {
                string reason = ex.Message == null ? "" : ex.Message.Replace("\r", " ").Replace("\n", " ");
                if (reason.Length > 300) reason = reason.Substring(0, 300);
                Deny(resp, 500, "The script could not be created while " + step + " (" + ex.GetType().Name + "): " + reason);
            }
        }

        private static void Deny(HttpResponse resp, int code, string message)
        {
            resp.Clear();
            resp.StatusCode = code;
            resp.ContentType = "text/plain";
            resp.Write(message);
        }
    }
}
