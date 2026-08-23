#!/usr/bin/env bash
# Reports whether the last Valheim launch actually loaded BepInEx and our plugin.
# Run it after a "Start modded" launch.

set -u

GAME="/mnt/e/SteamLibrary/steamapps/common/Valheim"
PROFILE="/mnt/c/Users/myrri/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/BottomlessChest Development"
PLAYER_LOG="/mnt/c/Users/myrri/AppData/LocalLow/IronGate/Valheim/Player.log"

status=0
say() { printf '%-34s %s\n' "$1" "$2"; }

# Doorstop injects only if these were copied into the real game folder.
if [ -f "$GAME/winhttp.dll" ]; then
    say "doorstop in game folder:" "yes"
else
    say "doorstop in game folder:" "NO  <- r2modman is not managing $GAME"
    status=1
fi

if [ -f "$PLAYER_LOG" ]; then
    ran_from=$(grep -m1 "Mono path\[0\]" "$PLAYER_LOG" | sed 's/.*= .//; s/.valheim_Data.*//')
    say "last run from:" "$ran_from"
    if grep -qiE "bepinex|doorstop" "$PLAYER_LOG"; then
        say "BepInEx in Player.log:" "yes"
    else
        say "BepInEx in Player.log:" "NO  <- launched vanilla"
        status=1
    fi
else
    say "Player.log:" "missing"
    status=1
fi

LOG="$PROFILE/BepInEx/LogOutput.log"
if [ -f "$LOG" ]; then
    say "BepInEx LogOutput.log:" "present ($(date -r "$LOG" '+%H:%M:%S'))"
    grep -iE "Jotunn.*(loaded|version)" "$LOG" | tail -1 | sed 's/^/    /'
    if grep -q "BottomlessChest .* loaded" "$LOG"; then
        grep -h "BottomlessChest" "$LOG" | tail -5 | sed 's/^/    /'
    else
        say "BottomlessChest plugin:" "NOT LOADED"
        status=1
    fi
    echo "--- errors/warnings mentioning us ---"
    grep -iE "error|exception|bottomless" "$LOG" | tail -15 | sed 's/^/    /'
else
    say "BepInEx LogOutput.log:" "MISSING  <- BepInEx never started"
    status=1
fi

exit $status
