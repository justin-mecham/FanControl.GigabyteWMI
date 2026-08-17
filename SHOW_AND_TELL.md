# FanControl Show and Tell Announcement Draft

**Category:** Show and tell  
**Title:** `[Plugin] FanControl.GigabyteWMI - ACPI Fan Control & Sensors for Gigabyte Laptops`

---

### What is this?
While ASUS laptops have `FanControl.AsusWMI` and Dell laptops have `FanControl.DellPlugin`, Gigabyte laptop users have historically had no native FanControl plugin and were stuck relying on the bulky Gigabyte Control Center (GCC) software.

**FanControl.GigabyteWMI** is a lightweight, open-source plugin that interfaces directly with Gigabyte's ACPI WMI layer (`GB_WMIACPI`) to bring native CPU/GPU fan speed control, live RPM telemetry, and temperature monitoring directly into FanControl without needing GCC running.

**GitHub Repository:** https://github.com/justin-mecham/FanControl.GigabyteWMI

---

### 🚀 Features
- **CPU & GPU Fan Speed Control**: Full manual percentage control mapped to Gigabyte's 16-step hardware duty register table.
- **Live Telemetry**: Real-time RPM telemetry (`getRpm1`, `getRpm2`) and CPU/GPU temperature sensors (`getCpuTemp`, `getGpuTemp1`).
- **Smooth & Non-Blocking**: Asynchronous background dispatch worker coalesces slider updates to guarantee 0ms UI latency with no slider lag or UI freezing.
- **Automatic Hardware Failsafe**: Reverts fans to automatic hardware EC control on application exit or shutdown.

---

### 🔬 Technical Details & Architecture

1. **Hardware Register Mapping**:
   Gigabyte's Embedded Controller (EC) expects raw hardware duty speeds rather than linear percentages. The plugin maps 0%–100% duty targets to the official 16-step hardware table extracted from `ComData.dll`:
   ```csharp
   private static readonly byte[] DutyTable = new byte[] {
       57, 68, 80, 91, 103, 114, 125, 137, 148, 160, 171, 183, 194, 206, 217, 229
   };
   ```
   - `0% - 10%`: Whisper quiet mode (`SetWhisperMode(1)`)
   - `11% - 97%`: Manual fixed step control (`SetFixedFanSpeed`, `SetGPUFanDuty`)
   - `98% - 100%`: Full Turbo mode (register `229` / `100%` -> 7500 RPM)

2. **Asynchronous Worker Threading**:
   FanControl's UI thread fires rapid `Set()` updates while dragging sliders. To prevent ACPI bus lockups or UI freezes, slider events simply signal an in-memory queue, and a dedicated background thread coalesces and applies the settled values to WMI.

---

### 💻 Hardware Compatibility Status

#### ✅ Confirmed Working Hardware
- **Model:** `GIGABYTE GAMING A16 CVH`
- **Specs:** Intel Core i7-13620H, NVIDIA GeForce RTX 5060 Laptop GPU, BIOS FB05, Windows 11 Home

#### ⚠️ Other Gigabyte Models (Untested / Unknown)
Compatibility with other Gigabyte laptop lines (AORUS 15/17, Aero 15/16, G5/G7, A5/A7) is currently **untested and unconfirmed**. Because different chassis revisions and BIOSes may use varying EC register offsets, testing is welcome.

To check if your laptop's BIOS exposes the required WMI class, run in an Administrator PowerShell (no plugins required):
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

### 🔮 Future Roadmap & Capabilities
Investigation of the `GB_WMIACPI` namespace revealed several additional supported capabilities that could be expanded:
- **3-Fan & 4-Fan Chassis Support**: Dynamic auto-detection of `getRpm3` / `getRpm4` and `SetFixedFan3Duty` / `SetFixedFan4Duty` on larger AORUS chassis.
- **Battery Temperature Telemetry**: Exposing `GetBatteryTemperature` as a sensor card.
- **Upstream LibreHardwareMonitor Integration**: Packaging this WMI driver for LibreHardwareMonitor so Gigabyte laptop support can eventually be built directly into core FanControl.

---

### 🛠️ Building & Installing

Pre-compiled binary releases are not distributed. You can build from source in seconds with the .NET SDK:

```bash
git clone https://github.com/justin-mecham/FanControl.GigabyteWMI.git
cd FanControl.GigabyteWMI
dotnet build -c Release
```

1. Close **FanControl** completely.
2. Copy `bin/Release/net48/FanControl.GigabyteWMI.dll` into your `FanControl/Plugins/` directory.
3. (If prompted) Right-click the `.dll` -> **Properties** -> Check **Unblock** -> **OK**.
4. Start **FanControl**. The new Gigabyte fan controls and sensors will appear automatically.

---

Feedback, pull requests, and hardware test reports for other Gigabyte laptop models are very welcome on the [GitHub Repository](https://github.com/justin-mecham/FanControl.GigabyteWMI)!
