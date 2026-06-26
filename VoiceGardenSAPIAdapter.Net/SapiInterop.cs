using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

#pragma warning disable CS0649

namespace VoiceGardenSAPIAdapter.SapiInterop;

public static class SapiClsids
{
    public static readonly Guid CLSID_TTSEngine = new("013AB33B-AD1A-401C-8BEE-F6E2B046A94E");
    public static readonly Guid CLSID_VoiceTokenEnumerator = new("B8B9E38F-E5A2-4661-9FDE-4AC7377AA6F6");
    public static readonly Guid CLSID_SpObjectToken = new("EF411752-3736-4CB4-9C8C-8EF4CCB58EFE");
    public static readonly Guid CLSID_SpObjectTokenEnum = new("3918D75F-0ACB-41F2-B733-92AA15BCECF6");
}

internal static class SapiIids
{
    public static readonly Guid IID_ISpTTSEngine = new("A74D7C8E-4CC5-4F2F-A6EB-804DEE18500E");
    public static readonly Guid IID_ISpTTSEngineSite = new("9880499B-CCE9-11D2-B503-00C04F797396");
    public static readonly Guid IID_ISpObjectWithToken = new("5B559F40-E952-11D2-BB91-00C04F8EE6C0");
    public static readonly Guid IID_ISpObjectToken = new("14056589-E16C-11D2-BB90-00C04F8EE6C0");
    public static readonly Guid IID_ISpDataKey = new("14056581-E16C-11D2-BB90-00C04F8EE6C0");
    public static readonly Guid IID_IEnumSpObjectTokens = new("06B64F9E-7FDA-11D2-B4F2-00C04F797396");
    public static readonly Guid IID_ISpObjectTokenCategory = new("2D3D3845-39AF-4850-BBF9-40B49780011D");
}

public static class SapiEventIds
{
    public const ushort SPEI_END_INPUT_STREAM = 14;
    public const ushort SPEI_TTS_BOOKMARK = 16;
    public const ushort SPEI_WORD_BOUNDARY = 17;
    public const ushort SPEI_PHONEME = 18;
    public const ushort SPEI_VISEME = 19;
    public const ushort SPEI_SENTENCE_BOUNDARY = 20;
    public const ushort SPEI_VISEME_CHANGED = 32;
    public const ushort SPEI_TTS_AUDIO_LEVEL = 33;
}

public static class SapiEventParamTypes
{
    public const ushort SPET_LPARAM_IS_UNDEFINED = 0;
    public const ushort SPET_LPARAM_IS_TOKEN = 1;
    public const ushort SPET_LPARAM_IS_OBJECT = 2;
    public const ushort SPET_LPARAM_IS_POINTER = 3;
    public const ushort SPET_LPARAM_IS_STRING = 4;
}

public enum SPVACTIONS
{
    SPVA_Speak = 0,
    SPVA_Silence = 1,
    SPVA_Pronounce = 2,
    SPVA_Bookmark = 3,
    SPVA_SpellOut = 4,
    SPVA_Section = 5,
    SPVA_ParseUnknownTag = 6,
}

public enum SPVSKIPTYPE
{
    SPVST_SENTENCE = 1,
    SPVST_WORD = 2,
}

[StructLayout(LayoutKind.Sequential)]
public struct SPVPITCH
{
    public int MiddleAdj;
    public int RangeAdj;
}

[StructLayout(LayoutKind.Sequential)]
public struct SPVCONTEXT
{
    public IntPtr pCategory;
    public IntPtr pBefore;
    public IntPtr pAfter;
}

[StructLayout(LayoutKind.Sequential)]
public struct SPVSTATE
{
    public SPVACTIONS eAction;
    public ushort LangID;
    public ushort wReserved;
    public int EmphAdj;
    public int RateAdj;
    public uint Volume;
    public SPVPITCH PitchAdj;
    public uint SilenceMSecs;
    public IntPtr pPhoneIds;
    public int ePartOfSpeech;
    public SPVCONTEXT Context;
}

[StructLayout(LayoutKind.Sequential)]
public struct SPVTEXTFRAG
{
    public IntPtr pNext;
    public SPVSTATE State;
    public IntPtr pTextStart;
    public uint ulTextLen;
    public uint ulTextSrcOffset;
}

[StructLayout(LayoutKind.Sequential)]
public struct SPEVENT
{
    public ushort eEventId;
    public ushort elParamType;
    public uint ulStreamNum;
    public ulong ullAudioStreamOffset;
    public IntPtr wParam;
    public IntPtr lParam;
}

[StructLayout(LayoutKind.Sequential)]
public struct WAVEFORMATEX
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint nSamplesPerSec;
    public uint nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WAVEFORMATEX_WITH_CB
{
    public WAVEFORMATEX Format;
    public ushort cbAdditionalData;
}

[ComImport]
[Guid("9880499B-CCE9-11D2-B503-00C04F797396")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface ISpTTSEngineSite
{
    [PreserveSig]
    int AddEvents(IntPtr pEventArray, uint ulCount);

    [PreserveSig]
    int GetEventInterest(out ulong pullEventInterest);

    [PreserveSig]
    uint GetActions();

    [PreserveSig]
    int Write(IntPtr pBuff, uint cb, out uint pcbWritten);

    [PreserveSig]
    int GetRate(out int pRateAdjust);

    [PreserveSig]
    int GetVolume(out ushort pusVolume);

    [PreserveSig]
    int GetSkipInfo(out SPVSKIPTYPE peType, out int plNumItems);

    [PreserveSig]
    int CompleteSkip(int ulNumSkipped);
}

[ComImport]
[Guid("A74D7C8E-4CC5-4F2F-A6EB-804DEE18500E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
internal interface ISpTTSEngine
{
    [PreserveSig]
    int Speak(
        uint dwSpeakFlags,
        ref Guid rguidFormatId,
        IntPtr pWaveFormatEx,
        IntPtr pTextFragList,
        [In] ISpTTSEngineSite pOutputSite);

    [PreserveSig]
    int GetOutputFormat(
        IntPtr pTargetFmtId,
        IntPtr pTargetWaveFormatEx,
        out Guid pOutputFormatId,
        out IntPtr ppCoMemOutputWaveFormatEx);
}

[ComImport]
[Guid("5B559F40-E952-11D2-BB91-00C04F8EE6C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface ISpObjectWithToken
{
    [PreserveSig]
    int SetObjectToken([In] ISpObjectToken pToken);

    [PreserveSig]
    int GetObjectToken(out ISpObjectToken ppToken);
}

[ComImport]
[Guid("14056589-E16C-11D2-BB90-00C04F8EE6C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface ISpObjectToken
{
    [PreserveSig]
    int SetData([In, MarshalAs(UnmanagedType.LPWStr)] string pszValueName, uint cbData, [In] IntPtr pData);

    [PreserveSig]
    int GetData([In, MarshalAs(UnmanagedType.LPWStr)] string pszValueName, ref uint pcbData, [Out] IntPtr pData);

    [PreserveSig]
    int SetStringValue([In, MarshalAs(UnmanagedType.LPWStr)] string? pszValueName, [In, MarshalAs(UnmanagedType.LPWStr)] string pszValue);

    [PreserveSig]
    int GetStringValue([In, MarshalAs(UnmanagedType.LPWStr)] string? pszValueName, [Out] out IntPtr ppszValue);

    [PreserveSig]
    int SetDWORD([In, MarshalAs(UnmanagedType.LPWStr)] string pszValueName, uint dwValue);

    [PreserveSig]
    int GetDWORD([In, MarshalAs(UnmanagedType.LPWStr)] string pszValueName, out uint pdwValue);

    [PreserveSig]
    int OpenKey([In, MarshalAs(UnmanagedType.LPWStr)] string pszSubKeyName, out ISpDataKey ppSubKey);

    [PreserveSig]
    int CreateKey([In, MarshalAs(UnmanagedType.LPWStr)] string pszSubKey, out ISpDataKey ppSubKey);

    [PreserveSig]
    int DeleteKey([In, MarshalAs(UnmanagedType.LPWStr)] string pszSubKey);

    [PreserveSig]
    int DeleteValue([In, MarshalAs(UnmanagedType.LPWStr)] string pszValueName);

    [PreserveSig]
    int EnumKeys(uint Index, [Out] out IntPtr ppszSubKeyName);

    [PreserveSig]
    int EnumValues(uint Index, [Out] out IntPtr ppszValueName);

    [PreserveSig]
    int SetId([In, MarshalAs(UnmanagedType.LPWStr)] string? pCategoryId, [In, MarshalAs(UnmanagedType.LPWStr)] string pszTokenId, [In, MarshalAs(UnmanagedType.Bool)] bool fCreateIfNotExist);

    [PreserveSig]
    int GetId([Out] out IntPtr ppszCoMemTokenId);

    [PreserveSig]
    int GetCategory([Out] out ISpObjectTokenCategory ppTokenCategory);

    [PreserveSig]
    int CreateToken([In, MarshalAs(UnmanagedType.LPWStr)] string pTokenId, [Out] out ISpObjectToken ppToken);

    [PreserveSig]
    int GetStorageFileName([In] ref Guid clsidCaller, [In, MarshalAs(UnmanagedType.LPWStr)] string pszValueName, [In, MarshalAs(UnmanagedType.LPWStr)] string pszFileNameOrElse, uint nFolder, [Out] out IntPtr ppszFilePath);

    [PreserveSig]
    int RemoveStorageFileName([In] ref Guid clsidCaller, [In, MarshalAs(UnmanagedType.LPWStr)] string pszKeyName, [In, MarshalAs(UnmanagedType.Bool)] bool fDeleteFile);

    [PreserveSig]
    int Remove([MarshalAs(UnmanagedType.LPWStr)] string? ppszCoMemTokenId);

    [PreserveSig]
    int IsUISupported([In, MarshalAs(UnmanagedType.LPWStr)] string pszTypeOfUI, [In] IntPtr pvExtraData, uint cbExtraData, [In] ISpObjectToken pTokenCur, [Out] out bool pfSupported);

    [PreserveSig]
    int DisplayUI([In] IntPtr hwndParent, [In, MarshalAs(UnmanagedType.LPWStr)] string pszTitle, [In, MarshalAs(UnmanagedType.LPWStr)] string pszTypeOfUI, [In] IntPtr pvExtraData, uint cbExtraData, [In] ISpObjectToken pTokenCur);
}

[ComImport]
[Guid("14056581-E16C-11D2-BB90-00C04F8EE6C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface ISpDataKey
{
    [PreserveSig]
    int SetData([In, MarshalAs(UnmanagedType.LPWStr)] string pszValueName, uint cbData, [In] IntPtr pData);

    [PreserveSig]
    int GetData([In, MarshalAs(UnmanagedType.LPWStr)] string pszValueName, ref uint pcbData, [Out] IntPtr pData);

    [PreserveSig]
    int SetStringValue([In, MarshalAs(UnmanagedType.LPWStr)] string pszValueName, [In, MarshalAs(UnmanagedType.LPWStr)] string pszValue);

    [PreserveSig]
    int GetStringValue([In, MarshalAs(UnmanagedType.LPWStr)] string pszValueName, [Out] out IntPtr ppszValue);

    [PreserveSig]
    int SetDWORD([In, MarshalAs(UnmanagedType.LPWStr)] string pszValueName, uint dwValue);

    [PreserveSig]
    int GetDWORD([In, MarshalAs(UnmanagedType.LPWStr)] string pszValueName, out uint pdwValue);

    [PreserveSig]
    int OpenKey([In, MarshalAs(UnmanagedType.LPWStr)] string pszSubKeyName, out ISpDataKey ppSubKey);

    [PreserveSig]
    int CreateKey([In, MarshalAs(UnmanagedType.LPWStr)] string pszSubKey, out ISpDataKey ppSubKey);

    [PreserveSig]
    int DeleteKey([In, MarshalAs(UnmanagedType.LPWStr)] string pszSubKey);

    [PreserveSig]
    int DeleteValue([In, MarshalAs(UnmanagedType.LPWStr)] string pszValueName);

    [PreserveSig]
    int EnumKeys(uint Index, [Out] out IntPtr ppszSubKeyName);

    [PreserveSig]
    int EnumValues(uint Index, [Out] out IntPtr ppszValueName);
}

[ComImport]
[Guid("06B64F9E-7FDA-11D2-B4F2-00C04F797396")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface IEnumSpObjectTokens
{
    [PreserveSig]
    int Next(uint celt, [Out] out ISpObjectToken pelt, out uint pceltFetched);

    [PreserveSig]
    int Skip(uint celt);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IEnumSpObjectTokens ppEnum);

    [PreserveSig]
    int Item(uint Index, out ISpObjectToken ppToken);

    [PreserveSig]
    int GetCount(out uint pCount);
}

[ComImport]
[Guid("2D3D3845-39AF-4850-BBF9-40B49780011D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
public interface ISpObjectTokenCategory
{
}

public static class SapiConstants
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int E_POINTER = unchecked((int)0x80004003);
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int E_INVALIDARG = unchecked((int)0x80070057);
    public const int E_OUTOFMEMORY = unchecked((int)0x8007000E);
    public const int CO_E_NOTINITIALIZED = unchecked((int)0x800401F0);
    public const uint SPVES_ABORT = 1;
    public const uint SPVES_RATE = 2;
    public const uint SPVES_VOLUME = 4;
    public static readonly Guid SPDFID_WaveFormatEx = new("C79ADBB0-3E93-4EB3-9463-CFCC4C7B0F36");
}
