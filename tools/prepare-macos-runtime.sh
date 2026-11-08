#!/bin/bash
# Collects the native helpers the macOS edition runs from its runtime/ folder:
# the FFmpeg built by tools/build-macos-ffmpeg.sh and, optionally, the
# unmodified llama.cpp b10809 macOS arm64 (Metal) release. Only llama-server and
# the libraries it loads are copied, each under the install name it is loaded by.
#
# Usage: tools/prepare-macos-runtime.sh <output-directory> <ffmpeg-directory> [llama-directory]
set -euo pipefail
LLAMA_BUILD=10809
if [[ $# -lt 2 ]]; then echo "usage: $0 <output-directory> <ffmpeg-directory> [llama-directory]" >&2; exit 64; fi
output=$(mkdir -p "$1" && cd "$1" && pwd); ffmpeg=$(cd "$2" && pwd); llama=${3:+$(cd "$3" && pwd)}

"$ffmpeg/ffmpeg" -hide_banner -decoders 2>/dev/null | grep -q libvpx-vp9 || { echo "$ffmpeg/ffmpeg lacks the libvpx-vp9 decoder." >&2; exit 65; }
install -m 0755 "$ffmpeg/ffmpeg" "$output/ffmpeg"
cp "$ffmpeg/FFmpeg-LICENSE.txt" "$ffmpeg/libvpx-LICENSE.txt" "$output/"

if [[ -n $llama ]]; then
    "$llama/llama-server" --version 2>&1 | grep -q "build $LLAMA_BUILD" || { echo "Expected llama.cpp b$LLAMA_BUILD in $llama." >&2; exit 66; }
    install -m 0755 "$llama/llama-server" "$output/llama-server"
    queue=("$output/llama-server")
    while ((${#queue[@]})); do
        binary=${queue[0]}; queue=("${queue[@]:1}")
        for name in $(otool -L "$binary" | awk '$1 ~ /^@rpath\// { sub("@rpath/", "", $1); print $1 }'); do
            [[ -e $output/$name ]] && continue
            [[ -e $llama/$name ]] || { echo "Missing $name required by $(basename "$binary")." >&2; exit 67; }
            install -m 0644 "$llama/$name" "$output/$name"   # follows version symlinks
            queue+=("$output/$name")
        done
    done
    cp "$llama/LICENSE" "$output/LLAMA-LICENSE"
    "$output/llama-server" --version >/dev/null 2>&1 || { echo "The copied llama-server cannot load its libraries." >&2; exit 68; }
fi
echo "macOS runtime ready: $output"
ls -1 "$output"
