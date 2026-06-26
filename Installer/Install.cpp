#include "Installer.h"
#include "RegKey.h"
#include "../include/AppDataLayout.h"
#include <system_error>
#include <stdexcept>
#include <vector>
#include <string>
#include <ShlObj.h>


namespace
{
constexpr wchar_t kTtsEngineClsid[] = L"{013ab33b-ad1a-401c-8bee-f6e2b046a94e}";
constexpr wchar_t kVoiceTokenEnumeratorClsid[] = L"{b8b9e38f-e5a2-4661-9fde-4ac7377aa6f6}";

std::string WideToUtf8(const std::wstring& value)
{
    if (value.empty())
        return {};

    int len = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), (int)value.size(), nullptr, 0, nullptr, nullptr);
    if (len <= 0)
        return {};

    std::string utf8(len, '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.c_str(), (int)value.size(), utf8.data(), len, nullptr, nullptr);
    return utf8;
}

void AppendInstallLog(const std::wstring& message)
{
    wchar_t localAppData[MAX_PATH] = {};
    if (FAILED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, SHGFP_TYPE_CURRENT, localAppData)))
        return;

    wchar_t dirPath[MAX_PATH] = {};
    wcsncpy_s(dirPath, localAppData, _TRUNCATE);
    const std::wstring localBase = localAppData;
    const std::wstring preferredRootName = AppDataLayout::ResolveInstallFolderNameNearModule(nullptr);
    const std::wstring rootName = AppDataLayout::ChooseExistingRootName(localBase, preferredRootName);
    if (!PathAppendW(dirPath, rootName.c_str()))
        return;

    CreateDirectoryW(dirPath, nullptr);

    wchar_t logPath[MAX_PATH] = {};
    wcsncpy_s(logPath, dirPath, _TRUNCATE);
    if (!PathAppendW(logPath, L"installer.log"))
        return;

    SYSTEMTIME st = {};
    GetLocalTime(&st);

    wchar_t line[1024] = {};
    swprintf_s(line, L"[%04u-%02u-%02u %02u:%02u:%02u] %s\r\n",
        st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, message.c_str());

    HANDLE hFile = CreateFileW(logPath, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
        OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (hFile == INVALID_HANDLE_VALUE)
        return;

    DWORD bytes = 0;
    std::string utf8 = WideToUtf8(line);
    WriteFile(hFile, utf8.data(), (DWORD)utf8.size(), &bytes, nullptr);
    CloseHandle(hFile);
}

std::wstring ReadRegistryString(HKEY root, const std::wstring& subKey, LPCWSTR valueName, REGSAM wowView)
{
    HKEY hKey = nullptr;
    if (RegOpenKeyExW(root, subKey.c_str(), 0, KEY_QUERY_VALUE | wowView, &hKey) != ERROR_SUCCESS)
        return {};

    DWORD type = 0;
    DWORD bytes = 0;
    if (RegQueryValueExW(hKey, valueName, nullptr, &type, nullptr, &bytes) != ERROR_SUCCESS ||
        (type != REG_SZ && type != REG_EXPAND_SZ) || bytes < sizeof(wchar_t))
    {
        RegCloseKey(hKey);
        return {};
    }

    std::wstring value(bytes / sizeof(wchar_t), L'\0');
    if (RegQueryValueExW(hKey, valueName, nullptr, nullptr, reinterpret_cast<LPBYTE>(value.data()), &bytes) != ERROR_SUCCESS)
    {
        RegCloseKey(hKey);
        return {};
    }
    RegCloseKey(hKey);

    size_t nullPos = value.find(L'\0');
    if (nullPos != std::wstring::npos)
        value.resize(nullPos);
    return value;
}

bool VerifyComClassRegistration(bool is64Bit, const wchar_t* clsid, const std::wstring& expectedDll, std::wstring& reason)
{
    const REGSAM wowView = is64Bit ? KEY_WOW64_64KEY : KEY_WOW64_32KEY;
    std::wstring key = std::wstring(L"SOFTWARE\\Classes\\CLSID\\") + clsid + L"\\InprocServer32";
    std::wstring actual = ReadRegistryString(HKEY_LOCAL_MACHINE, key, nullptr, wowView);
    if (actual.empty())
    {
        reason = L"Missing COM registration at HKLM\\" + key;
        return false;
    }

    if (_wcsicmp(actual.c_str(), expectedDll.c_str()) != 0)
    {
        reason = L"COM registration path mismatch. Expected '" + expectedDll + L"' but found '" + actual + L"'";
        return false;
    }

    return true;
}

bool VerifyTokenEnumeratorRegistration(bool is64Bit, std::wstring& reason)
{
    const REGSAM wowView = is64Bit ? KEY_WOW64_64KEY : KEY_WOW64_32KEY;
    constexpr wchar_t key[] = L"SOFTWARE\\Microsoft\\Speech\\Voices\\TokenEnums\\NaturalVoiceEnumerator";
    std::wstring clsid = ReadRegistryString(HKEY_LOCAL_MACHINE, key, L"CLSID", wowView);
    if (clsid.empty())
    {
        reason = L"Missing TokenEnums registration at HKLM\\SOFTWARE\\Microsoft\\Speech\\Voices\\TokenEnums\\NaturalVoiceEnumerator";
        return false;
    }
    if (_wcsicmp(clsid.c_str(), kVoiceTokenEnumeratorClsid) != 0)
    {
        reason = L"TokenEnums CLSID mismatch. Expected '" + std::wstring(kVoiceTokenEnumeratorClsid) + L"' but found '" + clsid + L"'";
        return false;
    }
    return true;
}

bool IsCurrentProcess64Bit() noexcept
{
#ifdef _WIN64
    return true;
#else
    return false;
#endif
}

bool SelfTestVoiceTokenEnumerator(std::wstring& reason)
{
    CLSID clsid = {};
    HRESULT hr = CLSIDFromString(const_cast<LPOLESTR>(kVoiceTokenEnumeratorClsid), &clsid);
    if (FAILED(hr))
    {
        reason = L"CLSIDFromString failed for VoiceTokenEnumerator.";
        return false;
    }

    IUnknown* instance = nullptr;
    hr = CoCreateInstance(clsid, nullptr, CLSCTX_INPROC_SERVER, IID_IUnknown, reinterpret_cast<void**>(&instance));
    if (FAILED(hr))
    {
        wchar_t buffer[128] = {};
        swprintf_s(buffer, L"CoCreateInstance failed with HRESULT 0x%08X", static_cast<unsigned int>(hr));
        reason = buffer;
        return false;
    }

    instance->Release();
    return true;
}

void VerifyRegistrationOrThrow(bool is64Bit, const std::wstring& expectedDll)
{
    std::wstring reason;

    if (!VerifyComClassRegistration(is64Bit, kTtsEngineClsid, expectedDll, reason))
    {
        AppendInstallLog(L"Registration verification failed: " + reason);
        throw std::runtime_error(WideToUtf8(reason));
    }

    if (!VerifyComClassRegistration(is64Bit, kVoiceTokenEnumeratorClsid, expectedDll, reason))
    {
        AppendInstallLog(L"Registration verification failed: " + reason);
        throw std::runtime_error(WideToUtf8(reason));
    }

    if (!VerifyTokenEnumeratorRegistration(is64Bit, reason))
    {
        AppendInstallLog(L"Registration verification failed: " + reason);
        throw std::runtime_error(WideToUtf8(reason));
    }

    if (is64Bit != IsCurrentProcess64Bit())
    {
        AppendInstallLog(L"Post-register COM self-test skipped due to bitness mismatch between installer process and target registration.");
        return;
    }

    HRESULT hrInit = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    const bool shouldUninit = SUCCEEDED(hrInit);
    if (FAILED(hrInit) && hrInit != RPC_E_CHANGED_MODE)
    {
        wchar_t buffer[128] = {};
        swprintf_s(buffer, L"COM init failed with HRESULT 0x%08X", static_cast<unsigned int>(hrInit));
        reason = buffer;
        AppendInstallLog(L"Registration verification failed: " + reason);
        throw std::runtime_error(WideToUtf8(reason));
    }

    if (!SelfTestVoiceTokenEnumerator(reason))
    {
        if (shouldUninit)
            CoUninitialize();
        AppendInstallLog(L"Post-register COM self-test failed: " + reason);
        throw std::runtime_error(WideToUtf8(reason));
    }

    if (shouldUninit)
        CoUninitialize();

    AppendInstallLog(L"Post-register COM self-test passed (VoiceTokenEnumerator instantiated successfully).");
}
}

// Returns the exit code. Throws if failed to launch.
static DWORD LaunchProcess(LPCWSTR pszApp, LPCWSTR pszCmdLine, bool asAdmin)
{
    HWND hWnd = GetActiveWindow();

    SHELLEXECUTEINFOW info = { sizeof info };
    info.fMask = SEE_MASK_NOCLOSEPROCESS;
    info.lpFile = pszApp;
    info.lpParameters = pszCmdLine;
    info.nShow = SW_HIDE;
    info.hwnd = hWnd;
    if (asAdmin && !IsAdmin() && SupportsUAC())
        info.lpVerb = L"runas";

    if (!ShellExecuteExW(&info))
    {
        throw std::system_error(GetLastError(), std::system_category());
    }

    DWORD exitcode = 0;
    if (info.hProcess)
    {
        HCURSOR hCur = SetCursor(LoadCursorW(nullptr, IDC_WAIT));
        WaitForSingleObject(info.hProcess, INFINITE);
        GetExitCodeProcess(info.hProcess, &exitcode);
        CloseHandle(info.hProcess);
        SetCursor(hCur);
    }

    return exitcode;
}

static void AddUninstallRegistryKey()
{
    // Skip if running from Program Files (MSI handles ARP entry)
    WCHAR modulePath[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, modulePath, MAX_PATH);
    if (wcsstr(modulePath, L"Program Files"))
        return;

    RegKey key;
    if (key.Create(HKEY_CURRENT_USER,
        L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\NaturalVoiceSAPIAdapter",
        KEY_SET_VALUE | KEY_WOW64_64KEY) != ERROR_SUCCESS)
        return;

    WCHAR uninstallCmdLine[MAX_PATH + 11];
    DWORD len = GetModuleFileNameW(nullptr, uninstallCmdLine, MAX_PATH);
    if (len == 0 || len >= MAX_PATH - 3)  // 3 for quotes + null
        return;
    PathQuoteSpacesW(uninstallCmdLine);
    wcscat_s(uninstallCmdLine, L" -uninstall");

    key.SetString(L"DisplayName", L"NaturalVoiceSAPIAdapter");
    key.SetString(L"DisplayVersion", L"0.2");
    key.SetString(L"Publisher", L"gexgd0419 on GitHub");
    key.SetString(L"UninstallString", uninstallCmdLine);
    key.SetString(L"HelpLink", L"https://github.com/gexgd0419/NaturalVoiceSAPIAdapter");
    key.SetString(L"URLInfoAbout", L"https://github.com/gexgd0419/NaturalVoiceSAPIAdapter");
    key.SetString(L"URLUpdateInfo", L"https://github.com/gexgd0419/NaturalVoiceSAPIAdapter/releases");
}

static void RemoveUninstallRegistryKey()
{
    RegDeleteKeyW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\NaturalVoiceSAPIAdapter");
}

static bool CombinePath(const std::wstring& base, const std::wstring& leaf, std::wstring& out)
{
    WCHAR buf[MAX_PATH];
    wcsncpy_s(buf, base.c_str(), _TRUNCATE);
    if (!PathAppendW(buf, leaf.c_str()))
        return false;
    out = buf;
    return true;
}

static bool IsDllArchitectureCompatible(const std::wstring& dllPath, bool is64Bit)
{
    HANDLE hFile = CreateFileW(dllPath.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (hFile == INVALID_HANDLE_VALUE)
        return false;

    HANDLE hMap = CreateFileMappingW(hFile, nullptr, PAGE_READONLY, 0, 0, nullptr);
    if (!hMap)
    {
        CloseHandle(hFile);
        return false;
    }

    auto* base = static_cast<const BYTE*>(MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, 0));
    if (!base)
    {
        CloseHandle(hMap);
        CloseHandle(hFile);
        return false;
    }

    bool compatible = false;
    auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic == IMAGE_DOS_SIGNATURE)
    {
        auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
        if (nt->Signature == IMAGE_NT_SIGNATURE)
        {
            WORD machine = nt->FileHeader.Machine;
            if (is64Bit)
            {
                compatible = (machine == IMAGE_FILE_MACHINE_AMD64 || machine == IMAGE_FILE_MACHINE_ARM64);
            }
            else
            {
                compatible = (machine == IMAGE_FILE_MACHINE_I386);
            }
        }
    }

    UnmapViewOfFile(base);
    CloseHandle(hMap);
    CloseHandle(hFile);
    return compatible;
}

static std::wstring GetParentDirectory(const std::wstring& dir)
{
    WCHAR buf[MAX_PATH];
    wcsncpy_s(buf, dir.c_str(), _TRUNCATE);
    PathRemoveFileSpecW(buf);
    return buf;
}

static bool FindPayloadDirectory(bool is64Bit, std::wstring& payloadDir)
{
    WCHAR exePath[MAX_PATH];
    DWORD len = GetModuleFileNameW(nullptr, exePath, MAX_PATH);
    if (len == 0 || len == MAX_PATH)
        return false;

    PathRemoveFileSpecW(exePath);
    std::wstring exeDir = exePath;
    std::wstring parentDir = GetParentDirectory(exeDir);

    const std::wstring archDir = is64Bit ? (IsArm64System() ? L"arm64" : L"x64") : L"x86";
    std::vector<std::wstring> candidates;
    candidates.push_back(exeDir + L"\\" + archDir);                 // ZIP layout: Installer.exe next to x64/x86
    candidates.push_back(exeDir);                                   // build-all local out layout
    candidates.push_back(parentDir + L"\\" + archDir);              // Installer in own subfolder beside x64/x86
    candidates.push_back(parentDir);                                // fallback for flat parent payload
    candidates.push_back(exeDir + L"\\..\\out\\" + archDir);        // dev layout
    candidates.push_back(exeDir + L"\\..\\out");                    // dev flat out
    candidates.push_back(exeDir + L"\\..\\" + archDir + L"\\Release"); // old CI layout

    for (const auto& candidate : candidates)
    {
        std::wstring dllPath;
        if (CombinePath(candidate, L"NaturalVoiceSAPIAdapter.dll", dllPath)
            && PathFileExistsW(dllPath.c_str())
            && IsDllArchitectureCompatible(dllPath, is64Bit))
        {
            payloadDir = candidate;
            return true;
        }
    }
    return false;
}

static bool FindResourcePath(const std::wstring& relativePath, std::wstring& absolutePath)
{
    WCHAR exePath[MAX_PATH];
    DWORD len = GetModuleFileNameW(nullptr, exePath, MAX_PATH);
    if (len == 0 || len == MAX_PATH)
        return false;

    PathRemoveFileSpecW(exePath);
    std::wstring exeDir = exePath;
    std::wstring parentDir = GetParentDirectory(exeDir);

    std::vector<std::wstring> candidates;
    candidates.push_back(exeDir + L"\\" + relativePath);
    candidates.push_back(parentDir + L"\\" + relativePath);      // installer in its own folder
    candidates.push_back(exeDir + L"\\..\\out\\" + relativePath); // dev layout

    for (const auto& candidate : candidates)
    {
        if (PathFileExistsW(candidate.c_str()))
        {
            absolutePath = candidate;
            return true;
        }
    }
    return false;
}

void Register(bool is64Bit)
{
    std::wstring payloadDir;
    if (!FindPayloadDirectory(is64Bit, payloadDir))
        throw std::system_error(ERROR_FILE_NOT_FOUND, std::system_category());

    if (!SupportsInstallingNarratorVoices())
    {
        // On systems that do not support Narrator voices natively,
        // we should patch the Azure Speech SDK DLLs
        std::wstring patcherPath;
        if (!CombinePath(payloadDir, L"SpeechSDKPatcher.exe", patcherPath))
            throw std::system_error(ERROR_FILENAME_EXCED_RANGE, std::system_category());
        if (!PathFileExistsW(patcherPath.c_str()))
            throw std::system_error(ERROR_FILE_NOT_FOUND, std::system_category());

        DWORD exitcode = LaunchProcess(patcherPath.c_str(), L"-quiet", false);

        // if no permission, try again as admin
        if (exitcode == ERROR_ACCESS_DENIED && !IsAdmin() && SupportsUAC())
            exitcode = LaunchProcess(patcherPath.c_str(), L"-quiet", true);

        if (exitcode != ERROR_SUCCESS)
            throw std::system_error(exitcode, std::system_category());
    }

    std::wstring dllPath;
    if (!CombinePath(payloadDir, L"NaturalVoiceSAPIAdapter.dll", dllPath))
        throw std::system_error(ERROR_FILENAME_EXCED_RANGE, std::system_category());
    if (!PathFileExistsW(dllPath.c_str()))
        throw std::system_error(ERROR_FILE_NOT_FOUND, std::system_category());

    std::wstring cmdline = std::wstring(L"/s \"") + dllPath + L'"';

    DWORD exitcode = LaunchProcess(L"regsvr32", cmdline.c_str(), true);
    if (exitcode != 0)
        throw std::system_error(exitcode, std::system_category());

    VerifyRegistrationOrThrow(is64Bit, dllPath);

    AddUninstallRegistryKey();
}

void Unregister(bool is64Bit)
{
    std::wstring dllpath = GetInstalledPath(is64Bit);

    if (!dllpath.empty())
    {
        std::wstring cmdline = L"/u /s \"" + dllpath + L'"';

        DWORD exitcode = LaunchProcess(L"regsvr32", cmdline.c_str(), true);
        if (exitcode != 0)
            throw std::system_error(exitcode, std::system_category());
    }

    if (is64Bit
        ? GetInstalledPath(false).empty()
        : (!Is64BitSystem() || GetInstalledPath(true).empty())
        )
    {
        RemoveUninstallRegistryKey();
    }
}

static void AddToRegistry(LPCWSTR regfile)
{
    std::wstring regFilePath;
    if (!FindResourcePath(regfile, regFilePath))
    {
        ReportError(ERROR_FILE_NOT_FOUND);
        return;
    }

    std::wstring cmdline = std::wstring(L"import \"") + regFilePath + L'"';

    DWORD exitcode = LaunchProcess(L"reg", cmdline.c_str(), true);
    // We can know if it failed or not, but not why failed
    ReportError(exitcode == 0 ? ERROR_SUCCESS : E_FAIL);
}

void CheckPhonemeConverters()
{
    HKEY hKey;
    bool hasConverters = true;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Speech\\PhoneConverters\\Tokens\\Universal",
        0, KEY_QUERY_VALUE | KEY_WOW64_32KEY, &hKey) == ERROR_SUCCESS)
    {
        RegCloseKey(hKey);
        if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Speech\\PhoneConverters\\Tokens\\Universal",
            0, KEY_QUERY_VALUE | KEY_WOW64_64KEY, &hKey) == ERROR_SUCCESS)
        {
            RegCloseKey(hKey);
        }
        else
            hasConverters = false;
    }
    else
        hasConverters = false;

    if (hasConverters)
        return;

    if (ShowMessageBox(IDS_INSTALL_PHONEME_CONVERTERS, MB_ICONASTERISK | MB_YESNO) != IDYES)
        return;

    try
    {
        if (Is64BitSystem())
            AddToRegistry(L"x64\\PhoneConverters.reg");
        else
            AddToRegistry(L"x86\\PhoneConverters.reg");
    }
    catch (const std::system_error& ex)
    {
        ReportError(ex.code().value());
    }
}
