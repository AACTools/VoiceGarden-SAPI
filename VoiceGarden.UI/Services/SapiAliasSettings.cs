using System;
using System.Collections.Generic;

namespace VoiceGarden.UI.Services;

/// <summary>
/// Global SAPI alias settings (Advanced tab). Both default to on so
/// non-English voices stay visible in English-only and RTL-capable apps
/// without extra configuration.
/// </summary>
public static class SapiAliasSettings
{
    /// <summary>Non-English voices also get an en-US token ("…-enUS").</summary>
    public static bool EnUsEnabled
    {
        get => RegistryService.GetFlag("SapiAliasEnUs", true);
        set => RegistryService.SetFlag("SapiAliasEnUs", value);
    }

    /// <summary>Right-to-left voices also get an ar-SA token ("…-ar").</summary>
    public static bool ArabicEnabled
    {
        get => RegistryService.GetFlag("SapiAliasArabic", true);
        set => RegistryService.SetFlag("SapiAliasArabic", value);
    }

    /// <summary>
    /// Alias descriptors for a voice: en-US alias for any non-English
    /// language, Arabic (ar-SA) alias for right-to-left languages. English
    /// and unresolvable languages get none (their primary token already is
    /// en-US).
    /// </summary>
    public static (string suffix, string locale, string langId, string marker)[] AliasesFor(string? language)
    {
        // Unresolvable languages keep the historical en-US primary token —
        // an en-US alias would just duplicate it.
        if (!SapiLanguage.TryResolve(language, out _, out _)) return Array.Empty<(string, string, string, string)>();
        var nonEnglish = !SapiLanguage.IsEnglish(language);

        var result = new List<(string, string, string, string)>();
        if (nonEnglish && EnUsEnabled)
            result.Add((SapiLanguage.EnUsAliasSuffix, SapiLanguage.EnUsLocale, SapiLanguage.EnUsLangId, "EnUs"));
        if (nonEnglish && ArabicEnabled && SapiLanguage.IsRightToLeft(language))
            result.Add((SapiLanguage.ArabicAliasSuffix, SapiLanguage.ArabicLocale, SapiLanguage.ArabicLangId, "Arabic"));

        return result.ToArray();
    }
}
