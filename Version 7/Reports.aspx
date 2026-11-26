<%@ Page Title="Reports" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeFile="Reports.aspx.cs" Inherits="PayrollWebApp.ReportsPage" %>
<asp:Content ID="c1" ContentPlaceHolderID="PageTitleSlot" runat="server">Reports</asp:Content>
<asp:Content ID="c2" ContentPlaceHolderID="PageBodySlot" runat="server">

<div class="report-heading">
    <h1>Payroll Reports</h1>
    <div class="report-meta">
        <strong><%= Server.HtmlEncode(PayrollWebApp.Auth.CurrentOrganizationName()) %></strong>
        <span class="report-meta-sep">·</span>
        Salary Month: <asp:Literal runat="server" ID="litReportMonth" Text="—" />
    </div>
</div>

<%-- Controls: month + primary actions --%>
<div class="card report-controls">
    <div class="toolbar">
        <div class="field">
            <label for="txtMonth">Month</label>
            <asp:TextBox runat="server" ID="txtMonth" TextMode="Month" />
        </div>
        <asp:Button runat="server" ID="btnShow" Text="Show Reports" CssClass="btn" OnClick="btnShow_Click" />
        <button type="button" class="btn secondary" onclick="window.print();">Print</button>
    </div>
    <asp:Panel runat="server" ID="pnlMessage" Visible="false" CssClass="message">
        <asp:Literal runat="server" ID="litMessage" />
    </asp:Panel>
    <div class="note">Choose the salary month, then Show Reports. Downloads appear below after data is loaded. Excel files are real .xlsx workbooks.</div>
</div>

<%-- KPI strip (shown after successful load) --%>
<asp:Panel runat="server" ID="pnlKpis" Visible="false" CssClass="report-kpis">
    <div class="report-kpi">
        <span>Employees</span>
        <b><asp:Literal runat="server" ID="litEmpCount" /></b>
    </div>
    <div class="report-kpi">
        <span>Branches</span>
        <b><asp:Literal runat="server" ID="litBranchCount" /></b>
    </div>
    <div class="report-kpi">
        <span>Total Gross</span>
        <b><asp:Literal runat="server" ID="litGross" /></b>
    </div>
    <div class="report-kpi">
        <span>Total Net</span>
        <b><asp:Literal runat="server" ID="litNet" /></b>
    </div>
</asp:Panel>

<%-- Grouped downloads --%>
<asp:Panel runat="server" ID="pnlDownloads" Visible="false" CssClass="download-grid">
    <div class="card download-card">
        <h3>Summary</h3>
        <p class="download-desc">Branch-wise payroll totals</p>
        <div class="actions">
            <asp:Button runat="server" ID="btnDownloadSummary" Text="Download Excel" CssClass="btn secondary" OnClick="btnDownloadSummary_Click" />
        </div>
    </div>
    <div class="card download-card">
        <h3>Bank / NEFT</h3>
        <p class="download-desc">Payment lists by bank or NEFT</p>
        <div class="actions">
            <asp:Button runat="server" ID="btnDownloadNEFT" Text="NEFT" CssClass="btn secondary" OnClick="btnDownloadNEFT_Click" />
            <asp:Button runat="server" ID="btnDownloadHouse" Text="House" CssClass="btn secondary" OnClick="btnDownloadHouse_Click" />
            <asp:Button runat="server" ID="btnDownloadIDBI" Text="IDBI" CssClass="btn secondary" OnClick="btnDownloadIDBI_Click" />
            <asp:Button runat="server" ID="btnDownloadHDFC" Text="HDFC" CssClass="btn secondary" OnClick="btnDownloadHDFC_Click" />
            <asp:Button runat="server" ID="btnDownloadKVB" Text="KVB" CssClass="btn secondary" OnClick="btnDownloadKVB_Click" />
        </div>
    </div>
    <div class="card download-card">
        <h3>Statutory</h3>
        <p class="download-desc">PF and ESI contribution lists</p>
        <div class="actions">
            <asp:Button runat="server" ID="btnDownloadPF" Text="PF Report" CssClass="btn secondary" OnClick="btnDownloadPF_Click" />
            <asp:Button runat="server" ID="btnDownloadESI" Text="ESI Report" CssClass="btn secondary" OnClick="btnDownloadESI_Click" />
        </div>
    </div>
</asp:Panel>

<%-- On-screen reports --%>
<div class="card report-section">
    <h2>Payroll Summary</h2>
    <div class="tablewrap">
        <asp:GridView runat="server" ID="gvSummary" AutoGenerateColumns="true"
            EmptyDataText="No payroll records found for the selected month. Choose a month and click Show Reports."
            CssClass="report-table" HeaderStyle-CssClass="report-th" />
    </div>
</div>

<div class="card report-section report-section-bank">
    <h2>Bank / NEFT Statement</h2>
    <div class="tablewrap">
        <asp:GridView runat="server" ID="gvBank" AutoGenerateColumns="true"
            EmptyDataText="No bank payment records found for the selected month."
            CssClass="report-table" HeaderStyle-CssClass="report-th" />
    </div>
</div>

</asp:Content>
