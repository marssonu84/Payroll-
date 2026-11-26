using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Text;

namespace PayrollWebApp
{
    /// <summary>
    /// Safe V10.21 database initializer/alignment service.
    /// It inspects an existing database first. Missing tables are created, but an
    /// existing table with missing required columns is never altered automatically.
    /// Existing legacy HRA/Overtime columns are left untouched.
    /// </summary>
    public static class V1021SchemaService
    {
        private static readonly string[] RequiredTables =
        {
            "Tenants", "Users", "UserPermissions", "Branches", "Employees",
            "Attendance", "AttendanceMonthlyFinalization",
            "AttendanceMonthlyEmployeeSummary", "Payroll", "Loans",
            "OrganisationAudit", "PlatformLicenses", "SystemAudit"
        };

        private static readonly IDictionary<string, string[]> RequiredColumns =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "Tenants", new[] { "TenantID", "OrganizationName", "IsActive", "OrganizationType", "ContactPerson", "Mobile", "Email", "AddressLine", "City", "StateName", "Pincode", "BoardType", "AffiliationNumber", "UdiseNumber", "BusinessType", "GSTIN", "PAN", "ApprovalStatus", "RejectionReason", "CreatedOn", "ApprovedOn", "ApprovedByUserID", "OriginalLocationName" } },
                { "Users", new[] { "id", "username", "password_hash", "IsAdmin", "IsActive", "IsNewUser", "AdminEmail", "TenantID", "IsOrgAdmin" } },
                { "UserPermissions", new[] { "UserID", "DashboardView", "EmployeesView", "EmployeesEdit", "PayrollView", "PayrollProcess", "LoansView", "LoansEdit", "ReportsView", "AttendanceView", "AttendanceImport" } },
                { "Branches", new[] { "BranchCode", "IsActive", "TenantID" } },
                { "Employees", new[] { "EmployeeID", "EmployeeCode", "EmployeeName", "Designation", "BranchCode", "DateOfJoining", "PFMember", "ESIMember", "UANNo", "ESINo", "BankAccount", "BankName", "IFSCCode", "StandardSalaryPrev", "IncrementAmount", "SalaryAdjustment", "IsActive", "TenantID" } },
                { "Attendance", new[] { "AttendanceID", "EmployeeID", "AttendanceDate", "InTime", "OutTime", "OfficialOutTime", "ApprovedPermission", "PermissionType", "ApprovedCasualLeave", "DeclaredHoliday", "IsPaidHoliday", "LateCount", "PermissionCount", "HalfDayUnits", "CasualLeaveDays", "AbsentDays", "AttendanceStatus", "Remarks", "ImportedOn" } },
                { "AttendanceMonthlyFinalization", new[] { "AttendanceFinalizationID", "TenantID", "BranchCode", "AttendanceMonth", "FinalizedOn", "FinalizedBy" } },
                { "AttendanceMonthlyEmployeeSummary", new[] { "AttendanceMonthlyEmployeeSummaryID", "EmployeeID", "AttendanceMonth", "WorkingDays", "DaysAttended", "PresentDays", "IncompletePunches", "PaidHolidays", "CasualLeaveDays", "LateCount", "PermissionCount", "HalfDayUnits", "AbsentDays", "LeaveEquivalent", "FreeAllowance", "DeductionDays" } },
                { "Payroll", new[] { "PayrollID", "TenantID", "EmployeeID", "SalaryMonth", "DaysInMonth", "Leaves", "StandardSalary", "LOP", "GrossSalary", "Basic", "DA", "PF", "ESI", "PT", "SalaryAdvance", "OtherDeductions", "TotalDeductions", "NetPay", "CreatedOn" } },
                { "Loans", new[] { "LoanID", "TenantID", "EmployeeID", "TxnDate", "OpeningBalance", "LoanAvailed", "Deduction", "NetBalance", "Remarks" } },
                { "OrganisationAudit", new[] { "AuditID", "TenantID", "UserID", "ActionName", "Details", "CreatedOn" } },
                { "PlatformLicenses", new[] { "LicenseID", "LicenseKey", "CustomerCode", "CustomerName", "Edition", "ValidFrom", "ValidUntil", "IsActive", "InstalledOn", "InstalledBy" } },
                { "SystemAudit", new[] { "AuditID", "UserName", "ActionName", "Details", "CreatedOn" } }
            };

        private static readonly IDictionary<string, string> CreateTables =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Tenants", @"CREATE TABLE dbo.Tenants
(
    TenantID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY,
    OrganizationName NVARCHAR(150) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Tenants_IsActive DEFAULT (1),
    OrganizationType NVARCHAR(30) NULL,
    ContactPerson NVARCHAR(120) NULL,
    Mobile NVARCHAR(20) NULL,
    Email NVARCHAR(150) NULL,
    AddressLine NVARCHAR(250) NULL,
    City NVARCHAR(80) NULL,
    StateName NVARCHAR(80) NULL,
    Pincode NVARCHAR(10) NULL,
    BoardType NVARCHAR(50) NULL,
    AffiliationNumber NVARCHAR(80) NULL,
    UdiseNumber NVARCHAR(30) NULL,
    BusinessType NVARCHAR(50) NULL,
    GSTIN NVARCHAR(20) NULL,
    PAN NVARCHAR(15) NULL,
    ApprovalStatus NVARCHAR(20) NULL,
    RejectionReason NVARCHAR(500) NULL,
    CreatedOn DATETIME NULL,
    ApprovedOn DATETIME NULL,
    ApprovedByUserID INT NULL,
    OriginalLocationName NVARCHAR(150) NULL
);" },
                { "Users", @"CREATE TABLE dbo.Users
(
    id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    username NVARCHAR(50) NOT NULL CONSTRAINT UQ_Users_username UNIQUE,
    password_hash NVARCHAR(500) NOT NULL,
    IsAdmin BIT NOT NULL CONSTRAINT DF_Users_IsAdmin DEFAULT (0),
    IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
    IsNewUser BIT NOT NULL CONSTRAINT DF_Users_IsNewUser DEFAULT (0),
    AdminEmail NVARCHAR(254) NULL,
    TenantID INT NULL,
    IsOrgAdmin BIT NOT NULL CONSTRAINT DF_Users_IsOrgAdmin DEFAULT (0)
);" },
                { "UserPermissions", @"CREATE TABLE dbo.UserPermissions
(
    UserID INT NOT NULL CONSTRAINT PK_UserPermissions PRIMARY KEY,
    DashboardView BIT NOT NULL CONSTRAINT DF_UserPermissions_DashboardView DEFAULT (0),
    EmployeesView BIT NOT NULL CONSTRAINT DF_UserPermissions_EmployeesView DEFAULT (0),
    EmployeesEdit BIT NOT NULL CONSTRAINT DF_UserPermissions_EmployeesEdit DEFAULT (0),
    PayrollView BIT NOT NULL CONSTRAINT DF_UserPermissions_PayrollView DEFAULT (0),
    PayrollProcess BIT NOT NULL CONSTRAINT DF_UserPermissions_PayrollProcess DEFAULT (0),
    LoansView BIT NOT NULL CONSTRAINT DF_UserPermissions_LoansView DEFAULT (0),
    LoansEdit BIT NOT NULL CONSTRAINT DF_UserPermissions_LoansEdit DEFAULT (0),
    ReportsView BIT NOT NULL CONSTRAINT DF_UserPermissions_ReportsView DEFAULT (0),
    AttendanceView BIT NOT NULL CONSTRAINT DF_UserPermissions_AttendanceView DEFAULT (0),
    AttendanceImport BIT NOT NULL CONSTRAINT DF_UserPermissions_AttendanceImport DEFAULT (0)
);" },
                { "Branches", @"CREATE TABLE dbo.Branches
(
    BranchCode NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Branches_IsActive DEFAULT (1),
    TenantID INT NOT NULL,
    CONSTRAINT UX_Branches_Tenant_BranchCode UNIQUE (TenantID, BranchCode)
);" },
                { "Employees", @"CREATE TABLE dbo.Employees
(
    EmployeeID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
    EmployeeCode NVARCHAR(50) NULL,
    EmployeeName NVARCHAR(150) NOT NULL,
    Designation NVARCHAR(100) NULL,
    BranchCode NVARCHAR(100) NOT NULL,
    DateOfJoining DATE NULL,
    PFMember BIT NOT NULL CONSTRAINT DF_Employees_PFMember DEFAULT (0),
    ESIMember BIT NOT NULL CONSTRAINT DF_Employees_ESIMember DEFAULT (0),
    UANNo NVARCHAR(50) NULL,
    ESINo NVARCHAR(50) NULL,
    BankAccount NVARCHAR(50) NULL,
    BankName NVARCHAR(100) NULL,
    IFSCCode NVARCHAR(20) NULL,
    StandardSalaryPrev DECIMAL(18,2) NOT NULL CONSTRAINT DF_Employees_StandardSalaryPrev DEFAULT (0),
    IncrementAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Employees_IncrementAmount DEFAULT (0),
    SalaryAdjustment DECIMAL(18,2) NOT NULL CONSTRAINT DF_Employees_SalaryAdjustment DEFAULT (0),
    IsActive BIT NOT NULL CONSTRAINT DF_Employees_IsActive DEFAULT (1),
    TenantID INT NOT NULL
);" },
                { "Attendance", @"CREATE TABLE dbo.Attendance
(
    AttendanceID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Attendance PRIMARY KEY,
    EmployeeID INT NOT NULL,
    AttendanceDate DATE NOT NULL,
    InTime TIME(0) NULL,
    OutTime TIME(0) NULL,
    OfficialOutTime TIME(0) NULL,
    ApprovedPermission BIT NOT NULL CONSTRAINT DF_Attendance_ApprovedPermission DEFAULT (0),
    PermissionType NVARCHAR(30) NULL,
    ApprovedCasualLeave BIT NOT NULL CONSTRAINT DF_Attendance_ApprovedCasualLeave DEFAULT (0),
    DeclaredHoliday BIT NOT NULL CONSTRAINT DF_Attendance_DeclaredHoliday DEFAULT (0),
    IsPaidHoliday BIT NOT NULL CONSTRAINT DF_Attendance_IsPaidHoliday DEFAULT (0),
    LateCount INT NOT NULL CONSTRAINT DF_Attendance_LateCount DEFAULT (0),
    PermissionCount INT NOT NULL CONSTRAINT DF_Attendance_PermissionCount DEFAULT (0),
    HalfDayUnits DECIMAL(5,2) NOT NULL CONSTRAINT DF_Attendance_HalfDayUnits DEFAULT (0),
    CasualLeaveDays DECIMAL(5,2) NOT NULL CONSTRAINT DF_Attendance_CasualLeaveDays DEFAULT (0),
    AbsentDays DECIMAL(5,2) NOT NULL CONSTRAINT DF_Attendance_AbsentDays DEFAULT (0),
    AttendanceStatus NVARCHAR(100) NOT NULL,
    Remarks NVARCHAR(500) NULL,
    ImportedOn DATETIME NOT NULL CONSTRAINT DF_Attendance_ImportedOn DEFAULT (GETDATE())
);" },
                { "AttendanceMonthlyFinalization", @"CREATE TABLE dbo.AttendanceMonthlyFinalization
(
    AttendanceFinalizationID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceMonthlyFinalization PRIMARY KEY,
    TenantID INT NOT NULL,
    BranchCode NVARCHAR(100) NOT NULL,
    AttendanceMonth DATE NOT NULL,
    FinalizedOn DATETIME NOT NULL CONSTRAINT DF_AttendanceMonthlyFinalization_FinalizedOn DEFAULT (GETDATE()),
    FinalizedBy NVARCHAR(150) NULL
);" },
                { "AttendanceMonthlyEmployeeSummary", @"CREATE TABLE dbo.AttendanceMonthlyEmployeeSummary
(
    AttendanceMonthlyEmployeeSummaryID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceMonthlyEmployeeSummary PRIMARY KEY,
    EmployeeID INT NOT NULL,
    AttendanceMonth DATE NOT NULL,
    WorkingDays INT NOT NULL CONSTRAINT DF_AMES_WorkingDays DEFAULT (0),
    DaysAttended INT NOT NULL CONSTRAINT DF_AMES_DaysAttended DEFAULT (0),
    PresentDays INT NOT NULL CONSTRAINT DF_AMES_PresentDays DEFAULT (0),
    IncompletePunches INT NOT NULL CONSTRAINT DF_AMES_IncompletePunches DEFAULT (0),
    PaidHolidays INT NOT NULL CONSTRAINT DF_AMES_PaidHolidays DEFAULT (0),
    CasualLeaveDays DECIMAL(8,2) NOT NULL CONSTRAINT DF_AMES_CasualLeaveDays DEFAULT (0),
    LateCount INT NOT NULL CONSTRAINT DF_AMES_LateCount DEFAULT (0),
    PermissionCount INT NOT NULL CONSTRAINT DF_AMES_PermissionCount DEFAULT (0),
    HalfDayUnits DECIMAL(8,2) NOT NULL CONSTRAINT DF_AMES_HalfDayUnits DEFAULT (0),
    AbsentDays DECIMAL(8,2) NOT NULL CONSTRAINT DF_AMES_AbsentDays DEFAULT (0),
    LeaveEquivalent DECIMAL(8,2) NOT NULL CONSTRAINT DF_AMES_LeaveEquivalent DEFAULT (0),
    FreeAllowance DECIMAL(8,2) NOT NULL CONSTRAINT DF_AMES_FreeAllowance DEFAULT (0),
    DeductionDays DECIMAL(8,2) NOT NULL CONSTRAINT DF_AMES_DeductionDays DEFAULT (0)
);" },
                { "Payroll", @"CREATE TABLE dbo.Payroll
(
    PayrollID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Payroll PRIMARY KEY,
    TenantID INT NOT NULL,
    EmployeeID INT NOT NULL,
    SalaryMonth DATE NOT NULL,
    DaysInMonth INT NOT NULL CONSTRAINT DF_Payroll_DaysInMonth DEFAULT (30),
    Leaves DECIMAL(10,2) NOT NULL CONSTRAINT DF_Payroll_Leaves DEFAULT (0),
    StandardSalary DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payroll_StandardSalary DEFAULT (0),
    LOP DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payroll_LOP DEFAULT (0),
    GrossSalary DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payroll_GrossSalary DEFAULT (0),
    Basic DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payroll_Basic DEFAULT (0),
    DA DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payroll_DA DEFAULT (0),
    PF DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payroll_PF DEFAULT (0),
    ESI DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payroll_ESI DEFAULT (0),
    PT DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payroll_PT DEFAULT (0),
    SalaryAdvance DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payroll_SalaryAdvance DEFAULT (0),
    OtherDeductions DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payroll_OtherDeductions DEFAULT (0),
    TotalDeductions DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payroll_TotalDeductions DEFAULT (0),
    NetPay DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payroll_NetPay DEFAULT (0),
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_Payroll_CreatedOn DEFAULT (GETDATE())
);" },
                { "Loans", @"CREATE TABLE dbo.Loans
(
    LoanID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Loans PRIMARY KEY,
    TenantID INT NOT NULL,
    EmployeeID INT NOT NULL,
    TxnDate DATE NOT NULL,
    OpeningBalance DECIMAL(18,2) NOT NULL CONSTRAINT DF_Loans_OpeningBalance DEFAULT (0),
    LoanAvailed DECIMAL(18,2) NOT NULL CONSTRAINT DF_Loans_LoanAvailed DEFAULT (0),
    Deduction DECIMAL(18,2) NOT NULL CONSTRAINT DF_Loans_Deduction DEFAULT (0),
    NetBalance DECIMAL(18,2) NOT NULL CONSTRAINT DF_Loans_NetBalance DEFAULT (0),
    Remarks NVARCHAR(500) NULL
);" },
                { "OrganisationAudit", @"CREATE TABLE dbo.OrganisationAudit
(
    AuditID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrganisationAudit PRIMARY KEY,
    TenantID INT NULL,
    UserID INT NULL,
    ActionName NVARCHAR(100) NOT NULL,
    Details NVARCHAR(500) NULL,
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_OrganisationAudit_CreatedOn DEFAULT (GETDATE())
);" },
                { "PlatformLicenses", @"CREATE TABLE dbo.PlatformLicenses
(
    LicenseID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlatformLicenses PRIMARY KEY,
    LicenseKey NVARCHAR(200) NOT NULL,
    CustomerCode NVARCHAR(100) NULL,
    CustomerName NVARCHAR(200) NULL,
    Edition NVARCHAR(50) NULL,
    ValidFrom DATE NULL,
    ValidUntil DATE NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_PlatformLicenses_IsActive DEFAULT (1),
    InstalledOn DATETIME NOT NULL CONSTRAINT DF_PlatformLicenses_InstalledOn DEFAULT (GETDATE()),
    InstalledBy NVARCHAR(100) NULL
);" },
                { "SystemAudit", @"CREATE TABLE dbo.SystemAudit
(
    AuditID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SystemAudit PRIMARY KEY,
    UserName NVARCHAR(100) NULL,
    ActionName NVARCHAR(100) NOT NULL,
    Details NVARCHAR(1000) NULL,
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_SystemAudit_CreatedOn DEFAULT (GETDATE())
);" }
            };

        public static bool IsSchemaComplete()
        {
            try
            {
                using (SqlConnection con = new SqlConnection(Db.ConnectionString))
                {
                    con.Open();
                    return GetMissingObjects(con).Count == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string GetSchemaStatus()
        {
            using (SqlConnection con = new SqlConnection(Db.ConnectionString))
            {
                con.Open();
                List<string> missing = GetMissingObjects(con);
                return missing.Count == 0
                    ? "V10.21 schema is complete."
                    : "Missing/incomplete: " + String.Join(", ", missing.ToArray());
            }
        }

        public static void EnsureSchema()
        {
            using (SqlConnection con = new SqlConnection(Db.ConnectionString))
            {
                con.Open();
                using (SqlTransaction tx = con.BeginTransaction())
                {
                    try
                    {
                        // Record which tables existed before alignment. Existing tables are
                        // preserved; only explicitly supported additive changes are applied.
                        Dictionary<string, bool> existed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                        foreach (string table in RequiredTables)
                            existed[table] = TableExists(con, tx, table);

                        // V10.21 has one known additive migration for the current hosting
                        // database: Payroll.DA. It is safe to add as NOT NULL with a zero
                        // default because existing payroll rows receive DA = 0 until the
                        // application recalculates/saves them. No existing data is deleted.
                        List<string> unsupportedMissing = GetMissingColumnObjects(con, tx);
                        unsupportedMissing.Remove("Payroll.DA");
                        if (unsupportedMissing.Count > 0)
                        {
                            throw new InvalidOperationException(
                                "V10.21 alignment stopped before changing the database. " +
                                "Unsupported missing column(s): " +
                                String.Join(", ", unsupportedMissing.ToArray()) + ".");
                        }

                        if (TableExists(con, tx, "Payroll") && !ColumnExists(con, tx, "Payroll", "DA"))
                        {
                            using (SqlCommand cmd = new SqlCommand(
                                "ALTER TABLE dbo.Payroll ADD DA DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payroll_DA DEFAULT (0)",
                                con, tx))
                            {
                                cmd.ExecuteNonQuery();
                            }
                        }

                        // Create only tables that do not exist.
                        foreach (string table in RequiredTables)
                        {
                            if (!existed[table])
                            {
                                using (SqlCommand cmd = new SqlCommand(CreateTables[table], con, tx))
                                    cmd.ExecuteNonQuery();
                            }
                        }

                        // Foreign keys are added only for tables created by this run. We do
                        // not impose new referential constraints on an established shared-
                        // hosting database because its existing data may legitimately rely
                        // on the historical schema (and the live diagnostic currently shows 0 FKs).
                        foreach (string table in new[] { "Users", "UserPermissions", "Branches", "Employees", "AttendanceMonthlyFinalization", "Payroll", "Loans", "OrganisationAudit" })
                        {
                            if (!existed[table])
                            {
                                AddForeignKeyIfMissing(con, tx, table);
                            }
                        }

                        EnsureIndex(con, tx, "Attendance", "UX_Attendance_Employee_Date", "CREATE UNIQUE INDEX UX_Attendance_Employee_Date ON dbo.Attendance(EmployeeID, AttendanceDate)");
                        EnsureIndex(con, tx, "AttendanceMonthlyFinalization", "UX_AttendanceMonthlyFinalization_Tenant_Branch_Month", "CREATE UNIQUE INDEX UX_AttendanceMonthlyFinalization_Tenant_Branch_Month ON dbo.AttendanceMonthlyFinalization(TenantID, BranchCode, AttendanceMonth)");
                        EnsureIndex(con, tx, "AttendanceMonthlyEmployeeSummary", "UX_AttendanceMonthlyEmployeeSummary_Employee_Month", "CREATE UNIQUE INDEX UX_AttendanceMonthlyEmployeeSummary_Employee_Month ON dbo.AttendanceMonthlyEmployeeSummary(EmployeeID, AttendanceMonth)");
                        EnsureIndex(con, tx, "Payroll", "UX_Payroll_Tenant_Employee_Month", "CREATE UNIQUE INDEX UX_Payroll_Tenant_Employee_Month ON dbo.Payroll(TenantID, EmployeeID, SalaryMonth)");
                        EnsureIndex(con, tx, "Employees", "IX_Employees_Tenant_Branch", "CREATE INDEX IX_Employees_Tenant_Branch ON dbo.Employees(TenantID, BranchCode)");
                        EnsureIndex(con, tx, "Employees", "IX_Employees_Tenant_Active", "CREATE INDEX IX_Employees_Tenant_Active ON dbo.Employees(TenantID, IsActive)");
                        EnsureIndex(con, tx, "Attendance", "IX_Attendance_Employee", "CREATE INDEX IX_Attendance_Employee ON dbo.Attendance(EmployeeID)");
                        EnsureIndex(con, tx, "Payroll", "IX_Payroll_Tenant_Month", "CREATE INDEX IX_Payroll_Tenant_Month ON dbo.Payroll(TenantID, SalaryMonth)");
                        EnsureIndex(con, tx, "Loans", "IX_Loans_Tenant_Employee_Date", "CREATE INDEX IX_Loans_Tenant_Employee_Date ON dbo.Loans(TenantID, EmployeeID, TxnDate)");
                        EnsureIndex(con, tx, "OrganisationAudit", "IX_OrganisationAudit_Tenant_CreatedOn", "CREATE INDEX IX_OrganisationAudit_Tenant_CreatedOn ON dbo.OrganisationAudit(TenantID, CreatedOn)");
                        EnsureIndex(con, tx, "SystemAudit", "IX_SystemAudit_CreatedOn", "CREATE INDEX IX_SystemAudit_CreatedOn ON dbo.SystemAudit(CreatedOn)");

                        tx.Commit();
                    }
                    catch
                    {
                        try { tx.Rollback(); } catch { }
                        throw;
                    }
                }
            }
        }

        private static List<string> GetMissingObjects(SqlConnection con)
        {
            List<string> result = new List<string>();
            foreach (string table in RequiredTables)
            {
                if (!TableExists(con, table))
                {
                    result.Add(table + " table");
                    continue;
                }

                string[] columns = RequiredColumns[table];
                foreach (string column in columns)
                {
                    if (!ColumnExists(con, table, column))
                        result.Add(table + "." + column);
                }
            }
            return result;
        }

        private static List<string> GetMissingColumnObjects(SqlConnection con, SqlTransaction tx)
        {
            List<string> result = new List<string>();
            foreach (string table in RequiredTables)
            {
                if (!TableExists(con, tx, table))
                    continue;

                foreach (string column in RequiredColumns[table])
                {
                    if (!ColumnExists(con, tx, table, column))
                        result.Add(table + "." + column);
                }
            }
            return result;
        }

        private static bool TableExists(SqlConnection con, string table)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@TableName,'U') IS NULL THEN 0 ELSE 1 END", con))
            {
                cmd.Parameters.AddWithValue("@TableName", table);
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        private static bool TableExists(SqlConnection con, SqlTransaction tx, string table)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@TableName,'U') IS NULL THEN 0 ELSE 1 END", con, tx))
            {
                cmd.Parameters.AddWithValue("@TableName", table);
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        private static bool ColumnExists(SqlConnection con, string table, string column)
        {
            using (SqlCommand cmd = new SqlCommand(@"
SELECT COUNT(*) FROM sys.columns
WHERE object_id = OBJECT_ID(@TableName) AND name = @ColumnName", con))
            {
                cmd.Parameters.AddWithValue("@TableName", table);
                cmd.Parameters.AddWithValue("@ColumnName", column);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private static bool ColumnExists(SqlConnection con, SqlTransaction tx, string table, string column)
        {
            using (SqlCommand cmd = new SqlCommand(@"
SELECT COUNT(*) FROM sys.columns
WHERE object_id = OBJECT_ID(@TableName) AND name = @ColumnName", con, tx))
            {
                cmd.Parameters.AddWithValue("@TableName", table);
                cmd.Parameters.AddWithValue("@ColumnName", column);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private static bool IndexExists(SqlConnection con, SqlTransaction tx, string table, string index)
        {
            using (SqlCommand cmd = new SqlCommand(@"
SELECT COUNT(*) FROM sys.indexes
WHERE object_id = OBJECT_ID(@TableName) AND name = @IndexName", con, tx))
            {
                cmd.Parameters.AddWithValue("@TableName", table);
                cmd.Parameters.AddWithValue("@IndexName", index);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private static void EnsureIndex(SqlConnection con, SqlTransaction tx, string table, string index, string createSql)
        {
            if (!IndexExists(con, tx, table, index))
            {
                using (SqlCommand cmd = new SqlCommand(createSql, con, tx))
                    cmd.ExecuteNonQuery();
            }
        }

        private static bool ForeignKeyExists(SqlConnection con, SqlTransaction tx, string table, string fkName)
        {
            using (SqlCommand cmd = new SqlCommand(@"
SELECT COUNT(*) FROM sys.foreign_keys
WHERE parent_object_id = OBJECT_ID(@TableName) AND name = @ForeignKeyName", con, tx))
            {
                cmd.Parameters.AddWithValue("@TableName", table);
                cmd.Parameters.AddWithValue("@ForeignKeyName", fkName);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private static void AddForeignKeyIfMissing(SqlConnection con, SqlTransaction tx, string table)
        {
            string fkName = null;
            string column = null;
            string parentTable = null;
            string parentColumn = null;

            switch (table)
            {
                case "Users": fkName = "FK_Users_Tenants"; column = "TenantID"; parentTable = "Tenants"; parentColumn = "TenantID"; break;
                case "UserPermissions": fkName = "FK_UserPermissions_Users"; column = "UserID"; parentTable = "Users"; parentColumn = "id"; break;
                case "Branches": fkName = "FK_Branches_Tenants"; column = "TenantID"; parentTable = "Tenants"; parentColumn = "TenantID"; break;
                case "Employees": fkName = "FK_Employees_Tenants"; column = "TenantID"; parentTable = "Tenants"; parentColumn = "TenantID"; break;
                case "AttendanceMonthlyFinalization": fkName = "FK_AttendanceMonthlyFinalization_Tenants"; column = "TenantID"; parentTable = "Tenants"; parentColumn = "TenantID"; break;
                case "Payroll": fkName = "FK_Payroll_Tenants"; column = "TenantID"; parentTable = "Tenants"; parentColumn = "TenantID"; break;
                case "Loans": fkName = "FK_Loans_Tenants"; column = "TenantID"; parentTable = "Tenants"; parentColumn = "TenantID"; break;
                case "OrganisationAudit": fkName = "FK_OrganisationAudit_Tenants"; column = "TenantID"; parentTable = "Tenants"; parentColumn = "TenantID"; break;
            }

            if (fkName == null || ForeignKeyExists(con, tx, table, fkName))
                return;

            string sql = "ALTER TABLE dbo.[" + table.Replace("]", "]]" ) + "] " +
                         "ADD CONSTRAINT [" + fkName.Replace("]", "]]" ) + "] FOREIGN KEY ([" +
                         column.Replace("]", "]]" ) + "]) REFERENCES dbo.[" +
                         parentTable.Replace("]", "]]" ) + "]([" + parentColumn.Replace("]", "]]" ) + "])";
            using (SqlCommand cmd = new SqlCommand(sql, con, tx))
                cmd.ExecuteNonQuery();
        }
    }
}
