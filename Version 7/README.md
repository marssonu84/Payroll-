# ASP.NET Payroll Web App (MS Access)

Built from the structure and formulas observed in `JUL Salaries - 2026.xlsm`.

## Technology
- ASP.NET Web Forms, .NET Framework 4.8
- C#
- Microsoft Access `.accdb`
- `System.Data.OleDb`
- Microsoft ACE OLE DB 12.0/16.0 provider

## Implemented workflow
- Employee master by branch
- PF / ESI membership flags
- Bank account details
- Previous standard salary, increment and adjustment
- Monthly leaves and LOP
- 60% Basic, 30% HRA, 10% Overtime Allowance
- PF, ESI and PT calculations matching the Excel workbook formulas
- Salary advance and other deductions
- Net payable salary
- Branch-wise salary summary
- Bank/NEFT statement
- Employee loan ledger

## First Run
1. Open `PayrollWebApp.sln` in Visual Studio 2019/2022 on Windows.
2. Ensure `.NET Framework 4.8` developer pack is installed.
3. Install Microsoft Access Database Engine (ACE) matching the IIS/Visual Studio process bitness.
4. Run the site and open `/Setup.aspx` once.
5. Click **Create / Verify Database**. This creates `App_Data/Payroll.accdb`.
6. Open **Employees** and enter staff master data.
7. Open **Payroll**, choose month and branch, load employees, enter leaves/advances/deductions, and process salary.

## Excel formula mapping
- Standard Salary = Previous Standard Salary + Increment + Salary Adjustment
- LOP = Standard Salary / Days in Month × Leaves
- Gross Salary = Standard Salary - LOP
- Basic = Gross × 60%
- HRA = Gross × 30%
- Overtime Allowance = Gross × 10%
- PF = 12% of Basic; if Basic > 15,000 then 1,800 (when PF member)
- ESI = 0.75% of Basic if Basic < 21,001 (when ESI member), because this is what the source workbook formula currently does
- PT = 0 up to 15,000; 150 for 15,001–20,000; 200 above 20,000
- Total Deductions = PF + ESI + PT + Salary Advance + Other Deductions
- Net Pay = Gross - Total Deductions

## Important accounting note
The workbook's ESI calculation tests **Basic** salary. Statutory ESI is normally based on the prescribed wage definition, so this rule should be verified with your payroll/accounts professional before production use. The app intentionally mirrors the workbook for parity.

## Suggested next production upgrades
- Login and role-based access
- Attendance import
- Excel employee import
- Payslip PDF generation
- PF/ESI/PT statutory reports
- Audit trail and salary lock after approval
- Database backup
- Move from Access to SQL Server when concurrent users or data volume grows

## Version 5 security upgrade
- Admin User Management now lists existing accounts.
- Admin can create, edit, activate/deactivate and delete users.
- Admin can reset a user's password by editing the user and entering a new password.
- Standard Users can be granted section/action permissions separately:
  - Employees: View / Add-Edit
  - Payroll: View / Calculate-Save
  - Loans: View / Add-Edit
  - Reports: View-Download
- Menu items are hidden when a user lacks section access.
- Direct URL access is also blocked server-side.
- Save/process actions are checked server-side, not only hidden in the UI.
- An administrator cannot delete or deactivate their own logged-in account.
- The last active administrator cannot be deleted, deactivated or downgraded.
- Existing Standard Users are given their previous Version 4 access on first upgrade so deployment does not unexpectedly lock them out; an Admin can then tighten each user's permissions from User Management.
- `Setup.aspx` is now administrator-only on an upgraded deployment.

VERSION 5.1 MOBILE-SAFE HOTFIX
- Fixes the User Management runtime error caused by nested ASP.NET server controls not being resolved reliably from Page.FindControl.
- No database replacement is required.
- Upload/extract these application files over Version 5. Keep the existing database unchanged.
