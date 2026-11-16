// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace BigBlueFish.Windows;

// Preloaded openings, as CompanionVideoPlayer keeps them on Windows: click and drag clips keep their first quarter second
// decoded, so a click or a drag shows motion at once instead of holding the old frame while the previous pipeline is
// released and the next one is created, prerolled and seeked. Openings are the 640x360 compatibility clips decoded on
// the CPU (a quarter second; on a Mac that decodes the 1280x720 clips in hardware, the clip continues at that size), and
// never more than PreloadBudget bytes for all of them together.
internal sealed partial class LinuxVideoPlayer
{
    const double OpeningSeconds = .25, OpeningInterval = 1.0 / 24;
    const int OpeningFrames = 6, OpeningWidth = 640, OpeningHeight = 360;
    const long PreloadBudget = 64L << 20;
    readonly SemaphoreSlim preloadGate = new(1);
    // UI thread only; null while an opening is being decoded, or when it could not be.
    readonly Dictionary<(string File, double Start), Opening?> openings = new();
    long preloadedBytes;
    sealed record Opening(byte[][] Frames, double[] Seconds, double End);

    internal bool HasOpening(string file, double startSeconds = 0) => openings.TryGetValue((file, startSeconds), out var opening) && opening != null;
    internal long PreloadedBytes => Interlocked.Read(ref preloadedBytes);

    // On the UI thread, with the path Play will be given. Openings are decoded one at a time in the background.
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
                if (opening != null) await Dispatcher.UIThread.InvokeAsync(() => { if (!disposed) openings[(file, startSeconds)] = opening; });
            }
            catch (Exception error) { DiagnosticLog.Write(DiagnosticEvent.AnimationFailed, kind: "cache", state: "unavailable", error: error); }
        });
    }

    Opening? DecodeOpening(string file, double start)
    {
        string source = Path.Combine(Path.GetDirectoryName(file)!, "compat", Path.GetFileName(file));
        if (!File.Exists(source)) throw new FileNotFoundException("找不到動畫素材", Path.GetFileName(source));
        const int frameBytes = OpeningWidth * OpeningHeight * 4;
        int limit = (int)Math.Min((PreloadBudget - Interlocked.Read(ref preloadedBytes)) / frameBytes, OpeningFrames);
        if (limit <= 0) return null;
        double end = start + OpeningSeconds;
        var frames = new List<byte[]>(); var seconds = new List<double>();
        CompanionMedia.Initialize();
        using (var stream = new CompanionMediaStream(source, false, OpeningWidth, OpeningHeight, start, end, 1))
        {
            stream.SetPaused(false);
            var watch = Stopwatch.StartNew();
            while (frames.Count < limit && watch.Elapsed.TotalSeconds < 5 && !disposed)
            {
                using var frame = stream.Read();
                if (frame == null) { if (stream.Ended) break; continue; }
                if (frame.Seconds >= end) break;
                // Stored the way playback presents frames: compression residue below alpha 8 cleared, ready for hit testing.
                var pixels = new byte[frameBytes];
                for (int y = 0; y < OpeningHeight; y++) Marshal.Copy(frame.Data + y * frame.Stride, pixels, y * OpeningWidth * 4, OpeningWidth * 4);
                for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] < 8) pixels[i] = 0;
                frames.Add(pixels); seconds.Add(frame.Seconds);
            }
        }
        if (frames.Count == 0) return null;
        Interlocked.Add(ref preloadedBytes, (long)frames.Count * frameBytes);
        // Cut short by the budget, the opening covers its last frame's interval only.
        double covered = frames.Count < limit ? end : Math.Min(end, seconds[^1] + OpeningInterval);
        return new(frames.ToArray(), seconds.ToArray(), covered);
    }

    // The preloaded opening of this clip, cut at its end; none while paused (a hidden pet shows no frames).
    Opening? TakeOpening(string file, double start, double? end)
    {
        if (Paused || !openings.TryGetValue((file, start), out var opening) || opening == null) return null;
        if (end is not { } stop || opening.End <= stop) return opening;
        int count = Array.FindIndex(opening.Seconds, s => s >= stop);
        if (count == 0) return null;
        if (count < 0) count = opening.Frames.Length;
        return opening with { Frames = opening.Frames[..count], Seconds = opening.Seconds[..count], End = stop };
    }

    // On the UI thread: through the same bitmap, click-through shape and position listeners as a decoded frame.
    void ShowOpening(Opening opening, int index, bool mirror) => Present(opening.Frames[index], OpeningWidth, OpeningHeight, mirror, opening.Seconds[index], index + 1);

    // Shows the rest of the opening on the clip's clock, which stands still while paused, and returns once the last frame
    // has had its interval, with the number of frames shown. Best effort: on any failure the pipeline simply takes over.
    async Task<long> PlayOpening(Opening opening, Stopwatch clock, long id, CancellationToken token, double rate, bool mirror)
    {
        long shown = 1;
        try
        {
            for (int index = 1; index < opening.Frames.Length; index++)
            {
                await WaitClock(clock, (opening.Seconds[index] - opening.Seconds[0]) / rate, token);
                int next = index;
                await Dispatcher.UIThread.InvokeAsync(() => { if (id == generation && !disposed) ShowOpening(opening, next, mirror); });
                shown++;
            }
            await WaitClock(clock, (opening.End - opening.Seconds[0]) / rate, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { DiagnosticLog.Write(DiagnosticEvent.AnimationFailed, kind: "cache", state: "failed", error: error); }
        return shown;
    }

    async Task WaitClock(Stopwatch clock, double target, CancellationToken token)
    {
        while (true)
        {
            if (Paused) { clock.Stop(); await Task.Delay(15, token); continue; }
            clock.Start();
            double remaining = target - clock.Elapsed.TotalSeconds;
            if (remaining <= 0) return;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, remaining * 1000)), token);
        }
    }
}
