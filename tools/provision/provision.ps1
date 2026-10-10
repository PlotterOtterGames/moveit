<#
.SYNOPSIS
  Turns a stock Android TV box into a MoveIt box: installs the app, makes it the home screen, hides the stock
  launcher and unwanted apps, stops screensavers/sleep, names the box, and (if the box allows) installs the boot
  animation. Everything except the boot animation is reversible with -Undo.

.EXAMPLE
  .\provision.ps1 -Inspect                    # first look at a new box model: info, launchers, all packages -> logs\
  .\provision.ps1                             # provision the box connected over USB
  .\provision.ps1 -Device 192.168.1.50:5555   # over the network (enable ADB debugging in the box's developer options)
  .\provision.ps1 -BootAnimation              # also replace the boot animation (needs a box whose adb allows root)
  .\provision.ps1 -DeviceOwner                # make Tundu the device owner: silent app + firmware updates (see below)
  .\provision.ps1 -Undo                       # give the box back its normal Android launcher and apps

  -DeviceOwner needs a freshly reset box with no Google/other accounts. It can only be undone by a factory reset
  (Android's rule), so try it on a sample box first. See docs/cloud.md "Updates".
#>
param(
    [string]$Device = "",
    [string]$Apk = "",
    [switch]$Inspect,
    [switch]$BootAnimation,
    [switch]$DeviceOwner,
    [switch]$Undo,
    [switch]$NoReboot
)

$ErrorActionPreference = "Continue"
$Root = (Resolve-Path "$PSScriptRoot\..\..").Path
$Brand = Get-Content "$Root\unity\MoveItGames\Assets\Resources\brand.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$Package = $Brand.androidPackage
$HomeActivity = "$Package/com.unity3d.player.UnityPlayerActivity"
if (-not $Apk) { $Apk = "$Root\builds\android\$($Brand.name).apk" }
$Logs = "$PSScriptRoot\logs"
New-Item -ItemType Directory -Force $Logs | Out-Null

function Find-Adb {
    $onPath = Get-Command adb -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $roots = @("D:\Unity", "$env:ProgramFiles\Unity\Hub\Editor")
    foreach ($r in $roots) {
        $found = Get-ChildItem "$r\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    throw "adb not found. Install the Android module in Unity Hub, or Google's platform-tools."
}

$AdbExe = Find-Adb
$Target = @()
if ($Device) { $Target = @("-s", $Device) }

function Adb {
    $output = & $AdbExe @Target @args
    $script:AdbExit = $LASTEXITCODE
    return $output
}

function Shell([string]$command) { return (Adb shell $command) }

function Step([string]$text) { Write-Host "`n== $text" -ForegroundColor Cyan }

# ---------- connect ----------
if ($Device -match ":\d+$") { & $AdbExe connect $Device | Out-Host }
$state = (Adb get-state) 2>$null
if ($state -ne "device") {
    Write-Host "No box found. Connect it with USB (or pass -Device ip:5555) and enable ADB debugging." -ForegroundColor Red
    exit 1
}
$model = (Shell "getprop ro.product.model").Trim()
$android = (Shell "getprop ro.build.version.release").Trim()
$sdk = (Shell "getprop ro.build.version.sdk").Trim()
$platform = (Shell "getprop ro.board.platform").Trim()
$buildType = (Shell "getprop ro.build.type").Trim()
$serial = (Adb get-serialno).Trim() -replace '[^\w\.-]', '_'
Write-Host "Box: $model  |  Android $android (API $sdk)  |  chip $platform  |  build $buildType  |  $serial"
$disabledLog = "$Logs\disabled-$serial.txt"

function Get-HomePackages {
    $lines = Shell "cmd package query-activities --brief -a android.intent.action.MAIN -c android.intent.category.HOME"
    return $lines | Where-Object { $_ -match "/" } | ForEach-Object { ($_.Trim() -split "/")[0] } | Sort-Object -Unique
}

# ---------- inspect ----------
if ($Inspect) {
    $file = "$Logs\inspect-$serial-$(Get-Date -Format yyyyMMdd-HHmm).txt"
    "Box: $model | Android $android (API $sdk) | $platform | $buildType" | Out-File $file -Encoding utf8
    "`n# Home screen apps (launchers)" | Out-File $file -Append -Encoding utf8
    Get-HomePackages | Out-File $file -Append -Encoding utf8
    "`n# All packages" | Out-File $file -Append -Encoding utf8
    Shell "pm list packages -f" | Sort-Object | Out-File $file -Append -Encoding utf8
    "`n# Properties" | Out-File $file -Append -Encoding utf8
    Shell "getprop" | Out-File $file -Append -Encoding utf8
    Step "Launchers on this box"
    Get-HomePackages | ForEach-Object { Write-Host "  $_" }
    Write-Host "`nFull report: $file"
    Write-Host "Look for vendor OTA updaters, IPTV apps and anything unknown; add them to disable-packages.txt."
    exit 0
}

# ---------- undo ----------
if ($Undo) {
    Step "Re-enabling packages disabled by provisioning"
    if (Test-Path $disabledLog) {
        foreach ($pkg in Get-Content $disabledLog) {
            if ($pkg) { Shell "pm enable $pkg" | Out-Null; Write-Host "  enabled $pkg" }
        }
        Remove-Item $disabledLog
    } else {
        Write-Host "  no record for this box ($disabledLog)"
    }
    Shell "pm clear-package-preferred-activities $Package" | Out-Null
    Write-Host "Done. Press Home on the remote and pick the Android launcher (MoveIt stays installed)."
    exit 0
}

# ---------- install ----------
Step "Installing $Apk"
if (-not (Test-Path $Apk)) {
    Write-Host "APK not found. Build it in Unity: MoveIt > Box > Build Android APK" -ForegroundColor Red
    exit 1
}
Adb install -r -g $Apk | Out-Host   # -g grants runtime permissions (camera) up front
if ($AdbExit -ne 0) { Write-Host "Install failed." -ForegroundColor Red; exit 1 }

# ---------- home screen ----------
Step "Making $($Brand.name) the home screen"
$result = Shell "cmd package set-home-activity $HomeActivity"
Write-Host "  $result"
if ($result -notmatch "Success") {
    Write-Host "  Could not set it automatically: after reboot press Home on the remote, pick $($Brand.name), then 'Always'." -ForegroundColor Yellow
}

Step "Hiding the stock launcher and unwanted apps"
$toDisable = @()
foreach ($pkg in Get-HomePackages) {
    # com.android.settings provides FallbackHome, which Android needs while booting: never disable it.
    if ($pkg -ne $Package -and $pkg -ne "com.android.settings" -and $pkg -ne "com.android.tv.settings") { $toDisable += $pkg }
}
$listFile = "$PSScriptRoot\disable-packages.txt"
$installed = Shell "pm list packages" | ForEach-Object { $_ -replace "^package:", "" }
foreach ($line in Get-Content $listFile) {
    $pkg = $line.Trim()
    if ($pkg -and -not $pkg.StartsWith("#") -and ($installed -contains $pkg)) { $toDisable += $pkg }
}
foreach ($pkg in ($toDisable | Sort-Object -Unique)) {
    $r = Shell "pm disable-user --user 0 $pkg"
    Write-Host "  $pkg -> $r"
    if ((Test-Path $disabledLog) -and (Get-Content $disabledLog) -contains $pkg) { continue }
    Add-Content $disabledLog $pkg
}

Step "Box settings (no screensaver, never sleep, device name)"
Shell "settings put secure screensaver_enabled 0" | Out-Null
Shell "settings put secure screensaver_activate_on_sleep 0" | Out-Null
Shell "settings put secure screensaver_activate_on_dock 0" | Out-Null
Shell "settings put system screen_off_timeout 2147483647" | Out-Null
Shell "settings put global stay_on_while_plugged_in 7" | Out-Null
Shell "settings put global device_name '$($Brand.name)'" | Out-Null
Shell "settings put global auto_time 1" | Out-Null
Write-Host "  done"

# ---------- boot animation (optional, needs root adb) ----------
if ($BootAnimation) {
    Step "Boot animation"
    $zip = "$Root\tools\branding\out\bootanimation.zip"
    if (-not (Test-Path $zip)) {
        Write-Host "  $zip missing: run tools\branding\make_branding.py first" -ForegroundColor Yellow
    } else {
        Adb root | Out-Host
        Start-Sleep -Seconds 3
        Adb wait-for-device | Out-Null
        Adb remount | Out-Host
        $placed = $false
        foreach ($dir in @("/oem/media", "/product/media", "/system/media")) {
            $exists = Shell "ls $dir/bootanimation.zip 2>/dev/null"
            if ($exists) {
                Adb push $zip "$dir/bootanimation.zip" | Out-Host
                if ($AdbExit -eq 0) {
                    Shell "chmod 644 $dir/bootanimation.zip" | Out-Null
                    Write-Host "  installed to $dir" ; $placed = $true ; break
                }
            }
        }
        if (-not $placed) {
            Write-Host "  This box does not allow it (no root/remount). Ask the ODM to put bootanimation.zip in the firmware." -ForegroundColor Yellow
        }
    }
}

# ---------- device owner (optional): silent updates ----------
if ($DeviceOwner) {
    Step "Making $($Brand.name) the device owner (silent app and firmware updates)"
    $features = Shell "pm list features"
    if (-not ($features -match "android.software.device_admin")) {
        Write-Host "  This box has no device-admin support (android.software.device_admin): updates will ask for OK on the remote." -ForegroundColor Yellow
    } else {
        $admin = "$Package/al.moveit.cloud.AdminReceiver"
        $result = Shell "dpm set-device-owner $admin"
        Write-Host "  $result"
        if ($result -match "Success") {
            Write-Host "  Done. Only a factory reset removes it." -ForegroundColor Green
        } else {
            Write-Host "  Not set. Usual causes: an account on the box (remove it, or factory reset), or a second user." -ForegroundColor Yellow
        }
    }
}

# ---------- finish ----------
"$(Get-Date -Format s)  $serial  $model  Android $android  provisioned" | Add-Content "$Logs\provisioned.txt"
if (-not $NoReboot) {
    Step "Rebooting: the box should come up straight into $($Brand.name)"
    Adb reboot | Out-Null
}
Write-Host "`nDone." -ForegroundColor Green
