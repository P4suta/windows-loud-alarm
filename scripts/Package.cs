#:property LangVersion=latest
#:property Nullable=enable
#:property ImplicitUsings=enable
// scripts/Package.cs — .NET 10 file-based program.
//
// Zips the assembled bundle (publish/dist/Alarm) and writes SHA256SUMS.txt for a
// GitHub Release. Standalone: run `just publish` first, then `just package vX.Y.Z`.
// The zip holds the bundle CONTENTS (root Alarm.exe + README + BUILDINFO + app/),
// so extracting it drops the launcher right where the user can see it. Same output
// names CI's release.yml build job produces (it calls `just package`), so a local
// package and a CI package have byte-identical layout.
//
// Run: `just package vX.Y.Z` (or `dotnet run scripts/Package.cs -- vX.Y.Z`).

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: just package vX.Y.Z");
    return 1;
}

var tag = args[0];
if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$"))
{
    Console.Error.WriteLine($"package requires a vX.Y.Z tag, got '{tag}'");
    return 1;
}

// Anchor on the repo root (see MeasureBuild.cs).
var repo = Directory.GetCurrentDirectory();
var bundle = Path.Combine(repo, "publish", "dist", "Alarm");
if (!File.Exists(Path.Combine(bundle, "Alarm.exe")))
{
    Console.Error.WriteLine("publish/dist/Alarm not found - run 'just publish' first");
    return 1;
}

var zipName = $"Alarm-{tag}-win-x64.zip";
var zipPath = Path.Combine(repo, "publish", zipName);
var sumsPath = Path.Combine(repo, "publish", "SHA256SUMS.txt");

File.Delete(zipPath);
File.Delete(sumsPath);

// includeBaseDirectory: false → the bundle's contents land at the zip root
// (Alarm.exe, README.txt, BUILDINFO.txt, app/…), matching the old
// `Compress-Archive -Path publish/dist/Alarm/*`.
ZipFile.CreateFromDirectory(bundle, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zipPath)));
// coreutils `sha256sum -c` format: "<hash>  <filename>\n" (two spaces).
File.WriteAllText(sumsPath, $"{hash}  {zipName}\n", Encoding.ASCII);

var prev = Console.ForegroundColor;
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"packaged publish/{zipName} ({hash})");
Console.ForegroundColor = prev;
return 0;
