<%@ Page Language="C#" AutoEventWireup="true" Inherits="PayrollWebApp.Login" %>

<!DOCTYPE html>
<html>
<head runat="server">

    <title>Login - Salary Management System</title>

    <meta name="viewport"
          content="width=device-width, initial-scale=1.0" />

    <style>

        body {
            font-family: Arial, sans-serif;
            background-color: #f4f7f6;
            display: flex;
            justify-content: center;
            align-items: center;
            min-height: 100vh;
            margin: 0;
        }

        .login-container {
            background-color: #ffffff;
            padding: 30px;
            border-radius: 8px;
            box-shadow: 0 4px 12px rgba(0,0,0,0.12);
            width: 90%;
            max-width: 340px;
            box-sizing: border-box;
        }

        .login-container h2 {
            text-align: center;
            color: #333333;
            margin-top: 0;
            margin-bottom: 25px;
        }

        .form-group {
            margin-bottom: 18px;
        }

        .form-group label {
            display: block;
            margin-bottom: 6px;
            color: #555555;
        }

        .form-group input[type="text"],
        .form-group input[type="password"] {
            width: 100%;
            padding: 11px;
            border: 1px solid #cccccc;
            border-radius: 4px;
            box-sizing: border-box;
            font-size: 16px;
        }

        .login-button {
            width: 100%;
            padding: 11px;
            background-color: #007bff;
            color: #ffffff;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            font-size: 16px;
        }

        .login-button:hover {
            background-color: #0056b3;
        }

        .error-message {
            color: #c62828;
            text-align: center;
            display: block;
            margin-top: 12px;
        }

        .success-message {
            color: #1b7f3a;
            text-align: center;
            display: block;
            margin-top: 12px;
        }

    </style>

</head>

<body>

    <form id="loginForm" runat="server">

        <div class="login-container">

            <h2>Login</h2>

            <div class="form-group">

                <label for="txtUsername">
                    Username
                </label>

                <input type="text"
                       id="txtUsername"
                       runat="server"
                       autocomplete="username" />

            </div>

            <div class="form-group">

                <label for="txtPassword">
                    Password
                </label>

                <input type="password"
                       id="txtPassword"
                       runat="server"
                       autocomplete="current-password" />

            </div>

            <div class="form-group">

                <asp:Button
                    ID="loginSubmitButton"
                    runat="server"
                    Text="Login"
                    CssClass="login-button"
                    OnClick="btnLogin_Click" />

            </div>

            <asp:Label
                ID="lblSuccess"
                runat="server"
                CssClass="success-message" />

            <asp:Label
                ID="lblError"
                runat="server"
                CssClass="error-message" />

        </div>

    </form>

</body>
</html>
