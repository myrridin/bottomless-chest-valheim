#!/usr/bin/env bash
# Shows whether the dedicated server loaded the mod, and what its chest stores contain.
set -uo pipefail

SERVER="/mnt/c/Program Files (x86)/Steam/steamapps/common/Valheim dedicated server"
SAVEDIR="/mnt/c/valheim_mods/server-save"
LOG="$SERVER/BepInEx/LogOutput.log"

# Count rather than `grep -q`: -q exits on first match, which SIGPIPEs tasklist and
# makes the pipeline fail under `set -o pipefail` even when the server is running.
running=$(tasklist.exe 2>/dev/null | grep -ci valheim_server || true)
printf '%-28s %s\n' "server running:" "$([ "$running" -gt 0 ] && echo yes || echo no)"

if [ -f "$LOG" ]; then
    printf '%-28s %s\n' "BepInEx log:" "$(date -r "$LOG" '+%H:%M:%S')"
    grep -a "BottomlessChest\|Jotunn.*version" "$LOG" | tail -6 | sed 's/^/    /'
    echo "  --- errors ---"
    grep -aiE "exception|error|degraded" "$LOG" | tail -8 | sed 's/^/    /'
else
    echo "BepInEx log:                 MISSING - server has not run with BepInEx"
fi

echo
echo "=== server-side chest stores ==="
find "$SAVEDIR" -name "*.bottomless.dat" 2>/dev/null | while read -r f; do
    python3 "$(dirname "$0")/dump-store.py" "$f" | grep -E "^=== |stores=|^  store"
done
[ -z "$(find "$SAVEDIR" -name '*.bottomless.dat' 2>/dev/null)" ] && echo "(none yet)"
