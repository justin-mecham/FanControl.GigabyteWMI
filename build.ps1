param(
    [switch]$NoTest,
    [switch]$LiveTest,
    [switch]$NoDeploy,
    [switch]$Launch
)

$ErrorActionPreference = "Stop"

Write-Host "`n========================================================" -ForegroundColor Cyan
Write-Host "   FanControl.GigabyteWMI - Build, Test & Deploy       " -ForegroundColor Cyan
Write-Host "========================================================`n" -ForegroundColor Cyan

$rootDir = $PSScriptRoot
Set-Location $rootDir

# 0. Ensure previous test & FanControl processes are stopped FIRST so they do not lock DLL files
Stop-Process -Name "TestHardware" -Force -ErrorAction SilentlyContinue
$wasRunning = $false
$fcProcesses = Get-Process -Name "FanControl" -ErrorAction SilentlyContinue
if ($fcProcesses) {
    Write-Host "[0/4] Stopping active FanControl process..." -ForegroundColor Yellow
    Stop-Process -Name "FanControl" -Force -ErrorAction SilentlyContinue
    $wasRunning = $true
    Start-Sleep -Seconds 2
}

# 1. Restore & Build Solution
Write-Host "[1/4] Building solution in Release configuration..." -ForegroundColor Yellow
dotnet build "$rootDir\FanControl.GigabyteWMI.sln" --configuration Release --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build succeeded!`n" -ForegroundColor Green

# 2. Run Unit Tests
if (-not $NoTest) {
    Write-Host "[2/4] Running xUnit test suite..." -ForegroundColor Yellow
    dotnet test "$rootDir\FanControl.GigabyteWMI.sln" --configuration Release --no-build --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Tests failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "All unit tests passed!`n" -ForegroundColor Green
} else {
    Write-Host "[2/4] Skipping unit tests (-NoTest specified).`n" -ForegroundColor DarkGray
}

# 3. Optional Live Hardware Diagnostic (runs with exclusive hardware access)
if ($LiveTest) {
    Write-Host "[*] Executing Live Hardware Diagnostic (Exclusive Access)..." -ForegroundColor Yellow
    dotnet run --project "$rootDir\tools\TestHardware\TestHardware.csproj" --configuration Release --no-build
    Write-Host ""
}

# 4. Deploy Plugin & Fix Configs
if (-not $NoDeploy) {
    Write-Host "[3/4] Deploying plugin & optimizing FanControl configs..." -ForegroundColor Yellow
    $pluginDir = "C:\Program Files (x86)\FanControl\Plugins"
    $targetDll = Join-Path $pluginDir "FanControl.GigabyteWMI.dll"
    $sourceDll = Join-Path $rootDir "bin\Release\net48\FanControl.GigabyteWMI.dll"

    if (Test-Path $pluginDir) {
        # Robust copy with retry loop
        $copied = $false
        for ($attempt = 1; $attempt -le 5; $attempt++) {
            try {
                Copy-Item -Path $sourceDll -Destination $targetDll -Force -ErrorAction Stop
                Unblock-File -Path $targetDll
                $copied = $true
                break
            } catch {
                Write-Host "  -> File busy, waiting for lock release (attempt $attempt/5)..." -ForegroundColor Yellow
                Start-Sleep -Seconds 2
            }
        }

        if ($copied) {
            Write-Host "  -> Successfully deployed to: $targetDll" -ForegroundColor Green

            # Automatically fix crashing desktop NvAPIWrapper in FanControl config files
            $cfgDir = "C:\Program Files (x86)\FanControl\Configurations"
            if (Test-Path $cfgDir) {
                Get-ChildItem -Path $cfgDir -Filter "*.json" | ForEach-Object {
                    try {
                        $raw = Get-Content $_.FullName -Raw
                        if ($raw -match '"NvAPIWrapperSettings"') {
                            $cfgJson = $raw | ConvertFrom-Json
                            if ($cfgJson.Sensors.NvAPIWrapperSettings.Enabled) {
                                $cfgJson.Sensors.NvAPIWrapperSettings.Enabled = $false
                                $cfgJson | ConvertTo-Json -Depth 15 | Set-Content $_.FullName -Force
                                Write-Host "  -> Disabled crashing desktop NvAPI provider in: $($_.Name)" -ForegroundColor Green
                            }
                        }
                    } catch { }
                }
            }

            if ($Launch -or $wasRunning) {
                Write-Host "[4/4] Launching FanControl..." -ForegroundColor Yellow
                Start-Process "C:\Program Files (x86)\FanControl\FanControl.exe"
                Write-Host "FanControl started!`n" -ForegroundColor Green
            } else {
                Write-Host "[4/4] Deployment complete! Launch FanControl when ready.`n" -ForegroundColor Green
            }
        } else {
            Write-Host "  [!] Failed to copy DLL after 5 attempts. Make sure FanControl is closed." -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "  [!] FanControl plugins directory not found at $pluginDir" -ForegroundColor Red
        Write-Host "      Built DLL is located at: $sourceDll`n" -ForegroundColor Gray
    }
} else {
    Write-Host "[3/4] Skipping deployment (-NoDeploy specified).`n" -ForegroundColor DarkGray
}

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "                      DONE!                             " -ForegroundColor Cyan
Write-Host "========================================================`n" -ForegroundColor Cyan
