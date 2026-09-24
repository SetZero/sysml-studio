#!/usr/bin/env bash
# Starts a built package and fails if the application does not stay up.
#
#   scripts/smoke.sh <rid> [packages-dir]
#
# Unpacks the package package.sh made for <rid>, opens the drone sample in
# it and checks, after a while, that the process is still running. It
# catches a package that cannot start: a missing native library, a bundle
# that does not extract, a resource that is not there.
set -euo pipefail

cd "$(dirname "$0")/.."
rid="${1:?usage: scripts/smoke.sh <rid> [packages-dir]}"
packages="$(cd "${2:-artifacts/packages}" && pwd)"
os="${rid%-*}"
arch="${rid#*-}"
work="$(mktemp -d)"
log="$work/app.log"

case "$os" in
win)
    unzip -q "$packages/SysML-Studio-windows-$arch.zip" -d "$work"
    dir="$work/SysML Studio"
    "$dir/SysML Studio.exe" "$dir/samples/drone" > "$log" 2>&1 &
    ;;
linux)
    tar -C "$work" -xzf "$packages/SysML-Studio-linux-$arch.tar.gz"
    dir="$work/sysml-studio"
    launcher=()
    if [ -z "${DISPLAY:-}" ] && command -v xvfb-run >/dev/null; then launcher=(xvfb-run -a); fi
    "${launcher[@]}" "$dir/sysml-studio" "$dir/samples/drone" > "$log" 2>&1 &
    ;;
osx)
    ditto -x -k "$packages/SysML-Studio-macos-$arch.zip" "$work"
    dir="$work/SysML Studio.app/Contents/MacOS"
    "$dir/SysmlStudio.App" "$dir/samples/drone" > "$log" 2>&1 &
    ;;
*)
    echo "unknown runtime $rid" >&2
    exit 1
    ;;
esac

pid=$!
sleep 15
if kill -0 "$pid" 2>/dev/null; then
    echo "==> $rid started and is still running"
    kill "$pid" 2>/dev/null || true
    # xvfb-run leaves the application behind when it is killed.
    pkill -f sysml-studio/sysml-studio 2>/dev/null || true
    exit 0
fi

wait "$pid" || status=$?
echo "==> $rid exited within 15 seconds (status ${status:-0})" >&2
cat "$log" >&2
exit 1
