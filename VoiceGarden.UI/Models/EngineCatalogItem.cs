using System;
using System.Collections.Generic;
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

    [ObservableProperty] private bool isSelected;

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
