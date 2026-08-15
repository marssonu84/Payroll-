<%@ Page Title="Dashboard"
    Language="C#"
    MasterPageFile="~/Site.Master"
    AutoEventWireup="true"
    CodeFile="Default.aspx.cs"
    Inherits="PayrollWebApp.Dashboard" %>

<asp:Content ID="c1"
    ContentPlaceHolderID="PageTitleSlot"
    runat="server">

    Dashboard

</asp:Content>


<asp:Content ID="c2"
    ContentPlaceHolderID="PageBodySlot"
    runat="server">

    <h1>Salary Dashboard</h1>


    <!-- ERROR MESSAGE -->

    <asp:Panel
        ID="pnlError"
        runat="server"
        Visible="false"
        CssClass="message error">

        <asp:Label
            ID="lblError"
            runat="server" />

    </asp:Panel>


    <!-- DASHBOARD STATISTICS -->

    <div class="grid4">

        <div class="stat">

            Active Employees

            <b>
                <asp:Label
                    ID="lblEmployees"
                    runat="server" />
            </b>

        </div>


        <div class="stat">

            Current Month Gross

            <b>
                ₹
                <asp:Label
                    ID="lblGross"
                    runat="server" />
            </b>

        </div>


        <div class="stat">

            Current Month Deductions

            <b>
                ₹
                <asp:Label
                    ID="lblDeductions"
                    runat="server" />
            </b>

        </div>


        <div class="stat">

            Current Month Net Pay

            <b>
                ₹
                <asp:Label
                    ID="lblNet"
                    runat="server" />
            </b>

        </div>

    </div>


    <br />


    <!-- ATTENDANCE MANAGEMENT -->

    <div class="card">

        <h2>Attendance Management</h2>

        <p class="note">
            Import employee attendance from Excel,
            review monthly attendance,
            calculate late marks, permissions,
            half days, casual leave and salary deduction days.
        </p>

        <p>
            <a href="Attendance.aspx"
               style="
                    display:inline-block;
                    background:#087ff5;
                    color:#ffffff;
                    padding:11px 18px;
                    border-radius:6px;
                    text-decoration:none;
                    font-weight:600;">
                Open Attendance
            </a>
        </p>

    </div>


    <!-- BRANCH SUMMARY -->

    <div class="card">

        <h2>Branch-wise Summary</h2>

        <div class="tablewrap">

            <asp:GridView
                ID="gvSummary"
                runat="server"
                AutoGenerateColumns="true"
                CssClass="table" />

        </div>

    </div>


    <!-- PAYROLL MODEL -->

    <div class="card">

        <h2>Workbook Model Implemented</h2>

        <p class="note">

            Branch payroll,
            PF,
            ESI,
            PT,
            salary advances,
            other deductions,
            net salary,
            bank details,
            monthly summary
            and loan ledger.

        </p>


        <asp:Panel
            ID="pnlDatabaseSetup"
            runat="server"
            Visible="false">

            <p>
                <a href="Setup.aspx">
                    Run database setup
                </a>
            </p>

        </asp:Panel>

    </div>


</asp:Content>
