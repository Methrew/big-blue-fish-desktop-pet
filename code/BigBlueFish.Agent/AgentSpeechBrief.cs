// Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
namespace BigBlueFish.Agent;

public sealed record AgentPromptImage(string MimeType, string Data)
{
    public static AgentPromptImage FromDataUrl(string value)
    {
        int comma = value.IndexOf(',');
        if (!value.StartsWith("data:image/", StringComparison.Ordinal) || comma < 0
            || !value[..comma].EndsWith(";base64", StringComparison.Ordinal))
            throw new InvalidDataException("前景截圖的編碼無效。");
        return new(value[5..value.IndexOf(';')], value[(comma + 1)..]);
    }
}

public sealed record AgentSpeechBrief(string Text, AgentPromptImage? Image = null, string? MaterialKind = null);
