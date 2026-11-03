#!/usr/bin/env bash
# Plays the main screens in English, without a window, and lists the texts shown there that
# have no English yet (Lang's --lang-report). The catalog itself is checked by lang.py --check;
# this finds what the code makes at run time.
#
#   godot/tools/lang-check.sh GAME OUT [CAPTURES]
#
# GAME runs the game: an export (path/SandboxShip.x86_64) or "godot --path godot/Game". OUT is
# the report; CAPTURES, if given, a folder for a picture of each screen (needs gamescope).
# Exits 1 if any text is missing.
set -u
game="$1"
out="$2"
shots="${3:-}"
: > "$out"

screens=(
  "--menu=home" "--menu=new" "--menu=skirmish --seats=4 --teams=1,1,2,2" "--menu=lobby-campaign"
  "--menu=lobby-survival" "--menu=lobby-zones" "--menu=lobby-assault" "--menu=lobby-regicide" "--menu=lobby-sandbox"
  "--menu=multiplayer --master=127.0.0.1:1" "--menu=options" "--menu=controls" "--menu=credits" "--menu=replays" "--menu=load" "--menu=profile"
  "--start=battle --skip=200" "--start=battle --mode=survival --skip=120" "--start=battle --mode=zones --skip=120"
  "--start=battle --mode=assault --skip=120" "--start=battle --mode=regicide --skip=120" "--start=sandbox"
  "--start=tutorial" "--start=campaign --skip=120" "--start=campaign --size=large --skip=600" "--intro --menu=home"
)

i=0
for s in "${screens[@]}"; do
  i=$((i + 1))
  # shellcheck disable=SC2086
  args=(-- --lang=en --lang-report="$out" $s --frames=150)
  if [ -n "$shots" ]; then
    mkdir -p "$shots"
    timeout 240 gamescope --backend headless -W 1280 -H 800 -w 1280 -h 800 -- $game --disable-vsync --audio-driver Dummy \
      "${args[@]}" --shot="$shots/$(printf %02d $i)-$(echo "$s" | tr -c 'a-z0-9=-' '_' | cut -c1-40).png" > /dev/null 2>&1
  else
    timeout 240 $game --headless --audio-driver Dummy "${args[@]}" --shot=/dev/null > /dev/null 2>&1
  fi
done

sort -u "$out" -o "$out"
n=$(grep -c . "$out")
echo "$n texts shown without English (in $out)"
[ "$n" -eq 0 ]
