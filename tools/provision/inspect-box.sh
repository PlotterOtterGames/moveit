# A new box model's hardware, security and network facts, for docs/hardware.md and docs/box-os.md. Read-only.
# Runs on the box (Android's sh + toybox, which refuses Windows line endings); provision.ps1 -Inspect pushes a copy with
# Unix line endings, runs it and saves the output in logs/. By hand, with a file that has LF line endings:
#   adb push tools/provision/inspect-box.sh /data/local/tmp/ && adb shell sh /data/local/tmp/inspect-box.sh

section() { echo; echo "# $1"; }
prop() { for p in "$@"; do echo "$p=$(getprop "$p")"; done; }

section "Identity"
prop ro.product.manufacturer ro.product.model ro.product.device ro.board.platform ro.hardware \
     ro.build.version.release ro.build.version.sdk ro.product.cpu.abilist ro.build.display.id ro.build.date \
     ro.build.fingerprint ro.build.version.security_patch
echo "kernel=$(uname -r)"

section "Security"
prop ro.build.type ro.build.tags ro.debuggable ro.secure ro.adb.secure ro.boot.verifiedbootstate ro.boot.flash.locked
echo "selinux=$(getenforce 2>/dev/null)"
for f in /system/bin/su /system/xbin/su /sbin/su /vendor/bin/su; do [ -e "$f" ] && ls -l "$f"; done
dumpsys account 2>/dev/null | grep -E "Accounts:|Account \{" | head -5
pm list users 2>/dev/null | grep UserInfo
echo "device admin feature: $(pm list features | grep -c android.software.device_admin)"
dumpsys device_policy 2>/dev/null | grep -iE "Device Owner|Profile Owner" | head -3

section "CPU, memory, storage"
grep -E "^Hardware|^CPU part" /proc/cpuinfo | sort | uniq -c
for c in /sys/devices/system/cpu/cpu[0-9]*; do
  f=$c/cpufreq
  [ -d "$f" ] && echo "$(basename $c): $(cat $f/cpuinfo_min_freq)-$(cat $f/cpuinfo_max_freq) kHz, now $(cat $f/scaling_cur_freq), $(cat $f/scaling_governor)"
done
grep -E "MemTotal|MemAvailable|SwapTotal" /proc/meminfo
df -h /data | tail -1

section "GPU"
dumpsys SurfaceFlinger 2>/dev/null | grep -E "^GLES" | head -2
prop ro.hardware.egl ro.hardware.vulkan ro.opengles.version
pm list features | grep -iE "vulkan|opengles"
for d in /sys/class/devfreq/*; do
  echo "$(basename $d): $(cat $d/available_frequencies 2>/dev/null) | now $(cat $d/cur_freq 2>/dev/null), $(cat $d/governor 2>/dev/null)"
done

section "Display"
wm size
wm density
dumpsys display 2>/dev/null | grep -E "DisplayDeviceInfo\{" | head -2
for c in /sys/class/drm/card*-HDMI*; do
  [ -d "$c" ] && echo "$(basename $c): $(cat $c/status), modes: $(head -12 $c/modes 2>/dev/null | tr '\n' ' ')"
done
getprop | grep -iE "resolution|hdmi|framebuffer" | head -8
dumpsys hdmi_control 2>/dev/null | grep -iE "hdmi_cec_enabled|mPowerStatus|mProhibitMode" | head -4

section "USB cameras (CameraX needs the kernel's UVC driver and a camera HAL that lists USB cameras)"
if [ -d /sys/bus/usb/drivers/uvcvideo ]; then echo "kernel UVC driver: yes"; else echo "kernel UVC driver: NOT FOUND"; fi
echo "USB drivers: $(ls /sys/bus/usb/drivers 2>/dev/null | tr '\n' ' ')"
pm list features | grep -i camera
for v in /sys/class/video4linux/*; do [ -e "$v/name" ] && echo "$(basename $v): $(cat $v/name)"; done
dumpsys media.camera 2>/dev/null | grep -iE "Number of camera devices|Camera module|provider|external" | head -8
lshal 2>/dev/null | grep -i camera | head -5
ls /vendor/lib64/hw /vendor/lib/hw 2>/dev/null | grep -i camera | sort -u
ls /vendor/etc 2>/dev/null | grep -iE "camera|external"
[ -r /vendor/etc/external_camera_config.xml ] && echo "USB camera fps limits: $(grep -oE 'width="[0-9]+" height="[0-9]+" fpsBound="[0-9.]+"' /vendor/etc/external_camera_config.xml | sed -E 's/width="([0-9]+)" height="([0-9]+)" fpsBound="([0-9.]+)"/\1x\2@\3/' | tr '\n' ' ')"
getprop | grep -i camera | head -10
echo "USB devices now:"; lsusb 2>/dev/null

section "NPU"
ls -l /dev/rknpu* /dev/dri 2>/dev/null
ls /sys/class/devfreq 2>/dev/null | grep -i npu
ls /vendor/lib64 /vendor/lib /system/lib64 2>/dev/null | grep -iE "rknn|rknpu" | sort -u
[ -r /vendor/lib64/librknnrt.so ] && grep -aoE "librknnrt version: [^)]*\)?" /vendor/lib64/librknnrt.so | head -1
cat /sys/kernel/debug/rknpu/version 2>/dev/null
dmesg 2>/dev/null | grep -iE "rknpu" | head -5
getprop | grep -iE "npu|rknn" | head -5

section "Temperatures (millidegrees C)"
for z in /sys/class/thermal/thermal_zone*; do echo "$(basename $z) $(cat $z/type): $(cat $z/temp)"; done

section "Input devices (the remote is one of them)"
for i in /sys/class/input/input*; do [ -e "$i/name" ] && echo "$(basename $i): $(cat $i/name)"; done

section "Network and clock"
ip -4 -o addr show 2>/dev/null | grep -v " lo "
dumpsys wifi 2>/dev/null | grep -m1 "mWifiInfo" | sed -e 's/SSID: [^,]*,//' -e 's/BSSID: [^,]*,//' -e 's/MAC: [^,]*,//'
echo "auto_time=$(settings get global auto_time) timezone=$(getprop persist.sys.timezone) now=$(date '+%Y-%m-%d %H:%M:%S %Z') uptime=$(cut -d' ' -f1 /proc/uptime) s"

section "Connections right now: listening sockets and every connection off the box (BADBOX check)"
pm list packages -U 2>/dev/null > /data/local/tmp/inspect-uids.txt
awk -v uids=/data/local/tmp/inspect-uids.txt '
function hex(s,   i, v) { v = 0; s = toupper(s); for (i = 1; i <= length(s); i++) v = v * 16 + index("0123456789ABCDEF", substr(s, i, 1)) - 1; return v }
function addr(a,   p, ip) {
  split(a, p, ":"); ip = p[1]
  if (length(ip) == 32) { if (substr(ip, 1, 24) != "0000000000000000FFFF0000") return "[" ip "]:" hex(p[2]); ip = substr(ip, 25, 8) }
  return hex(substr(ip, 7, 2)) "." hex(substr(ip, 5, 2)) "." hex(substr(ip, 3, 2)) "." hex(substr(ip, 1, 2)) ":" hex(p[2])
}
BEGIN {
  while ((getline line < uids) > 0) {
    split(line, f, " "); pkg = f[1]; sub("package:", "", pkg); u = f[2]; sub("uid:", "", u)
    owner[u] = (u in owner) ? owner[u] "," pkg : pkg
  }
}
FNR == 1 { next }
{
  proto = FILENAME; sub(".*/", "", proto)
  st = $4; loc = addr($2); rem = addr($3); uid = $8
  who = (uid + 0 < 10000) ? "system uid " uid : ((uid in owner) ? owner[uid] : "uid " uid)
  if (st == "0A" || (proto ~ /udp/ && st == "07" && rem ~ /^0\.0\.0\.0:0$|^\[0+\]:0$/)) { print proto " LISTEN " loc "  " who; next }
  if (rem ~ /^127\./ || rem ~ /^0\.0\.0\.0:/) next
  state = (st == "01") ? "ESTABLISHED" : (st == "02") ? "SYN_SENT" : (st == "06") ? "TIME_WAIT" : (st == "08") ? "CLOSE_WAIT" : "state " st
  print proto " " state " " loc " -> " rem "  " who
}' /proc/net/tcp /proc/net/tcp6 /proc/net/udp /proc/net/udp6
rm -f /data/local/tmp/inspect-uids.txt

section "Apps the user installed, and system apps outside AOSP and Google"
pm list packages -3 -f 2>/dev/null
pm list packages -s 2>/dev/null | sed 's/package://' | grep -vE "^(android|com\.android\.|com\.google\.android\.|com\.qualcomm|android\.)" | sort

section "Home screen apps"
cmd package query-activities --brief -a android.intent.action.MAIN -c android.intent.category.HOME 2>/dev/null | grep /

section "Boot animation"
ls -l /oem/media/bootanimation.zip /product/media/bootanimation.zip /system/media/bootanimation.zip 2>/dev/null

section "Tundu"
dumpsys package al.tundu.games 2>/dev/null | grep -E "versionName|versionCode|lastUpdateTime" | head -3
