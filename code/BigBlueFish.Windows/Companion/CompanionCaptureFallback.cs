// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BigBlueFish.Windows;

internal readonly record struct CompanionCaptureResult<T>(T? Image, bool FullScreen) where T : class;

internal static class CompanionCaptureFallback
{
#if COMPANION_MAC
    // screencapture with one output file takes the main display only (MacCapture.cs).
    internal const string ScreenIdentity = "本輪畫面是主螢幕的全螢幕截圖（不含其他顯示器），可能同時包含前景程式、背景視窗和桌面。";
#else
    internal const string ScreenIdentity = "本輪畫面是所有螢幕的全螢幕截圖，可能同時包含前景程式、背景視窗和桌面。";
#endif

    internal static async Task<CompanionCaptureResult<T>> ReadAsync<T>(Func<Task<T?>> foreground,
        Func<Task<T?>> screen, CancellationToken token) where T : class
    {
        foreach (bool fullScreen in new[] { false, true })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var image = await (fullScreen ? screen() : foreground()).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (image != null) return new(image, fullScreen);
                DiagnosticLog.Write(DiagnosticEvent.ScreenReadFallback, kind: fullScreen ? "screen" : "foreground", state: "empty");
            }
            catch (Exception error) when (error is not OperationCanceledException && !token.IsCancellationRequested)
            {
                DiagnosticLog.Write(DiagnosticEvent.ScreenReadFallback, kind: fullScreen ? "screen" : "foreground", error: error);
            }
        }
        return new(null, false);
    }
}
