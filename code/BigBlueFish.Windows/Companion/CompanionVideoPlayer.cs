// Modified by acer1204 on 2026-10-26 for the taiwan-edition fork of Bai-Cute/big-blue-fish-desktop-pet (Apache-2.0).
// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BigBlueFish.Windows;

internal sealed class CompanionVideoPlayer : IDisposable
{
    [DllImport("winmm.dll")]
    static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")]
    static extern uint timeEndPeriod(uint period);
    // Preloaded openings: a quarter second of each clip at no more than 24 frames a second (the rate of the
    // compatibility clips), at their 640x360, and never more than PreloadBudget bytes for all of them together.
    const double OpeningSeconds = .25, OpeningInterval = 1.0 / 24;
    const int OpeningFrames = 6, OpeningWidth = 640, OpeningHeight = 360;
    const long PreloadBudget = 64L << 20;
    readonly Image image;
    readonly SemaphoreSlim serial = new(1);
    readonly SemaphoreSlim preloadGate = new(1);
    // null while an opening is being decoded, or when it could not be.
    readonly Dictionary<(string File, double Start), Opening?> openings = new();
    long preloadedBytes;
    WriteableBitmap? bitmap, spareBitmap;
    CancellationTokenSource? cancellation;
    volatile bool paused;
    long generation;
    bool disposed, hardwareFailed, fineTimer;
    string? displayedFile;
    double displayedSeconds;
    internal long Frames { get; private set; }
    internal ulong RenderedFrames => (ulong)Frames;
    internal double PositionSeconds { get; private set; }
    internal double PlaybackRate { get; private set; } = 1;
    internal double StartSeconds { get; private set; }
    internal string Backend { get; private set; } = "未執行";
    internal int OutputWidth { get; private set; }
    internal int OutputHeight { get; private set; }
    internal bool ForceSoftware { get; set; }
    internal bool Paused { get => paused; set { paused = value; UpdateTimerResolution(); } }
    internal event Action<string>? Failed;
    internal event Action<double>? FramePresented;
    internal CompanionVideoPlayer(Image target) { image = target; UpdateTimerResolution(); }
    internal void ResetHardwareFallback() => hardwareFailed = false;

    // The default 15.6 ms system timer made frames alternate between roughly 31 and 47 ms. Request 1 ms
    // resolution only while frames are shown (the waits of preloaded openings, the hand-off of each frame to
    // the dispatcher); a hidden or paused pet releases it.
    void UpdateTimerResolution()
    {
        bool fine = !paused && !disposed;
        if (fine == fineTimer) return;
        fineTimer = fine;
        if (fine) timeBeginPeriod(1); else timeEndPeriod(1);
    }

    sealed record Opening(byte[][] Frames, double[] Seconds, int Width, int Height, double End);

    internal bool HasOpening(string file, double startSeconds = 0) => openings.TryGetValue((file, startSeconds), out var opening) && opening != null;

    // Click, drag and walk_to clips keep their opening frames decoded, so a click or a drag shows motion at once
    // instead of holding the old frame while the previous pipeline is released and a new one is created. The
    // opening is decoded on the path playback takes: the GPU clip while hardware decoding is in use, otherwise
    // the compatibility clip. Openings are decoded one at a time in the background.
    internal void Preload(string file, double startSeconds = 0)
    {
        if (disposed || !double.IsFinite(startSeconds) || startSeconds < 0 || !openings.TryAdd((file, startSeconds), null)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                Opening? opening;
                await preloadGate.WaitAsync();
                try { opening = disposed ? null : DecodeOpening(file, startSeconds); }
                finally { preloadGate.Release(); }
                if (opening != null)
                    await image.Dispatcher.InvokeAsync(() => { if (!disposed) openings[(file, startSeconds)] = opening; });
            }
            catch (Exception ex) { DiagnosticLog.Write(DiagnosticEvent.AnimationFailed, kind: "cache", state: "unavailable", error: ex); }
        });
    }

    Opening? DecodeOpening(string file, double start)
    {
        CompanionMedia.Initialize();
        if (!ForceSoftware && !hardwareFailed && CompanionMedia.HardwareAvailable)
        {
            try { return DecodeOpening(file, start, true); }
            catch (Exception ex) { DiagnosticLog.Write(DiagnosticEvent.AnimationFailed, kind: "cache", state: "hardware_fallback", error: ex); }
        }
        return DecodeOpening(CompatFile(file), start, false);
    }

    Opening? DecodeOpening(string source, double start, bool hardware)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("找不到動畫素材", Path.GetFileName(source));
        const int frameBytes = OpeningWidth * OpeningHeight * 4;
        long room = (PreloadBudget - Interlocked.Read(ref preloadedBytes)) / frameBytes;
        int limit = (int)Math.Min(room, OpeningFrames);
        if (limit <= 0) return null;
        double end = start + OpeningSeconds;
        var frames = new List<byte[]>();
        var seconds = new List<double>();
        bool visible = false;
        using (var stream = new CompanionMediaStream(source, hardware, OpeningWidth, OpeningHeight, start, end, 1))
        {
            stream.SetPaused(false);
            var watch = Stopwatch.StartNew();
            while (frames.Count < limit && watch.Elapsed.TotalSeconds < 5)
            {
                using var frame = stream.Read();
                if (frame == null) { if (stream.Ended) break; continue; }
                if (frame.Seconds >= end) break;
                // The GPU clips run at 60 fps; keeping every frame would cover too short an opening.
                if (seconds.Count > 0 && frame.Seconds < seconds[^1] + OpeningInterval - .002) continue;
                // Stored the way playback presents frames: compression residue cleared, ready for pixel hit testing.
                var pixels = new byte[frameBytes];
                if (ExtractFrame(frame, pixels, !visible)) visible = true;
                frames.Add(pixels); seconds.Add(frame.Seconds);
            }
        }
        if (frames.Count == 0) return null;
        // Like playback, a GPU decode without a visible pixel is not trusted; the compatibility clip is used instead.
        if (hardware && !visible) throw new IOException("GPU 硬體解碼沒有輸出可見的動畫像素。");
        Interlocked.Add(ref preloadedBytes, (long)frames.Count * frameBytes);
        // Cut short by the budget, the opening covers its last frame's interval only.
        double covered = frames.Count < limit ? end : Math.Min(end, seconds[^1] + OpeningInterval);
        return new(frames.ToArray(), seconds.ToArray(), OpeningWidth, OpeningHeight, covered);
    }

    // The preloaded opening of this clip, cut at its end; none while paused.
    Opening? TakeOpening(string file, double start, double? end)
    {
        if (paused || !openings.TryGetValue((file, start), out var opening) || opening == null) return null;
        if (end is not { } stop || opening.End <= stop) return opening;
        int count = Array.FindIndex(opening.Seconds, s => s >= stop);
        if (count == 0) return null;
        if (count < 0) count = opening.Frames.Length;
        return opening with { Frames = opening.Frames[..count], Seconds = opening.Seconds[..count], End = stop };
    }

    static string CompatFile(string file) => Path.Combine(Path.GetDirectoryName(file)!, "compat", Path.GetFileName(file));

    internal void Play(string file, bool mirrored, Action completed, double startSeconds = 0, double? endSeconds = null, double playbackRate = 1)
    {
        if (!double.IsFinite(startSeconds) || startSeconds < 0 || !double.IsFinite(playbackRate) || playbackRate <= 0
            || endSeconds.HasValue && (!double.IsFinite(endSeconds.Value) || endSeconds.Value <= startSeconds))
            throw new ArgumentOutOfRangeException(nameof(startSeconds));
        Stop();
        if (disposed) return;
        image.Opacity = 1;
        image.RenderTransformOrigin = new(.5, .5);
        image.RenderTransform = mirrored ? new ScaleTransform(-1, 1) : Transform.Identity;
        Frames = 0; PositionSeconds = StartSeconds = startSeconds; PlaybackRate = playbackRate;
        double raw = image.ActualWidth > 0 ? image.ActualWidth : image.Width;
        var pixels = raw * VisualTreeHelper.GetDpi(image).DpiScaleX;
        int width = Math.Clamp((int)Math.Ceiling((double.IsFinite(pixels) && pixels > 0 ? pixels : 640) / 128) * 128, 256, 1280);
        int height = width * 9 / 16;
        cancellation = new();
        var token = cancellation.Token;
        var id = Interlocked.Increment(ref generation);
        // A preloaded opening shows its first frame right now and the rest on the clip's clock while the previous
        // pipeline is released and this one is created; the pipeline then continues where the opening ends.
        var opening = TakeOpening(file, startSeconds, endSeconds);
        Task<long> shown = Task.FromResult(0L);
        if (opening != null)
        {
            var clock = Stopwatch.StartNew();
            ShowOpening(opening, 0, file);
            shown = Task.Run(() => PlayOpening(opening, clock, id, token, playbackRate, file));
        }
        double resume = opening?.End ?? startSeconds;
        _ = Task.Run(async () =>
        {
            try
            {
                if (endSeconds.HasValue && resume >= endSeconds.Value)
                {
                    // The opening is the whole segment.
                    await shown;
                    token.ThrowIfCancellationRequested();
                    await image.Dispatcher.InvokeAsync(() => { if (id == Interlocked.Read(ref generation) && !disposed) completed(); });
                    return;
                }
                await serial.WaitAsync(token);
                try
                {
                    CompanionMedia.Initialize();
                    bool hardware = !ForceSoftware && !hardwareFailed && CompanionMedia.HardwareAvailable;
                    while (true)
                    {
                        try
                        {
                            await Decode(file, hardware, width, height, id, token, completed, resume, endSeconds, playbackRate, shown);
                            break;
                        }
                        catch (Exception ex) when (hardware && !token.IsCancellationRequested)
                        {
                            hardwareFailed = true;
                            hardware = false;
                            DiagnosticLog.Write(DiagnosticEvent.AnimationFailed, state: "hardware_fallback", error: ex);
                        }
                    }
                }
                finally { serial.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await image.Dispatcher.InvokeAsync(() => { if (id == Interlocked.Read(ref generation) && !disposed) Failed?.Invoke(ex.Message); });
            }
        });
    }

    // On the dispatcher. An opening frame goes through the same WPF bitmap and per-pixel hit testing as a decoded
    // frame, so clicks on its transparent pixels reach the window behind and a drag keeps its mouse capture.
    void ShowOpening(Opening opening, int index, string file)
    {
        Present(opening.Frames[index], opening.Width, opening.Height);
        Frames = index + 1;
        PositionSeconds = opening.Seconds[index];
        displayedFile = file;
        displayedSeconds = opening.Seconds[index];
        FramePresented?.Invoke(PositionSeconds);
    }

    // Shows the rest of the opening on the clip's clock, which stands still while paused, and returns once the
    // last frame has had its interval, with the number of frames shown. The opening is best effort: on any failure
    // the pipeline simply takes over.
    async Task<long> PlayOpening(Opening opening, Stopwatch clock, long id, CancellationToken token, double rate, string file)
    {
        long shown = 1;
        try
        {
            for (int index = 1; index < opening.Frames.Length; index++)
            {
                await WaitClock(clock, (opening.Seconds[index] - opening.Seconds[0]) / rate, token);
                int next = index;
                await image.Dispatcher.InvokeAsync(() =>
                {
                    if (id == Interlocked.Read(ref generation) && !disposed) ShowOpening(opening, next, file);
                }, System.Windows.Threading.DispatcherPriority.Render, token);
                shown++;
            }
            await WaitClock(clock, (opening.End - opening.Seconds[0]) / rate, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DiagnosticLog.Write(DiagnosticEvent.AnimationFailed, kind: "cache", state: "failed", error: ex); }
        return shown;
    }

    async Task WaitClock(Stopwatch clock, double target, CancellationToken token)
    {
        while (true)
        {
            if (paused) { clock.Stop(); await Task.Delay(15, token); continue; }
            clock.Start();
            double remaining = target - clock.Elapsed.TotalSeconds;
            if (remaining <= 0) return;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, remaining * 1000 - .5)), token);
        }
    }

    async Task Decode(string file, bool hardware, int width, int height, long id, CancellationToken token, Action completed, double start, double? end, double rate, Task<long> opening)
    {
        if (!hardware)
        {
            file = CompatFile(file);
            width = 640; height = 360;
            if (!File.Exists(file)) throw new IOException("找不到相容動畫素材：" + Path.GetFileName(file));
        }
        using var stream = new CompanionMediaStream(file, hardware, width, height, start, end, rate);
        token.ThrowIfCancellationRequested();
        Backend = stream.Description;
        OutputWidth = width; OutputHeight = height;
        // The prerolled pipeline waits for a preloaded opening to finish and starts where it ends.
        long presented = await opening;
        token.ThrowIfCancellationRequested();
        stream.SetPaused(paused);
        bool wasPaused = paused;
        bool verifiedVisible = !hardware;
        int silentFrames = 0;
        byte[][] pixelBuffers = [new byte[width * height * 4], new byte[width * height * 4]];
        int slot = 0;

        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (wasPaused != paused) { wasPaused = paused; stream.SetPaused(paused); }
            if (paused) { await Task.Delay(40, token); continue; }
            double seconds;
            byte[] currentPixels = pixelBuffers[slot];
            using (var frame = stream.Read())
            {
                if (frame == null) { if (stream.Ended) break; continue; }
                seconds = frame.Seconds;
                bool hasVisible = ExtractFrame(frame, currentPixels, !verifiedVisible);
                if (!verifiedVisible)
                {
                    if (hasVisible) verifiedVisible = true;
                    else if (++silentFrames >= 5) throw new IOException("GPU 硬體解碼沒有輸出可見的動畫像素。");
                }
            }
            slot ^= 1;
            await image.Dispatcher.InvokeAsync(() =>
            {
                if (id != Interlocked.Read(ref generation) || disposed) return;
                Present(currentPixels, width, height);
                Frames = ++presented;
                PositionSeconds = seconds;
                displayedFile = file;
                displayedSeconds = seconds;
                FramePresented?.Invoke(PositionSeconds);
            }, System.Windows.Threading.DispatcherPriority.Render);
        }
        if (presented == 0) throw new IOException("動畫沒有產生可顯示的影格。");
        await image.Dispatcher.InvokeAsync(() => { if (id == Interlocked.Read(ref generation) && !disposed) completed(); });
    }

    static unsafe bool ExtractFrame(CompanionMediaFrame frame, byte[] pixels, bool checkVisible)
    {
        bool visible = false;
        fixed (byte* dstBase = pixels)
        {
            for (int y = 0; y < frame.Height; y++)
            {
                uint* source = (uint*)((byte*)frame.Data + y * frame.Stride);
                uint* target = (uint*)(dstBase + y * frame.Width * 4);
                int x = 0;
                if (Vector256.IsHardwareAccelerated)
                {
                    for (; x <= frame.Width - 8; x += 8)
                    {
                        var vec = Vector256.Load(source + x);
                        var alpha = Vector256.ShiftRightLogical(vec, 24);
                        var transparent = Vector256.LessThan(alpha, Vector256.Create((uint)CompanionSpriteImage.AlphaThreshold));
                        (vec & ~(transparent & Vector256.Create(0xff000000u))).Store(target + x);
                        if (checkVisible && !visible && Vector256.LessThan(Vector256.Create(15u), alpha) != Vector256<uint>.Zero)
                            visible = true;
                    }
                }
                for (; x < frame.Width; x++)
                {
                    uint p = source[x];
                    uint a = p >> 24;
                    target[x] = a < CompanionSpriteImage.AlphaThreshold ? p & 0x00ffffffu : p;
                    if (checkVisible && a >= 16) visible = true;
                }
            }
        }
        return visible;
    }

    unsafe void Present(byte[] pixels, int width, int height)
    {
        if (bitmap == null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
        {
            // Preloaded openings (640x360) and the DPI-aware pipeline size alternate on every click or drag, so the
            // bitmap of the other size is kept for the next switch instead of being allocated again.
            var reuse = spareBitmap != null && spareBitmap.PixelWidth == width && spareBitmap.PixelHeight == height ? spareBitmap : null;
            spareBitmap = bitmap;
            bitmap = reuse ?? new(width, height, 96, 96, PixelFormats.Bgra32, null);
            image.Source = bitmap;
        }
        bitmap.Lock();
        try
        {
            fixed (byte* srcBase = pixels)
            {
                for (int y = 0; y < height; y++)
                    Buffer.MemoryCopy(srcBase + y * width * 4, (byte*)bitmap.BackBuffer + y * bitmap.BackBufferStride,
                        bitmap.BackBufferStride, width * 4);
            }
            bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally { bitmap.Unlock(); }
        if (image is CompanionSpriteImage sprite) sprite.UpdatePixels(pixels, width, height);
    }

    internal void Stop()
    {
        Interlocked.Increment(ref generation);
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
    }

    internal Task<BitmapSource?> CaptureSnapshotAsync()
    {
        if (!Backend.StartsWith("GPU") || displayedFile == null)
        {
            var copy = bitmap?.Clone();
            copy?.Freeze();
            return Task.FromResult<BitmapSource?>(copy);
        }
        string file = displayedFile;
        double seconds = displayedSeconds;
        return Task.Run<BitmapSource?>(() =>
        {
            using var capture = new CompanionMediaStream(file, true, 1280, 720, seconds, null, 1);
            capture.SetPaused(false);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                using var frame = capture.Read();
                if (frame == null) { if (capture.Ended) break; continue; }
                var pixels = new byte[frame.Width * frame.Height * 4];
                for (int y = 0; y < frame.Height; y++)
                    Marshal.Copy(frame.Data + y * frame.Stride, pixels, y * frame.Width * 4, frame.Width * 4);
                var result = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, pixels, frame.Width * 4);
                result.Freeze();
                return result;
            }
            throw new IOException("未能取得動畫截圖。");
        });
    }

    internal void RenderSnapshot(RenderTargetBitmap target, FrameworkElement layout, BitmapSource? snapshot)
    {
        _ = snapshot;
        target.Render(layout);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
        UpdateTimerResolution();
    }
}
