using System.Resources;

namespace VoiceGarden.UI.Localization;

/// <summary>
/// Simple localization extension for .resx-based string lookup.
/// Usage in XAML: {lang:Localize AdapterInstallation}
/// </summary>
public static class Loc
{
    private static readonly ResourceManager _rm =
        new("VoiceGarden.UI.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>
    /// Get a localized string. Falls back to the key if not found.
    /// </summary>
    public static string GetString(string key, params object[]? args)
    {
        var value = _rm.GetString(key) ?? key;
        return args != null && args.Length > 0
            ? string.Format(value, args)
            : value;
    }

    /// <summary>
    /// Try to get a localized string.
    /// </summary>
    public static bool TryGetString(string key, out string value)
    {
        value = _rm.GetString(key) ?? key;
        return value != key;
    }
}
