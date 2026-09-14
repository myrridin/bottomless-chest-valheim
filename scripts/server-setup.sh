#!/usr/bin/env bash
# Mirrors BepInEx and Jotunn from the r2modman client profile into the dedicated server.
#
# Copying from the profile rather than downloading separately is deliberate: it makes the
# server run byte-identical loader and library versions to the client. NetworkCompatibility
# compares mod versions on join, so drift between the two is a rejected connection.

set -euo pipefail

PROFILE="/mnt/c/Users/myrri/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/BottomlessChest Development"
SERVER="/mnt/c/Program Files (x86)/Steam/steamapps/common/Valheim dedicated server"

[ -d "$PROFILE" ] || { echo "Client profile not found: $PROFILE" >&2; exit 1; }
[ -f "$SERVER/valheim_server.exe" ] || { echo "Dedicated server not found: $SERVER" >&2; exit 1; }

echo "Mirroring loader from the client profile..."
for f in winhttp.dll doorstop_config.ini .doorstop_version; do
    [ -f "$PROFILE/$f" ] && cp -p "$PROFILE/$f" "$SERVER/$f" && echo "  $f"
done
cp -rp "$PROFILE/doorstop_libs" "$SERVER/" 2>/dev/null && echo "  doorstop_libs/"

mkdir -p "$SERVER/BepInEx/plugins" "$SERVER/BepInEx/config" "$SERVER/BepInEx/patchers"
cp -rp "$PROFILE/BepInEx/core" "$SERVER/BepInEx/" && echo "  BepInEx/core/"
[ -f "$PROFILE/BepInEx/config/BepInEx.cfg" ] && cp -p "$PROFILE/BepInEx/config/BepInEx.cfg" "$SERVER/BepInEx/config/"

JOTUNN="$PROFILE/BepInEx/plugins/ValheimModding-Jotunn"
[ -d "$JOTUNN" ] || { echo "Jotunn not found in the profile" >&2; exit 1; }
rm -rf "$SERVER/BepInEx/plugins/ValheimModding-Jotunn"
cp -rp "$JOTUNN" "$SERVER/BepInEx/plugins/"
echo "  Jotunn $(grep -oP '"version_number":\s*"\K[^"]+' "$JOTUNN/manifest.json" 2>/dev/null || echo '?')"

# ValheimPlus, when the client profile has it. Stations run on whichever peer owns them,
# so on a dedicated server the ones near the world's centre run V+ on the server itself -
# testing that needs V+ there too, with the same config the client has.
VPLUS="$PROFILE/BepInEx/plugins/Grantapher-ValheimPlus_Grantapher_Temporary"
if [ -d "$VPLUS" ]; then
    rm -rf "$SERVER/BepInEx/plugins/Grantapher-ValheimPlus_Grantapher_Temporary"
    cp -rp "$VPLUS" "$SERVER/BepInEx/plugins/"
    for cfg in org.bepinex.plugins.valheim_plus.cfg; do
        [ -f "$PROFILE/BepInEx/config/$cfg" ] && cp -p "$PROFILE/BepInEx/config/$cfg" "$SERVER/BepInEx/config/"
    done
    echo "  ValheimPlus $(grep -oP '"version_number":\s*"\K[^"]+' "$VPLUS/manifest.json" 2>/dev/null || echo '?')"
else
    rm -rf "$SERVER/BepInEx/plugins/Grantapher-ValheimPlus_Grantapher_Temporary"
    echo "  ValheimPlus not in the profile; removed from the server too"
fi

echo
echo "Server loader ready. Build the mod to deploy it:"
echo '  "/mnt/c/Program Files/dotnet/dotnet.exe" build src/BottomlessChest/BottomlessChest.csproj'
