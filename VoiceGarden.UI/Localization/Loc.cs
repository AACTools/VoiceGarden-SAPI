using System.Globalization;
using System.Resources;

namespace VoiceGarden.UI.Localization;

/// <summary>
/// Simple localization helper for .resx-based string lookup.
/// Automatically uses CurrentUICulture (set by Windows OS language).
/// </summary>
public static class Loc
{
    private static readonly ResourceManager _rm =
        new("VoiceGarden.UI.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>
    /// Get a localized string. Falls back to the key if not found.
    /// Supports format args: Loc.GetString("FoundVoices", count)
    /// </summary>
    public static string GetString(string key, params object?[]? args)
    {
        var value = _rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return args is { Length: > 0 } ? string.Format(value, args) : value;
    }

    /// <summary>
    /// Try to get a localized string.
    /// </summary>
    public static bool TryGetString(string key, out string value)
    {
        value = _rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return value != key;
    }
}
