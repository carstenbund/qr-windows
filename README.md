# QR Code (Windows)

Windows version of the Mac QRCode app. It asks for text or a URL, creates a
QR code, saves it as `qrcode.png` in the temp folder (`%TEMP%`) and opens it
in the default image viewer.

- Open `QRCodeWin.sln` in Visual Studio 2017, then Build > Build Solution.
- Requires the .NET Framework 4.6.1 targeting pack (included in the
  ".NET desktop development" workload).
- No NuGet packages and no Python needed: the QR encoder is in `QrCode.cs`.
- Output: `bin\Release\QRCode.exe` is a single file and can be copied anywhere.

Code settings (same as the Mac version): error correction L, byte mode
(UTF-8, no ECI header), 4-module white border, 10 px per module.
Tick "Large (for print)" for 40 px per module.
