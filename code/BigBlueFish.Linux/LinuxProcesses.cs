using System.Diagnostics;

namespace BigBlueFish.Windows;

internal static class LinuxProcesses
{
    internal static Process Start(ProcessStartInfo original)
    {
        var guard = Path.Combine(AppContext.BaseDirectory, "runtime", "BigBlueFish.RunnerGuard");
        if (!File.Exists(guard)) throw new IOException("缺少隨程式安裝的處理程序結束保護元件。");
        var command = original.FileName; original.FileName = guard;
        original.ArgumentList.Insert(0, command);
        return Process.Start(original) ?? throw new IOException("無法啟動本機元件。");
    }
}
internal sealed class CompanionJob : IDisposable
{
    private readonly List<Process> children = [];
    internal bool Attach(Process process) { children.Add(process); return true; }
    public void Dispose() { foreach (var child in children) try { if (!child.HasExited) child.Kill(true); } catch { } children.Clear(); }
}
