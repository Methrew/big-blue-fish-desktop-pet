#!/bin/bash
# Builds the minimal FFmpeg used by the macOS edition to decode the transparent
# VP9 WebM animations into raw BGRA frames. Only the pieces the player needs are
# enabled: Matroska demuxer, libvpx VP9 decoder (alpha via BlockAdditional),
# swscale conversion and the rawvideo muxer on a pipe. No GPL components are
# enabled, so the resulting binary is LGPL-2.1-or-later; libvpx is BSD-3-Clause.
#
# Usage: tools/build-macos-ffmpeg.sh <output-directory> [work-directory]
set -euo pipefail

FFMPEG_VERSION=9.0.1
FFMPEG_SHA256=cf38e0e28c7e5605942c4a77755349b0145804a397af37eb1fb4c77cb237f635
LIBVPX_VERSION=1.17.0
LIBVPX_SHA256=1020f184046187baa2985dbde38e0691f49c44088bca7a1842b0236c6081dc0a
MIN_MACOS=${MACOSX_DEPLOYMENT_TARGET:-14.0}

if [[ $# -lt 1 ]]; then echo "usage: $0 <output-directory> [work-directory]" >&2; exit 64; fi
ROOT=$(cd "$(dirname "$0")/.." && pwd)
OUTPUT=$(mkdir -p "$1" && cd "$1" && pwd)
WORK=${2:-$ROOT/.work/ffmpeg-build}
mkdir -p "$WORK/downloads"
WORK=$(cd "$WORK" && pwd)
PREFIX=$WORK/prefix
JOBS=$(sysctl -n hw.ncpu)
ARCH=$(uname -m)
[[ $ARCH == arm64 ]] || { echo "Only Apple silicon (arm64) is supported by this script." >&2; exit 65; }

fetch() { # url file sha256
    local target="$WORK/downloads/$2"
    if [[ ! -f $target ]]; then curl -fL --retry 3 -o "$target.partial" "$1" && mv "$target.partial" "$target"; fi
    echo "$3  $target" | shasum -a 256 -c - >/dev/null || { echo "Checksum mismatch: $target" >&2; rm -f "$target"; exit 66; }
}
fetch "https://github.com/webmproject/libvpx/archive/refs/tags/v$LIBVPX_VERSION.tar.gz" "libvpx-$LIBVPX_VERSION.tar.gz" "$LIBVPX_SHA256"
fetch "https://ffmpeg.org/releases/ffmpeg-$FFMPEG_VERSION.tar.xz" "ffmpeg-$FFMPEG_VERSION.tar.xz" "$FFMPEG_SHA256"

export MACOSX_DEPLOYMENT_TARGET=$MIN_MACOS
FLAGS="-mmacosx-version-min=$MIN_MACOS -O2"

if [[ ! -f $PREFIX/lib/libvpx.a ]]; then
    rm -rf "$WORK/libvpx-$LIBVPX_VERSION"
    tar -xzf "$WORK/downloads/libvpx-$LIBVPX_VERSION.tar.gz" -C "$WORK"
    (cd "$WORK/libvpx-$LIBVPX_VERSION" &&
        ./configure --prefix="$PREFIX" --target=arm64-darwin22-gcc --enable-static --disable-shared --enable-pic \
            --disable-examples --disable-tools --disable-docs --disable-unit-tests \
            --disable-vp8 --disable-vp9-encoder --enable-vp9-decoder --extra-cflags="$FLAGS" &&
        make -j"$JOBS" && make install)
fi

rm -rf "$WORK/ffmpeg-$FFMPEG_VERSION"
tar -xJf "$WORK/downloads/ffmpeg-$FFMPEG_VERSION.tar.xz" -C "$WORK"
(cd "$WORK/ffmpeg-$FFMPEG_VERSION" &&
    ./configure --prefix="$WORK/ffmpeg-install" --arch=arm64 --cc=clang \
        --disable-everything --disable-autodetect --disable-doc --disable-debug --disable-network \
        --disable-ffplay --disable-ffprobe --enable-ffmpeg --enable-pthreads \
        --enable-avcodec --enable-avformat --enable-avfilter --enable-swscale \
        --enable-libvpx --enable-decoder=libvpx_vp9 --enable-demuxer=matroska \
        --enable-muxer=rawvideo --enable-encoder=rawvideo --enable-protocol=file,pipe \
        --enable-filter=scale,format,null,hflip \
        --extra-cflags="$FLAGS -I$PREFIX/include" --extra-ldflags="-mmacosx-version-min=$MIN_MACOS -L$PREFIX/lib" &&
    make -j"$JOBS" ffmpeg)

install -m 0755 "$WORK/ffmpeg-$FFMPEG_VERSION/ffmpeg" "$OUTPUT/ffmpeg"
strip -x "$OUTPUT/ffmpeg"
cp "$WORK/ffmpeg-$FFMPEG_VERSION/COPYING.LGPLv2.1" "$OUTPUT/FFmpeg-LICENSE.txt"
cp "$WORK/libvpx-$LIBVPX_VERSION/LICENSE" "$OUTPUT/libvpx-LICENSE.txt"
"$OUTPUT/ffmpeg" -hide_banner -decoders 2>/dev/null | grep -q libvpx-vp9 || { echo "libvpx-vp9 decoder missing from the build." >&2; exit 67; }
echo "FFmpeg $FFMPEG_VERSION with libvpx $LIBVPX_VERSION: $OUTPUT/ffmpeg"
