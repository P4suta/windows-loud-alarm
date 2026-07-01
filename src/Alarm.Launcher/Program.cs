using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Alarm.Launcher;

/// <summary>
/// The tiny executable a user double-clicks at the root of the distributable
/// bundle.
/// <para>
/// The real WinUI app and its self-contained runtime (~329 files) live one level
/// down in <c>app\</c>: they cannot move, because the .NET apphost resolves its
/// managed DLLs, <c>*.deps.json</c> and <c>*.runtimeconfig.json</c> from its own
/// directory. This launcher sits alone at the top (beside only README.txt /
/// BUILDINFO.txt) so "which file do I run" is obvious, then starts
/// <c>app\Alarm.exe</c>, forwarding its own command-line arguments, and exits —
/// the GUI app outlives it, so only one process remains.
/// </para>
/// </summary>
internal static partial class Program
{
    /// <summary>Subdirectory holding the real self-contained app bundle.</summary>
    private const string AppSubdir = "app";

    /// <summary>The real WinUI apphost inside <see cref="AppSubdir"/>.</summary>
    private const string AppExe = "Alarm.exe";

    private static int Main(string[] args)
    {
        // AppContext.BaseDirectory is the launcher's own folder (the bundle root).
        var root = AppContext.BaseDirectory;
        var appDir = Path.Combine(root, AppSubdir);
        var appExe = Path.Combine(appDir, AppExe);

        if (!File.Exists(appExe))
        {
            Fatal(
                $"The application was not found at:\n{appExe}\n\n" +
                "The download may be incomplete — re-extract the .zip, keeping its " +
                "folder structure intact.");
            return 1;
        }

        var psi = new ProcessStartInfo(appExe)
        {
            WorkingDirectory = appDir,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            // Spawn-and-exit: do not wait. Disposing the Process releases only our
            // local handle — it does not terminate the detached GUI child, which
            // keeps running after this launcher returns.
            using var child = Process.Start(psi);
            if (child is null)
            {
                Fatal("Could not start the application (no process was created).");
                return 1;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Fatal($"Could not start the application:\n{ex.Message}");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Surface a fatal message to a GUI user. Under the Windows subsystem there is
    /// no console, so a message box is the only way to report the failure instead
    /// of vanishing silently.
    /// </summary>
    private static void Fatal(string message) =>
        _ = MessageBoxW(IntPtr.Zero, message, "Alarm", MessageBoxOk | MessageBoxIconError);

    private const uint MessageBoxOk = 0x0000_0000;
    private const uint MessageBoxIconError = 0x0000_0010;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
