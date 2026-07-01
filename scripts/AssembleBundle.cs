#:property LangVersion=latest
#:property Nullable=enable
#:property ImplicitUsings=enable
// scripts/AssembleBundle.cs — .NET 10 file-based program.
//
// Assembles the downloadable bundle in publish/dist/Alarm: a root launcher Alarm.exe
// + README.txt + BUILDINFO.txt, with the self-contained app isolated under app/. See
// docs/ARCHITECTURE.md "Distribution layout". Self-verifies, so a bundle that could
// not launch fails here rather than downstream.
//
// Run: `just publish` (or `dotnet run scripts/AssembleBundle.cs`).

using System.Diagnostics;
using System.Globalization;
using System.Text;

// Anchor on the repo root (see MeasureBuild.cs).
var repo = Directory.GetCurrentDirectory();
var dist = Path.Combine(repo, "publish", "dist", "Alarm");
var app = Path.Combine(dist, "app");
var presentation = Path.Combine(repo, "src", "Alarm.Presentation", "Alarm.Presentation.csproj");
var launcher = Path.Combine(repo, "src", "Alarm.Launcher", "Alarm.Launcher.csproj");

// Native AOT's link step needs vswhere.exe on PATH to find the MSVC toolset; put it
// there so `just publish` works outside a VS developer prompt (no-op on CI runners).
var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
var vswhereDir = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer");
if (File.Exists(Path.Combine(vswhereDir, "vswhere.exe")))
{
    var path = Environment.GetEnvironmentVariable("PATH") ?? "";
    if (!path.Contains(vswhereDir, StringComparison.OrdinalIgnoreCase))
    {
        Environment.SetEnvironmentVariable("PATH", vswhereDir + Path.PathSeparator + path);
    }
}

// Fresh bundle. Best-effort clean: a running app can lock files; the publishes
// overwrite and the self-verify at the end is the real gate.
TryDelete(dist);
Directory.CreateDirectory(app);

// 1) The real app: self-contained win-x64 into app/. Relies on WindowsAppRuntime 2.x
//    on the target (see docs/ARCHITECTURE.md for why WindowsAppSDKSelfContained isn't set).
WriteColor("-> publishing app -> publish/dist/Alarm/app", ConsoleColor.Cyan);
Dotnet("publish", presentation, "-c", "Release", "-r", "win-x64", "-p:Platform=x64", "--self-contained", "true", "-o", app);

// 2) The launcher: a tiny Native AOT Alarm.exe at the bundle root. Publish to a
//    staging dir and copy only the .exe — the .pdb/.xml stay out of the bundle.
WriteColor("-> publishing launcher (Native AOT) -> publish/dist/Alarm/Alarm.exe", ConsoleColor.Cyan);
var stage = Path.Combine(repo, "publish", ".launcher-stage");
TryDelete(stage);
Dotnet("publish", launcher, "-c", "Release", "-r", "win-x64", "-p:Platform=x64", "-o", stage);
File.Copy(Path.Combine(stage, "Alarm.exe"), Path.Combine(dist, "Alarm.exe"), overwrite: true);
TryDelete(stage);

// 3) README.txt + BUILDINFO.txt at the root (UTF-8 BOM + CRLF for Notepad).
WriteBundleText(Path.Combine(dist, "README.txt"), Readme());

// BUILDINFO keeps a downloaded copy identifiable after the .zip name is lost.
// Source Link suffixes ProductVersion with "+<full-sha>", so drop that; git may be
// absent in a source tarball.
var appExe = Path.Combine(app, "Alarm.exe");
var info = FileVersionInfo.GetVersionInfo(appExe);
var version = (info.ProductVersion ?? info.FileVersion ?? "0.0.0").Split('+', 2)[0];
var sha = GitOrDefault("unknown", "rev-parse", "--short", "HEAD");
var commitDate = GitOrDefault("unknown", "show", "-s", "--format=%cs", "HEAD");
var buildinfo = $"Alarm {version}\ncommit {sha} ({commitDate})\ntarget win-x64\nhttps://github.com/P4suta/windows-loud-alarm\n";
WriteBundleText(Path.Combine(dist, "BUILDINFO.txt"), buildinfo);

// 4) Self-verify: a bundle missing the launcher or the apphost would not launch.
string[] required =
[
    Path.Combine(dist, "Alarm.exe"), // the launcher the user double-clicks
    Path.Combine(app, "Alarm.exe"),  // the real apphost the launcher starts
    Path.Combine(app, "Alarm.dll"),  // the managed entry the apphost loads
];
var missing = required.Where(p => !File.Exists(p)).ToList();
if (missing.Count > 0)
{
    Console.Error.WriteLine($"bundle at {dist} is missing:\n  {string.Join("\n  ", missing)}\nit would not launch");
    return 1;
}

var appFiles = new DirectoryInfo(app).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
var totalMb = appFiles.Sum(f => f.Length) / (1024.0 * 1024.0);
Console.WriteLine();
WriteColor($"OK bundle assembled: publish/dist/Alarm  (Alarm {version}, {sha})", ConsoleColor.Green);
WriteColor(
    string.Create(CultureInfo.InvariantCulture, $"  root: Alarm.exe + README.txt + BUILDINFO.txt   app/: {appFiles.Count} files, {totalMb:N1} MB"),
    ConsoleColor.DarkGray);
return 0;

// ─── Helpers ──────────────────────────────────────────────────────────────────

static void Dotnet(params string[] arguments)
{
    var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false };
    foreach (var a in arguments)
    {
        psi.ArgumentList.Add(a);
    }
    using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start dotnet");
    p.WaitForExit();
    if (p.ExitCode != 0)
    {
        throw new InvalidOperationException($"dotnet {arguments[0]} exited with code {p.ExitCode}");
    }
}

static string GitOrDefault(string fallback, params string[] arguments)
{
    try
    {
        var psi = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var a in arguments)
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi);
        if (p is null)
        {
            return fallback;
        }
        var output = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return (p.ExitCode == 0 && output.Length > 0) ? output : fallback;
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return fallback;
    }
}

static void TryDelete(string dir)
{
    try
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
    catch (IOException)
    {
        // Best-effort — a running app can lock files; the publish overwrites and
        // the self-verify is the real gate.
    }
    catch (UnauthorizedAccessException)
    {
    }
}

static void WriteBundleText(string path, string text)
{
    var crlf = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
    File.WriteAllText(path, crlf, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
}

static void WriteColor(string text, ConsoleColor color)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ForegroundColor = prev;
}

// End-user README dropped at the bundle root. UTF-8 BOM + CRLF via WriteBundleText.
static string Readme() => @"Alarm — a loud Windows alarm clock
==================================

>> To start: double-click  Alarm.exe  (here, in this folder).

That's it. The app and all of its runtime files live in the  app\  subfolder;
this launcher simply starts them. Keep this folder's structure intact — to move
or remove the app, copy or delete the whole folder.

If the app does not start, install the Windows App Runtime 2.x (a small Microsoft
runtime this build expects to already be on the machine):
  https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads

https://github.com/P4suta/windows-loud-alarm
";

