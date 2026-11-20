using Tavrynexo.Core;

namespace Tavrynexo.Services;

public static class RestorePointService
{
    public static bool Create(string description)
    {
        var desc = description.Replace("'", "").Replace("\"", "");
        if (desc.Length > 100) desc = desc[..100];

        // Windows normally allows one restore point per 24h; temporarily lift that limit, then restore the original value.
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            try { Enable-ComputerRestore -Drive ($env:SystemDrive + '\') } catch {}
            $k = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
            $old = $null
            try { $old = (Get-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -ErrorAction Stop).SystemRestorePointCreationFrequency } catch {}
            Set-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord -Force
            try {
                Checkpoint-Computer -Description '{{desc}}' -RestorePointType 'APPLICATION_UNINSTALL'
            } finally {
                if ($old -ne $null) { Set-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -Value $old -Type DWord -Force }
                else { Remove-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue }
            }
            """;

        var (code, _, err) = ProcessRunner.RunPowerShell(script, 240_000);
        Logger.Info($"Restore point '{desc}' => {code} {err}");
        return code == 0;
    }
}
