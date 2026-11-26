using System;
using System.Data.SqlClient;

namespace PayrollWebApp
{
    public static class FutureScopeService
    {
        public static void EnsureSchema()
        {
            string[] scripts =
            {
                @"IF OBJECT_ID('PlatformLicenses','U') IS NULL
                  BEGIN
                    CREATE TABLE PlatformLicenses
                    (
                        LicenseID INT IDENTITY(1,1) PRIMARY KEY,
                        LicenseKey NVARCHAR(200) NOT NULL,
                        CustomerCode NVARCHAR(100) NULL,
                        CustomerName NVARCHAR(200) NULL,
                        Edition NVARCHAR(50) NULL,
                        ValidFrom DATE NULL,
                        ValidUntil DATE NULL,
                        IsActive BIT NOT NULL DEFAULT 1,
                        InstalledOn DATETIME NOT NULL DEFAULT GETDATE(),
                        InstalledBy NVARCHAR(100) NULL
                    )
                  END",
                @"IF OBJECT_ID('SystemAudit','U') IS NULL
                  BEGIN
                    CREATE TABLE SystemAudit
                    (
                        AuditID INT IDENTITY(1,1) PRIMARY KEY,
                        UserName NVARCHAR(100) NULL,
                        ActionName NVARCHAR(100) NOT NULL,
                        Details NVARCHAR(1000) NULL,
                        CreatedOn DATETIME NOT NULL DEFAULT GETDATE()
                    )
                  END"
            };

            using (SqlConnection con = new SqlConnection(Db.ConnectionString))
            {
                con.Open();
                foreach (string sql in scripts)
                    using (SqlCommand cmd = new SqlCommand(sql, con)) cmd.ExecuteNonQuery();
            }
        }

        public static void Audit(string action, string details)
        {
            try
            {
                Db.Execute("INSERT INTO SystemAudit(UserName,ActionName,Details) VALUES(?,?,?)",
                    new System.Data.OleDb.OleDbParameter("", Auth.CurrentUserName()),
                    new System.Data.OleDb.OleDbParameter("", action),
                    new System.Data.OleDb.OleDbParameter("", details));
            }
            catch { }
        }
    }
}
