/* Attendance Module - Chitra Educational Society Salary Management System */

IF OBJECT_ID('Attendance','U') IS NULL
BEGIN
    CREATE TABLE Attendance
    (
        AttendanceID INT IDENTITY(1,1) PRIMARY KEY,
        EmployeeID INT NOT NULL,
        AttendanceDate DATE NOT NULL,
        InTime TIME(0) NULL,
        OutTime TIME(0) NULL,
        OfficialOutTime TIME(0) NULL,
        ApprovedPermission BIT NOT NULL DEFAULT 0,
        PermissionType NVARCHAR(30) NULL,
        ApprovedCasualLeave BIT NOT NULL DEFAULT 0,
        DeclaredHoliday BIT NOT NULL DEFAULT 0,
        IsPaidHoliday BIT NOT NULL DEFAULT 0,
        LateCount INT NOT NULL DEFAULT 0,
        PermissionCount INT NOT NULL DEFAULT 0,
        HalfDayUnits DECIMAL(5,2) NOT NULL DEFAULT 0,
        CasualLeaveDays DECIMAL(5,2) NOT NULL DEFAULT 0,
        AbsentDays DECIMAL(5,2) NOT NULL DEFAULT 0,
        AttendanceStatus NVARCHAR(100) NOT NULL,
        Remarks NVARCHAR(500) NULL,
        ImportedOn DATETIME NOT NULL DEFAULT GETDATE()
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name='UX_Attendance_Employee_Date'
      AND object_id=OBJECT_ID('Attendance')
)
BEGIN
    CREATE UNIQUE INDEX UX_Attendance_Employee_Date
    ON Attendance(EmployeeID, AttendanceDate);
END;

IF COL_LENGTH('UserPermissions','AttendanceView') IS NULL
BEGIN
    ALTER TABLE UserPermissions
    ADD AttendanceView BIT NOT NULL
        CONSTRAINT DF_UserPermissions_AttendanceView DEFAULT 0;
END;

IF COL_LENGTH('UserPermissions','AttendanceImport') IS NULL
BEGIN
    ALTER TABLE UserPermissions
    ADD AttendanceImport BIT NOT NULL
        CONSTRAINT DF_UserPermissions_AttendanceImport DEFAULT 0;
END;

/* Preserve access for existing standard users, matching the application's previous upgrade behaviour. */
UPDATE UserPermissions
SET AttendanceView = 1,
    AttendanceImport = 1
WHERE AttendanceView = 0
  AND AttendanceImport = 0;
