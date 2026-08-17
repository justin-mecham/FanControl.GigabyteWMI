# FanControl.GigabyteWMI

A **[FanControl](https://github.com/Rem0o/FanControl.Releases)** plugin providing fan control, RPM monitoring, and temperature sensing for **Gigabyte laptops** via ACPI WMI (`GB_WMIACPI`).

![Target Framework](https://img.shields.io/badge/.NET%20Framework-4.8-blue)
![License](https://img.shields.io/badge/license-MIT-green)

---

## 💻 Hardware Compatibility

### ✅ Verified Test Machine (Only Known Good)

This plugin was developed and tested on:

| Model | CPU | GPU | BIOS | OS |
| :--- | :--- | :--- | :--- | :--- |
| **GIGABYTE GAMING A16 CVH** | Intel Core i7-13620H | NVIDIA GeForce RTX 5060 Laptop | FB05 | Windows 11 Home |

### ⚠️ Compatibility Notice

> [!WARNING]
> **Compatibility with other Gigabyte models is UNTESTED and UNCONFIRMED.**
> 
> Different Gigabyte laptop lines and BIOS revisions often use varying Embedded Controller (EC) register layouts and WMI behaviors. While other models might expose the `GB_WMIACPI_Set` namespace, there is **no guarantee** this plugin will function correctly or safely on them. **Use at your own risk.**

#### Possible (Untested) Candidates:
Models that also feature the `GB_WMIACPI_Set` interface may include:
- **Gigabyte AORUS** (e.g., 15G, 15P, 17G, 17H, 15/17 BSF/XF/etc.)
- **Gigabyte Aero** (e.g., 15, 16, 17 OLED / HDR series)
- **Gigabyte Gaming** (e.g., G5 / G7 / A5 / A7 series)

To check if your laptop exposes the basic WMI interface, run in an Administrator PowerShell (no plugins required):
```powershell
Get-WmiObject -Namespace "root\WMI" -Class "GB_WMIACPI_Set"
```

**Expected output on compatible hardware:**
```text
__GENUS          : 2
__CLASS          : GB_WMIACPI_Set
__SUPERCLASS     : 
__DYNASTY        : GB_WMIACPI_Set
__RELPATH        : GB_WMIACPI_Set.InstanceName="ACPI\\PNP0C14\\DCK_0"
__PROPERTY_COUNT : 2
__DERIVATION     : {}
__SERVER         : YOUR-PC
__NAMESPACE      : root\WMI
__PATH           : \\YOUR-PC\root\WMI:GB_WMIACPI_Set.InstanceName="ACPI\\PNP0C14\\DCK_0"
Active           : True
InstanceName     : ACPI\PNP0C14\DCK_0
PSComputerName   : YOUR-PC
```

---

## 🚀 Features

- **CPU & GPU Fan Speed Control**: Manual percentage control mapped to Gigabyte's 16-step hardware duty register table.
- **Fan RPM Monitoring**: Real-time RPM telemetry for both CPU (`getRpm1`) and GPU (`getRpm2`) fans.
- **Temperature Sensing**: CPU and GPU temperature sensors reported directly by ACPI WMI.
- **Automatic Failsafe**: Restores fans to automatic hardware EC control on exit or shutdown.
- **Non-Blocking UI**: Asynchronous background dispatch prevents FanControl UI lockups during slider adjustments.

---

## 🛠️ Building & Installation

> [!NOTE]
> Pre-compiled binary releases are not distributed. Please build from source using the .NET SDK.

### 1. Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) (or .NET Framework 4.8 targeting pack / Visual Studio 2022).

### 2. Build from Source
```bash
git clone https://github.com/justin-mecham/FanControl.GigabyteWMI.git
cd FanControl.GigabyteWMI
dotnet build -c Release
```

The compiled binary will be located at:  
`bin/Release/net48/FanControl.GigabyteWMI.dll`

### 3. Install into FanControl
1. Close **FanControl** completely.
2. Copy `bin/Release/net48/FanControl.GigabyteWMI.dll` into the `Plugins/` folder inside your FanControl installation directory:
   ```text
   FanControl/
   ├── Plugins/
   │   └── FanControl.GigabyteWMI.dll
   └── FanControl.exe
   ```
3. (If needed) Right-click `FanControl.GigabyteWMI.dll` -> **Properties** -> Check **Unblock** -> **OK**.
4. Start **FanControl**. The new Gigabyte fan controls and sensors will appear automatically.

---

## 🔬 How It Works

The plugin interfaces directly with Gigabyte's ACPI WMI namespace (`root\WMI` -> `GB_WMIACPI_Set` / `GB_WMIACPI_Get`):

- **16-Step Hardware Duty Table**: Maps 0%-100% duty cycles to Gigabyte's hardware register values extracted from `ComData.dll`:
  `[57, 68, 80, 91, 103, 114, 125, 137, 148, 160, 171, 183, 194, 206, 217, 229]`
- **Mode Switching**: Toggles hardware `SetFixedFanStatus`, `SetStepFanStatus`, `SetAutoFanStatus`, `SetGPUFanDuty`, and `SetWhisperMode` appropriate for requested fan curve targets.

---

## ⚠️ Disclaimer

This plugin is an independent open-source tool and is not affiliated with or endorsed by Gigabyte Technology Co., Ltd.  
Modifying fan profiles carries inherent hardware risks. Ensure proper cooling curves are set to prevent thermal throttling or overheating.

---

## 📄 License

Distributed under the [MIT License](LICENSE).
