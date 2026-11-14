// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
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
