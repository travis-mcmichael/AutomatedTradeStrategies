
using System;

namespace AutomatedTrading.Core.Framework.Utility;

public class Operations
{
    public static double Safe(double? v) => v ?? 0;

    public static double SafeDiv(double a, double b) => b == 0 ? 0 : a / b;
        /// <summary>
    /// Converts paths between WSL (/mnt/c/...) and Windows (C:\...) formats
    /// based on the current OS, so the same config works in both environments.
    /// </summary>
    public static string NormalizePath(string path)
    {
        var isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        if (isWindows && path.StartsWith("/mnt/") && path.Length > 6)
        {
            var drive = char.ToUpper(path[5]);
            var rest  = path.Substring(6).Replace('/', '\\');
            return $"{drive}:{rest}";
        }
        if (!isWindows && path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':')
        {
            var drive = char.ToLower(path[0]);
            var rest  = path.Substring(2).Replace('\\', '/');
            return $"/mnt/{drive}{rest}";
        }
        return path;
    }
}
