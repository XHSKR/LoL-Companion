# LoL-Companion
Ultimate tool for your League of Legends Client

![](screenshot1.png)
![](screenshot2.png)
![](screenshot3.png)
![](screenshot4.png)
## Features
Duplicate client, Import and Export Item sets, Auto Login, Auto Ready-Check, Automatic Report, Position Call out, Champion Instant Lock & Ban, Chat Macro, Spell Tracker, Profile Background Changer, and so on.

## Account list and auto login
Edit `credentials.conf` beside the application executable. The first line is a comma-separated
list of Riot login usernames; the second line is their shared password (the original file format).
Select an account on the League tab, then choose **Log-in (KO)** or **Log-in (EN)**.
The account list is reloaded when logging in, and the selected account is remembered in `settings.ini`.
**Start League** launches League using the Riot Client's current session without switching accounts.

Login uses the Riot Client's local authentication and session APIs. If Riot requests CAPTCHA,
Companion falls back to filling the official Sign-in form so the client can run its normal
verification flow. The text field is verified with a non-secret marker before typing, the
clipboard is restored, and the password is never copied to it. Keep the Riot window focused
during this short input step. If CAPTCHA, MFA, or acceptance of terms is displayed, complete it
in the official Riot Client; Companion waits for the
selected account to be authorized before launching League. **Cancel Login** stops waiting.
Account switching closes the existing League client and is unavailable while a game is running.
Existing lifecycle sessions are updated instead of deleted and recreated. A creation conflict
(HTTP 409) is recovered by reading the session again and updating it; credential requests are
never automatically retried. Sign-out also respects the Riot Client's permission for other games.

Regression tests are in `tests/Run-AccountLoginTests.ps1`. Pass a C# compiler path, a .NET Framework
4.7.2 reference assembly directory, and the built app directory. `-LiveReadOnly` additionally checks
the running Riot Client and its API schema without logging out or submitting credentials.

## Dependencies
HtmlAgilityPack.1.11.43

InputSimulatorPlus.1.0.7

MaterialSkin.Updated.0.2.2

Newtonsoft.Json.13.0.1

WebSocketSharp-netstandard.1.0.1
