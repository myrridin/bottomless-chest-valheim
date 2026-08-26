#!/usr/bin/env bash
# Builds a Thunderstore-ready zip in dist/.
#
# The same layout works with r2modman's "Import local mod", so the package can be
# installed and tested exactly as a user would receive it before anything is published.

set -euo pipefail

cd "$(dirname "$0")/.."

DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
BIN="src/BottomlessChest/bin/Debug"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

VERSION=$(grep -oP '"version_number":\s*"\K[^"]+' package/manifest.json)
OUT="dist/BottomlessChest-$VERSION.zip"

echo "Building $VERSION..."
# SkipDeploy: packaging must not write into the live profile or the server,
# which would disturb whatever is being tested.
"$DOTNET" build src/BottomlessChest/BottomlessChest.csproj -c Release -p:SkipDeploy=true -v q --nologo >/dev/null
[ -d "src/BottomlessChest/bin/Release" ] && BIN="src/BottomlessChest/bin/Release"

for f in BottomlessChest.dll BottomlessChest.Logic.dll; do
    [ -f "$BIN/$f" ] || { echo "missing $BIN/$f" >&2; exit 1; }
    cp "$BIN/$f" "$STAGE/"
done

cp package/manifest.json package/README.md package/CHANGELOG.md "$STAGE/"
[ -f package/LICENSE ] && cp package/LICENSE "$STAGE/"

if [ -f package/icon.png ]; then
    cp package/icon.png "$STAGE/"
else
    echo
    echo "  NO icon.png - Thunderstore requires one, exactly 256x256." >&2
    echo "  Put it at package/icon.png. The zip is still written so the layout can be" >&2
    echo "  checked, but it will be rejected on upload." >&2
    echo
fi

if [ ! -f package/LICENSE ]; then
    echo "  No package/LICENSE - worth adding before a public release." >&2
fi

mkdir -p dist
rm -f "$OUT"

# python rather than zip(1), which is not installed in this WSL image.
python3 - "$STAGE" "$OUT" <<'PYZIP'
import os, sys, zipfile
stage, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for name in sorted(os.listdir(stage)):
        z.write(os.path.join(stage, name), name)
print(f"Wrote {out}")
for info in zipfile.ZipFile(out).infolist():
    print(f"  {info.filename:<28} {info.file_size} bytes")
PYZIP
