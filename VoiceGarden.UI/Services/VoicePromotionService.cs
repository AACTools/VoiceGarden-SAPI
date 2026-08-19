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
    private const string OneCoreTokensRoot = @"SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens";
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

            using var cfgKey = tokenKey?.OpenSubKey("VoiceGardenConfig");
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
    /// The primary token carries the voice's real language (when the wrapper
    /// reports one we can map); alias tokens are added per the Advanced
    /// settings so English-only and RTL-capable apps can find the voice.
    /// </summary>
    public static bool Promote(string engine, string voiceId, string key, string? region = null,
        string? language = null, string? gender = null)
    {
        var tokenName = $"Cloud-{engine}-{voiceId}".Replace("/", "_").Replace("\\", "_");
        var (locale, langId) = ResolveLocale(language);
        var genderValue = gender is "Male" or "Female" ? gender : "Neutral";

        try
        {
            WriteCloudToken(SapiTokensRoot, tokenName, engine, voiceId, key, region, locale, langId,
                genderValue, aliasMarker: null);
            WriteCloudOneCoreToken(tokenName, engine, voiceId, key, region, locale, langId, genderValue,
                aliasMarker: null);

            foreach (var alias in SapiAliasSettings.AliasesFor(language))
            {
                WriteCloudToken(SapiTokensRoot, tokenName + alias.suffix, engine, voiceId, key, region,
                    alias.locale, alias.langId, genderValue, alias.marker);
                WriteCloudOneCoreToken(tokenName + alias.suffix, engine, voiceId, key, region,
                    alias.locale, alias.langId, genderValue, alias.marker);
            }

            // Azure backward compatibility: also save to Enumerator
            if (engine.Equals("azure", StringComparison.OrdinalIgnoreCase))
            {
                using var enumKey = Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\VoiceGardenSAPIAdapter\Enumerator", writable: true);
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

    private static (string locale, string langId) ResolveLocale(string? language) =>
        SapiLanguage.TryResolve(language, out var locale, out var langId)
            ? (locale, langId)
            : (SapiLanguage.EnUsLocale, SapiLanguage.EnUsLangId);

    private static void WriteCloudToken(string tokensRoot, string tokenName, string engine, string voiceId,
        string key, string? region, string locale, string langId, string genderValue, string? aliasMarker)
    {
        var tokenPath = $@"{tokensRoot}\{tokenName}";
        var friendlyName = $"{Cap(engine)} {voiceId}" + (aliasMarker != null ? $" ({aliasMarker} alias)" : "");

        using var token = Registry.LocalMachine.CreateSubKey(tokenPath, writable: true);
        if (token == null) throw new InvalidOperationException("Cannot create HKLM token");

        token.SetValue("", friendlyName, RegistryValueKind.String);
        token.SetValue("CLSID", TtsEngineClsid, RegistryValueKind.String);
        if (aliasMarker != null)
            token.SetValue(SapiLanguage.AliasMarkerValue, aliasMarker, RegistryValueKind.String);

        using var config = token.CreateSubKey("VoiceGardenConfig", writable: true);
        config.SetValue("EngineType", Cap(engine), RegistryValueKind.String);
        config.SetValue("Voice", voiceId, RegistryValueKind.String);
        config.SetValue("Key", key ?? "", RegistryValueKind.String);
        if (!string.IsNullOrEmpty(region))
            config.SetValue("Region", region, RegistryValueKind.String);
        config.SetValue("IsCloudVoice", 1, RegistryValueKind.DWord);

        using var attrs = token.CreateSubKey("Attributes", writable: true);
        attrs.SetValue("Name", voiceId, RegistryValueKind.String);
        attrs.SetValue("Gender", genderValue, RegistryValueKind.String);
        attrs.SetValue("Age", "Adult", RegistryValueKind.String);
        attrs.SetValue("Language", langId, RegistryValueKind.String);
        attrs.SetValue("Locale", locale, RegistryValueKind.String);
        attrs.SetValue("Vendor", Cap(engine), RegistryValueKind.String);
        attrs.SetValue("VoiceGardenType", "Cloud", RegistryValueKind.String);
    }

    private static void WriteCloudOneCoreToken(string tokenName, string engine, string voiceId,
        string key, string? region, string locale, string langId, string genderValue, string? aliasMarker)
    {
        var oneCorePath = $@"{OneCoreTokensRoot}\{tokenName}";
        var friendlyName = $"{Cap(engine)} {voiceId}" + (aliasMarker != null ? $" ({aliasMarker} alias)" : "");

        using var ocToken = Registry.LocalMachine.CreateSubKey(oneCorePath, writable: true);
        if (ocToken == null) return;

        ocToken.SetValue("", friendlyName, RegistryValueKind.String);
        ocToken.SetValue("CLSID", TtsEngineClsid, RegistryValueKind.String);

        using var ocConfig = ocToken.CreateSubKey("VoiceGardenConfig", writable: true);
        ocConfig.SetValue("EngineType", Cap(engine), RegistryValueKind.String);
        ocConfig.SetValue("Voice", voiceId, RegistryValueKind.String);
        ocConfig.SetValue("Key", key ?? "", RegistryValueKind.String);
        if (!string.IsNullOrEmpty(region))
            ocConfig.SetValue("Region", region, RegistryValueKind.String);
        ocConfig.SetValue("ErrorMode", 0, RegistryValueKind.DWord);

        using var ocAttrs = ocToken.CreateSubKey("Attributes", writable: true);
        ocAttrs.SetValue("Name", voiceId, RegistryValueKind.String);
        ocAttrs.SetValue("Gender", genderValue, RegistryValueKind.String);
        ocAttrs.SetValue("Age", "Adult", RegistryValueKind.String);
        ocAttrs.SetValue("Language", langId, RegistryValueKind.String);
        ocAttrs.SetValue("Locale", locale, RegistryValueKind.String);
        ocAttrs.SetValue("Vendor", Cap(engine), RegistryValueKind.String);
    }

    /// <summary>
    /// Promote via elevated .reg file import (when not running as admin).
    /// EngineConfig.exe was removed — this replaces it. The primary token
    /// carries the voice's real language; aliases follow the Advanced settings.
    /// </summary>
    public static int PromoteElevated(string engine, string voiceId, string key, string? region = null,
        string? gender = null, string? language = null)
    {
        // Try direct first (works if HKLM is writable without UAC)
        if (Promote(engine, voiceId, key, region, language, gender))
            return 0;

        var genderValue = gender is "Male" or "Female" ? gender : "Neutral";
        var (locale, langId) = ResolveLocale(language);

        // Generate .reg file and import elevated
        var tokenName = $"Cloud-{engine}-{voiceId}".Replace("/", "_").Replace("\\", "_");
        var lines = new List<string> { "Windows Registry Editor Version 5.00", "" };

        // Rebuild aliases from scratch so settings changes apply on re-promote
        foreach (var alias in SapiAliasSettings.AliasesFor(language))
        {
            lines.Add($"[-HKEY_LOCAL_MACHINE\\{SapiTokensRoot}\\{tokenName}{alias.suffix}]");
            lines.Add($"[-HKEY_LOCAL_MACHINE\\{OneCoreTokensRoot}\\{tokenName}{alias.suffix}]");
        }

        AppendCloudTokenToReg(lines, tokenName, engine, voiceId, key, region, locale, langId, genderValue, null);

        foreach (var alias in SapiAliasSettings.AliasesFor(language))
        {
            AppendCloudTokenToReg(lines, tokenName + alias.suffix, engine, voiceId, key, region,
                alias.locale, alias.langId, genderValue, alias.marker);
        }

        var regDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VoiceGardenSAPIAdapter");
        Directory.CreateDirectory(regDir);
        var regPath = Path.Combine(regDir, "promote_voice.reg");
        File.WriteAllLines(regPath, lines);

        try
        {
            var psi = new ProcessStartInfo("reg.exe", $"import \"{regPath}\"")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };
            var p = Process.Start(psi);
            p?.WaitForExit(15000);
            var rc = p?.ExitCode ?? -1;
            TryDelete(regPath);
            return rc;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            TryDelete(regPath);
            return -2; // UAC cancelled
        }
        catch (Exception)
        {
            TryDelete(regPath);
            return -1;
        }
    }

    private static void AppendCloudTokenToReg(List<string> lines, string tokenName, string engine, string voiceId,
        string key, string? region, string locale, string langId, string genderValue, string? aliasMarker)
    {
        var cap = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(engine.ToLowerInvariant());
        var friendlyName = $"{cap} {voiceId}" + (aliasMarker != null ? $" ({aliasMarker} alias)" : "");

        // Legacy SAPI token
        var path = $@"HKEY_LOCAL_MACHINE\{SapiTokensRoot}\{tokenName}";
        lines.Add($"[{path}]");
        lines.Add($"@=\"{friendlyName}\"");
        lines.Add($"\"CLSID\"=\"{TtsEngineClsid}\"");
        if (aliasMarker != null)
            lines.Add($"\"{SapiLanguage.AliasMarkerValue}\"=\"{aliasMarker}\"");
        lines.Add($"[{path}\\VoiceGardenConfig]");
        lines.Add($"\"EngineType\"=\"{cap}\"");
        lines.Add($"\"Voice\"=\"{voiceId}\"");
        lines.Add($"\"Key\"=\"{key ?? ""}\"");
        if (!string.IsNullOrEmpty(region)) lines.Add($"\"Region\"=\"{region}\"");
        lines.Add($"\"IsCloudVoice\"=dword:00000001");
        lines.Add($"\"ErrorMode\"=dword:00000000");
        lines.Add($"[{path}\\Attributes]");
        lines.Add($"\"Name\"=\"{voiceId}\"");
        lines.Add($"\"Gender\"=\"{genderValue}\"");
        lines.Add("\"Age\"=\"Adult\"");
        lines.Add($"\"Language\"=\"{langId}\"");
        lines.Add($"\"Locale\"=\"{locale}\"");
        lines.Add($"\"Vendor\"=\"{cap}\"");
        lines.Add("");

        // Speech_OneCore token (Chrome/Edge)
        var ocPath = $@"HKEY_LOCAL_MACHINE\{OneCoreTokensRoot}\{tokenName}";
        lines.Add($"[{ocPath}]");
        lines.Add($"@=\"{friendlyName}\"");
        lines.Add($"\"CLSID\"=\"{TtsEngineClsid}\"");
        lines.Add($"[{ocPath}\\VoiceGardenConfig]");
        lines.Add($"\"EngineType\"=\"{cap}\"");
        lines.Add($"\"Voice\"=\"{voiceId}\"");
        lines.Add($"\"Key\"=\"{key ?? ""}\"");
        if (!string.IsNullOrEmpty(region)) lines.Add($"\"Region\"=\"{region}\"");
        lines.Add($"\"ErrorMode\"=dword:00000000");
        lines.Add($"[{ocPath}\\Attributes]");
        lines.Add($"\"Name\"=\"{voiceId}\"");
        lines.Add($"\"Gender\"=\"{genderValue}\"");
        lines.Add("\"Age\"=\"Adult\"");
        lines.Add($"\"Language\"=\"{langId}\"");
        lines.Add($"\"Locale\"=\"{locale}\"");
        lines.Add($"\"Vendor\"=\"{cap}\"");
        lines.Add("");
    }

    /// <summary>
    /// Remove a promoted voice and its alias tokens from HKLM.
    /// </summary>
    public static bool Unpromote(string tokenName)
    {
        try
        {
            foreach (var name in TokenNameFamily(tokenName))
            {
                Registry.LocalMachine.DeleteSubKeyTree($@"{SapiTokensRoot}\{name}", throwOnMissingSubKey: false);
                Registry.LocalMachine.DeleteSubKeyTree($@"{OneCoreTokensRoot}\{name}", throwOnMissingSubKey: false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A voice's primary token name plus its alias token names.</summary>
    private static string[] TokenNameFamily(string tokenName) => new[]
    {
        tokenName,
        tokenName + SapiLanguage.EnUsAliasSuffix,
        tokenName + SapiLanguage.ArabicAliasSuffix,
    };

    public static int UnpromoteElevated(string tokenName)
    {
        if (Unpromote(tokenName))
            return 0;

        // One elevated .reg import deleting the whole token family from both trees.
        try
        {
            var lines = new List<string> { "Windows Registry Editor Version 5.00", "" };
            foreach (var name in TokenNameFamily(tokenName))
            {
                lines.Add($"[-HKEY_LOCAL_MACHINE\\{SapiTokensRoot}\\{name}]");
                lines.Add($"[-HKEY_LOCAL_MACHINE\\{OneCoreTokensRoot}\\{name}]");
            }

            var regDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VoiceGardenSAPIAdapter");
            Directory.CreateDirectory(regDir);
            var regPath = Path.Combine(regDir, "unpromote_voice.reg");
            File.WriteAllLines(regPath, lines);

            try
            {
                var psi = new ProcessStartInfo("reg.exe", $"import \"{regPath}\"")
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                };
                var p = Process.Start(psi);
                p?.WaitForExit(15000);
                var rc = p?.ExitCode ?? -1;
                TryDelete(regPath);
                return rc == 0 ? 0 : -1;
            }
            finally
            {
                TryDelete(regPath);
            }
        }
        catch
        {
            return -1;
        }
    }

    private static string Cap(string s) =>
        System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
