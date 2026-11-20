using System.Text.Json;
using System.Text.RegularExpressions;
using Tavrynexo.Core;
using Tavrynexo.Models;

namespace Tavrynexo.Services;

public static class StoreAppService
{
    public static List<InstalledProgram> Scan()
    {
        var list = new List<InstalledProgram>();
        const string script = """
            Get-AppxPackage |
              Where-Object { -not $_.IsFramework -and -not $_.NonRemovable -and $_.SignatureKind -ne 'System' } |
              Select-Object Name, PackageFullName, Publisher, @{n='Version';e={$_.Version.ToString()}}, InstallLocation |
              ConvertTo-Json -Compress
            """;

        var (code, output, error) = ProcessRunner.RunPowerShell(script);
        if (code != 0 || string.IsNullOrWhiteSpace(output))
        {
            Logger.Error("Get-AppxPackage failed: " + error);
            return list;
        }

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            IEnumerable<JsonElement> items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : new[] { root };
            foreach (var e in items)
            {
                string Get(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var rawName = Get("Name");
                if (string.IsNullOrEmpty(rawName)) continue;

                var cn = Regex.Match(Get("Publisher"), @"CN=([^,]+)");
                list.Add(new InstalledProgram
                {
                    IsStore = true,
                    Name = Pretty(rawName),
                    Publisher = cn.Success ? cn.Groups[1].Value.Trim('"') : "",
                    Version = Get("Version"),
                    PackageFullName = Get("PackageFullName"),
                    InstallLocation = Get("InstallLocation"),
                });
            }
        }
        catch (Exception ex) { Logger.Error("Parse appx json", ex); }

        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string Pretty(string name)
    {
        var last = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
        if (last.Length < 3) last = name;
        return Regex.Replace(last, "(?<=[a-z0-9])(?=[A-Z])", " ");
    }

    public static (bool ok, string error) Remove(InstalledProgram p)
    {
        var pkg = p.PackageFullName.Replace("'", "''");
        var (code, _, error) = ProcessRunner.RunPowerShell($"$ErrorActionPreference='Stop'; Remove-AppxPackage -Package '{pkg}'");
        Logger.Info($"Remove-AppxPackage {p.PackageFullName} => {code} {error}");
        return (code == 0, error.Trim());
    }
}
