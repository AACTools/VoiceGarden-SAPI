using System;
using Microsoft.Win32;

namespace VoiceGarden.UI.Services;

/// <summary>
/// Reads/writes adapter configuration in HKCU\SOFTWARE\NaturalVoiceSAPIAdapter
/// </summary>
public static class RegistryService
{
    private const string EnumeratorPath = @"SOFTWARE\NaturalVoiceSAPIAdapter\Enumerator";
    private const string SapiTokensRoot = @"SOFTWARE\Microsoft\Speech\Voices\Tokens";

    public static bool GetFlag(string name, bool defaultValue = false)
    {
        using var key = Registry.CurrentUser.OpenSubKey(EnumeratorPath);
        if (key?.GetValue(name) is int val)
            return val != 0;
        return defaultValue;
    }

    public static void SetFlag(string name, bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(EnumeratorPath, writable: true);
        key?.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }

    public static string? GetString(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(EnumeratorPath);
        return key?.GetValue(name) as string;
    }

    public static void SetString(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(EnumeratorPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.String);
    }

    public static int GetDword(string name, int defaultValue = 0)
    {
        using var key = Registry.CurrentUser.OpenSubKey(EnumeratorPath);
        if (key?.GetValue(name) is int val)
            return val;
        return defaultValue;
    }

    public static void SetDword(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(EnumeratorPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }

    public static int CountHklmTokens(string prefix)
    {
        using var key = Registry.LocalMachine.OpenSubKey(SapiTokensRoot);
        if (key == null) return 0;
        int count = 0;
        foreach (var name in key.GetSubKeyNames())
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count;
    }

    public static void DeleteHklmToken(string tokenName)
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree($@"{SapiTokensRoot}\{tokenName}", throwOnMissingSubKey: false);
        }
        catch { }
    }
}
