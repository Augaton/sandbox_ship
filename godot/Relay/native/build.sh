#!/bin/bash
# Builds libsandboxship_enet.so, the online server's ENet (relay_enet.c over ENet 1.3.18, the
# version and commit Godot 4.7.2 embeds), into the folder given (here by default).
# ENet's source comes from its official repository, at that commit, checked against its SHA-256;
# it is fetched into a fresh temporary folder, or taken from ENET_ARCHIVE if set.
#   godot/Relay/native/build.sh [OUT_DIR]
set -euo pipefail

ENET_COMMIT=2662c0de09e36f2a2030ccc2c528a3e4c9e8138a
ENET_SHA256=37381831c5ccbb41c3c71e867203fee847670f67717f297f930290d18985ad5f
here="$(cd "$(dirname "$0")" && pwd)"
out="$(mkdir -p "${1:-$here}" && cd "${1:-$here}" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

archive="${ENET_ARCHIVE:-$work/enet.tar.gz}"
if [ -z "${ENET_ARCHIVE:-}" ]; then
  curl -fsSL --retry 3 -o "$archive" "https://github.com/lsalzman/enet/archive/$ENET_COMMIT.tar.gz"
fi
echo "$ENET_SHA256  $archive" | sha256sum -c --quiet -
tar -xzf "$archive" -C "$work"
src="$work/enet-$ENET_COMMIT"

# What ENet's own CMakeLists finds on Linux.
defines="-DHAS_FCNTL=1 -DHAS_POLL=1 -DHAS_GETADDRINFO=1 -DHAS_GETNAMEINFO=1 -DHAS_GETHOSTBYNAME_R=1 -DHAS_GETHOSTBYADDR_R=1
  -DHAS_INET_PTON=1 -DHAS_INET_NTOP=1 -DHAS_MSGHDR_FLAGS=1 -DHAS_SOCKLEN_T=1"
# shellcheck disable=SC2086
gcc -O2 -fPIC -shared -fvisibility=default $defines -I "$src/include" \
  "$src"/callbacks.c "$src"/compress.c "$src"/host.c "$src"/list.c "$src"/packet.c "$src"/peer.c "$src"/protocol.c "$src"/unix.c \
  "$here/relay_enet.c" -o "$out/libsandboxship_enet.so"
echo "Construit : $out/libsandboxship_enet.so (ENet 1.3.18, $ENET_COMMIT)"
