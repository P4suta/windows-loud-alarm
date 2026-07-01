#:property LangVersion=latest
#:property Nullable=enable
#:property ImplicitUsings=enable
// scripts/ArtifactSummary.cs — .NET 10 file-based program.
//
// Prints the assembled bundle's root layout + total footprint. Standalone so you
// can rerun it without rebuilding when you just want to see what's on disk. Part of
// the `just full` pipeline.
//
// Run: `just artifact-summary` (or `dotnet run scripts/ArtifactSummary.cs`).

using System.Globalization;

// `just` runs us from the repo root; that's our anchor (see MeasureBuild.cs).
var repo = Directory.GetCurrentDirectory();
var bundle = Path.Combine(repo, "publish", "dist", "Alarm");

Console.WriteLine();
WriteColor("── publish/dist/Alarm ──", ConsoleColor.Cyan);

if (!File.Exists(Path.Combine(bundle, "Alarm.exe")))
{
    WriteColor("  (no bundle — run 'just publish' or 'just full' first)", ConsoleColor.Yellow);
    return 0;
}

var rootFiles = new DirectoryInfo(bundle)
    .EnumerateFiles()
    .OrderBy(f => f.Name, StringComparer.Ordinal)
    .ToList();

var nameWidth = Math.Max("Name".Length, rootFiles.Max(f => f.Name.Length));
var lenWidth = Math.Max("Length".Length, rootFiles.Max(f => f.Length.ToString("N0", CultureInfo.InvariantCulture).Length));
Console.WriteLine($"  {"Name".PadRight(nameWidth)}  {"Length".PadLeft(lenWidth)}  LastWriteTime");
Console.WriteLine($"  {new string('-', nameWidth)}  {new string('-', lenWidth)}  -------------");
foreach (var f in rootFiles)
{
    var len = f.Length.ToString("N0", CultureInfo.InvariantCulture);
    var when = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    Console.WriteLine($"  {f.Name.PadRight(nameWidth)}  {len.PadLeft(lenWidth)}  {when}");
}

var all = new DirectoryInfo(bundle).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
var totalMb = all.Sum(f => f.Length) / (1024.0 * 1024.0);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"  Total: {totalMb:N1} MB across {all.Count} files (root launcher + app/)"));
return 0;

static void WriteColor(string text, ConsoleColor color)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ForegroundColor = prev;
}
