# Gigabyte FanToggle Utility

A standalone, zero-dependency tray utility for Gigabyte Gaming/AERO/AORUS laptops that instantly toggles fans between **Silent / Whisper Eco Mode** (~1600 RPM) and **100% MAX Turbo Mode** (7500 RPM) via WMI ACPI.

## Features
- **One-Click Toggle**: Switch between whisper quiet operation and maximum cooling performance.
- **Windows Notification Banner**: Displays current CPU temperature and live tachometer RPM on toggle.
- **Zero Overhead**: Does not stay running in the background; sets the hardware registers and immediately exits.

## How to Compile

Run the following command in PowerShell:

```powershell
Add-Type -AssemblyName System.Windows.Forms
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /target:winexe /out:FanToggle.exe /r:System.Windows.Forms.dll,System.Drawing.dll,System.Management.dll FanToggle.cs
```
