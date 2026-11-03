#!/usr/bin/env bash
# Makes the Linux export into an AppImage: one file that runs as it is. Inside is the export
# unchanged, the executable and its data_Game_* folder side by side, with the game's icon and
# its .desktop file. The saves stay in user:// (~/.local/share/godot/app_userdata/Sandbox Ship),
# outside the image, which is read-only.
#
#   godot/packaging/appimage.sh EXPORT OUT APPIMAGETOOL RUNTIME
#
# EXPORT is the Linux export's folder, OUT the AppImage to make. APPIMAGETOOL and RUNTIME are
# appimagetool and the AppImage runtime at their pinned versions: the CI downloads them and
# checks their hashes (.github/workflows/build.yml).
set -eu
export="$(realpath "$1")"
out="$(realpath -m "$2")"
tool="$(realpath "$3")"
runtime="$(realpath "$4")"
here="$(dirname "$(realpath "$0")")"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
app="$work/SandboxShip.AppDir"
mkdir -p "$app/usr/bin"
cp -a "$export/." "$app/usr/bin/"
test -x "$app/usr/bin/SandboxShip.x86_64"
# The runtime starts AppRun; the game, started through this link, finds its data beside itself.
ln -s usr/bin/SandboxShip.x86_64 "$app/AppRun"
cp "$here/sandbox-ship.desktop" "$app/"
cp "$here/../Game/icon.png" "$app/sandbox-ship.png"
ln -s sandbox-ship.png "$app/.DirIcon"

# appimagetool is an AppImage itself: unpacked to run, it needs no FUSE on the machine.
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$tool" --no-appstream --runtime-file "$runtime" "$app" "$out"
