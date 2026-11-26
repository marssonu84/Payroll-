using System;
using System.Data.SqlClient;
using System.IO;

namespace PayrollWebApp
{
    public static class BackupService
    {
        public static string BackupDirectory
        {
            get
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "Backups");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string CreateBackup()
        {
            string databaseName = GetDatabaseName();
            string safeName = databaseName.Replace(" ", "_").Replace("\\", "_").Replace("/", "_");
            string file = Path.Combine(BackupDirectory, safeName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak");
            string sql = "BACKUP DATABASE [" + databaseName.Replace("]", "]]" ) + "] TO DISK = @file WITH INIT, CHECKSUM, STATS = 10";
            using (SqlConnection con = new SqlConnection(Db.ConnectionString))
            using (SqlCommand cmd = new SqlCommand(sql, con))
            {
                cmd.CommandTimeout = 0;
                cmd.Parameters.AddWithValue("@file", file);
                con.Open();
                cmd.ExecuteNonQuery();
            }
            return file;
        }

        private static string GetDatabaseName()
        {
            SqlConnectionStringBuilder b = new SqlConnectionStringBuilder(Db.ConnectionString);
            if (String.IsNullOrWhiteSpace(b.InitialCatalog)) throw new InvalidOperationException("Database name is missing.");
            return b.InitialCatalog;
        }
    }
}
