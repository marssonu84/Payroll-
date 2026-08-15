/* Version 5 security upgrade - optional manual database migration.
   The application also runs this upgrade automatically after an administrator logs in. */
IF COL_LENGTH('Users','IsActive') IS NULL
    ALTER TABLE Users ADD IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1;

IF OBJECT_ID('UserPermissions','U') IS NULL
BEGIN
    CREATE TABLE UserPermissions(
        UserID INT NOT NULL PRIMARY KEY,
        EmployeesView BIT NOT NULL DEFAULT 0,
        EmployeesEdit BIT NOT NULL DEFAULT 0,
        PayrollView BIT NOT NULL DEFAULT 0,
        PayrollProcess BIT NOT NULL DEFAULT 0,
        LoansView BIT NOT NULL DEFAULT 0,
        LoansEdit BIT NOT NULL DEFAULT 0,
        ReportsView BIT NOT NULL DEFAULT 0
    );
END;

INSERT INTO UserPermissions(UserID,EmployeesView,EmployeesEdit,PayrollView,PayrollProcess,LoansView,LoansEdit,ReportsView)
SELECT U.id,1,1,1,1,1,1,1
FROM Users U
WHERE ISNULL(U.IsAdmin,0)=0
  AND NOT EXISTS(SELECT 1 FROM UserPermissions P WHERE P.UserID=U.id);
