#!/bin/zsh
# The trailer: every shot of it staged and filmed in a run of the game of its own (src/Game/Main.Trailer.cs), out of
# sight (the window hidden, as for the self-test), written frame by frame by Godot's movie maker at 30 frames a second;
# each shot cut from its black opening and encoded, the shots laid end to end with short dissolves and dips to black,
# their sounds crossfaded, and the score made for this cut (tools/trailer/score.py) laid under them.
#
#   tools/trailer.command          film every shot and cut the trailer
#   tools/trailer.command 3 7      film only these shots again (the others are kept from last time), and cut it afresh
#   tools/trailer.command cut      only cut it again from the shots there are
#
# The trailer goes to ~/Documents/voxel-tower-trailer/trailer.mp4 (OUT=folder to put it elsewhere), the shots to
# shots/ beside it. A shot takes a few minutes; the whole of it, half an hour and more. Needs ffmpeg, python3 with
# numpy and scipy, the .NET SDK and Godot Mono in /Applications.
set -e
cd "$(dirname "$0")/.."
export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
# the Python the music is made with: the first one on the way that has numpy and scipy
PY=""
for p in $(which -a python3 2>/dev/null) /opt/homebrew/bin/python3 /usr/bin/python3; do
  if "$p" -c "import numpy, scipy" 2>/dev/null; then PY="$p"; break; fi
done
[[ -z "$PY" ]] && { echo "no python3 with numpy and scipy (pip3 install numpy scipy)"; exit 1; }
export PATH="$PATH:/opt/homebrew/bin"
command -v ffmpeg > /dev/null || { echo "no ffmpeg (brew install ffmpeg)"; exit 1; }
OUT="${OUT:-$HOME/Documents/voxel-tower-trailer}"
FPS=30
NAMES=(valley raise horde assault melee blast hurl roll magic eyes title)
# the music under each shot: a section starts where its first shot does
MUSIC=(intro build dread battle battle battle battle battle magic pulse title)
# how each shot gives way to the next: a dissolve or a dip to black, and for how long
TRANS=("fadeblack 0.8" "fade 0.5" "fade 0.3" "fadeblack 0.4" "fadeblack 0.4" "fade 0.2" "fade 0.2" "fadeblack 0.5" "fadeblack 0.5" "fadeblack 0.6")

mkdir -p "$OUT/shots"
WORK=$(mktemp -d "${TMPDIR:-/tmp}/trailer.XXXXXX")
[[ -z "$KEEP" ]] && trap 'rm -rf "$WORK"' EXIT

if [[ "$1" != "cut" ]]; then
  dotnet build VoxelTower.csproj -nologo -v q > "$WORK/build.txt" 2>&1 || { cat "$WORK/build.txt"; exit 1; }
  if (( $# > 0 )); then todo=("$@"); else todo=({0..10}); fi
  for n in $todo; do
    name=${NAMES[$((n + 1))]}
    tag=$(printf %02d $n)
    dir="$WORK/s$tag"
    mkdir -p "$dir"
    echo "— shot $tag «$name»: filming"
    start=$SECONDS
    # a run that does not get as far as filming (the machine busy, the build being written) is tried once more
    for try in 1 2; do
      rm -f "$dir"/f*.png(N) "$dir"/f.wav(N)
      "$GODOT" --path . --resolution 1920x1080 --fixed-fps $FPS --disable-vsync --write-movie "$dir/f.png" -- --trailer=$n > "$dir/log.txt" 2>&1 || true
      pre=$(grep -o "\[trailer\] preroll [0-9]*" "$dir/log.txt" | grep -o "[0-9]*$" || true)
      [[ -n "$pre" ]] && grep -q "\[trailer\] frames" "$dir/log.txt" && break
      pre=""
    done
    [[ -z "$pre" ]] && { echo "shot $tag did not film:"; grep -v "^\s*$" "$dir/log.txt" | tail -20; exit 1; }
    # what the score strikes with: the shot's great moments, and when the name comes up
    grep -o "\[trailer\] \(hit\|reveal\) [0-9.]*" "$dir/log.txt" | sed 's/\[trailer\] //' > "$OUT/shots/$tag-$name.cues" || true
    frames=$(ls "$dir" | grep -c "^f[0-9]*\.png$")
    echo "  $frames frames ($pre of them black at the start) in $((SECONDS - start)) s; encoding"
    ffmpeg -v error -y -framerate $FPS -start_number $pre -i "$dir/f%08d.png" -ss $(python3 -c "print($pre / $FPS)") -i "$dir/f.wav" \
      -vf "scale=1920:1080:flags=lanczos" -c:v libx264 -crf 16 -preset slow -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -shortest \
      "$OUT/shots/$tag-$name.mp4"
    rm -rf "$dir"
  done
fi

# ---------------------------------------------------------------- the cut
shots=()
for n in {0..10}; do
  f=$(ls "$OUT/shots/$(printf %02d $n)-"*.mp4 2>/dev/null | head -1)
  [[ -z "$f" ]] && { echo "shot $n is missing: film it first (tools/trailer.command $n)"; exit 1; }
  shots+=("$f")
done
echo "— cutting"
MUSIC_PLAN="${MUSIC[*]}" TRANS_PLAN="${(j:,:)TRANS}" python3 - "$OUT" "$WORK" "${shots[@]}" <<'PY'
import json, os, subprocess, sys
out, work, shots = sys.argv[1], sys.argv[2], sys.argv[3:]
music = os.environ["MUSIC_PLAN"].split()
trans = [t.split() for t in os.environ["TRANS_PLAN"].split(",")]
def length(f):
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "json", f], capture_output=True, text=True)
    return float(json.loads(r.stdout)["format"]["duration"])
lens = [length(f) for f in shots]
# where each shot starts in the cut: the next comes in as the last goes out
starts, at = [], 0.0
for i, d in enumerate(lens):
    starts.append(at)
    at += d - (float(trans[i][1]) if i < len(trans) else 0)
total = at
cues, hits, reveal = [], [], None
for i, f in enumerate(shots):
    if i == 0 or music[i] != music[i - 1]:
        cues.append(f"{starts[i]:.2f}:{music[i]}")
    try:
        for line in open(f[:-4] + ".cues"):
            kind, t = line.split()
            if kind == "hit": hits.append(starts[i] + float(t))
            elif kind == "reveal": reveal = starts[i] + float(t)
    except FileNotFoundError:
        pass
# the video: each shot dissolving or dipping into the next; the sound crossfaded alike
inputs, parts, v, a, chain = [], [], "[0:v]", "[0:a]", 0.0
for f in shots: inputs += ["-i", f]
chain = lens[0]
for i in range(1, len(shots)):
    kind, d = trans[i - 1][0], float(trans[i - 1][1])
    off = chain - d
    parts.append(f"{v}[{i}:v]xfade=transition={kind}:duration={d}:offset={off:.3f}[v{i}]")
    parts.append(f"{a}[{i}:a]acrossfade=d={d}:c1=tri:c2=tri[a{i}]")
    v, a = f"[v{i}]", f"[a{i}]"
    chain = off + lens[i]
plan = {"total": total, "cues": cues, "hits": hits, "reveal": reveal, "inputs": inputs, "graph": ";".join(parts), "v": v, "a": a}
json.dump(plan, open(f"{work}/plan.json", "w"))
print(f"  {len(shots)} shots, {total:.1f} s; music: {' '.join(cues)}; {len(hits)} hits" + (f", the name at {reveal:.1f} s" if reveal else ""))
PY
plan="$WORK/plan.json"
total=$(python3 -c "import json; print(json.load(open('$plan'))['total'])")
args=($(python3 -c "import json; p = json.load(open('$plan')); print(' '.join(['--cue ' + c for c in p['cues']] + ['--hit %.2f' % h for h in p['hits']] + (['--reveal %.2f' % p['reveal']] if p['reveal'] else [])))"))
echo "— the score"
"$PY" tools/trailer/score.py "$WORK/score.wav" --length $total $args
echo "— the trailer"
graph=$(python3 -c "import json; print(json.load(open('$plan'))['graph'])")
vout=$(python3 -c "import json; print(json.load(open('$plan'))['v'])")
aout=$(python3 -c "import json; print(json.load(open('$plan'))['a'])")
inputs=($(python3 -c "import json; print(' '.join(json.load(open('$plan'))['inputs']))"))
nin=$(( ${#inputs} / 2 ))
# the game's own sounds over the score, the score a little under them; held from clipping
ffmpeg -v error -y $inputs -i "$WORK/score.wav" -filter_complex \
  "$graph;${aout}aresample=48000,volume=1.0[game];[${nin}:a]aresample=48000,volume=0.62[music];[game][music]amix=inputs=2:normalize=0:duration=first,alimiter=limit=0.93:level=false[mix]" \
  -map "$vout" -map "[mix]" -c:v libx264 -crf 16 -preset slow -pix_fmt yuv420p -c:a aac -b:a 224k -movflags +faststart "$OUT/trailer.mp4"
echo "— $OUT/trailer.mp4 ($(python3 -c "print(round($total, 1))") s)"
