using System;
using System.Runtime.InteropServices;
using VoiceGardenSAPIAdapter.SapiInterop;

namespace VoiceGardenSAPIAdapter;

[ComVisible(true)]
[Guid("E1A2B3C4-D5E6-4F7A-8B9C-0D1E2F3A4B5C")]
[ClassInterface(ClassInterfaceType.None)]
public class SpObjectToken : ISpObjectToken
{
    private readonly SpDataKey _data = new();
    private string _tokenId = "";

    public SpDataKey Data => _data;

    public int SetData(string pszValueName, uint cbData, IntPtr pData) => _data.SetData(pszValueName, cbData, pData);
    public int GetData(string pszValueName, ref uint pcbData, IntPtr pData) => _data.GetData(pszValueName, ref pcbData, pData);
    public int SetStringValue(string? pszValueName, string pszValue) => _data.SetStringValue(pszValueName, pszValue);
    public int GetStringValue(string? pszValueName, out IntPtr ppszValue) { Logger.Info($"SpObjectToken.GetStringValue('{pszValueName}')"); return _data.GetStringValue(pszValueName, out ppszValue); }
    public int SetDWORD(string pszValueName, uint dwValue) => _data.SetDWORD(pszValueName, dwValue);
    public int GetDWORD(string pszValueName, out uint pdwValue) { Logger.Info($"SpObjectToken.GetDWORD('{pszValueName}')"); return _data.GetDWORD(pszValueName, out pdwValue); }
    public int OpenKey(string pszSubKeyName, out ISpDataKey ppSubKey) { Logger.Info($"SpObjectToken.OpenKey('{pszSubKeyName}')"); return _data.OpenKey(pszSubKeyName, out ppSubKey); }
    public int CreateKey(string pszSubKey, out ISpDataKey ppSubKey) => _data.CreateKey(pszSubKey, out ppSubKey);
    public int DeleteKey(string pszSubKey) => _data.DeleteKey(pszSubKey);
    public int DeleteValue(string pszValueName) => _data.DeleteValue(pszValueName);
    public int EnumKeys(uint Index, out IntPtr ppszSubKeyName) => _data.EnumKeys(Index, out ppszSubKeyName);
    public int EnumValues(uint Index, out IntPtr ppszValueName) => _data.EnumValues(Index, out ppszValueName);

    public int SetId(string? pCategoryId, string pszTokenId, bool fCreateIfNotExist)
    {
        _tokenId = pszTokenId;
        return SapiConstants.S_OK;
    }

    public int GetId(out IntPtr ppszCoMemTokenId)
    {
        Logger.Info($"SpObjectToken.GetId() -> '{_tokenId}'");
        ppszCoMemTokenId = Marshal.StringToCoTaskMemUni(_tokenId);
        return SapiConstants.S_OK;
    }

    public int GetCategory(out ISpObjectTokenCategory ppTokenCategory)
    {
        ppTokenCategory = null!;
        return SapiConstants.E_NOTIMPL;
    }

    public int CreateToken(string pTokenId, out ISpObjectToken ppToken)
    {
        ppToken = null!;
        return SapiConstants.E_NOTIMPL;
    }

    public int GetStorageFileName(ref Guid clsidCaller, string pszValueName, string pszFileNameOrElse, uint nFolder, out IntPtr ppszFilePath)
    {
        ppszFilePath = IntPtr.Zero;
        return SapiConstants.E_NOTIMPL;
    }

    public int RemoveStorageFileName(ref Guid clsidCaller, string pszKeyName, bool fDeleteFile) => SapiConstants.E_NOTIMPL;
    public int Remove(string? ppszCoMemTokenId) => SapiConstants.E_NOTIMPL;

    public int IsUISupported(string pszTypeOfUI, IntPtr pvExtraData, uint cbExtraData, ISpObjectToken pTokenCur, out bool pfSupported)
    {
        pfSupported = false;
        return SapiConstants.S_OK;
    }

    public int DisplayUI(IntPtr hwndParent, string pszTitle, string pszTypeOfUI, IntPtr pvExtraData, uint cbExtraData, ISpObjectToken pTokenCur)
    {
        return SapiConstants.E_NOTIMPL;
    }
}
