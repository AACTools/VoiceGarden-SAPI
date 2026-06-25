using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32;

namespace VoiceGarden.UI.Services;

/// <summary>
/// Promotes voices to HKLM as SAPI voice tokens. Requires elevation.
/// Uses a helper process approach: writes a temp .reg file and imports it elevated,
/// or directly writes to HKLM if already elevated.
/// </summary>
public static class VoicePromotionService
{
    private const string SapiTokensRoot = @"SOFTWARE\Microsoft\Speech\Voices\Tokens";
    private const string TtsEngineClsid = "{013AB33B-AD1A-401C-8BEE-F6E2B046A94E}";

    public class PromotedVoice
    {
        public string TokenName { get; set; } = "";
        public string Engine { get; set; } = "";
        public string VoiceId { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// List all promoted cloud/sherpa voices in HKLM.
    /// </summary>
    public static List<PromotedVoice> ListPromoted()
    {
        var result = new List<PromotedVoice>();
        using var key = Registry.LocalMachine.OpenSubKey(SapiTokensRoot);
        if (key == null) return result;

        foreach (var name in key.GetSubKeyNames())
        {
            if (!name.StartsWith("Cloud-", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("Sherpa-", StringComparison.OrdinalIgnoreCase))
                continue;

            var pv = new PromotedVoice { TokenName = name };
            using var tokenKey = key.OpenSubKey(name);
            if (tokenKey?.GetValue("") is string desc)
                pv.DisplayName = desc;

            using var cfgKey = tokenKey?.OpenSubKey("NaturalVoiceConfig");
            if (cfgKey != null)
            {
                pv.Engine = cfgKey.GetValue("EngineType") as string ?? "";
                pv.VoiceId = cfgKey.GetValue("Voice") as string ??
                             cfgKey.GetValue("VoiceId") as string ?? "";
            }
            result.Add(pv);
        }
        return result;
    }

    /// <summary>
    /// Promote a single voice to HKLM. Requires admin.
    /// </summary>
    public static bool Promote(string engine, string voiceId, string key, string? region = null, string? locale = "en-US")
    {
        var tokenName = $"Cloud-{engine}-{voiceId}".Replace("/", "_").Replace("\\", "_");
        var tokenPath = $@"{SapiTokensRoot}\{tokenName}";

        try
        {
            using var token = Registry.LocalMachine.CreateSubKey(tokenPath, writable: true);
            if (token == null) return false;

            token.SetValue("", $"{Cap(engine)} {voiceId}", RegistryValueKind.String);
            token.SetValue("CLSID", TtsEngineClsid, RegistryValueKind.String);

            using var config = token.CreateSubKey("NaturalVoiceConfig", writable: true);
            config.SetValue("EngineType", Cap(engine), RegistryValueKind.String);
            config.SetValue("Voice", voiceId, RegistryValueKind.String);
            config.SetValue("Key", key ?? "", RegistryValueKind.String);
            if (!string.IsNullOrEmpty(region))
                config.SetValue("Region", region, RegistryValueKind.String);
            config.SetValue("IsCloudVoice", 1, RegistryValueKind.DWord);

            using var attrs = token.CreateSubKey("Attributes", writable: true);
            attrs.SetValue("Name", voiceId, RegistryValueKind.String);
            attrs.SetValue("Gender", "Neutral", RegistryValueKind.String);
            attrs.SetValue("Age", "Adult", RegistryValueKind.String);
            attrs.SetValue("Language", "409", RegistryValueKind.String);
            attrs.SetValue("Locale", locale ?? "en-US", RegistryValueKind.String);
            attrs.SetValue("Vendor", Cap(engine), RegistryValueKind.String);
            attrs.SetValue("NaturalVoiceType", "Cloud", RegistryValueKind.String);

            // Azure backward compatibility: also save to Enumerator
            if (engine.Equals("azure", StringComparison.OrdinalIgnoreCase))
            {
                using var enumKey = Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\NaturalVoiceSAPIAdapter\Enumerator", writable: true);
                enumKey?.SetValue("AzureVoiceKey", key, RegistryValueKind.String);
                if (!string.IsNullOrEmpty(region))
                    enumKey?.SetValue("AzureVoiceRegion", region, RegistryValueKind.String);
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Promote via elevated helper process (when not running as admin).
    /// Uses a temporary .reg file approach for reliability.
    /// </summary>
    public static int PromoteElevated(string engine, string voiceId, string key, string? region = null)
    {
        // Try direct first (works if HKLM is writable without UAC)
        if (Promote(engine, voiceId, key, region))
            return 0;

        // Fall back to EngineConfig.exe if available
        var exePath = FindEngineConfig();
        if (exePath != null)
        {
            var args = $"promote --engine {engine} --voice \"{voiceId}\" --key \"{key}\"";
            if (!string.IsNullOrEmpty(region))
                args += $" --region {region}";
            return ComRegistrationService.RunElevated(exePath, args);
        }

        return -1;
    }

    /// <summary>
    /// Remove a promoted voice from HKLM.
    /// </summary>
    public static bool Unpromote(string tokenName)
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree($@"{SapiTokensRoot}\{tokenName}", throwOnMissingSubKey: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static int UnpromoteElevated(string tokenName)
    {
        var exePath = FindEngineConfig();
        if (exePath != null)
        {
            return ComRegistrationService.RunElevated(exePath, $"unpromote --voice \"{tokenName}\"");
        }
        return Unpromote(tokenName) ? 0 : -1;
    }

    private static string? FindEngineConfig()
    {
        var dir = ComRegistrationService.GetInstallDir(true);
        var exe = Path.Combine(dir, "EngineConfig.exe");
        return File.Exists(exe) ? exe : null;
    }

    private static string Cap(string s) => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
}
