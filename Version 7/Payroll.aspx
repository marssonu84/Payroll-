<%@ Page Title="Payroll" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeFile="Payroll.aspx.cs" Inherits="PayrollWebApp.PayrollPage" %>
<asp:Content ID="c1" ContentPlaceHolderID="PageTitleSlot" runat="server">Payroll</asp:Content>
<asp:Content ID="c2" ContentPlaceHolderID="PageBodySlot" runat="server">
<h1>Monthly Payroll Processing</h1><asp:Label runat="server" ID="lblMsg" />
<div class="card"><div class="toolbar">
<div class="field"><label>Salary Month</label><asp:TextBox runat="server" ID="txtMonth" TextMode="Month" /></div>
<div class="field"><label>Branch</label><asp:DropDownList runat="server" ID="ddlBranch" /></div>
<div class="field"><label>Days in Month</label><asp:TextBox runat="server" ID="txtDays" Text="31" /></div>
<asp:Button runat="server" ID="btnLoad" Text="Load Employees" CssClass="btn secondary" OnClick="btnLoad_Click" />
<asp:Button runat="server" ID="btnProcess" Text="Calculate & Save Payroll" CssClass="btn" OnClick="btnProcess_Click" />
</div><p class="note">Enter Leaves, Salary Advance and Other Deductions. All other salary columns are calculated from the Excel rules.</p>
<div class="tablewrap"><asp:GridView runat="server" ID="gvPayroll" AutoGenerateColumns="false" DataKeyNames="EmployeeID">
<Columns>
<asp:BoundField DataField="EmployeeID" HeaderText="ID" ReadOnly="true" />
<asp:BoundField DataField="EmployeeName" HeaderText="Employee" ReadOnly="true" />
<asp:BoundField DataField="Designation" HeaderText="Designation" ReadOnly="true" />
<asp:BoundField DataField="StandardSalary" HeaderText="Std Salary" ReadOnly="true" DataFormatString="{0:N0}" />
<asp:TemplateField HeaderText="Leaves"><ItemTemplate><asp:TextBox runat="server" ID="txtLeaves" Text='<%# Eval("Leaves") %>' Width="70" /></ItemTemplate></asp:TemplateField>
<asp:BoundField DataField="LOP" HeaderText="LOP" ReadOnly="true" DataFormatString="{0:N0}" />
<asp:BoundField DataField="GrossSalary" HeaderText="Gross" ReadOnly="true" DataFormatString="{0:N0}" />
<asp:BoundField DataField="Basic" HeaderText="Basic 60%" ReadOnly="true" DataFormatString="{0:N0}" />
<asp:BoundField DataField="HRA" HeaderText="HRA 30%" ReadOnly="true" DataFormatString="{0:N0}" />
<asp:BoundField DataField="OvertimeAllowance" HeaderText="OT 10%" ReadOnly="true" DataFormatString="{0:N0}" />
<asp:BoundField DataField="PF" HeaderText="PF" ReadOnly="true" DataFormatString="{0:N0}" />
<asp:BoundField DataField="ESI" HeaderText="ESI" ReadOnly="true" DataFormatString="{0:N0}" />
<asp:BoundField DataField="PT" HeaderText="PT" ReadOnly="true" DataFormatString="{0:N0}" />
<asp:TemplateField HeaderText="Advance"><ItemTemplate><asp:TextBox runat="server" ID="txtAdvance" Text='<%# Eval("SalaryAdvance") %>' Width="90" /></ItemTemplate></asp:TemplateField>
<asp:TemplateField HeaderText="Other Ded."><ItemTemplate><asp:TextBox runat="server" ID="txtOther" Text='<%# Eval("OtherDeductions") %>' Width="90" /></ItemTemplate></asp:TemplateField>
<asp:BoundField DataField="TotalDeductions" HeaderText="Total Ded." ReadOnly="true" DataFormatString="{0:N0}" />
<asp:BoundField DataField="NetPay" HeaderText="Net Pay" ReadOnly="true" DataFormatString="{0:N0}" />
</Columns></asp:GridView></div></div>
</asp:Content>
