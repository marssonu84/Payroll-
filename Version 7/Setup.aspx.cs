using System;
using System.Data.SqlClient;
using System.Web.UI;
using PayrollWebApp;

public partial class Setup : Page
{
    protected void Page_Load(object sender, EventArgs e)
    {
        Auth.RequireAdmin();
    }

    protected void btnSetup_Click(object sender, EventArgs e)
    {
        Auth.RequireAdmin();

        try
        {
            CreateTables();
            UpgradeEmployeesTable();
            UpgradeUsersTable();
            UpgradeMultiTenant();
            Auth.EnsureSecuritySchema();

            lblMsg.Text =
                "Database setup and verification completed successfully.";

            lblMsg.ForeColor =
                System.Drawing.Color.Green;
        }
        catch (Exception ex)
        {
            lblMsg.Text =
                "Database setup failed: " +
                ex.Message;

            lblMsg.ForeColor =
                System.Drawing.Color.Red;
        }
    }

    private void CreateTables()
    {
        string[] scripts =
        {
            @"
IF OBJECT_ID('Users','U') IS NULL
BEGIN
    CREATE TABLE Users
    (
        id INT IDENTITY(1,1) PRIMARY KEY,
        username NVARCHAR(50) NOT NULL UNIQUE,
        password_hash NVARCHAR(500) NOT NULL,
        IsAdmin BIT NOT NULL DEFAULT 0
    )
END
",

            @"
IF OBJECT_ID('Branches','U') IS NULL
BEGIN
    CREATE TABLE Branches
    (
        BranchCode NVARCHAR(100) NOT NULL PRIMARY KEY,
        IsActive BIT NOT NULL DEFAULT 1
    )
END
",

            @"
IF OBJECT_ID('Employees','U') IS NULL
BEGIN
    CREATE TABLE Employees
    (
        EmployeeID INT IDENTITY(1,1) PRIMARY KEY,
        EmployeeCode NVARCHAR(50) NULL,
        EmployeeName NVARCHAR(150) NOT NULL,
        Designation NVARCHAR(100),
        BranchCode NVARCHAR(100) NOT NULL,
        DateOfJoining DATE NULL,

        PFMember BIT NOT NULL DEFAULT 0,
        ESIMember BIT NOT NULL DEFAULT 0,

        UANNo NVARCHAR(50),
        ESINo NVARCHAR(50),

        BankAccount NVARCHAR(50),
        BankName NVARCHAR(100),
        IFSCCode NVARCHAR(20),

        StandardSalaryPrev DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        IncrementAmount DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        SalaryAdjustment DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        IsActive BIT NOT NULL DEFAULT 1
    )
END
",

            @"
IF OBJECT_ID('Payroll','U') IS NULL
BEGIN
    CREATE TABLE Payroll
    (
        PayrollID INT IDENTITY(1,1) PRIMARY KEY,
        EmployeeID INT NOT NULL,
        SalaryMonth DATE NOT NULL,

        DaysInMonth INT NOT NULL DEFAULT 30,
        Leaves DECIMAL(10,2) NOT NULL DEFAULT 0,

        StandardSalary DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        LOP DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        GrossSalary DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        Basic DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        HRA DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        OvertimeAllowance DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        PF DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        ESI DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        PT DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        SalaryAdvance DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        OtherDeductions DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        TotalDeductions DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        NetPay DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        CreatedOn DATETIME
            NOT NULL DEFAULT GETDATE()
    )
END
",

            @"
IF OBJECT_ID('Loans','U') IS NULL
BEGIN
    CREATE TABLE Loans
    (
        LoanID INT IDENTITY(1,1) PRIMARY KEY,
        EmployeeID INT NOT NULL,
        TxnDate DATE NOT NULL,

        OpeningBalance DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        LoanAvailed DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        Deduction DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        NetBalance DECIMAL(18,2)
            NOT NULL DEFAULT 0,

        Remarks NVARCHAR(500)
    )
END
"
        };

        using (SqlConnection con =
            new SqlConnection(Db.ConnectionString))
        {
            con.Open();

            foreach (string script in scripts)
            {
                using (SqlCommand cmd =
                    new SqlCommand(script, con))
                {
                    cmd.ExecuteNonQuery();
                }
            }

            InsertDefaultBranches(con);
        }
    }

    private void UpgradeEmployeesTable()
    {
        string sql = @"
IF COL_LENGTH(
    'Employees',
    'EmployeeCode'
) IS NULL
BEGIN
    ALTER TABLE Employees
    ADD EmployeeCode NVARCHAR(50) NULL
END;

IF COL_LENGTH(
    'Employees',
    'DateOfJoining'
) IS NULL
BEGIN
    ALTER TABLE Employees
    ADD DateOfJoining DATE NULL
END;

IF COL_LENGTH(
    'Employees',
    'IFSCCode'
) IS NULL
BEGIN
    ALTER TABLE Employees
    ADD IFSCCode NVARCHAR(20) NULL
END;
";

        using (SqlConnection con =
            new SqlConnection(Db.ConnectionString))
        {
            con.Open();

            using (SqlCommand cmd =
                new SqlCommand(sql, con))
            {
                cmd.ExecuteNonQuery();
            }
        }
    }

    private void UpgradeUsersTable()
    {
        using (SqlConnection con =
            new SqlConnection(Db.ConnectionString))
        {
            con.Open();

            bool usersTableExists;

            using (SqlCommand cmd =
                new SqlCommand(
                    @"SELECT CASE
                        WHEN OBJECT_ID(
                            'Users',
                            'U'
                        ) IS NULL
                        THEN 0
                        ELSE 1
                      END",
                    con))
            {
                usersTableExists =
                    Convert.ToInt32(
                        cmd.ExecuteScalar()
                    ) == 1;
            }

            if (!usersTableExists)
            {
                return;
            }

            bool isAdminColumnExists;

            using (SqlCommand cmd =
                new SqlCommand(
                    @"SELECT CASE
                        WHEN COL_LENGTH(
                            'Users',
                            'IsAdmin'
                        ) IS NULL
                        THEN 0
                        ELSE 1
                      END",
                    con))
            {
                isAdminColumnExists =
                    Convert.ToInt32(
                        cmd.ExecuteScalar()
                    ) == 1;
            }

            if (!isAdminColumnExists)
            {
                using (SqlCommand cmd =
                    new SqlCommand(
                        @"ALTER TABLE Users
                          ADD IsAdmin BIT
                          NOT NULL
                          CONSTRAINT
                          DF_Users_IsAdmin
                          DEFAULT 0",
                        con))
                {
                    cmd.ExecuteNonQuery();
                }
            }

            using (SqlCommand cmd =
                new SqlCommand(
                    @"IF EXISTS
                      (
                          SELECT 1
                          FROM Users
                      )
                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM Users
                          WHERE IsAdmin = 1
                      )
                      BEGIN
                          DECLARE
                              @FirstUserId INT;

                          SELECT TOP 1
                              @FirstUserId = id
                          FROM Users
                          ORDER BY id;

                          UPDATE Users
                          SET IsAdmin = 1
                          WHERE id = @FirstUserId;
                      END",
                    con))
            {
                cmd.ExecuteNonQuery();
            }
        }
    }

    private void UpgradeMultiTenant()
    {
        using (SqlConnection con =
            new SqlConnection(Db.ConnectionString))
        {
            con.Open();

            ExecuteSql(
                con,
                @"
IF OBJECT_ID('Tenants','U') IS NULL
BEGIN
    CREATE TABLE Tenants
    (
        TenantID INT IDENTITY(1,1) PRIMARY KEY,
        OrganizationName NVARCHAR(150) NOT NULL,
        IsActive BIT NOT NULL DEFAULT 1
    )
END
");

            ExecuteSql(
                con,
                @"
IF NOT EXISTS
(
    SELECT 1
    FROM Tenants
)
BEGIN
    INSERT INTO Tenants
    (
        OrganizationName,
        IsActive
    )
    VALUES
    (
        'Default Organisation',
        1
    )
END
");

            int tenantId;

            using (SqlCommand cmd =
                new SqlCommand(
                    @"SELECT TOP 1 TenantID
                      FROM Tenants
                      ORDER BY TenantID",
                    con))
            {
                tenantId =
                    Convert.ToInt32(
                        cmd.ExecuteScalar()
                    );
            }

            AddTenantColumnIfMissing(
                con,
                "Users",
                tenantId);

            AddTenantColumnIfMissing(
                con,
                "Branches",
                tenantId);

            AddTenantColumnIfMissing(
                con,
                "Employees",
                tenantId);

            AddTenantColumnIfMissing(
                con,
                "Payroll",
                tenantId);

            AddTenantColumnIfMissing(
                con,
                "Loans",
                tenantId);
        }
    }

    private void AddTenantColumnIfMissing(
        SqlConnection con,
        string tableName,
        int tenantId)
    {
        string checkSql =
            @"SELECT CASE
              WHEN OBJECT_ID(@TableName,'U') IS NULL
              THEN 0
              WHEN COL_LENGTH(
                  @TableName,
                  'TenantID'
              ) IS NULL
              THEN 1
              ELSE 2
              END";

        int status;

        using (SqlCommand cmd =
            new SqlCommand(
                checkSql,
                con))
        {
            cmd.Parameters.AddWithValue(
                "@TableName",
                tableName);

            status =
                Convert.ToInt32(
                    cmd.ExecuteScalar()
                );
        }

        if (status == 0)
        {
            return;
        }

        if (status == 1)
        {
            string alterSql =
                "ALTER TABLE [" +
                tableName +
                "] ADD TenantID INT NULL";

            ExecuteSql(
                con,
                alterSql);
        }

        string updateSql =
            "UPDATE [" +
            tableName +
            "] " +
            "SET TenantID = @TenantID " +
            "WHERE TenantID IS NULL";

        using (SqlCommand cmd =
            new SqlCommand(
                updateSql,
                con))
        {
            cmd.Parameters.AddWithValue(
                "@TenantID",
                tenantId);

            cmd.ExecuteNonQuery();
        }

        string nullableCheckSql =
            @"SELECT is_nullable
              FROM sys.columns
              WHERE object_id =
                  OBJECT_ID(@TableName)
              AND name = 'TenantID'";

        bool isNullable = false;

        using (SqlCommand cmd =
            new SqlCommand(
                nullableCheckSql,
                con))
        {
            cmd.Parameters.AddWithValue(
                "@TableName",
                tableName);

            object result =
                cmd.ExecuteScalar();

            if (result != null)
            {
                isNullable =
                    Convert.ToBoolean(result);
            }
        }

        if (isNullable)
        {
            string notNullSql =
                "ALTER TABLE [" +
                tableName +
                "] ALTER COLUMN " +
                "TenantID INT NOT NULL";

            ExecuteSql(
                con,
                notNullSql);
        }
    }

    private void ExecuteSql(
        SqlConnection con,
        string sql)
    {
        using (SqlCommand cmd =
            new SqlCommand(
                sql,
                con))
        {
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertDefaultBranches(
        SqlConnection con)
    {
        string sql = @"
IF NOT EXISTS
(
    SELECT 1
    FROM Branches
    WHERE BranchCode='Pollocks'
)
INSERT INTO Branches
(
    BranchCode,
    IsActive
)
VALUES
(
    'Pollocks',
    1
);

IF NOT EXISTS
(
    SELECT 1
    FROM Branches
    WHERE BranchCode='The Intelli School'
)
INSERT INTO Branches
(
    BranchCode,
    IsActive
)
VALUES
(
    'The Intelli School',
    1
);

IF NOT EXISTS
(
    SELECT 1
    FROM Branches
    WHERE BranchCode='Intelli Core One'
)
INSERT INTO Branches
(
    BranchCode,
    IsActive
)
VALUES
(
    'Intelli Core One',
    1
);

IF NOT EXISTS
(
    SELECT 1
    FROM Branches
    WHERE BranchCode='Mini Minds'
)
INSERT INTO Branches
(
    BranchCode,
    IsActive
)
VALUES
(
    'Mini Minds',
    1
);
";

        using (SqlCommand cmd =
            new SqlCommand(sql, con))
        {
            cmd.ExecuteNonQuery();
        }
    }
}
