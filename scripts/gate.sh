#!/usr/bin/env bash
# Every gate CI runs, cheapest first, in one command.
#
#   scripts/gate.sh            all gates
#   scripts/gate.sh format     one gate by name
#
# Judge it by its exit status: it stops at the first gate that fails.
set -euo pipefail

cd "$(dirname "$0")/.."
only="${1:-}"

run() {
    local name="$1"; shift
    if [ -n "$only" ] && [ "$only" != "$name" ]; then return 0; fi
    echo "==> $name"
    "$@"
}

# 1. Formatting and code style, as .editorconfig defines them.
run format dotnet format SysmlStudio.slnx --verify-no-changes --severity info

# 2. Build with the analyzers on and warnings failing.
run build dotnet build SysmlStudio.slnx -warnaserror --nologo

# 3. Tests.
run test dotnet test SysmlStudio.slnx --no-build --nologo

# 4. The real model, when a Ferrix checkout is beside this one: every file
#    parses and every element lands somewhere in the text.
model="${SYSML_STUDIO_MODEL:-../os/docs/sysml}"
run sample dotnet run --project tools/ParseCheck --no-build -- samples/vehicle
run sample dotnet run --project tools/ParseCheck --no-build -- samples/drone
if [ -d "$model" ]; then
    run model dotnet run --project tools/ParseCheck --no-build -- "$model"
else
    echo "==> model (skipped: $model does not exist)"
fi

echo "all gates passed"
