#:property LangVersion=latest
#:property Nullable=enable
#:property ImplicitUsings=enable
#:package MSBuild.StructuredLogger
// scripts/AnalyzeBinlog.cs — .NET 10 file-based program.
//
// Reads artifacts/perf/build.binlog (produced by `just measure-binlog` or any
// `dotnet build -bl:…`) and prints per-target and per-task elapsed-time rankings.
//
// Run: `just analyze-binlog` (or `dotnet run scripts/AnalyzeBinlog.cs [-- path/to/build.binlog]`).
// Output: docs to stdout + a markdown summary at artifacts/perf/binlog-summary.md.

using System.Globalization;
using System.Text;
using Microsoft.Build.Logging.StructuredLogger;

var binlog = args.Length > 0
    ? args[0]
    : Path.Combine("artifacts", "perf", "build.binlog");

if (!File.Exists(binlog))
{
    Console.Error.WriteLine($"binlog not found: {binlog}");
    Console.Error.WriteLine("Run `just measure-binlog` (or `just measure`) first to produce one.");
    return 1;
}

Console.WriteLine("── analyze-binlog ──");
Console.WriteLine($"  source : {binlog}");
Console.WriteLine();

var build = BinaryLog.ReadBuild(binlog);
build.VisitAllChildren<Project>(_ => { });   // Force materialize tree.

// ── Target rollup ──────────────────────────────────────────────────────────────
// Per-target totals (summed across every project, every invocation).
var targets = new Dictionary<string, TargetStats>(StringComparer.Ordinal);
build.VisitAllChildren<Target>(t =>
{
    if (string.IsNullOrEmpty(t.Name))
    {
        return;
    }
    var ms = t.Duration.TotalMilliseconds;
    if (!targets.TryGetValue(t.Name, out var s))
    {
        s = new TargetStats(t.Name);
        targets[t.Name] = s;
    }
    s.TotalMs += ms;
    s.Calls += 1;
    if (ms > s.MaxMs)
    {
        s.MaxMs = ms;
    }
});

// ── Task rollup ────────────────────────────────────────────────────────────────
var tasks = new Dictionary<string, TaskStats>(StringComparer.Ordinal);
build.VisitAllChildren<Microsoft.Build.Logging.StructuredLogger.Task>(t =>
{
    if (string.IsNullOrEmpty(t.Name))
    {
        return;
    }
    var ms = t.Duration.TotalMilliseconds;
    if (!tasks.TryGetValue(t.Name, out var s))
    {
        s = new TaskStats(t.Name);
        tasks[t.Name] = s;
    }
    s.TotalMs += ms;
    s.Calls += 1;
    if (ms > s.MaxMs)
    {
        s.MaxMs = ms;
    }
});

// ── Per-project rollup ─────────────────────────────────────────────────────────
var projects = new List<(string Name, double Ms)>();
build.VisitAllChildren<Project>(p =>
{
    if (!string.IsNullOrEmpty(p.Name))
    {
        projects.Add((p.Name, p.Duration.TotalMilliseconds));
    }
});

var topTargets = targets.Values
    .OrderByDescending(s => s.TotalMs)
    .Take(20)
    .ToList();

var topTasks = tasks.Values
    .OrderByDescending(s => s.TotalMs)
    .Take(20)
    .ToList();

var topProjects = projects
    .GroupBy(p => p.Name, StringComparer.Ordinal)
    .Select(g => (Name: g.Key, Ms: g.Sum(x => x.Ms), Calls: g.Count()))
    .OrderByDescending(g => g.Ms)
    .Take(20)
    .ToList();

// ── Render summary ────────────────────────────────────────────────────────────
var md = new StringBuilder();
md.AppendLine("# Binlog analysis");
md.AppendLine();
md.Append("Source: `").Append(binlog).AppendLine("`");
md.Append(CultureInfo.InvariantCulture, $"Captured: {File.GetLastWriteTime(binlog):yyyy-MM-dd HH:mm:ss}").AppendLine();
md.AppendLine();

md.AppendLine("## Per-project elapsed (sum across invocations)");
md.AppendLine();
md.AppendLine("| ms | calls | project |");
md.AppendLine("|---:|---:|---|");
foreach (var p in topProjects)
{
    md.Append(CultureInfo.InvariantCulture, $"| {p.Ms,8:0} | {p.Calls,5} | `{p.Name}` |").AppendLine();
}
md.AppendLine();

md.AppendLine("## Top 20 targets by total elapsed");
md.AppendLine();
md.AppendLine("| total ms | calls | max ms / call | target |");
md.AppendLine("|---:|---:|---:|---|");
foreach (var t in topTargets)
{
    md.Append(CultureInfo.InvariantCulture, $"| {t.TotalMs,8:0} | {t.Calls,5} | {t.MaxMs,8:0} | `{t.Name}` |").AppendLine();
}
md.AppendLine();

md.AppendLine("## Top 20 tasks by total elapsed");
md.AppendLine();
md.AppendLine("| total ms | calls | max ms / call | task |");
md.AppendLine("|---:|---:|---:|---|");
foreach (var t in topTasks)
{
    md.Append(CultureInfo.InvariantCulture, $"| {t.TotalMs,8:0} | {t.Calls,5} | {t.MaxMs,8:0} | `{t.Name}` |").AppendLine();
}

var outDir = Path.GetDirectoryName(binlog) ?? ".";
Directory.CreateDirectory(outDir);
var outPath = Path.Combine(outDir, "binlog-summary.md");
File.WriteAllText(outPath, md.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

Console.WriteLine("Top 10 targets by total elapsed:");
foreach (var t in topTargets.Take(10))
{
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {t.TotalMs,7:0} ms  (×{t.Calls,-3} max {t.MaxMs,6:0})  {t.Name}"));
}
Console.WriteLine();
Console.WriteLine("Top 10 tasks by total elapsed:");
foreach (var t in topTasks.Take(10))
{
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {t.TotalMs,7:0} ms  (×{t.Calls,-3} max {t.MaxMs,6:0})  {t.Name}"));
}
Console.WriteLine();
Console.WriteLine($"Wrote {outPath}");
return 0;

file sealed class TargetStats(string name)
{
    public string Name { get; } = name;
    public double TotalMs { get; set; }
    public int Calls { get; set; }
    public double MaxMs { get; set; }
}

file sealed class TaskStats(string name)
{
    public string Name { get; } = name;
    public double TotalMs { get; set; }
    public int Calls { get; set; }
    public double MaxMs { get; set; }
}
