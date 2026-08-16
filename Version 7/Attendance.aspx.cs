using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace PayrollWebApp
{
    public partial class AttendancePage : Page
    {
        private static readonly TimeSpan LateThreshold =
            new TimeSpan(8, 25, 0);

        private const int PermissionMinutes = 90;


        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                txtMonth.Text =
                    DateTime.Today.ToString("yyyy-MM");

                BindBranches();
            }
        }


        // =========================================================
        // BRANCHES
        // =========================================================

        private void BindBranches()
        {
            try
            {
                DataTable dt = Db.Query(
                    @"SELECT BranchCode
                      FROM Branches
                      WHERE IsActive = 1
                      ORDER BY BranchCode");

                ddlBranch.Items.Clear();

                ddlBranch.Items.Add(
                    new ListItem(
                        "All Branches",
                        ""));

                foreach (DataRow row in dt.Rows)
                {
                    string code =
                        Convert.ToString(
                            row["BranchCode"]);

                    ddlBranch.Items.Add(
                        new ListItem(
                            code,
                            code));
                }
            }
            catch
            {
                ddlBranch.Items.Clear();

                ddlBranch.Items.Add(
                    new ListItem(
                        "All Branches",
                        ""));
            }
        }


        // =========================================================
        // IMPORT
        // =========================================================

        protected void btnImport_Click(
            object sender,
            EventArgs e)
        {
            try
            {
                if (!fuAttendance.HasFile)
                {
                    throw new Exception(
                        "Please select the attendance CSV file.");
                }


                string extension =
                    Path.GetExtension(
                        fuAttendance.FileName);


                if (!String.Equals(
                    extension,
                    ".csv",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        "Please upload a .csv attendance file.");
                }


                TimeSpan officialOutTime;

                if (!TimeSpan.TryParse(
                    txtOfficialOutTime.Text,
                    out officialOutTime))
                {
                    throw new Exception(
                        "Please enter the Official OUT Time.");
                }


                DataTable attendanceData =
                    ReadCsv(
                        fuAttendance.PostedFile.InputStream);


                int imported =
                    ProcessRows(
                        attendanceData,
                        officialOutTime);


                ShowSuccess(
                    imported.ToString()
                    +
                    " attendance row(s) imported successfully.");


                if (
                    attendanceData.Rows.Count > 0
                    &&
                    attendanceData.Columns.Contains("Date"))
                {
                    DateTime firstDate;

                    if (
                        TryDate(
                            attendanceData.Rows[0]["Date"],
                            out firstDate))
                    {
                        txtMonth.Text =
                            firstDate.ToString(
                                "yyyy-MM");
                    }
                }


                BindSummary();
            }
            catch (Exception ex)
            {
                ShowError(
                    ex.Message);
            }
        }


        // =========================================================
        // LOAD SUMMARY
        // =========================================================

        protected void btnLoad_Click(
            object sender,
            EventArgs e)
        {
            try
            {
                BindSummary();
            }
            catch (Exception ex)
            {
                ShowError(
                    ex.Message);
            }
        }


        // =========================================================
        // PROCESS IMPORT ROWS
        // =========================================================

        private int ProcessRows(
            DataTable dt,
            TimeSpan officialOutTime)
        {
            RequireColumns(
                dt,
                "Employee ID",
                "Employee Name",
                "Date",
                "IN Time",
                "OUT Time",
                "Approved Permission",
                "Permission Type",
                "Approved Casual Leave",
                "Declared Holiday",
                "Remarks");


            int importedCount = 0;


            foreach (DataRow row in dt.Rows)
            {
                string employeeCode =
                    S(
                        row["Employee ID"]);


                if (
                    String.IsNullOrWhiteSpace(
                        employeeCode))
                {
                    continue;
                }


                DateTime attendanceDate;

                if (
                    !TryDate(
                        row["Date"],
                        out attendanceDate))
                {
                    throw new Exception(
                        "Invalid date for Employee ID: "
                        +
                        employeeCode);
                }


                object employeeObject =
                    Db.Scalar(
                        @"SELECT EmployeeID
                          FROM Employees
                          WHERE EmployeeCode = ?
                          AND IsActive = 1",
                        P(employeeCode));


                if (
                    employeeObject == null
                    ||
                    employeeObject == DBNull.Value)
                {
                    throw new Exception(
                        "Employee ID not found or inactive: "
                        +
                        employeeCode);
                }


                int employeeId =
                    Convert.ToInt32(
                        employeeObject);


                TimeSpan? inTime =
                    ParseTime(
                        row["IN Time"]);


                TimeSpan? outTime =
                    ParseTime(
                        row["OUT Time"]);


                bool approvedPermission =
                    Yes(
                        row["Approved Permission"]);


                string permissionType =
                    S(
                        row["Permission Type"]);


                bool approvedCasualLeave =
                    Yes(
                        row["Approved Casual Leave"]);


                bool declaredHoliday =
                    Yes(
                        row["Declared Holiday"]);


                string remarks =
                    S(
                        row["Remarks"]);


                DayResult result =
                    CalculateDay(
                        attendanceDate,
                        inTime,
                        outTime,
                        officialOutTime,
                        approvedPermission,
                        approvedCasualLeave,
                        declaredHoliday);


                Db.Execute(
                    @"DELETE FROM Attendance
                      WHERE EmployeeID = ?
                      AND AttendanceDate = ?",

                    P(employeeId),
                    P(attendanceDate.Date));


                Db.Execute(
                    @"INSERT INTO Attendance
                    (
                        EmployeeID,
                        AttendanceDate,
                        InTime,
                        OutTime,
                        OfficialOutTime,
                        ApprovedPermission,
                        PermissionType,
                        ApprovedCasualLeave,
                        DeclaredHoliday,
                        IsPaidHoliday,
                        LateCount,
                        PermissionCount,
                        HalfDayUnits,
                        CasualLeaveDays,
                        AbsentDays,
                        AttendanceStatus,
                        Remarks,
                        ImportedOn
                    )
                    VALUES
                    (
                        ?,?,?,?,?,?,?,?,?,?,
                        ?,?,?,?,?,?,?,?
                    )",

                    P(employeeId),

                    P(attendanceDate.Date),

                    PTime(inTime),

                    PTime(outTime),

                    P(officialOutTime),

                    P(approvedPermission),

                    P(permissionType),

                    P(approvedCasualLeave),

                    P(declaredHoliday),

                    P(result.PaidHoliday),

                    P(result.LateCount),

                    P(result.PermissionCount),

                    P(result.HalfDayUnits),

                    P(result.CasualLeaveDays),

                    P(result.AbsentDays),

                    P(result.Status),

                    P(remarks),

                    P(DateTime.Now));


                importedCount++;
            }


            return importedCount;
        }


        // =========================================================
        // DAILY ATTENDANCE RULES
        // =========================================================

        private DayResult CalculateDay(
            DateTime date,
            TimeSpan? inTime,
            TimeSpan? outTime,
            TimeSpan officialOutTime,
            bool approvedPermission,
            bool approvedCasualLeave,
            bool declaredHoliday)
        {
            DayResult result =
                new DayResult();


            // Sunday or declared holiday

            if (
                date.DayOfWeek ==
                    DayOfWeek.Sunday
                ||
                declaredHoliday)
            {
                result.PaidHoliday = true;

                result.Status =
                    "Paid Holiday";

                return result;
            }


            // Approved Casual Leave

            if (approvedCasualLeave)
            {
                result.CasualLeaveDays =
                    1m;

                result.Status =
                    "Casual Leave";

                return result;
            }


            // No IN and no OUT

            if (
                !inTime.HasValue
                &&
                !outTime.HasValue)
            {
                result.AbsentDays =
                    1m;

                result.Status =
                    "Absent";

                return result;
            }


            int lateMinutes = 0;

            if (
                inTime.HasValue
                &&
                inTime.Value >
                    LateThreshold)
            {
                lateMinutes =
                    (int)Math.Ceiling(
                        (
                            inTime.Value
                            -
                            LateThreshold
                        ).TotalMinutes);
            }


            int earlyMinutes = 0;

            if (
                outTime.HasValue
                &&
                outTime.Value <
                    officialOutTime)
            {
                earlyMinutes =
                    (int)Math.Ceiling(
                        (
                            officialOutTime
                            -
                            outTime.Value
                        ).TotalMinutes);
            }


            List<string> statuses =
                new List<string>();


            if (approvedPermission)
            {
                ApplyApprovedIrregularity(
                    result,
                    lateMinutes,
                    "Late Permission",
                    statuses);


                ApplyApprovedIrregularity(
                    result,
                    earlyMinutes,
                    "Early Permission",
                    statuses);
            }
            else
            {
                ApplyUnapprovedIrregularity(
                    result,
                    lateMinutes,
                    "Late",
                    statuses);


                ApplyUnapprovedIrregularity(
                    result,
                    earlyMinutes,
                    "Early Departure",
                    statuses);
            }


            if (statuses.Count == 0)
            {
                result.Status =
                    "Present";
            }
            else
            {
                result.Status =
                    String.Join(
                        " + ",
                        statuses.ToArray());
            }


            return result;
        }


        // =========================================================
        // APPROVED PERMISSION
        // =========================================================

        private static void ApplyApprovedIrregularity(
            DayResult result,
            int minutes,
            string label,
            List<string> statuses)
        {
            if (minutes <= 0)
            {
                return;
            }


            if (
                minutes >
                PermissionMinutes)
            {
                result.HalfDayUnits +=
                    0.5m;

                statuses.Add(
                    "Half Day");
            }
            else
            {
                result.PermissionCount++;

                statuses.Add(
                    label);
            }
        }


        // =========================================================
        // UNAPPROVED IRREGULARITY
        // =========================================================

        private static void ApplyUnapprovedIrregularity(
            DayResult result,
            int minutes,
            string label,
            List<string> statuses)
        {
            if (minutes <= 0)
            {
                return;
            }


            if (
                minutes >
                PermissionMinutes)
            {
                result.HalfDayUnits +=
                    0.5m;

                statuses.Add(
                    "Half Day");
            }
            else
            {
                result.LateCount++;

                statuses.Add(
                    label);
            }
        }


        // =========================================================
        // MONTHLY SUMMARY
        // =========================================================

        private void BindSummary()
        {
            DateTime month;

            if (
                !DateTime.TryParse(
                    txtMonth.Text + "-01",
                    out month))
            {
                throw new Exception(
                    "Please select a valid month.");
            }


            DateTime nextMonth =
                month.AddMonths(1);


            string branch =
                ddlBranch.SelectedValue;


            DataTable source =
                Db.Query(
                    @"SELECT
                        E.EmployeeCode,
                        E.EmployeeName,
                        E.BranchCode,

                        A.AttendanceDate,
                        A.IsPaidHoliday,
                        A.LateCount,
                        A.PermissionCount,
                        A.HalfDayUnits,
                        A.CasualLeaveDays,
                        A.AbsentDays,
                        A.AttendanceStatus

                      FROM Attendance A

                      INNER JOIN Employees E
                      ON E.EmployeeID =
                         A.EmployeeID

                      WHERE
                          A.AttendanceDate >= ?
                          AND
                          A.AttendanceDate < ?

                          AND
                          (
                              ? = ''
                              OR
                              E.BranchCode = ?
                          )

                      ORDER BY
                          E.EmployeeName,
                          A.AttendanceDate",

                    P(month.Date),

                    P(nextMonth.Date),

                    P(branch),

                    P(branch));


            DataTable table =
                CreateSummaryTable();


            Dictionary<string, Summary>
                dictionary =
                    new Dictionary<string, Summary>(
                        StringComparer.OrdinalIgnoreCase);


            foreach (DataRow row
                in source.Rows)
            {
                string employeeCode =
                    S(
                        row["EmployeeCode"]);


                Summary summary;

                if (
                    !dictionary.TryGetValue(
                        employeeCode,
                        out summary))
                {
                    summary =
                        new Summary();


                    summary.EmployeeCode =
                        employeeCode;


                    summary.EmployeeName =
                        S(
                            row["EmployeeName"]);


                    dictionary[
                        employeeCode] =
                        summary;
                }


                bool paidHoliday =
                    Convert.ToBoolean(
                        row["IsPaidHoliday"]);


                if (paidHoliday)
                {
                    summary.PaidHolidays++;
                }
                else
                {
                    summary.WorkingDays++;
                }


                string status =
                    S(
                        row["AttendanceStatus"]);


                if (
                    !paidHoliday
                    &&
                    status ==
                        "Present")
                {
                    summary.PresentDays++;
                }


                summary.CasualLeaveDays +=
                    D(
                        row["CasualLeaveDays"]);


                summary.LateCount +=
                    Convert.ToInt32(
                        row["LateCount"]);


                summary.PermissionCount +=
                    Convert.ToInt32(
                        row["PermissionCount"]);


                summary.HalfDayUnits +=
                    D(
                        row["HalfDayUnits"]);


                summary.AbsentDays +=
                    D(
                        row["AbsentDays"]);
            }


            foreach (
                Summary summary
                in dictionary.Values)
            {
                // 3 Lates = 1 day

                decimal lateEquivalent =
                    (
                        summary.LateCount / 3
                    );


                // 2 Permissions = Half Day

                decimal permissionEquivalent =
                    (
                        summary.PermissionCount / 2
                    )
                    *
                    0.5m;


                decimal leaveEquivalent =
                    summary.CasualLeaveDays
                    +
                    lateEquivalent
                    +
                    permissionEquivalent
                    +
                    summary.HalfDayUnits;


                decimal freeAllowance =
                    Math.Min(
                        1m,
                        leaveEquivalent);


                decimal deductionDays =
                    Math.Max(
                        0m,
                        leaveEquivalent - 1m)
                    +
                    summary.AbsentDays;


                table.Rows.Add(

                    summary.EmployeeCode,

                    summary.EmployeeName,

                    summary.WorkingDays,

                    summary.PresentDays,

                    summary.PaidHolidays,

                    summary.CasualLeaveDays,

                    summary.LateCount,

                    summary.PermissionCount,

                    summary.HalfDayUnits,

                    summary.AbsentDays,

                    leaveEquivalent,

                    freeAllowance,

                    deductionDays
                );
            }


            gvSummary.DataSource =
                table;

            gvSummary.DataBind();
        }


        // =========================================================
        // SUMMARY TABLE
        // =========================================================

        private static DataTable
            CreateSummaryTable()
        {
            DataTable table =
                new DataTable();


            table.Columns.Add(
                "EmployeeCode",
                typeof(string));


            table.Columns.Add(
                "EmployeeName",
                typeof(string));


            table.Columns.Add(
                "WorkingDays",
                typeof(int));


            table.Columns.Add(
                "PresentDays",
                typeof(int));


            table.Columns.Add(
                "PaidHolidays",
                typeof(int));


            table.Columns.Add(
                "CasualLeaveDays",
                typeof(decimal));


            table.Columns.Add(
                "LateCount",
                typeof(int));


            table.Columns.Add(
                "PermissionCount",
                typeof(int));


            table.Columns.Add(
                "HalfDayUnits",
                typeof(decimal));


            table.Columns.Add(
                "AbsentDays",
                typeof(decimal));


            table.Columns.Add(
                "LeaveEquivalent",
                typeof(decimal));


            table.Columns.Add(
                "FreeAllowance",
                typeof(decimal));


            table.Columns.Add(
                "DeductionDays",
                typeof(decimal));


            return table;
        }


        // =========================================================
        // CSV READER
        // =========================================================

        private static DataTable
            ReadCsv(
                Stream stream)
        {
            DataTable table =
                new DataTable();


            using (
                StreamReader reader =
                    new StreamReader(
                        stream,
                        Encoding.UTF8,
                        true))
            {
                string headerLine =
                    reader.ReadLine();


                if (
                    String.IsNullOrWhiteSpace(
                        headerLine))
                {
                    throw new Exception(
                        "Attendance file is empty.");
                }


                List<string> headers =
                    ParseCsvLine(
                        headerLine);


                foreach (
                    string header
                    in headers)
                {
                    string name =
                        header.Trim();


                    if (
                        String.IsNullOrEmpty(
                            name))
                    {
                        name =
                            "Column"
                            +
                            (
                                table.Columns.Count
                                +
                                1
                            ).ToString();
                    }


                    table.Columns.Add(
                        name,
                        typeof(string));
                }


                string line;


                while (
                    (line =
                        reader.ReadLine())
                    != null)
                {
                    if (
                        String.IsNullOrWhiteSpace(
                            line))
                    {
                        continue;
                    }


                    List<string> values =
                        ParseCsvLine(
                            line);


                    DataRow row =
                        table.NewRow();


                    for (
                        int i = 0;
                        i <
                        table.Columns.Count;
                        i++)
                    {
                        if (
                            i <
                            values.Count)
                        {
                            row[i] =
                                values[i].Trim();
                        }
                        else
                        {
                            row[i] =
                                "";
                        }
                    }


                    table.Rows.Add(
                        row);
                }
            }


            return table;
        }


        // =========================================================
        // CSV PARSER
        // Handles commas inside quoted fields
        // =========================================================

        private static List<string>
            ParseCsvLine(
                string line)
        {
            List<string> values =
                new List<string>();


            StringBuilder current =
                new StringBuilder();


            bool insideQuotes =
                false;


            for (
                int i = 0;
                i <
                line.Length;
                i++)
            {
                char c =
                    line[i];


                if (c == '"')
                {
                    if (
                        insideQuotes
                        &&
                        i + 1 <
                        line.Length
                        &&
                        line[i + 1] ==
                            '"')
                    {
                        current.Append(
                            '"');

                        i++;
                    }
                    else
                    {
                        insideQuotes =
                            !insideQuotes;
                    }
                }
                else if (
                    c == ','
                    &&
                    !insideQuotes)
                {
                    values.Add(
                        current.ToString());

                    current.Length =
                        0;
                }
                else
                {
                    current.Append(
                        c);
                }
            }


            values.Add(
                current.ToString());


            return values;
        }


        // =========================================================
        // REQUIRED COLUMNS
        // =========================================================

        private static void
            RequireColumns(
                DataTable table,
                params string[] columns)
        {
            foreach (
                string column
                in columns)
            {
                if (
                    !table.Columns.Contains(
                        column))
                {
                    throw new Exception(
                        "Attendance file column missing: "
                        +
                        column);
                }
            }
        }


        // =========================================================
        // DATE
        // =========================================================

        private static bool
            TryDate(
                object value,
                out DateTime result)
        {
            string text =
                S(
                    value);


            return
                DateTime.TryParse(
                    text,
                    out result)

                ||

                DateTime.TryParseExact(
                    text,
                    "dd-MM-yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out result)

                ||

                DateTime.TryParseExact(
                    text,
                    "dd/MM/yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out result);
        }


        // =========================================================
        // TIME
        // =========================================================

        private static TimeSpan?
            ParseTime(
                object value)
        {
            string text =
                S(
                    value);


            if (
                String.IsNullOrWhiteSpace(
                    text))
            {
                return null;
            }


            TimeSpan time;


            if (
                TimeSpan.TryParse(
                    text,
                    out time))
            {
                return time;
            }


            DateTime date;


            if (
                DateTime.TryParse(
                    text,
                    out date))
            {
                return
                    date.TimeOfDay;
            }


            throw new Exception(
                "Invalid time value: "
                +
                text);
        }


        // =========================================================
        // YES / NO
        // =========================================================

        private static bool
            Yes(
                object value)
        {
            string text =
                S(
                    value);


            return
                text.Equals(
                    "Yes",
                    StringComparison.OrdinalIgnoreCase)

                ||

                text.Equals(
                    "Y",
                    StringComparison.OrdinalIgnoreCase)

                ||

                text.Equals(
                    "True",
                    StringComparison.OrdinalIgnoreCase)

                ||

                text ==
                    "1";
        }


        // =========================================================
        // HELPERS
        // =========================================================

        private static string
            S(
                object value)
        {
            if (
                value == null
                ||
                value == DBNull.Value)
            {
                return "";
            }


            return
                Convert.ToString(
                    value).Trim();
        }


        private static decimal
            D(
                object value)
        {
            if (
                value == null
                ||
                value == DBNull.Value)
            {
                return 0m;
            }


            return
                Convert.ToDecimal(
                    value);
        }


        private static OleDbParameter
            P(
                object value)
        {
            return
                new OleDbParameter(
                    "?",
                    value ??
                    DBNull.Value);
        }


        private static OleDbParameter
            PTime(
                TimeSpan? value)
        {
            if (!value.HasValue)
            {
                return
                    P(
                        DBNull.Value);
            }


            return
                P(
                    value.Value);
        }


        // =========================================================
        // MESSAGES
        // =========================================================

        private void ShowError(
            string message)
        {
            lblMsg.CssClass =
                "message error";

            lblMsg.Text =
                message;
        }


        private void ShowSuccess(
            string message)
        {
            lblMsg.CssClass =
                "message success";

            lblMsg.Text =
                message;
        }


        // =========================================================
        // DAILY RESULT
        // =========================================================

        private class DayResult
        {
            public bool PaidHoliday;

            public int LateCount;

            public int PermissionCount;

            public decimal HalfDayUnits;

            public decimal CasualLeaveDays;

            public decimal AbsentDays;

            public string Status =
                "Present";
        }


        // =========================================================
        // MONTHLY SUMMARY
        // =========================================================

        private class Summary
        {
            public string EmployeeCode;

            public string EmployeeName;

            public int WorkingDays;

            public int PresentDays;

            public int PaidHolidays;

            public decimal CasualLeaveDays;

            public int LateCount;

            public int PermissionCount;

            public decimal HalfDayUnits;

            public decimal AbsentDays;
        }
    }
}
