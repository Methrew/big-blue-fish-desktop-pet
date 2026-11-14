// Modified by Methrew on 2026-11-09 to merge the taiwan-edition fork with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
using System.Text.Json;

namespace BigBlueFish.Agent;

public sealed record AgentWakeup(string Id, string Harness, string Session, string Purpose, DateTimeOffset Due, int? RepeatSeconds);

/// <summary>Only executes explicit Agent reservations. Missed periods coalesce; waiting never calls a model.</summary>
public sealed class AgentWakeups
{
    private readonly object gate = new();
    private readonly string path;
    private readonly List<AgentWakeup> jobs;
    public AgentWakeups(string root)
    {
        path = Path.Combine(root, "wakeups.json");
        try { jobs = JsonSerializer.Deserialize<List<AgentWakeup>>(File.ReadAllText(path)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException) { jobs = []; }
    }
    public AgentWakeup Schedule(string harness, string session, string purpose, int delaySeconds, int? repeatSeconds, string? id, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(purpose) || purpose.Length > 1000) throw new ArgumentException("purpose 必須是 1–1000 個字元。");
        if (delaySeconds is < 30 or > 604800 || repeatSeconds is < 30 or > 604800) throw new ArgumentException("預約間隔是 30–604800 秒。");
        if (id?.Length > 100) throw new ArgumentException("預約編號太長。");
        lock (gate)
        {
            id ??= Guid.NewGuid().ToString("N");
            int existing = jobs.FindIndex(x => x.Id == id && x.Harness == harness && x.Session == session);
            if (existing < 0 && jobs.Count >= 32) throw new InvalidOperationException("最多保留 32 個預約。");
            var job = new AgentWakeup(id, harness, session, purpose, now.AddSeconds(delaySeconds), repeatSeconds);
            if (existing >= 0) jobs[existing] = job; else jobs.Add(job);
            Save(); return job;
        }
    }
    public AgentWakeup[] List(string harness, string session) { lock (gate) return jobs.Where(x => x.Harness == harness && x.Session == session).OrderBy(x => x.Due).ToArray(); }
    public bool Cancel(string harness, string session, string id)
    {
        lock (gate) { bool removed = jobs.RemoveAll(x => x.Harness == harness && x.Session == session && x.Id == id) > 0; if (removed) Save(); return removed; }
    }
    public void Clear(string? harness = null, string? session = null)
    {
        lock (gate) { if (jobs.RemoveAll(x => (harness == null || x.Harness == harness) && (session == null || x.Session == session)) > 0) Save(); }
    }
    public AgentWakeup[] PeekDue(string harness, string session, DateTimeOffset now) => List(harness, session).Where(x => x.Due <= now).ToArray();
    public void Acknowledge(AgentWakeup delivered, DateTimeOffset now)
    {
        lock (gate)
        {
            int i = jobs.FindIndex(x => x == delivered);
            if (i < 0) return;
            if (delivered.RepeatSeconds is int repeat) jobs[i] = delivered with { Due = now.AddSeconds(repeat) };
            else jobs.RemoveAt(i);
            Save();
        }
    }
    private void Save() => AtomicText.Write(path, JsonSerializer.Serialize(jobs, new JsonSerializerOptions { WriteIndented = true }));
}
