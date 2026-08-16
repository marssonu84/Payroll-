

-- Create Tenants Table

IF OBJECT_ID('Tenants','U') IS NULL

BEGIN

    CREATE TABLE Tenants (

        TenantID INT IDENTITY(1,1) PRIMARY KEY,

        OrganizationName NVARCHAR(150) NOT NULL,

        IsActive BIT DEFAULT 1

    )

END

-- Add TenantID columns to existing entities

IF COL_LENGTH('Branches','TenantID') IS NULL 

    ALTER TABLE Branches ADD TenantID INT NOT NULL DEFAULT 1;

IF COL_LENGTH('Employees','TenantID') IS NULL 

    ALTER TABLE Employees ADD TenantID INT NOT NULL DEFAULT 1;

IF COL_LENGTH('Payroll','TenantID') IS NULL 

    ALTER TABLE Payroll ADD TenantID INT NOT NULL DEFAULT 1;

IF COL_LENGTH('Loans','TenantID') IS NULL 

    ALTER TABLE Loans ADD TenantID INT NOT NULL DEFAULT 1;



