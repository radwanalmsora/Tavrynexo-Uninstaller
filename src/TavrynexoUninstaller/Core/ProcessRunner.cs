using System.Diagnostics;
using System.Text;

namespace Tavrynexo.Core;

public static class ProcessRunner
{
    public static (int code, string output, string error) Run(string file, string args, int timeoutMs = 120_000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return (-1, "", "failed to start");
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return (-1, "", "timeout");
            }
            return (p.ExitCode, o.Result, e.Result);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    public static (int code, string output, string error) RunPowerShell(string script, int timeoutMs = 180_000)
    {
        var full = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'; " + script;
        var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi);
            if (p == null) return (-1, "", "failed to start powershell");
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return (-1, "", "timeout");
            }
            return (p.ExitCode, o.Result, e.Result);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}
