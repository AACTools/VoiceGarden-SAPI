#include "Installer.h"
#include "RegKey.h"
#include "../include/nlohmann/json.hpp"
#include "../include/AppDataLayout.h"
#include <vector>
#include <string>
#include <fstream>
#include <sstream>
#include <system_error>
#include <algorithm>
#include <iostream>
#include <cwctype>
#include <ShlObj.h>

void Register(bool is64Bit);
void Unregister(bool is64Bit);
INT_PTR CALLBACK MainDlg(HWND hDlg, UINT message, WPARAM wParam, LPARAM lParam);
void CheckPhonemeConverters();

namespace
{
struct InstallPlan
{
    int version = 0;
    bool scopeAllUsers = false;
    bool archX64 = false;
    bool archX86 = false;

    bool enableAzure = false;
    std::wstring azureKey;
    std::wstring azureRegion;
    bool azureValidate = true;

    bool enableEdge = false;
    bool enableNarrator = true;
    bool enableSherpa = false;
    bool enableEmbeddedMsix = false;
    std::wstring embeddedMsixPath;
    bool embeddedMsixInstall = true;
    std::wstring narratorVoicePath;

    std::vector<std::wstring> sherpaModelsToDownload;
    bool sherpaRescan = true;
    bool sherpaPromoteHklm = false;
    bool sherpaCompatEnUs = false;
    std::vector<std::wstring> sherpaCompatModels;
    std::wstring sherpaTestVoiceId;

    bool registerCom = true;
    bool verifyRegistration = true;
    bool runSelfTest = true;
};

struct CliOptions
{
    bool uninstall = false;
    bool silent = false;
    bool json = false;
    bool dryRun = false;
    bool showHelp = false;
    std::wstring planPath;

    bool useDirectPlan = false;
    InstallPlan directPlan;
};

std::string WideToUtf8(const std::wstring& value)
{
    if (value.empty())
        return {};
    int len = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), (int)value.size(), nullptr, 0, nullptr, nullptr);
    if (len <= 0)
        return {};
    std::string out(len, '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.c_str(), (int)value.size(), out.data(), len, nullptr, nullptr);
    return out;
}

std::wstring Utf8ToWide(const std::string& value)
{
    if (value.empty())
        return {};
    int len = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), (int)value.size(), nullptr, 0);
    if (len <= 0)
        return {};
    std::wstring out(len, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value.c_str(), (int)value.size(), out.data(), len);
    return out;
}

void PrintUsage()
{
    std::wcout
        << L"NaturalVoice Installer CLI\n\n"
        << L"Usage:\n"
        << L"  Installer.exe\n"
        << L"  Installer.exe -uninstall\n"
        << L"  Installer.exe <install-plan.json>\n"
        << L"  Installer.exe --silent --plan <file.json>\n"
        << L"  Installer.exe --silent --scope current-user --arch x64 --engine sherpa --sherpa-rescan\n\n"
        << L"Common options:\n"
        << L"  --silent\n"
        << L"  --json\n"
        << L"  --dry-run\n"
        << L"  --plan <file>\n"
        << L"  --scope current-user|all-users\n"
        << L"  --arch x64|x86|x64,x86\n"
        << L"  --engine azure|edge|sherpa|narrator (repeatable)\n"
        << L"  --azure-key <key> --azure-region <region> [--azure-validate]\n"
        << L"  --msix <file-or-folder> [--msix-install|--msix-extract-only]\n"
        << L"  --narrator-path <folder>\n"
        << L"  --sherpa-model <id> (repeatable)\n"
        << L"  --sherpa-rescan\n"
        << L"  --sherpa-promote-hklm\n"
        << L"  --sherpa-compat-alias none|en-us|dual\n"
        << L"  --sherpa-compat-model <id> (repeatable)\n"
        << L"  --sherpa-test-voice <id>\n";
}

void PrintJsonResult(bool ok, int code, const std::wstring& message)
{
    nlohmann::json j;
    j["ok"] = ok;
    j["code"] = code;
    j["message"] = WideToUtf8(message);
    std::cout << j.dump() << std::endl;
}

DWORD RunProcess(LPCWSTR app, LPCWSTR args, bool asAdmin)
{
    SHELLEXECUTEINFOW info = { sizeof info };
    info.fMask = SEE_MASK_NOCLOSEPROCESS;
    info.lpFile = app;
    info.lpParameters = args;
    info.nShow = SW_HIDE;
    if (asAdmin && !IsAdmin() && SupportsUAC())
        info.lpVerb = L"runas";

    if (!ShellExecuteExW(&info))
        throw std::system_error(GetLastError(), std::system_category());

    DWORD exitcode = 0;
    if (info.hProcess)
    {
        WaitForSingleObject(info.hProcess, INFINITE);
        GetExitCodeProcess(info.hProcess, &exitcode);
        CloseHandle(info.hProcess);
    }
    return exitcode;
}

bool EndsWithI(const std::wstring& value, const std::wstring& suffix)
{
    if (suffix.size() > value.size())
        return false;
    return _wcsicmp(value.c_str() + (value.size() - suffix.size()), suffix.c_str()) == 0;
}

std::wstring GetLocalAppDataPath()
{
    WCHAR path[MAX_PATH] = {};
    if (FAILED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, SHGFP_TYPE_CURRENT, path)))
        return {};
    return path;
}

bool EnsureDirectory(const std::wstring& path)
{
    if (path.empty())
        return false;
    if (PathFileExistsW(path.c_str()))
        return true;
    return CreateDirectoryW(path.c_str(), nullptr) != 0;
}

std::wstring Quote(const std::wstring& s)
{
    return L"\"" + s + L"\"";
}

std::wstring GetExecutableNameLower()
{
    WCHAR path[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    std::wstring file = PathFindFileNameW(path);
    std::transform(file.begin(), file.end(), file.begin(), towlower);
    return file;
}

std::wstring GetExecutableDir()
{
    WCHAR path[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    PathRemoveFileSpecW(path);
    return path;
}

int SetupEmbeddedMsix(const InstallPlan& plan, std::wstring& err)
{
    if (!plan.enableEmbeddedMsix)
        return 0;

    if (plan.embeddedMsixPath.empty())
    {
        err = L"embedded_msix is enabled but package_path is empty.";
        return 5;
    }
    if (!PathFileExistsW(plan.embeddedMsixPath.c_str()))
    {
        err = L"embedded_msix package/path does not exist: " + plan.embeddedMsixPath;
        return 5;
    }

    std::wstring narratorPath = plan.narratorVoicePath;
    if (narratorPath.empty())
    {
        std::wstring local = GetLocalAppDataPath();
        if (local.empty())
        {
            err = L"Unable to resolve %LOCALAPPDATA% for embedded_msix extraction.";
            return 5;
        }
        const std::wstring preferredRootName = AppDataLayout::ResolveInstallFolderNameNearModule(nullptr);
        const std::wstring rootName = AppDataLayout::ChooseExistingRootName(local, preferredRootName);
        narratorPath = local + L"\\" + rootName + L"\\LocalVoices";
    }
    if (!EnsureDirectory(narratorPath))
    {
        err = L"Failed to create narrator voice path: " + narratorPath;
        return 5;
    }

    if (plan.embeddedMsixInstall && (EndsWithI(plan.embeddedMsixPath, L".msix") || EndsWithI(plan.embeddedMsixPath, L".appx")))
    {
        // Best effort install for supported systems.
        std::wstring psArgs = L"-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path " +
            Quote(plan.embeddedMsixPath) + L"\"";
        DWORD rc = RunProcess(L"powershell.exe", psArgs.c_str(), plan.scopeAllUsers);
        if (rc != 0)
        {
            // Fallback: extract package as zip to local voices folder.
            std::wstring stem = plan.embeddedMsixPath;
            size_t slash = stem.find_last_of(L"\\/");
            if (slash != std::wstring::npos) stem = stem.substr(slash + 1);
            size_t dot = stem.find_last_of(L'.');
            if (dot != std::wstring::npos) stem = stem.substr(0, dot);
            std::wstring dest = narratorPath + L"\\" + stem;
            EnsureDirectory(dest);
            std::wstring extractArgs = L"-NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath " +
                Quote(plan.embeddedMsixPath) + L" -DestinationPath " + Quote(dest) + L" -Force\"";
            DWORD erc = RunProcess(L"powershell.exe", extractArgs.c_str(), false);
            if (erc != 0)
            {
                err = L"embedded_msix install and extract fallback both failed.";
                return 5;
            }
        }
    }
    else if (EndsWithI(plan.embeddedMsixPath, L".msix") || EndsWithI(plan.embeddedMsixPath, L".appx"))
    {
        std::wstring stem = plan.embeddedMsixPath;
        size_t slash = stem.find_last_of(L"\\/");
        if (slash != std::wstring::npos) stem = stem.substr(slash + 1);
        size_t dot = stem.find_last_of(L'.');
        if (dot != std::wstring::npos) stem = stem.substr(0, dot);
        std::wstring dest = narratorPath + L"\\" + stem;
        EnsureDirectory(dest);
        std::wstring extractArgs = L"-NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath " +
            Quote(plan.embeddedMsixPath) + L" -DestinationPath " + Quote(dest) + L" -Force\"";
        DWORD erc = RunProcess(L"powershell.exe", extractArgs.c_str(), false);
        if (erc != 0)
        {
            err = L"embedded_msix extract failed.";
            return 5;
        }
    }
    else
    {
        // Directory mode.
        narratorPath = plan.embeddedMsixPath;
    }

    RegKey key;
    key.Create(HKEY_CURRENT_USER, L"Software\\NaturalVoiceSAPIAdapter\\Enumerator", KEY_SET_VALUE);
    key.SetString(L"NarratorVoicePath", narratorPath.c_str());
    key.SetDword(L"NoNarratorVoices", 0);

    return 0;
}

bool FindSherpaConfigToolPath(std::wstring& configToolPath)
{
    WCHAR path[MAX_PATH];
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    PathRemoveFileSpecW(path);

    std::vector<std::wstring> candidates;
    candidates.emplace_back(std::wstring(path) + L"\\SherpaOnnxConfig.exe");
    candidates.emplace_back(std::wstring(path) + L"\\x64\\SherpaOnnxConfig.exe");
    candidates.emplace_back(std::wstring(path) + L"\\x86\\SherpaOnnxConfig.exe");
    candidates.emplace_back(std::wstring(path) + L"\\..\\x64\\Release\\SherpaOnnxConfig.exe");
    candidates.emplace_back(std::wstring(path) + L"\\..\\out\\SherpaOnnxConfig.exe");

    for (const auto& p : candidates)
    {
        if (PathFileExistsW(p.c_str()))
        {
            configToolPath = p;
            return true;
        }
    }
    return false;
}

bool ParseCli(CliOptions& opt, std::wstring& err)
{
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv)
    {
        err = L"Command line parsing failed.";
        return false;
    }
    std::unique_ptr<void, decltype(&LocalFree)> argvHolder(argv, LocalFree);

    for (int i = 1; i < argc; ++i)
    {
        std::wstring a = argv[i];
        if (_wcsicmp(a.c_str(), L"-uninstall") == 0)
            opt.uninstall = true;
        else if (_wcsicmp(a.c_str(), L"--silent") == 0)
            opt.silent = true;
        else if (_wcsicmp(a.c_str(), L"--json") == 0)
            opt.json = true;
        else if (_wcsicmp(a.c_str(), L"--dry-run") == 0)
            opt.dryRun = true;
        else if (_wcsicmp(a.c_str(), L"--help") == 0 || _wcsicmp(a.c_str(), L"-h") == 0 || _wcsicmp(a.c_str(), L"/?") == 0)
            opt.showHelp = true;
        else if (_wcsicmp(a.c_str(), L"--plan") == 0)
        {
            if (i + 1 >= argc)
            {
                err = L"--plan requires a file path.";
                return false;
            }
            opt.planPath = argv[++i];
        }
        else if (_wcsicmp(a.c_str(), L"--scope") == 0)
        {
            if (i + 1 >= argc) { err = L"--scope requires value."; return false; }
            std::wstring v = argv[++i];
            opt.useDirectPlan = true;
            opt.directPlan.scopeAllUsers = (_wcsicmp(v.c_str(), L"all-users") == 0);
        }
        else if (_wcsicmp(a.c_str(), L"--arch") == 0)
        {
            if (i + 1 >= argc) { err = L"--arch requires value."; return false; }
            std::wstring v = argv[++i];
            opt.useDirectPlan = true;
            if (v.find(L"x64") != std::wstring::npos) opt.directPlan.archX64 = true;
            if (v.find(L"x86") != std::wstring::npos) opt.directPlan.archX86 = true;
        }
        else if (_wcsicmp(a.c_str(), L"--engine") == 0)
        {
            if (i + 1 >= argc) { err = L"--engine requires value."; return false; }
            std::wstring v = argv[++i];
            opt.useDirectPlan = true;
            std::transform(v.begin(), v.end(), v.begin(), towlower);
            if (v.find(L"azure") != std::wstring::npos) opt.directPlan.enableAzure = true;
            if (v.find(L"edge") != std::wstring::npos) opt.directPlan.enableEdge = true;
            if (v.find(L"sherpa") != std::wstring::npos) opt.directPlan.enableSherpa = true;
            if (v.find(L"narrator") != std::wstring::npos) opt.directPlan.enableNarrator = true;
        }
        else if (_wcsicmp(a.c_str(), L"--azure-key") == 0)
        {
            if (i + 1 >= argc) { err = L"--azure-key requires value."; return false; }
            opt.useDirectPlan = true;
            opt.directPlan.enableAzure = true;
            opt.directPlan.azureKey = argv[++i];
        }
        else if (_wcsicmp(a.c_str(), L"--azure-region") == 0)
        {
            if (i + 1 >= argc) { err = L"--azure-region requires value."; return false; }
            opt.useDirectPlan = true;
            opt.directPlan.enableAzure = true;
            opt.directPlan.azureRegion = argv[++i];
        }
        else if (_wcsicmp(a.c_str(), L"--azure-validate") == 0)
        {
            opt.useDirectPlan = true;
            opt.directPlan.azureValidate = true;
        }
        else if (_wcsicmp(a.c_str(), L"--msix") == 0)
        {
            if (i + 1 >= argc) { err = L"--msix requires value."; return false; }
            opt.useDirectPlan = true;
            opt.directPlan.enableEmbeddedMsix = true;
            opt.directPlan.embeddedMsixPath = argv[++i];
            opt.directPlan.enableNarrator = true;
        }
        else if (_wcsicmp(a.c_str(), L"--msix-install") == 0)
        {
            opt.useDirectPlan = true;
            opt.directPlan.enableEmbeddedMsix = true;
            opt.directPlan.embeddedMsixInstall = true;
        }
        else if (_wcsicmp(a.c_str(), L"--msix-extract-only") == 0)
        {
            opt.useDirectPlan = true;
            opt.directPlan.enableEmbeddedMsix = true;
            opt.directPlan.embeddedMsixInstall = false;
        }
        else if (_wcsicmp(a.c_str(), L"--narrator-path") == 0)
        {
            if (i + 1 >= argc) { err = L"--narrator-path requires value."; return false; }
            opt.useDirectPlan = true;
            opt.directPlan.narratorVoicePath = argv[++i];
        }
        else if (_wcsicmp(a.c_str(), L"--sherpa-model") == 0)
        {
            if (i + 1 >= argc) { err = L"--sherpa-model requires value."; return false; }
            opt.useDirectPlan = true;
            opt.directPlan.enableSherpa = true;
            opt.directPlan.sherpaModelsToDownload.push_back(argv[++i]);
        }
        else if (_wcsicmp(a.c_str(), L"--sherpa-rescan") == 0)
        {
            opt.useDirectPlan = true;
            opt.directPlan.enableSherpa = true;
            opt.directPlan.sherpaRescan = true;
        }
        else if (_wcsicmp(a.c_str(), L"--sherpa-promote-hklm") == 0)
        {
            opt.useDirectPlan = true;
            opt.directPlan.enableSherpa = true;
            opt.directPlan.sherpaPromoteHklm = true;
        }
        else if (_wcsicmp(a.c_str(), L"--sherpa-compat-alias") == 0)
        {
            if (i + 1 >= argc) { err = L"--sherpa-compat-alias requires value."; return false; }
            std::wstring v = argv[++i];
            std::transform(v.begin(), v.end(), v.begin(), towlower);
            opt.useDirectPlan = true;
            opt.directPlan.enableSherpa = true;
            opt.directPlan.sherpaCompatEnUs = (v == L"en-us" || v == L"dual");
        }
        else if (_wcsicmp(a.c_str(), L"--sherpa-compat-model") == 0)
        {
            if (i + 1 >= argc) { err = L"--sherpa-compat-model requires value."; return false; }
            opt.useDirectPlan = true;
            opt.directPlan.enableSherpa = true;
            opt.directPlan.sherpaCompatModels.push_back(argv[++i]);
        }
        else if (_wcsicmp(a.c_str(), L"--sherpa-test-voice") == 0)
        {
            if (i + 1 >= argc) { err = L"--sherpa-test-voice requires value."; return false; }
            opt.useDirectPlan = true;
            opt.directPlan.enableSherpa = true;
            opt.directPlan.sherpaTestVoiceId = argv[++i];
        }
        else if (_wcsicmp(a.c_str(), L"--register") == 0)
        {
            opt.useDirectPlan = true;
            opt.directPlan.registerCom = true;
        }
        else if (_wcsicmp(a.c_str(), L"--no-register") == 0)
        {
            opt.useDirectPlan = true;
            opt.directPlan.registerCom = false;
        }
        else if (_wcsicmp(a.c_str(), L"--verify") == 0)
        {
            opt.useDirectPlan = true;
            opt.directPlan.verifyRegistration = true;
        }
        else if (_wcsicmp(a.c_str(), L"--no-self-test") == 0)
        {
            opt.useDirectPlan = true;
            opt.directPlan.runSelfTest = false;
        }
        else if (!a.empty() && a[0] != L'-' && a[0] != L'/')
        {
            // Positional plan file path for drag/drop and simple invocation.
            if (opt.planPath.empty() && EndsWithI(a, L".json"))
            {
                opt.planPath = a;
            }
            else
            {
                err = L"Unknown positional argument: " + a;
                return false;
            }
        }
        else if (!a.empty())
        {
            err = L"Unknown argument: " + a;
            return false;
        }
    }
    if (opt.useDirectPlan)
    {
        opt.directPlan.version = 1;
        if (!opt.directPlan.archX64 && !opt.directPlan.archX86)
        {
            if (Is64BitSystem()) opt.directPlan.archX64 = true;
            else opt.directPlan.archX86 = true;
        }
    }
    return true;
}

void PrintPlanSummary(const InstallPlan& plan)
{
    std::wcout << L"[dry-run] version=" << plan.version << L"\n";
    std::wcout << L"[dry-run] scope=" << (plan.scopeAllUsers ? L"all-users" : L"current-user") << L"\n";
    std::wcout << L"[dry-run] arch=" << (plan.archX64 ? L"x64 " : L"") << (plan.archX86 ? L"x86" : L"") << L"\n";
    std::wcout << L"[dry-run] register_com=" << (plan.registerCom ? L"true" : L"false") << L"\n";
    std::wcout << L"[dry-run] engines: narrator=" << (plan.enableNarrator ? L"on" : L"off")
               << L" edge=" << (plan.enableEdge ? L"on" : L"off")
               << L" azure=" << (plan.enableAzure ? L"on" : L"off")
               << L" sherpa=" << (plan.enableSherpa ? L"on" : L"off")
               << L" embedded_msix=" << (plan.enableEmbeddedMsix ? L"on" : L"off") << L"\n";
    if (plan.enableEmbeddedMsix)
    {
        std::wcout << L"[dry-run] msix path=" << plan.embeddedMsixPath << L"\n";
        std::wcout << L"[dry-run] msix install=" << (plan.embeddedMsixInstall ? L"true" : L"false") << L"\n";
    }
    if (plan.enableSherpa)
    {
        std::wcout << L"[dry-run] sherpa models=" << plan.sherpaModelsToDownload.size() << L"\n";
        std::wcout << L"[dry-run] sherpa rescan=" << (plan.sherpaRescan ? L"true" : L"false") << L"\n";
        std::wcout << L"[dry-run] sherpa promote_hklm=" << (plan.sherpaPromoteHklm ? L"true" : L"false") << L"\n";
    }
}

bool ParseInstallPlanFile(const std::wstring& path, InstallPlan& plan, std::wstring& err)
{
    std::ifstream in(path, std::ios::binary);
    if (!in.is_open())
    {
        err = L"Cannot open plan file: " + path;
        return false;
    }
    std::stringstream buffer;
    buffer << in.rdbuf();

    nlohmann::json j;
    try
    {
        j = nlohmann::json::parse(buffer.str());
    }
    catch (const std::exception& ex)
    {
        err = L"Invalid JSON in plan file: " + Utf8ToWide(ex.what());
        return false;
    }

    if (!j.contains("version") || !j["version"].is_number_integer())
    {
        err = L"Plan file missing integer 'version'.";
        return false;
    }
    plan.version = j["version"].get<int>();
    if (plan.version != 1)
    {
        err = L"Unsupported plan version. Expected 1.";
        return false;
    }

    std::string scope = j.value("scope", "current-user");
    plan.scopeAllUsers = (_stricmp(scope.c_str(), "all-users") == 0);

    if (!j.contains("architectures") || !j["architectures"].is_array() || j["architectures"].empty())
    {
        err = L"Plan must contain non-empty 'architectures' array.";
        return false;
    }
    for (const auto& a : j["architectures"])
    {
        std::string s = a.get<std::string>();
        if (_stricmp(s.c_str(), "x64") == 0) plan.archX64 = true;
        else if (_stricmp(s.c_str(), "x86") == 0) plan.archX86 = true;
    }
    if (!plan.archX86 && !plan.archX64)
    {
        err = L"Plan architectures must include x64 and/or x86.";
        return false;
    }

    if (j.contains("engines") && j["engines"].is_object())
    {
        const auto& engines = j["engines"];

        if (engines.contains("azure_online") && engines["azure_online"].is_object())
        {
            const auto& az = engines["azure_online"];
            plan.enableAzure = az.value("enabled", false);
            plan.azureValidate = az.value("validate", true);
            if (az.contains("key")) plan.azureKey = Utf8ToWide(az["key"].get<std::string>());
            if (az.contains("region")) plan.azureRegion = Utf8ToWide(az["region"].get<std::string>());
        }

        if (engines.contains("sherpa_offline") && engines["sherpa_offline"].is_object())
        {
            const auto& sh = engines["sherpa_offline"];
            plan.enableSherpa = sh.value("enabled", false);
            plan.sherpaRescan = sh.value("rescan", true);
            plan.sherpaPromoteHklm = sh.value("promote_hklm", false);
            if (sh.contains("download") && sh["download"].is_array())
            {
                for (const auto& id : sh["download"])
                    plan.sherpaModelsToDownload.push_back(Utf8ToWide(id.get<std::string>()));
            }
            if (sh.contains("test_voice_id") && sh["test_voice_id"].is_string())
                plan.sherpaTestVoiceId = Utf8ToWide(sh["test_voice_id"].get<std::string>());

            if (sh.contains("compat_alias") && sh["compat_alias"].is_object())
            {
                const auto& ca = sh["compat_alias"];
                std::string mode = ca.value("mode", "none");
                plan.sherpaCompatEnUs = (_stricmp(mode.c_str(), "en-us") == 0 || _stricmp(mode.c_str(), "dual") == 0);
                if (ca.contains("model_ids") && ca["model_ids"].is_array())
                {
                    for (const auto& id : ca["model_ids"])
                        plan.sherpaCompatModels.push_back(Utf8ToWide(id.get<std::string>()));
                }
            }
        }

        if (engines.contains("embedded_msix") && engines["embedded_msix"].is_object())
        {
            const auto& em = engines["embedded_msix"];
            plan.enableEmbeddedMsix = em.value("enabled", false);
            plan.embeddedMsixInstall = em.value("install", true);
            if (em.contains("package_path") && em["package_path"].is_string())
                plan.embeddedMsixPath = Utf8ToWide(em["package_path"].get<std::string>());
        }
    }

    if (j.contains("post_install") && j["post_install"].is_object())
    {
        const auto& pi = j["post_install"];
        plan.registerCom = pi.value("register_com", true);
        plan.verifyRegistration = pi.value("verify_registration", true);
        plan.runSelfTest = pi.value("run_self_test", true);
    }

    if (plan.enableAzure && plan.azureValidate &&
        (plan.azureKey.empty() || plan.azureRegion.empty()))
    {
        err = L"Azure enabled with validate=true requires non-empty key and region.";
        return false;
    }
    return true;
}

bool ValidateInstallPlan(const InstallPlan& plan, std::wstring& err)
{
    if (plan.version != 1)
    {
        err = L"Install plan version must be 1.";
        return false;
    }
    if (!plan.archX64 && !plan.archX86)
    {
        err = L"No target architecture selected (x64/x86).";
        return false;
    }
    if (plan.archX64 && !Is64BitSystem())
    {
        err = L"x64 architecture requested on non-64-bit system.";
        return false;
    }
    if (plan.enableAzure && plan.azureValidate &&
        (plan.azureKey.empty() || plan.azureRegion.empty()))
    {
        err = L"Azure validate requires non-empty key and region.";
        return false;
    }
    if (plan.enableEmbeddedMsix)
    {
        if (plan.embeddedMsixPath.empty())
        {
            err = L"embedded_msix enabled but package path is missing.";
            return false;
        }
        if (!PathFileExistsW(plan.embeddedMsixPath.c_str()))
        {
            err = L"embedded_msix package/path not found: " + plan.embeddedMsixPath;
            return false;
        }
    }
    if (plan.enableSherpa)
    {
        std::wstring sherpaExe;
        if (!FindSherpaConfigToolPath(sherpaExe))
        {
            err = L"SherpaOnnxConfig.exe not found but sherpa_offline was requested.";
            return false;
        }
    }
    return true;
}

void ApplyEnumeratorSettings(const InstallPlan& plan)
{
    RegKey key;
    key.Create(HKEY_CURRENT_USER, L"Software\\NaturalVoiceSAPIAdapter\\Enumerator", KEY_SET_VALUE);
    key.SetDword(L"NoAzureVoices", plan.enableAzure ? 0 : 1);
    key.SetDword(L"NoEdgeVoices", plan.enableEdge ? 0 : 1);
    key.SetDword(L"NoNarratorVoices", plan.enableNarrator ? 0 : 1);
    key.SetDword(L"NoSherpaVoices", plan.enableSherpa ? 0 : 1);

    if (plan.enableAzure)
    {
        key.SetString(L"AzureVoiceKey", plan.azureKey.c_str());
        key.SetString(L"AzureVoiceRegion", plan.azureRegion.c_str());
    }
}

int RunSherpaCommand(const std::wstring& sherpaExe, const std::wstring& args, bool asAdmin)
{
    DWORD exitCode = RunProcess(sherpaExe.c_str(), args.c_str(), asAdmin);
    return static_cast<int>(exitCode);
}

int ExecuteInstallPlan(const InstallPlan& plan, std::wstring& err)
{
    if (!ValidateInstallPlan(plan, err))
        return 2;

    if (plan.registerCom)
    {
        if (plan.archX86)
            Register(false);
        if (plan.archX64 && Is64BitSystem())
            Register(true);
    }

    int msixRc = SetupEmbeddedMsix(plan, err);
    if (msixRc != 0)
        return msixRc;

    ApplyEnumeratorSettings(plan);

    if (plan.enableSherpa)
    {
        std::wstring sherpaExe;
        if (!FindSherpaConfigToolPath(sherpaExe))
        {
            err = L"SherpaOnnxConfig.exe not found for sherpa_offline plan steps.";
            return 3;
        }

        for (const auto& modelId : plan.sherpaModelsToDownload)
        {
            std::wstring cmd = L"download \"" + modelId + L"\"";
            int rc = RunSherpaCommand(sherpaExe, cmd, false);
            if (rc != 0)
            {
                err = L"Sherpa download failed for model: " + modelId;
                return 6;
            }
        }

        if (plan.sherpaRescan)
        {
            int rc = RunSherpaCommand(sherpaExe, L"rescan", false);
            if (rc != 0 && rc != 2)
            {
                err = L"Sherpa rescan failed.";
                return 6;
            }
        }

        if (plan.sherpaPromoteHklm)
        {
            for (const auto& modelId : plan.sherpaModelsToDownload)
            {
                std::wstring cmd = L"promote-hklm \"" + modelId + L"\"";
                if (plan.sherpaCompatEnUs &&
                    std::find(plan.sherpaCompatModels.begin(), plan.sherpaCompatModels.end(), modelId) != plan.sherpaCompatModels.end())
                {
                    cmd += L" --compat-en-us";
                }
                int rc = RunSherpaCommand(sherpaExe, cmd, true);
                if (rc != 0)
                {
                    err = L"Sherpa HKLM promotion failed for model: " + modelId;
                    return 6;
                }
            }
        }

        if (!plan.sherpaTestVoiceId.empty())
        {
            std::wstring cmd = L"sapi-probe --voice \"" + plan.sherpaTestVoiceId + L"\" --timeout 20";
            int rc = RunSherpaCommand(sherpaExe, cmd, false);
            if (rc != 0)
            {
                err = L"Sherpa voice self-test failed for voice: " + plan.sherpaTestVoiceId;
                return 7;
            }
        }
    }

    return 0;
}
}

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
    _In_opt_ HINSTANCE hPrevInstance,
    _In_ LPWSTR    lpCmdLine,
    _In_ int       nCmdShow)
{
    UNREFERENCED_PARAMETER(hPrevInstance);
    UNREFERENCED_PARAMETER(nCmdShow);
    UNREFERENCED_PARAMETER(lpCmdLine);

    CliOptions opt;
    std::wstring parseError;
    if (!ParseCli(opt, parseError))
    {
        if (opt.json)
            PrintJsonResult(false, 1, parseError.empty() ? L"Invalid arguments." : parseError);
        else
            ReportError(ERROR_INVALID_PARAMETER);
        return 1;
    }

    // InstallPlanRunner mode: if executable name is InstallPlanRunner.exe and no explicit
    // CLI mode was selected, auto-run adjacent install-plan.json silently.
    const std::wstring exeName = GetExecutableNameLower();
    const bool isPlanRunner = (exeName == L"installplanrunner.exe");
    if (isPlanRunner && !opt.uninstall && !opt.showHelp &&
        opt.planPath.empty() && !opt.useDirectPlan)
    {
        std::wstring defaultPlan = GetExecutableDir() + L"\\install-plan.json";
        if (PathFileExistsW(defaultPlan.c_str()))
        {
            opt.planPath = defaultPlan;
            opt.silent = true;
        }
    }

    if (opt.showHelp)
    {
        PrintUsage();
        if (opt.json)
            PrintJsonResult(true, 0, L"Help displayed.");
        return 0;
    }

    if (opt.uninstall)
    {
        try
        {
            Unregister(false);
            if (Is64BitSystem())
                Unregister(true);

            if (opt.json)
                PrintJsonResult(true, 0, L"Uninstall completed.");
            else
                ReportError(ERROR_SUCCESS);
        }
        catch (const std::system_error& ex)
        {
            DWORD err = ex.code().value();
            if (opt.json)
                PrintJsonResult(false, static_cast<int>(err), L"Uninstall failed.");
            else
                ReportError(err);
            return err;
        }
    }
    else if (!opt.planPath.empty())
    {
        try
        {
            InstallPlan plan;
            std::wstring err;
            if (!ParseInstallPlanFile(opt.planPath, plan, err))
            {
                if (opt.json)
                    PrintJsonResult(false, 2, err);
                if (!opt.silent)
                    ShowMessageBox(err.c_str(), MB_ICONEXCLAMATION);
                return 2;
            }

            if (opt.dryRun)
            {
                PrintPlanSummary(plan);
                if (opt.json)
                    PrintJsonResult(true, 0, L"Dry-run complete.");
                return 0;
            }

            int rc = ExecuteInstallPlan(plan, err);
            if (rc != 0)
            {
                if (opt.json)
                    PrintJsonResult(false, rc, err);
                if (!opt.silent)
                    ShowMessageBox(err.c_str(), MB_ICONEXCLAMATION);
                return rc;
            }

            if (opt.json)
                PrintJsonResult(true, 0, L"Install plan completed successfully.");
            if (!opt.silent)
                ShowMessageBox(L"Install plan completed successfully.", MB_ICONINFORMATION);
            return 0;
        }
        catch (const std::system_error& ex)
        {
            DWORD err = ex.code().value();
            if (opt.json)
                PrintJsonResult(false, err == 0 ? 1 : static_cast<int>(err), L"System error while executing plan.");
            if (!opt.silent)
                ReportError(err);
            return err == 0 ? 1 : static_cast<int>(err);
        }
        catch (const std::exception&)
        {
            if (opt.json)
                PrintJsonResult(false, 1, L"Unhandled exception while executing plan.");
            if (!opt.silent)
                ReportError(ERROR_GEN_FAILURE);
            return 1;
        }
    }
    else if (opt.useDirectPlan)
    {
        try
        {
            InstallPlan plan = opt.directPlan;
            if (opt.dryRun)
            {
                PrintPlanSummary(plan);
                if (opt.json)
                    PrintJsonResult(true, 0, L"Dry-run complete.");
                return 0;
            }

            std::wstring err;
            int rc = ExecuteInstallPlan(plan, err);
            if (rc != 0)
            {
                if (opt.json)
                    PrintJsonResult(false, rc, err.empty() ? L"Direct CLI execution failed." : err);
                if (!opt.silent)
                    ShowMessageBox((err.empty() ? L"Direct CLI execution failed." : err).c_str(), MB_ICONEXCLAMATION);
                return rc;
            }

            if (opt.json)
                PrintJsonResult(true, 0, L"Direct CLI execution completed successfully.");
            if (!opt.silent)
                ShowMessageBox(L"Direct CLI execution completed successfully.", MB_ICONINFORMATION);
            return 0;
        }
        catch (const std::system_error& ex)
        {
            DWORD err = ex.code().value();
            if (opt.json)
                PrintJsonResult(false, err == 0 ? 1 : static_cast<int>(err), L"System error while executing direct CLI.");
            if (!opt.silent)
                ReportError(err);
            return err == 0 ? 1 : static_cast<int>(err);
        }
    }
    else
    {
        DialogBoxParamW(hInstance, MAKEINTRESOURCEW(IDD_MAIN), nullptr, MainDlg, 0);
    }

    return 0;
}
