#!/usr/bin/env bash
# Installs a built package the way a user would, starts it, and fails if
# the application does not stay up. Then does the same for the portable
# archive. Meant for a throwaway CI machine: it installs system-wide.
#
#   scripts/smoke.sh <rid> [packages-dir]
#
#   win-*    the .msi (msiexec), then the .zip
#   osx-*    the .pkg (installer), then the .zip
#   linux-*  the .deb (apt), then the .tar.gz
#
# It catches a package that cannot start: a missing native library, a
# bundle that does not extract, an installer that leaves something out.
set -euo pipefail

cd "$(dirname "$0")/.."
rid="${1:?usage: scripts/smoke.sh <rid> [packages-dir]}"
packages="$(cd "${2:-artifacts/packages}" && pwd)"
os="${rid%-*}"
arch="${rid#*-}"
work="$(mktemp -d)"

# start <what> <program> <model>: runs it for 15 seconds, fails if it exits.
start() {
    local what="$1" program="$2" model="$3" log="$work/$RANDOM.log" launcher=() status=0
    [ -e "$program" ] || { echo "==> $what: $program is not there" >&2; exit 1; }
    [ -d "$model" ] || { echo "==> $what: $model is not there" >&2; exit 1; }
    if [ "$os" = linux ] && [ -z "${DISPLAY:-}" ]; then launcher=(xvfb-run -a); fi
    "${launcher[@]}" "$program" "$model" > "$log" 2>&1 &
    local pid=$!
    sleep 15
    if kill -0 "$pid" 2>/dev/null; then
        echo "==> $what started and is still running"
        kill "$pid" 2>/dev/null || true
        pkill -f "$program" 2>/dev/null || true
        return 0
    fi
    wait "$pid" || status=$?
    echo "==> $what exited within 15 seconds (status $status)" >&2
    cat "$log" >&2
    exit 1
}

case "$os" in
win)
    msi="$(cygpath -w "$packages/SysML-Studio-windows-$arch.msi")"
    msilog="$(cygpath -w "$work/msi.log")"
    code=$(powershell -NoProfile -Command \
        "(Start-Process msiexec -ArgumentList '/i','\"$msi\"','/qn','/l*v','\"$msilog\"' -Wait -PassThru).ExitCode")
    code="${code//$'\r'/}"
    if [ "$code" != 0 ]; then
        echo "==> msiexec failed ($code)" >&2
        iconv -f UTF-16LE -t UTF-8 "$work/msi.log" 2>/dev/null | tail -40 >&2 || tail -40 "$work/msi.log" >&2
        exit 1
    fi
    installed="$(cygpath -u "${PROGRAMFILES:-C:\\Program Files}")/SysML Studio"
    start ".msi" "$installed/SysML Studio.exe" "$installed/samples/drone"

    unzip -q "$packages/SysML-Studio-windows-$arch.zip" -d "$work/zip"
    start ".zip" "$work/zip/SysML Studio/SysML Studio.exe" "$work/zip/SysML Studio/samples/drone"
    ;;

osx)
    sudo installer -pkg "$packages/SysML-Studio-macos-$arch.pkg" -target /
    app="/Applications/SysML Studio.app/Contents/MacOS"
    start ".pkg" "$app/SysmlStudio.App" "$app/samples/drone"

    ditto -x -k "$packages/SysML-Studio-macos-$arch.zip" "$work/zip"
    app="$work/zip/SysML Studio.app/Contents/MacOS"
    start ".zip" "$app/SysmlStudio.App" "$app/samples/drone"
    ;;

linux)
    case "$arch" in x64) debarch=amd64 ;; arm64) debarch=arm64 ;; esac
    sudo apt-get install -y "$packages/sysml-studio_$debarch.deb" > "$work/apt.log" 2>&1 \
        || { cat "$work/apt.log" >&2; exit 1; }
    start ".deb" /usr/bin/sysml-studio /opt/sysml-studio/samples/drone

    tar -C "$work" -xzf "$packages/SysML-Studio-linux-$arch.tar.gz"
    start ".tar.gz" "$work/sysml-studio/sysml-studio" "$work/sysml-studio/samples/drone"
    ;;

*)
    echo "unknown runtime $rid" >&2
    exit 1
    ;;
esac
