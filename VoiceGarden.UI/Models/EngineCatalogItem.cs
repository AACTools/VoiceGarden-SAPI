using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VoiceGarden.UI.Models;

public enum EngineKind
{
    /// <summary>SherpaOnnx offline models (no credentials).</summary>
    OfflineModel,
    /// <summary>Cloud engine that requires credentials.</summary>
    CloudCreds,
    /// <summary>Cloud engine without credentials (Edge Read-Aloud).</summary>
    CloudFree,
}

/// <summary>
/// One selectable row in the "Voice Engines" tab. Wraps either a
/// CloudEngineSetting (credentialed cloud engines) or one of the two
/// no-credentials engines (SherpaOnnx offline, Edge).
/// </summary>
public partial class EngineCatalogItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public EngineKind Kind { get; init; }

    public bool IsOffline => Kind == EngineKind.OfflineModel;
    public bool NeedsCredentials => Kind == EngineKind.CloudCreds;

    /// <summary>Underlying persistent setting for credentialed engines; null for Sherpa/Edge.</summary>
    public CloudEngineSetting? CloudSetting { get; init; }

    /// <summary>True when an API key is stored for this engine.</summary>
    [ObservableProperty] private bool hasStoredKey;

    /// <summary>Languages this engine is known to support (empty = not known yet).</summary>
    public HashSet<string> Languages { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Prefix-search tokens: engine name/id/description words plus language
    /// names (English + native), BCP-47 tags and ISO 639-1/639-3 codes.
    /// </summary>
    public HashSet<string> SearchTokens { get; } = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private bool isSelected;

    /// <summary>Seed the static tokens (name/id/description). Call once after init.</summary>
    public void BuildSearchTokens()
    {
        AddTokens(SearchTokens, DisplayName);
        AddTokens(SearchTokens, Id);
        AddTokens(SearchTokens, Description);
    }

    /// <summary>Derive search tokens from one language string (name or tag).</summary>
    public static void AddLanguageTokens(HashSet<string> tokens, string lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return;
        AddTokens(tokens, lang);

        // "ar-SA"-style tags (and bare tags like "en") resolve to English +
        // native display names and both ISO codes via Windows culture data.
        var primary = lang.Split('-')[0];
        if (primary.Length is 2 or 3)
        {
            TryCulture(tokens, primary);
            if (lang.Contains('-')) TryCulture(tokens, lang);
        }
    }

    private static void TryCulture(HashSet<string> tokens, string name)
    {
        try
        {
            var c = CultureInfo.GetCultureInfo(name);
            tokens.Add(c.TwoLetterISOLanguageName);
            tokens.Add(c.ThreeLetterISOLanguageName);
            AddTokens(tokens, c.EnglishName);
            AddTokens(tokens, c.NativeName);
        }
        catch (CultureNotFoundException)
        {
            // Not a culture name (e.g. sherpa ISO 639-3 codes) — the raw
            // token added by the caller is all we get.
        }
    }

    private static void AddTokens(HashSet<string> tokens, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        tokens.Add(text.Trim());
        foreach (var word in text.Split(new[] { ' ', '(', ')', ',', '-', '/', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            tokens.Add(word);
    }

    public string TypeBadge => IsOffline
        ? Localization.Loc.GetString("BadgeOffline")
        : Localization.Loc.GetString("BadgeCloud");

    public string CredsBadge => NeedsCredentials
        ? Localization.Loc.GetString("BadgeCredsNeeded")
        : Localization.Loc.GetString("BadgeNoCreds");

    public bool HasLanguages => Languages.Count > 0;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke(this, value);
    }

    /// <summary>Raised after IsSelected changes; the engines view model persists the value.</summary>
    public event Action<EngineCatalogItem, bool>? SelectionChanged;

    /// <summary>Notify bindings that the known-language set changed.</summary>
    public void NotifyLanguagesChanged() => OnPropertyChanged(nameof(HasLanguages));

    /// <summary>
    /// Human label for a wrapper credential key ("apiKey" → "API key", …).
    /// </summary>
    public static string CredentialKeyLabel(string key) => key switch
    {
        "apiKey" => Localization.Loc.GetString("FieldAPIKey"),
        "subscriptionKey" => Localization.Loc.GetString("FieldSubscriptionKey"),
        "accessKeyId" => Localization.Loc.GetString("FieldAccessKeyId"),
        "secretAccessKey" => Localization.Loc.GetString("FieldSecretAccessKey"),
        "token" => Localization.Loc.GetString("FieldToken"),
        "region" => Localization.Loc.GetString("FieldRegion"),
        "userId" => Localization.Loc.GetString("FieldUserId"),
        "instanceId" => Localization.Loc.GetString("FieldInstanceId"),
        _ => key,
    };
}
