using Microsoft.Win32;

namespace MouseBridge;

/// <summary>
/// The per-user "run at sign-in" entry, held in the standard Run key.
/// </summary>
/// <remarks>
/// HKCU rather than HKLM deliberately: the tool needs no elevation, and a
/// machine-wide entry would demand admin rights just to tick a menu item.
/// </remarks>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MouseBridge";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Unreadable key means we cannot claim it is registered; the menu
            // shows unchecked and toggling will surface the real error.
            return false;
        }
    }

    /// <summary>
    /// Adds or removes the entry. Enabling always rewrites the path, so an
    /// executable that has since been moved re-registers itself correctly.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException($@"Could not open HKCU\{RunKey}.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine this executable's path.");

        // Quoted so an install folder containing spaces still parses as one argument.
        key.SetValue(ValueName, $"\"{exe}\"");
    }
}
