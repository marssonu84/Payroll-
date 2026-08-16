<%@ Page Title="Attendance"
    Language="C#"
    MasterPageFile="~/Site.Master"
    AutoEventWireup="true"
    CodeFile="Attendance.aspx.cs"
    Inherits="PayrollWebApp.AttendancePage" %>

<asp:Content ID="c1"
    ContentPlaceHolderID="PageTitleSlot"
    runat="server">

    Attendance

</asp:Content>


<asp:Content ID="c2"
    ContentPlaceHolderID="PageBodySlot"
    runat="server">

    <h1>Attendance Management</h1>

    <asp:Label
        ID="lblMsg"
        runat="server" />


    <!-- EXCEL IMPORT -->

    <div class="card">

        <h2>Import Attendance from Excel</h2>

        <div class="formgrid">

            <div class="field">

                <label>
    Attendance File (.csv)
</label>

                <asp:FileUpload
                    ID="fuAttendance"
                    runat="server" />

            </div>


            <div class="field">

                <label>
                    Official OUT Time
                </label>

                <asp:TextBox
                    ID="txtOfficialOutTime"
                    runat="server"
                    TextMode="Time" />

            </div>

        </div>


        <p class="note">

            Reporting after <strong>8:25 AM</strong>
            is treated as Late unless approved permission exists.

            Approved permission is allowed up to
            <strong>1.5 hours</strong>.

            More than 1.5 hours late arrival or
            early departure will be treated as Half Day.

        </p>


        <asp:Button
            ID="btnImport"
            runat="server"
            Text="Import & Process Attendance"
            CssClass="btn"
            OnClick="btnImport_Click" />

    </div>


    <!-- MONTHLY SUMMARY -->

    <div class="card">

        <h2>Monthly Attendance Summary</h2>


        <div class="formgrid">

            <div class="field">

                <label>Month</label>

                <asp:TextBox
                    ID="txtMonth"
                    runat="server"
                    TextMode="Month" />

            </div>


            <div class="field">

                <label>Branch</label>

                <asp:DropDownList
                    ID="ddlBranch"
                    runat="server" />

            </div>

        </div>


        <asp:Button
            ID="btnLoad"
            runat="server"
            Text="Load Summary"
            CssClass="btn"
            OnClick="btnLoad_Click" />


        <div class="tablewrap"
             style="margin-top:18px;">

            <asp:GridView
                ID="gvSummary"
                runat="server"
                AutoGenerateColumns="False"
                GridLines="None"
                CssClass="table">

                <Columns>


                    <asp:BoundField
                        DataField="EmployeeCode"
                        HeaderText="Code" />


                    <asp:BoundField
                        DataField="EmployeeName"
                        HeaderText="Employee" />


                    <asp:BoundField
                        DataField="WorkingDays"
                        HeaderText="Working Days" />


                    <asp:BoundField
                        DataField="PresentDays"
                        HeaderText="Present" />


                    <asp:BoundField
                        DataField="PaidHolidays"
                        HeaderText="Paid Holidays" />


                    <asp:BoundField
                        DataField="CasualLeaveDays"
                        HeaderText="CL"
                        DataFormatString="{0:0.##}" />


                    <asp:BoundField
                        DataField="LateCount"
                        HeaderText="Lates" />


                    <asp:BoundField
                        DataField="PermissionCount"
                        HeaderText="Permissions" />


                    <asp:BoundField
                        DataField="HalfDayUnits"
                        HeaderText="Half Days"
                        DataFormatString="{0:0.##}" />


                    <asp:BoundField
                        DataField="AbsentDays"
                        HeaderText="Absent"
                        DataFormatString="{0:0.##}" />


                    <asp:BoundField
                        DataField="LeaveEquivalent"
                        HeaderText="Leave Equivalent"
                        DataFormatString="{0:0.##}" />


                    <asp:BoundField
                        DataField="FreeAllowance"
                        HeaderText="Free Allowance"
                        DataFormatString="{0:0.##}" />


                    <asp:BoundField
                        DataField="DeductionDays"
                        HeaderText="Salary Deduction Days"
                        DataFormatString="{0:0.##}" />


                </Columns>

            </asp:GridView>

        </div>

    </div>


    <!-- ATTENDANCE RULES -->

    <div class="card">

        <h2>Attendance Rules</h2>

        <ul>

            <li>
                Reporting after
                <strong>8:25 AM</strong>
                = Late, unless approved permission exists.
            </li>

            <li>
                One day equivalent every month is allowed
                without salary deduction.
            </li>

            <li>
                The monthly free allowance does
                <strong>not carry forward</strong>.
            </li>

            <li>
                Every <strong>3 Lates = 1 day</strong>
                equivalent.
            </li>

            <li>
                Every <strong>2 Permissions = ½ day</strong>
                equivalent.
            </li>

            <li>
                Approved Permission can cover late arrival
                or early departure up to
                <strong>1.5 hours</strong>.
            </li>

            <li>
                More than 1.5 hours late arrival
                or early departure =
                <strong>Half Day</strong>.
            </li>

            <li>
                Two Half Days =
                <strong>1 full-day equivalent</strong>.
            </li>

            <li>
                No IN + No OUT + No approved leave =
                <strong>Absent</strong>
                and 1 full-day salary deduction.
            </li>

            <li>
                Sundays and declared holidays =
                <strong>Paid Holidays</strong>.
            </li>

            <li>
                Saturday is a normal working day
                unless specifically declared a holiday.
            </li>

            <li>
                Salary is calculated for the previous
                calendar month and payment is scheduled
                for the <strong>4th of every month</strong>.
            </li>

        </ul>

    </div>


    <!-- BACK TO DASHBOARD -->

    <div style="text-align:center;
                margin-top:20px;
                margin-bottom:30px;">

        <a href="Default.aspx"
           style="
                display:inline-block;
                background:#6b7280;
                color:#ffffff;
                padding:11px 20px;
                border-radius:6px;
                text-decoration:none;
                font-weight:600;">

            ← Back to Dashboard

        </a>

    </div>


</asp:Content>

