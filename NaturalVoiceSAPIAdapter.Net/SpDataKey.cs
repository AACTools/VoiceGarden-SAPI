using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NaturalVoiceSAPIAdapter.SapiInterop;

namespace NaturalVoiceSAPIAdapter;

[ComVisible(true)]
[Guid("F2B3C4D5-E6F7-4A8B-9C0D-1E2F3A4B5C6D")]
[ClassInterface(ClassInterfaceType.None)]
public class SpDataKey : ISpDataKey
{
    internal readonly Dictionary<string, string> StringValues;
    internal readonly Dictionary<string, uint> DwordValues;
    internal readonly Dictionary<string, SpDataKey> SubKeys;

    public SpDataKey()
    {
        StringValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DwordValues = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        SubKeys = new Dictionary<string, SpDataKey>(StringComparer.OrdinalIgnoreCase);
    }

    public void SetValue(string? name, string value) => StringValues[name ?? ""] = value;
    public void SetDwordValue(string name, uint value) => DwordValues[name] = value;
    public SpDataKey GetOrCreateSubKey(string name)
    {
        if (!SubKeys.TryGetValue(name, out var key))
        {
            key = new SpDataKey();
            SubKeys[name] = key;
        }
        return key;
    }

    public int SetData(string pszValueName, uint cbData, IntPtr pData) => SapiConstants.E_NOTIMPL;
    public int GetData(string pszValueName, ref uint pcbData, IntPtr pData) => SapiConstants.E_NOTIMPL;

    public int SetStringValue(string? pszValueName, string pszValue)
    {
        StringValues[pszValueName ?? ""] = pszValue;
        return SapiConstants.S_OK;
    }

    public int GetStringValue(string? pszValueName, out IntPtr ppszValue)
    {
        if (StringValues.TryGetValue(pszValueName ?? "", out var val))
        {
            ppszValue = Marshal.StringToCoTaskMemUni(val);
            return SapiConstants.S_OK;
        }
        ppszValue = IntPtr.Zero;
        return SapiConstants.E_INVALIDARG;
    }

    public int SetDWORD(string pszValueName, uint dwValue)
    {
        DwordValues[pszValueName] = dwValue;
        return SapiConstants.S_OK;
    }

    public int GetDWORD(string pszValueName, out uint pdwValue)
    {
        if (DwordValues.TryGetValue(pszValueName, out var val))
        {
            pdwValue = val;
            return SapiConstants.S_OK;
        }
        pdwValue = 0;
        return SapiConstants.E_INVALIDARG;
    }

    public int OpenKey(string pszSubKeyName, out ISpDataKey ppSubKey)
    {
        if (SubKeys.TryGetValue(pszSubKeyName, out var key))
        {
            ppSubKey = key;
            return SapiConstants.S_OK;
        }
        ppSubKey = null!;
        return SapiConstants.E_INVALIDARG;
    }

    public int CreateKey(string pszSubKey, out ISpDataKey ppSubKey)
    {
        var key = GetOrCreateSubKey(pszSubKey);
        ppSubKey = key;
        return SapiConstants.S_OK;
    }

    public int DeleteKey(string pszSubKey) => SubKeys.Remove(pszSubKey) ? SapiConstants.S_OK : SapiConstants.E_INVALIDARG;
    public int DeleteValue(string pszValueName) => StringValues.Remove(pszValueName) ? SapiConstants.S_OK : SapiConstants.E_INVALIDARG;

    public int EnumKeys(uint Index, out IntPtr ppszSubKeyName)
    {
        int i = 0;
        foreach (var kv in SubKeys)
        {
            if (i == Index)
            {
                ppszSubKeyName = Marshal.StringToCoTaskMemUni(kv.Key);
                return SapiConstants.S_OK;
            }
            i++;
        }
        ppszSubKeyName = IntPtr.Zero;
        return SapiConstants.S_FALSE;
    }

    public int EnumValues(uint Index, out IntPtr ppszValueName)
    {
        int i = 0;
        foreach (var kv in StringValues)
        {
            if (i == Index)
            {
                ppszValueName = Marshal.StringToCoTaskMemUni(kv.Key);
                return SapiConstants.S_OK;
            }
            i++;
        }
        ppszValueName = IntPtr.Zero;
        return SapiConstants.S_FALSE;
    }
}
