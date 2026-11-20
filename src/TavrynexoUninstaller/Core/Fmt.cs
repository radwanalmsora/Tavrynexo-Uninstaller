using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Tavrynexo.Core;

public static class Fmt
{
    public static string Size(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return string.Format(CultureInfo.InvariantCulture, i == 0 ? "{0:0} {1}" : "{0:0.#} {1}", v, u[i]);
    }

    /// <summary>Lower-case alphanumerics only. Works for any Unicode letters.</summary>
    public static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    public static long DirSize(string path, CancellationToken ct = default)
    {
        long total = 0;
        try
        {
            if (!Directory.Exists(path)) return 0;
            var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", opt))
            {
                if (ct.IsCancellationRequested) break;
                try { total += f.Length; } catch { }
            }
        }
        catch { }
        return total;
    }

    /// <summary>Strip versions, architecture tags and bracketed text from a display name.</summary>
    public static string CleanProductName(string name)
    {
        var s = Regex.Replace(name, @"[\(\[][^\)\]]*[\)\]]", " ");
        s = Regex.Replace(s, @"\b(x64|x86|amd64|64-?bit|32-?bit|version|update)\b", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bv?\d+(\.\d+)+\b", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+\d{1,4}\s*$", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
