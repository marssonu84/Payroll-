<%@ Page Language="C#" AutoEventWireup="true"
    CodeFile="Setup.aspx.cs" Inherits="Setup" %>

<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <title>Payroll Setup</title>
    <link href="Content/site.css" rel="stylesheet" />
</head>

<body>

<form id="setupForm" runat="server">

<main class="container">

    <div class="card">

        <h1>Payroll Database Setup</h1>

        <p>
            Creates or verifies the required payroll database tables and user-role security.
        </p>

        <asp:Button
            runat="server"
            ID="btnSetup"
            Text="Create / Verify Database"
            CssClass="btn"
            OnClick="btnSetup_Click" />

        &nbsp;

        <a href="Default.aspx" class="btn">
            ← Back to Dashboard
        </a>

        <br /><br />

        <asp:Label
            runat="server"
            ID="lblMsg" />

    </div>

</main>

</form>

</body>
</html>
</form>

</body>
</html>
