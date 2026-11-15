// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BigBlueFish.Windows;

// Taiwan edition speech bubble on Linux and macOS: ShowText converts every line with CompanionTraditional and
// Partial shows streamed snapshots through ConvertPartial, so Simplified characters never reach the screen.
internal sealed partial class LinuxPetWindow
{
    // A finished model reply. When it was streamed, ConvertPartial held back the last run of Chinese, so the
    // converted final text is longer than the last snapshot and the bubble is measured again before it shows.
    void FinishReply(string text)
    {
        if (!streamed) { ShowText(text, false, fromModel: true); return; }
        fullText = CompanionTraditional.Convert(text); bubbleText.Text = fullText; ResetDwell(); MoveBubble();
    }

    // Layoutable.Measure returns the cached size while the element is valid and the available size is unchanged,
    // and InvalidateMeasure does not reach the ancestors; without this the frame keeps the previous text's size.
    // The cache stays in use while the text and scale are the ones it was measured with (drags move it per event).
    (string Text, double Scale)? bubbleMeasured;
    void InvalidateBubbleMeasure()
    {
        if (bubbleMeasured == (fullText, Preferences.BubbleScale)) return;
        bubbleMeasured = (fullText, Preferences.BubbleScale);
        bubbleText.InvalidateMeasure(); (bubbleText.Parent as Layoutable)?.InvalidateMeasure(); bubbleFrame.InvalidateMeasure();
    }

    // --test-mode only: "stream <text>" replays <text> as a streamed reply through the real Partial and FinishReply,
    // then sends one late snapshot, and writes bubble-status.json with every text the bubble showed and the bubble
    // size next to a fresh measure of the final text. Partial needs ModelEnabled, no Puppet mode and AC power.
    async Task<bool> BubbleTestCommand(string command)
    {
        if (!command.StartsWith("stream ")) return false;
        if (generating) return true;
        string text = command[7..]; var shown = new List<string>();
        generating = true; long version = ++speechVersion; streamed = false;
        try
        {
            var ends = StringInfo.ParseCombiningCharacters(text).Skip(1).Append(text.Length);
            foreach (int end in ends)
            {
                Partial(text[..end], version); await Task.Delay(30);
                if (bubbleText.Text is { } current && (shown.Count == 0 || shown[^1] != current)) shown.Add(current);
            }
            FinishReply(text);
        }
        finally { generating = false; }
        Partial(text[..Math.Min(text.Length, 2)], version); // arrives after the reply ended and must be dropped
        await Task.Delay(300);
        var fresh = new TextBlock { Text = fullText, TextWrapping = TextWrapping.Wrap, FontFamily = bubbleText.FontFamily, FontSize = bubbleText.FontSize,
            MaxWidth = bubbleText.MaxWidth, Margin = bubbleText.Margin };
        var padding = bubbleFrame.Padding; fresh.Measure(new Size(320 * Preferences.BubbleScale - padding.Left - padding.Right, double.PositiveInfinity));
        File.WriteAllText("bubble-status.json", JsonSerializer.Serialize(new {
            streamed, shown, text = bubbleText.Text, fullText, bubbleVisible = bubbleWindow.IsVisible,
            bubbleWidth = bubbleWindow.Width, bubbleHeight = bubbleWindow.Height,
            textWidth = bubbleText.Bounds.Width, textHeight = bubbleText.Bounds.Height,
            freshTextWidth = fresh.DesiredSize.Width - fresh.Margin.Left - fresh.Margin.Right, freshTextHeight = fresh.DesiredSize.Height,
            freshBubbleHeight = fresh.DesiredSize.Height + padding.Top + padding.Bottom }));
        return true;
    }
}
