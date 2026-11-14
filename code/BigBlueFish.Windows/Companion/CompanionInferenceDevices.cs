// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BigBlueFish.Windows;

// Observe actual model buffers, not all devices enumerated by the backend. Each
// runner owns a separate observer so a retired process cannot change its successor.
internal sealed class CompanionInferenceDevices
{
    private readonly object gate = new();
    private readonly Dictionary<string, string> names = new(StringComparer.Ordinal);
    private readonly HashSet<string> modelDevices = new(StringComparer.Ordinal);
    private bool cpuWeights;
    private static readonly Regex Device = new(@"llama_prepare_model_devices:\s+using device (\S+) \((.*)\) \([^)]*\) - \d+ MiB free", RegexOptions.Compiled);
    private static readonly Regex Buffer = new(@"load_tensors:\s+(\S+) model buffer size\s*=\s*([\d.]+) MiB", RegexOptions.Compiled);

    internal void Observe(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var device = Device.Match(line);
        if (device.Success)
        {
            lock (gate) names[device.Groups[1].Value] = device.Groups[2].Value;
            return;
        }
        var buffer = Buffer.Match(line);
        if (!buffer.Success || !double.TryParse(buffer.Groups[2].Value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out double size) || size <= 0) return;
        string id = buffer.Groups[1].Value;
        lock (gate)
        {
            if (id is "CPU" or "CPU_Mapped") cpuWeights = true;
            // Host buffers belong to system RAM; they do not establish GPU use.
            else if (!id.EndsWith("_Host", StringComparison.Ordinal)) modelDevices.Add(id);
        }
    }

    internal string Describe(bool ready)
    {
        lock (gate)
        {
            if (modelDevices.Count > 0)
                return "GPU：" + string.Join("、", modelDevices.OrderBy(id => id, StringComparer.Ordinal)
                    .Select(id => names.TryGetValue(id, out string? name) ? name + "（" + id + "）" : id));
            if (cpuWeights) return "CPU";
            return ready ? "執行裝置未確認" : "正在確認執行裝置";
        }
    }
}
