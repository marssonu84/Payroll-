V13 – Reports section redesign package
======================================

What this package improves
--------------------------
1. Cleaner layout: month + Show Reports + Print at the top.
2. KPI strip after load (Employees, Branches, Total Gross, Total Net).
3. Downloads grouped into three cards:
     - Summary
     - Bank / NEFT  (NEFT, House, IDBI, HDFC, KVB)
     - Statutory    (PF, ESI)
4. Clear validation / empty-data messages.
5. Real .xlsx downloads (no more HTML-as-Excel .xls).
6. Safer response handling (CompleteRequest instead of Response.End).
7. Print-friendly: controls and download cards hidden; bank section starts on a new page.

Files included
--------------
  Reports.aspx                  – new markup
  Reports.aspx.cs               – cleaned code-behind
  App_Code/ReportExcelService.cs – shared .xlsx builder for report tables
  Content/site.css              – full CSS with report redesign styles appended
  README_REPORTS_REDESIGN.txt   – this file

How to install
--------------
1. Back up your current:
     Reports.aspx
     Reports.aspx.cs
     Content/site.css
     (and any existing App_Code/ReportExcelService.cs if present)

2. Copy the files from this package into the site root, keeping the same
   relative paths:
     Reports.aspx          → site root
     Reports.aspx.cs       → site root
     App_Code/ReportExcelService.cs → App_Code/
     Content/site.css      → Content/  (overwrites; report styles are at the end)

3. Recycle the application pool / restart the site so App_Code is recompiled.

4. Log in with a user that has ReportsView permission and open Reports.

Notes
-----
- ReportExcelService builds a simple single-sheet Open XML .xlsx (no third-party DLL).
- Bank matching logic is unchanged (HOUSE branch, NEFT = non-named banks, etc.).
- Tenant isolation and parameterized queries are preserved.
- If you already customized site.css heavily, merge only the block under
  “Reports page redesign” from the package CSS instead of overwriting the whole file.

Compatibility
-------------
- ASP.NET Web Forms, .NET Framework (same target as the rest of V13).
- Uses System.IO.Compression.ZipArchive (available on the supported runtime).
- Auth, Db, and permission checks are unchanged from the main product.
