<#
.SYNOPSIS
  Dev loop for testing the box software without a box: installs the latest APK into the MuMu Player emulator (or
  any adb device), grants camera permission, starts it, and streams its logs.

  Build first in Unity: MoveIt > Box > Build Android APK.
  MuMu's camera must use the laptop webcam: MuMu > Settings > Device/Camera.

.EXAMPLE
  .\run-on-mumu.ps1                    # MuMu Player 12 (adb port 16384)
  .\run-on-mumu.ps1 -Device 127.0.0.1:7555
  .\run-on-mumu.ps1 -NoLogs            # install + start only
#>
param(
    [string]$Device = "127.0.0.1:16384",
    [switch]$NoLogs
)

$ErrorActionPreference = "Continue"
$Root = (Resolve-Path "$PSScriptRoot\..\..").Path
$Brand = Get-Content "$Root\unity\MoveItGames\Assets\Resources\brand.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$Apk = "$Root\builds\android\$($Brand.name).apk"
$Activity = "$($Brand.androidPackage)/com.unity3d.player.UnityPlayerActivity"

$Adb = (Get-Command adb -ErrorAction SilentlyContinue).Source
if (-not $Adb) {
    $Adb = (Get-ChildItem "D:\Unity\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
}
if (-not $Adb) { Write-Host "adb not found (install Unity's Android module)." -ForegroundColor Red; exit 1 }
if (-not (Test-Path $Apk)) { Write-Host "No APK at $Apk. Build it in Unity: MoveIt > Box > Build Android APK" -ForegroundColor Red; exit 1 }

& $Adb connect $Device | Out-Host
if ((& $Adb -s $Device get-state 2>$null) -ne "device") {
    Write-Host "Can't reach $Device. Is MuMu running? (MuMu 12 uses 127.0.0.1:16384, older versions 127.0.0.1:7555)" -ForegroundColor Red
    exit 1
}

Write-Host "Installing $Apk ..." -ForegroundColor Cyan
& $Adb -s $Device install -r -g $Apk | Out-Host   # -g: grant the camera permission up front
& $Adb -s $Device shell am force-stop $Brand.androidPackage | Out-Null
& $Adb -s $Device logcat -c | Out-Null
& $Adb -s $Device shell am start -n $Activity | Out-Host

if (-not $NoLogs) {
    Write-Host "Logs (Ctrl+C to stop): motion service + Unity" -ForegroundColor Cyan
    & $Adb -s $Device logcat -v time MoveItMotion:V Unity:I '*:S'
}
