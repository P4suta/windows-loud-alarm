#:property LangVersion=latest
#:property Nullable=enable
#:property ImplicitUsings=enable
// scripts/Doctor.cs — .NET 10 file-based program.
//
// Diagnoses the toolchain. Run this first when something is off: mise pins, the
// dotnet SDKs mise exposes, just, and the MSVC C++ toolchain the Native AOT
// launcher needs at publish time (see CLAUDE.md → Toolchain).
//
// Run: `just doctor` (or `dotnet run scripts/Doctor.cs`).

using System.Diagnostics;

WriteHeader("mise current");
Run("mise", "current");
WriteHeader("mise installed tools");
Run("mise", "ls", "--installed");
WriteHeader("dotnet SDKs visible to mise");
Run("dotnet", "--list-sdks");
WriteHeader("just version");
Run("just", "--version");

WriteHeader("MSVC C++ toolchain (Native AOT launcher)");
CheckMsvc();
return 0;

// Native AOT's link step needs the MSVC toolset; ask vswhere for a VS install
// carrying the x64 C++ tools (see AssembleBundle.cs).
static void CheckMsvc()
{
    var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)")
        ?? @"C:\Program Files (x86)";
    var vswhere = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
    if (!File.Exists(vswhere))
    {
        WriteColor(
            "  MISSING - vswhere.exe not found; install Visual Studio / Build Tools with the C++ workload for 'just publish'",
            ConsoleColor.Yellow);
        return;
    }

    var version = Capture(vswhere,
        "-latest", "-products", "*",
        "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
        "-property", "installationVersion");
    var firstLine = version?.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

    if (!string.IsNullOrEmpty(firstLine))
    {
        WriteColor($"  OK - VS with C++ tools {firstLine} (needed by 'just publish')", ConsoleColor.Green);
    }
    else
    {
        WriteColor(
            "  MISSING - install the 'Desktop development with C++' workload; 'just publish' (Native AOT) will fail without it",
            ConsoleColor.Yellow);
    }
}

// Run a diagnostic command with inherited stdout/stderr. Best-effort: a missing
// tool prints its own error and we keep going (this is a diagnostic, not a gate).
static void Run(string file, params string[] arguments)
{
    try
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in arguments)
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi);
        p?.WaitForExit();
    }
    catch (System.ComponentModel.Win32Exception)
    {
        WriteColor($"  (could not run '{file}' — not on PATH?)", ConsoleColor.Yellow);
    }
}

// Run a command and capture its stdout (for parsing).
static string? Capture(string file, params string[] arguments)
{
    try
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        foreach (var a in arguments)
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi);
        if (p is null)
        {
            return null;
        }
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return null;
    }
}

static void WriteHeader(string text)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"── {text} ──");
    Console.ForegroundColor = prev;
}

static void WriteColor(string text, ConsoleColor color)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ForegroundColor = prev;
}
