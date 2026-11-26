# Changelog

## V14.5 – Super Admin authenticator-protected recovery
**Recovery security**
- Web recovery remains enabled but now requires the existing PBKDF2 recovery key plus a valid authenticator TOTP code or one unused backup code for the selected Super User.
- Authenticator must be enrolled in advance through `TwoStepSetup.aspx` while signed in; the public recovery page cannot enroll a new device.
- Removed the public recovery option to disable two-step sign-in.
- Preserved the existing recovery-key hash, HTTPS gate, session/ViewState binding, and database-backed throttling; added account-specific recovery throttling.
- Cloud A and Cloud B remain separate databases; enroll and test each deployment independently.
- Existing TOTP secret storage format is unchanged (plaintext in the Users table); see `VERSION_14.5_SUPERADMIN_AUTHENTICATOR_RECOVERY.txt` for the limitation and deployment steps.
- Full .NET Framework compilation and live hosting tests were not available; static package checks passed.

## V14 – Security hardening and rule-driven payroll
**Security**
- DB-backed login throttling (`LoginThrottle.cs`, table `LoginAttempts`): 5 failures / 15 min locks a username; IP limit 20. Applied in `Login.aspx.cs`.
- Web.config: security headers (HSTS, nosniff, X-Frame-Options, Referrer-Policy, Permissions-Policy, report-only CSP), forms cookie `requireSSL`, version header off, 10 MB request cap. Optional HTTPS redirect included (commented).
- CSRF: `ViewStateUserKey` set in `Site.Master` for signed-in pages.
- Database Manager SQL console is SELECT-only by default (`SqlConsoleReadOnly`).
- Removed SQL login name from `VERSION_13.txt`.

**Recovery**
- `SuperAdminReset.aspx` rewritten for V14: database-backed throttling (`LoginThrottle`), `CompleteRequest` instead of `Response.End`, clears lockouts, optional "turn off two-step", expiry/IP/HTTPS gates kept. Ships disabled.
- `Offline_Tools/SuperAdminReset`: console tool rewritten with `build.bat`, `--disable-2fa`, automatic lockout clearing, tolerance for missing optional columns, safer connection-string handling.

**Payroll**
- New `StatutoryRules` table and admin page (`StatutoryRules.aspx`): PF, ESI, PT and Basic % are effective-dated and can be overridden per entity. Defaults equal the V13 hard-coded values, so results do not change until a rule is added.
- `PayrollCalculator` keeps its V13 signature; new overload accepts rules. `Payroll.aspx.cs` (processing and template build) now passes the selected payroll month, so past months use the rates in force at that time.
- `Tests/CalculatorTests.cs`: hand-verified cases plus a regression grid against the V13 formulas.

## V13.x and earlier
See the original `VERSION_*.txt` files.
