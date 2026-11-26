SUPER USER OFFLINE RESET TOOL (V14)
===================================
Keep this folder OFF the web server. Run it on a trusted PC that can reach the SQL Server.

1. Double-click build.bat (creates SuperAdminReset.exe).
2. Typical use (random temporary password, also fixes lockout / lost authenticator):
     SuperAdminReset.exe --config "C:\path\to\Database.config" --generate --reactivate --disable-2fa
   Without --config it asks for the connection string (input hidden).
   Add --user <name> to skip the account list, --dry-run to preview.
3. Sign in with the temporary password and change it at once.
4. Delete the exe if you do not need it; do not leave passwords in shell history (avoid --conn).

If the database uses an ENC: connection string made on the web server, the tool cannot decrypt it on
another PC - give it the plain connection string at the prompt instead.
