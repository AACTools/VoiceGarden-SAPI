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
    /// The wrapper's get_voices carries Gender for cloud voices — pass it
    /// through so SAPI apps (Grid 3 etc.) can filter on it.
    /// </summary>
    public static bool Promote(string engine, string voiceId, string key, string? region = null, string? locale = "en-US", string? gender = null)
    {
        var tokenName = $"Cloud-{engine}-{voiceId}".Replace("/", "_").Replace("\\", "_");
        var tokenPath = $@"{SapiTokensRoot}\{tokenName}";
        var genderValue = gender is "Male" or "Female" ? gender : "Neutral";

        try
        {
            using var token = Registry.LocalMachine.CreateSubKey(tokenPath, writable: true);
            if (token == null) return false;

            token.SetValue("", $"{Cap(engine)} {voiceId}", RegistryValueKind.String);
            token.SetValue("CLSID", TtsEngineClsid, RegistryValueKind.String);

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
            attrs.SetValue("Language", "409", RegistryValueKind.String);
            attrs.SetValue("Locale", locale ?? "en-US", RegistryValueKind.String);
            attrs.SetValue("Vendor", Cap(engine), RegistryValueKind.String);
            attrs.SetValue("VoiceGardenType", "Cloud", RegistryValueKind.String);

            // Also register in Speech_OneCore so Chrome/Edge can see the voice
            var oneCorePath = $@"{OneCoreTokensRoot}\{tokenName}";
            using var ocToken = Registry.LocalMachine.CreateSubKey(oneCorePath, writable: true);
            if (ocToken != null)
            {
                ocToken.SetValue("", $"{Cap(engine)} {voiceId}", RegistryValueKind.String);
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
                ocAttrs.SetValue("Language", "409", RegistryValueKind.String);
                ocAttrs.SetValue("Locale", locale ?? "en-US", RegistryValueKind.String);
                ocAttrs.SetValue("Vendor", Cap(engine), RegistryValueKind.String);
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

    /// <summary>
    /// Promote via elevated .reg file import (when not running as admin).
    /// EngineConfig.exe was removed — this replaces it.
    /// </summary>
    public static int PromoteElevated(string engine, string voiceId, string key, string? region = null, string? gender = null)
    {
        // Try direct first (works if HKLM is writable without UAC)
        if (Promote(engine, voiceId, key, region, gender: gender))
            return 0;

        var genderValue = gender is "Male" or "Female" ? gender : "Neutral";

        // Generate .reg file and import elevated
        var tokenName = $"Cloud-{engine}-{voiceId}".Replace("/", "_").Replace("\\", "_");
        var cap = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(engine.ToLowerInvariant());
        var lines = new List<string> { "Windows Registry Editor Version 5.00", "" };

        // Legacy SAPI token
        var path = $@"HKEY_LOCAL_MACHINE\{SapiTokensRoot}\{tokenName}";
        lines.Add($"[{path}]");
        lines.Add($"@=\"{cap} {voiceId}\"");
        lines.Add($"\"CLSID\"=\"{TtsEngineClsid}\"");
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
        lines.Add("\"Language\"=\"409\"");
        lines.Add("\"Locale\"=\"en-US\"");
        lines.Add($"\"Vendor\"=\"{cap}\"");
        lines.Add("");

        // Speech_OneCore token (Chrome/Edge)
        var ocPath = $@"HKEY_LOCAL_MACHINE\{OneCoreTokensRoot}\{tokenName}";
        lines.Add($"[{ocPath}]");
        lines.Add($"@=\"{cap} {voiceId}\"");
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
        lines.Add("\"Language\"=\"409\"");
        lines.Add("\"Locale\"=\"en-US\"");
        lines.Add($"\"Vendor\"=\"{cap}\"");
        lines.Add("");

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

    /// <summary>
    /// Remove a promoted voice from HKLM.
    /// </summary>
    public static bool Unpromote(string tokenName)
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree($@"{SapiTokensRoot}\{tokenName}", throwOnMissingSubKey: false);
            Registry.LocalMachine.DeleteSubKeyTree($@"{OneCoreTokensRoot}\{tokenName}", throwOnMissingSubKey: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static int UnpromoteElevated(string tokenName)
    {
        if (Unpromote(tokenName))
            return 0;

        try
        {
            var psi = new ProcessStartInfo("reg.exe", $"delete \"HKLM\\{SapiTokensRoot}\\{tokenName}\" /f")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };
            var p = Process.Start(psi);
            p?.WaitForExit(10000);

            // Also remove from OneCore
            var psi2 = new ProcessStartInfo("reg.exe", $"delete \"HKLM\\{OneCoreTokensRoot}\\{tokenName}\" /f")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };
            var p2 = Process.Start(psi2);
            p2?.WaitForExit(10000);
            return 0;
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
