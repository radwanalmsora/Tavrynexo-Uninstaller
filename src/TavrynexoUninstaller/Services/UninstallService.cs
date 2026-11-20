using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Tavrynexo.Core;
using Tavrynexo.Models;

namespace Tavrynexo.Services;

public static class UninstallService
{
    public sealed record Result(bool Started, int? ExitCode, string? Error);

    /// <summary>Runs the program's own uninstaller and waits (including self-relocating helper processes).</summary>
    public static Result Run(InstalledProgram p, bool silent = false)
    {
        var cmd = BuildCommand(p, silent);
        if (cmd == null) return new Result(false, null, "no uninstall command");

        try
        {
            Logger.Info($"Uninstall [{p.Name}] => {cmd}");
            var psi = new ProcessStartInfo("cmd.exe", $"/s /c \"{cmd}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return new Result(false, null, "failed to start");
            proc.WaitForExit();
            var code = proc.ExitCode;
            WaitForHelperProcesses();
            Logger.Info($"Uninstaller exit code {code}");
            return new Result(true, code, null);
        }
        catch (Exception ex)
        {
            Logger.Error("Uninstall run failed", ex);
            return new Result(false, null, ex.Message);
        }
    }

    public static bool IsStillInstalled(InstalledProgram p)
    {
        if (string.IsNullOrEmpty(p.SubKeyPath)) return false;
        try
        {
            using var b = RegistryKey.OpenBaseKey(p.Hive, p.View);
            using var k = b.OpenSubKey(p.SubKeyPath);
            return k != null;
        }
        catch { return false; }
    }

    private static string? BuildCommand(InstalledProgram p, bool silent)
    {
        var cmd = silent && !string.IsNullOrWhiteSpace(p.QuietUninstallString) ? p.QuietUninstallString : p.UninstallString;
        if (string.IsNullOrWhiteSpace(cmd)) cmd = p.QuietUninstallString;
        if (string.IsNullOrWhiteSpace(cmd)) return null;

        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());

        // Many MSI entries store "/I{GUID}" (modify/repair) - we want removal.
        var m = Regex.Match(cmd, @"msiexec(?:\.exe)?""?\s+/[iI]\s*(\{[0-9A-Fa-f\-]{36}\})", RegexOptions.IgnoreCase);
        if (m.Success)
            cmd = $"msiexec.exe /x {m.Groups[1].Value}" + (silent ? " /qn /norestart" : "");

        return cmd;
    }

    private static void WaitForHelperProcesses()
    {
        // Inno Setup / NSIS uninstallers copy themselves to %TEMP% and relaunch, so the first process exits early.
        Thread.Sleep(1500);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromMinutes(10))
        {
            var any = false;
            foreach (var pr in Process.GetProcesses())
            {
                try
                {
                    if (pr.Id != Environment.ProcessId && IsHelper(pr.ProcessName)) any = true;
                }
                catch { }
                finally { pr.Dispose(); }
                if (any) break;
            }
            if (!any) break;
            Thread.Sleep(1000);
        }
    }

    private static bool IsHelper(string n)
    {
        if (n.StartsWith("Tavrynexo", StringComparison.OrdinalIgnoreCase)) return false;
        return n.StartsWith("_iu", StringComparison.OrdinalIgnoreCase) ||
               n.StartsWith("_unin", StringComparison.OrdinalIgnoreCase) ||
               n.Equals("Au_", StringComparison.OrdinalIgnoreCase) ||
               n.StartsWith("Un_A", StringComparison.OrdinalIgnoreCase) ||
               n.StartsWith("unins0", StringComparison.OrdinalIgnoreCase) ||
               n.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase);
    }
}
