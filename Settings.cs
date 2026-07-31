using Microsoft.Win32;

namespace MouseBridge;

/// <summary>
/// Menu choices that should still be set the next time the app starts.
/// </summary>
/// <remarks>
/// Kept in the registry under the current user rather than in a file, because
/// the app already reads and writes there for the start-on-login entry, and
/// there is far too little here to be worth a settings file.
/// </remarks>
internal static class Settings
{
    private const string Key = @"Software\MouseBridge";

    public static bool DebugOverlay
    {
        get => Read(nameof(DebugOverlay));
        set => Write(nameof(DebugOverlay), value);
    }

    /// <summary>Never throws: a setting that cannot be read just falls back to off.</summary>
    private static bool Read(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(name) is int value && value != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Throws if the setting cannot be saved, so the caller can say so.</summary>
    private static void Write(string name, bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key, writable: true)
            ?? throw new InvalidOperationException($@"Could not open HKCU\{Key}.");

        key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }
}
