#include "framework.h"
#include "Installer.h"
#include "SherpaManagerDlg.h"
#include "../include/nlohmann/json.hpp"

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <map>
#include <set>
#include <string>
#include <system_error>
#include <vector>
#include <cwctype>
#include <shlobj.h>

namespace
{
struct SherpaModelItem
{
    std::wstring id;
    std::wstring language;
    std::vector<std::wstring> languages;
    bool hasFileSize = false;
    double fileSizeMb = 0.0;
    bool hasLocalDir = false;
    bool hasIssue = false;
    std::wstring issueText;
};

struct SherpaDialogState
{
    std::wstring sherpaExePath;
    std::wstring catalogPath;
    std::wstring modelsRoot;
    std::vector<SherpaModelItem> models;
    std::vector<int> filtered;
};

constexpr LPCWSTR kInstallerSherpaPrefs = L"Software\\NaturalVoiceSAPIAdapter\\Installer";
constexpr LPCWSTR kSherpaCompatPrefs = L"Software\\NaturalVoiceSAPIAdapter\\SherpaCompat";

std::string WideToUtf8(const std::wstring& value)
{
    if (value.empty())
        return {};
    int len = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (len <= 0)
        return {};
    std::string out(len, '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), out.data(), len, nullptr, nullptr);
    return out;
}

std::wstring Utf8ToWide(const std::string& value)
{
    if (value.empty())
        return {};
    int len = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), nullptr, 0);
    if (len <= 0)
        return {};
    std::wstring out(len, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), out.data(), len);
    return out;
}

void AppendLog(HWND hDlg, const std::wstring& text)
{
    HWND hLog = GetDlgItem(hDlg, IDC_SHERPA_LOG);
    if (!hLog)
        return;

    int len = GetWindowTextLengthW(hLog);
    SendMessageW(hLog, EM_SETSEL, len, len);
    SendMessageW(hLog, EM_REPLACESEL, FALSE, reinterpret_cast<LPARAM>(text.c_str()));
    SendMessageW(hLog, EM_SCROLLCARET, 0, 0);
}

std::wstring Trim(const std::wstring& s)
{
    size_t start = 0;
    while (start < s.size() && iswspace(s[start]))
        ++start;
    size_t end = s.size();
    while (end > start && iswspace(s[end - 1]))
        --end;
    return s.substr(start, end - start);
}

std::wstring ToLower(std::wstring s)
{
    std::transform(s.begin(), s.end(), s.begin(), towlower);
    return s;
}

bool FindSherpaConfigToolPath(std::wstring& outPath)
{
    WCHAR path[MAX_PATH] = {};

    GetModuleFileNameW(nullptr, path, MAX_PATH);
    PathRemoveFileSpecW(path);
    std::vector<std::wstring> candidates;
    candidates.emplace_back(std::wstring(path) + L"\\SherpaOnnxConfig.exe");
    candidates.emplace_back(std::wstring(path) + L"\\x64\\SherpaOnnxConfig.exe");
    candidates.emplace_back(std::wstring(path) + L"\\x86\\SherpaOnnxConfig.exe");
    candidates.emplace_back(std::wstring(path) + L"\\..\\x64\\Release\\SherpaOnnxConfig.exe");
    candidates.emplace_back(std::wstring(path) + L"\\..\\out\\SherpaOnnxConfig.exe");

    for (const auto& c : candidates)
    {
        if (PathFileExistsW(c.c_str()))
        {
            outPath = c;
            return true;
        }
    }
    return false;
}

std::wstring BuildModelsRoot()
{
    WCHAR path[MAX_PATH] = {};
    if (SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA, nullptr, SHGFP_TYPE_CURRENT, path) != S_OK)
        return {};
    if (!PathAppendW(path, L"NaturalVoiceSAPIAdapter"))
        return {};
    std::wstring root = path;
    root += L"\\models";
    return root;
}

std::wstring ResolveCatalogPath(const std::wstring& sherpaExePath)
{
    WCHAR installerDir[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, installerDir, MAX_PATH);
    PathRemoveFileSpecW(installerDir);

    WCHAR sherpaDir[MAX_PATH] = {};
    wcsncpy_s(sherpaDir, sherpaExePath.c_str(), _TRUNCATE);
    PathRemoveFileSpecW(sherpaDir);

    std::vector<std::wstring> candidates;
    candidates.emplace_back(std::wstring(sherpaDir) + L"\\merged_models.json");
    candidates.emplace_back(std::wstring(installerDir) + L"\\merged_models.json");
    candidates.emplace_back(std::wstring(installerDir) + L"\\..\\out\\merged_models.json");
    candidates.emplace_back(std::wstring(installerDir) + L"\\..\\SherpaOnnxConfig\\merged_models.json");

    for (const auto& c : candidates)
    {
        if (PathFileExistsW(c.c_str()))
            return c;
    }
    return {};
}

std::map<std::wstring, std::wstring, std::less<>> LoadScanIssues(const std::wstring& modelsRoot)
{
    std::map<std::wstring, std::wstring, std::less<>> issues;
    if (modelsRoot.empty())
        return issues;

    std::filesystem::path scanPath = std::filesystem::path(modelsRoot).parent_path() / "sherpa_model_scan_errors.json";
    if (!std::filesystem::exists(scanPath))
        return issues;

    try
    {
        std::ifstream in(scanPath);
        nlohmann::json j;
        in >> j;
        if (!j.is_array())
            return issues;

        for (const auto& item : j)
        {
            if (!item.is_object())
                continue;
            std::string modelId = item.value("ModelId", item.value("modelId", ""));
            std::string error = item.value("Error", item.value("error", ""));
            if (!modelId.empty())
                issues[Utf8ToWide(modelId)] = Utf8ToWide(error);
        }
    }
    catch (...)
    {
    }

    return issues;
}

std::set<std::wstring, std::less<>> GetDownloadedModelDirs(const std::wstring& modelsRoot)
{
    std::set<std::wstring, std::less<>> downloaded;
    if (modelsRoot.empty())
        return downloaded;

    std::error_code ec;
    std::filesystem::create_directories(modelsRoot, ec);

    for (const auto& entry : std::filesystem::directory_iterator(modelsRoot, ec))
    {
        if (ec)
            break;
        if (!entry.is_directory())
            continue;
        downloaded.insert(entry.path().filename().wstring());
    }
    return downloaded;
}

std::vector<SherpaModelItem> LoadCatalogItems(const std::wstring& catalogPath, const std::wstring& modelsRoot)
{
    std::vector<SherpaModelItem> items;
    if (catalogPath.empty())
        return items;

    std::ifstream in(catalogPath);
    if (!in)
        return items;

    nlohmann::json j;
    in >> j;
    if (!j.is_object())
        return items;

    auto downloaded = GetDownloadedModelDirs(modelsRoot);
    auto issues = LoadScanIssues(modelsRoot);

    for (auto it = j.begin(); it != j.end(); ++it)
    {
        const auto& v = it.value();
        std::string idUtf8 = v.value("id", it.key());
        if (idUtf8.empty())
            continue;

        SherpaModelItem item;
        item.id = Utf8ToWide(idUtf8);
        item.hasLocalDir = downloaded.find(item.id) != downloaded.end();

        std::set<std::wstring, std::less<>> langs;
        if (v.contains("language") && v["language"].is_array())
        {
            for (const auto& langObj : v["language"])
            {
                if (!langObj.is_object())
                    continue;

                auto readField = [&langObj](const char* k1, const char* k2 = nullptr, const char* k3 = nullptr) -> std::string
                {
                    if (langObj.contains(k1) && langObj[k1].is_string())
                        return langObj[k1].get<std::string>();
                    if (k2 && langObj.contains(k2) && langObj[k2].is_string())
                        return langObj[k2].get<std::string>();
                    if (k3 && langObj.contains(k3) && langObj[k3].is_string())
                        return langObj[k3].get<std::string>();
                    return {};
                };

                // Support both catalog schemas:
                // - lang_code / language_name (legacy)
                // - Iso Code / Language Name (MMS merged catalog)
                std::string name = readField("language_name", "Language Name", "languageName");
                std::string code = readField("lang_code", "Iso Code", "iso_code");
                if (!name.empty())
                    langs.insert(Utf8ToWide(name));
                else if (!code.empty())
                    langs.insert(Utf8ToWide(code));
            }
        }
        if (langs.empty())
            langs.insert(L"Unknown");

        item.languages.assign(langs.begin(), langs.end());
        bool first = true;
        for (const auto& lang : langs)
        {
            if (!first)
                item.language += L", ";
            item.language += lang;
            first = false;
        }

        if (v.contains("filesize_mb") && v["filesize_mb"].is_number())
        {
            item.hasFileSize = true;
            item.fileSizeMb = v["filesize_mb"].get<double>();
        }
        else if (v.contains("filesize_MB") && v["filesize_MB"].is_number())
        {
            item.hasFileSize = true;
            item.fileSizeMb = v["filesize_MB"].get<double>();
        }

        auto issueIt = issues.find(item.id);
        if (issueIt != issues.end())
        {
            item.hasIssue = true;
            item.issueText = issueIt->second;
        }

        items.push_back(std::move(item));
    }

    std::sort(items.begin(), items.end(), [](const SherpaModelItem& a, const SherpaModelItem& b)
    {
        return _wcsicmp(a.id.c_str(), b.id.c_str()) < 0;
    });
    return items;
}

std::wstring StatusPrefix(const SherpaModelItem& m)
{
    if (m.hasIssue)
        return L"[!]";
    if (m.hasLocalDir)
        return L"[OK]";
    return L"[ ]";
}

DWORD RunProcess(LPCWSTR app, const std::wstring& args, bool asAdmin)
{
    SHELLEXECUTEINFOW info = { sizeof info };
    info.fMask = SEE_MASK_NOCLOSEPROCESS;
    info.lpFile = app;
    info.lpParameters = args.c_str();
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

void SaveDialogPrefs(HWND hDlg)
{
    RegKey key;
    key.Create(HKEY_CURRENT_USER, kInstallerSherpaPrefs, KEY_SET_VALUE);
    key.SetDword(L"SherpaApplyAllAdmin", IsDlgButtonChecked(hDlg, IDC_SHERPA_ADMIN_ALL) == BST_CHECKED ? 1 : 0);
    key.SetDword(L"SherpaApplyAllAlias", IsDlgButtonChecked(hDlg, IDC_SHERPA_ALIAS_ALL) == BST_CHECKED ? 1 : 0);
}

void LoadDialogPrefs(HWND hDlg)
{
    RegKey key;
    key.Open(HKEY_CURRENT_USER, kInstallerSherpaPrefs, KEY_QUERY_VALUE);
    CheckDlgButton(hDlg, IDC_SHERPA_ADMIN_ALL, key.GetDword(L"SherpaApplyAllAdmin", 0) ? BST_CHECKED : BST_UNCHECKED);
    CheckDlgButton(hDlg, IDC_SHERPA_ALIAS_ALL, key.GetDword(L"SherpaApplyAllAlias", 0) ? BST_CHECKED : BST_UNCHECKED);
}

void SetAliasPreferenceForModels(const std::vector<std::wstring>& modelIds, bool enabled)
{
    RegKey key;
    key.Create(HKEY_CURRENT_USER, kSherpaCompatPrefs, KEY_SET_VALUE);
    for (const auto& id : modelIds)
    {
        key.SetDword(id.c_str(), enabled ? 1 : 0);
    }
}

void RefreshFilterList(HWND hDlg, SherpaDialogState& st)
{
    WCHAR langBuf[256] = {};
    WCHAR filterBuf[256] = {};
    GetDlgItemTextW(hDlg, IDC_SHERPA_LANGUAGE, langBuf, ARRAYSIZE(langBuf));
    GetDlgItemTextW(hDlg, IDC_SHERPA_FILTER, filterBuf, ARRAYSIZE(filterBuf));
    std::wstring selectedLang = Trim(langBuf);
    std::wstring filter = ToLower(Trim(filterBuf));

    HWND hList = GetDlgItem(hDlg, IDC_SHERPA_MODELS);
    SendMessageW(hList, LB_RESETCONTENT, 0, 0);
    st.filtered.clear();

    int downloadedCount = 0;
    for (size_t i = 0; i < st.models.size(); ++i)
    {
        const auto& m = st.models[i];
        if (m.hasLocalDir)
            ++downloadedCount;

        bool matchLang = (selectedLang.empty() || _wcsicmp(selectedLang.c_str(), L"All Languages") == 0);
        if (!matchLang)
        {
            std::wstring langLower = ToLower(m.language);
            std::wstring selectedLower = ToLower(selectedLang);
            matchLang = langLower.find(selectedLower) != std::wstring::npos;
        }
        if (!matchLang)
            continue;

        if (!filter.empty())
        {
            std::wstring hay = ToLower(m.id + L" " + m.language);
            if (hay.find(filter) == std::wstring::npos)
                continue;
        }

        std::wstring line = StatusPrefix(m) + L" " + m.id + L" (" + m.language + L")";
        if (m.hasFileSize)
        {
            WCHAR sizeBuf[32] = {};
            swprintf_s(sizeBuf, L" - %.1f MB", m.fileSizeMb);
            line += sizeBuf;
        }
        if (m.hasIssue && !m.issueText.empty())
            line += L" - " + m.issueText;
        int idx = static_cast<int>(SendMessageW(hList, LB_ADDSTRING, 0, reinterpret_cast<LPARAM>(line.c_str())));
        SendMessageW(hList, LB_SETITEMDATA, idx, i);
        st.filtered.push_back(static_cast<int>(i));
    }

    std::wstring status = L"Catalog: " + std::to_wstring(st.models.size()) +
        L" models, downloaded: " + std::to_wstring(downloadedCount);
    SetDlgItemTextW(hDlg, IDC_SHERPA_STATUS, status.c_str());
}

void ReloadCatalog(HWND hDlg, SherpaDialogState& st)
{
    st.models = LoadCatalogItems(st.catalogPath, st.modelsRoot);

    std::set<std::wstring, std::less<>> langs;
    for (const auto& m : st.models)
    {
        for (const auto& language : m.languages)
        {
            std::wstring token = Trim(language);
            if (!token.empty())
                langs.insert(token);
        }
    }

    HWND hLang = GetDlgItem(hDlg, IDC_SHERPA_LANGUAGE);
    WCHAR prevLang[256] = {};
    GetWindowTextW(hLang, prevLang, ARRAYSIZE(prevLang));

    SendMessageW(hLang, CB_RESETCONTENT, 0, 0);
    SendMessageW(hLang, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"All Languages"));
    for (const auto& lang : langs)
        SendMessageW(hLang, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(lang.c_str()));

    int sel = static_cast<int>(SendMessageW(hLang, CB_FINDSTRINGEXACT, static_cast<WPARAM>(-1), reinterpret_cast<LPARAM>(prevLang)));
    if (sel == CB_ERR)
        sel = 0;
    SendMessageW(hLang, CB_SETCURSEL, static_cast<WPARAM>(sel), 0);

    RefreshFilterList(hDlg, st);
}

bool TryGetSelectedModel(HWND hDlg, SherpaDialogState& st, SherpaModelItem& outModel)
{
    HWND hList = GetDlgItem(hDlg, IDC_SHERPA_MODELS);
    int sel = static_cast<int>(SendMessageW(hList, LB_GETCURSEL, 0, 0));
    if (sel == LB_ERR)
        return false;
    int idx = static_cast<int>(SendMessageW(hList, LB_GETITEMDATA, sel, 0));
    if (idx < 0 || idx >= static_cast<int>(st.models.size()))
        return false;
    outModel = st.models[idx];
    return true;
}

std::vector<std::wstring> GatherDownloadedModelIds(const SherpaDialogState& st)
{
    std::vector<std::wstring> ids;
    for (const auto& m : st.models)
    {
        if (m.hasLocalDir)
            ids.push_back(m.id);
    }
    return ids;
}

void OnDownloadSelected(HWND hDlg, SherpaDialogState& st)
{
    SherpaModelItem model;
    if (!TryGetSelectedModel(hDlg, st, model))
    {
        MessageBoxW(hDlg, L"Select a model first.", L"Sherpa Models", MB_ICONINFORMATION);
        return;
    }

    std::wstring cmd = L"download \"" + model.id + L"\"";
    AppendLog(hDlg, L"Downloading: " + model.id + L"\r\n");
    try
    {
        DWORD rc = RunProcess(st.sherpaExePath.c_str(), cmd, false);
        if (rc == 0)
        {
            AppendLog(hDlg, L"Download completed.\r\n");
            ReloadCatalog(hDlg, st);
        }
        else
        {
            AppendLog(hDlg, L"Download failed with exit code " + std::to_wstring(rc) + L".\r\n");
        }
    }
    catch (const std::exception& ex)
    {
        AppendLog(hDlg, L"Download launch failed.\r\n");
        ShowMessageBox(ex.what(), MB_ICONEXCLAMATION);
    }
}

void OnRescan(HWND hDlg, SherpaDialogState& st)
{
    AppendLog(hDlg, L"Rescanning models...\r\n");
    try
    {
        DWORD rc = RunProcess(st.sherpaExePath.c_str(), L"rescan", false);
        if (rc == 0 || rc == 2)
        {
            AppendLog(hDlg, L"Rescan completed.\r\n");
            ReloadCatalog(hDlg, st);
        }
        else
        {
            AppendLog(hDlg, L"Rescan failed with exit code " + std::to_wstring(rc) + L".\r\n");
        }
    }
    catch (const std::exception& ex)
    {
        AppendLog(hDlg, L"Rescan launch failed.\r\n");
        ShowMessageBox(ex.what(), MB_ICONEXCLAMATION);
    }
}

void OnApplyGlobal(HWND hDlg, SherpaDialogState& st)
{
    bool useAdmin = IsDlgButtonChecked(hDlg, IDC_SHERPA_ADMIN_ALL) == BST_CHECKED;
    bool useAlias = IsDlgButtonChecked(hDlg, IDC_SHERPA_ALIAS_ALL) == BST_CHECKED;
    auto downloaded = GatherDownloadedModelIds(st);
    if (downloaded.empty())
    {
        MessageBoxW(hDlg, L"No downloaded models found.", L"Sherpa Models", MB_ICONINFORMATION);
        return;
    }

    SaveDialogPrefs(hDlg);
    SetAliasPreferenceForModels(downloaded, useAlias);

    if (useAdmin)
    {
        AppendLog(hDlg, L"Applying HKLM tokens for downloaded models...\r\n");
        for (const auto& id : downloaded)
        {
            std::wstring cmd = L"promote-hklm \"" + id + L"\"";
            if (useAlias)
                cmd += L" --compat-en-us";
            try
            {
                DWORD rc = RunProcess(st.sherpaExePath.c_str(), cmd, true);
                if (rc != 0)
                {
                    AppendLog(hDlg, L"Promotion failed for " + id + L" (exit " + std::to_wstring(rc) + L").\r\n");
                    MessageBoxW(hDlg, L"Promotion failed. Check log details in this dialog.", L"Sherpa Models", MB_ICONEXCLAMATION);
                    return;
                }
            }
            catch (const std::exception& ex)
            {
                ShowMessageBox(ex.what(), MB_ICONEXCLAMATION);
                return;
            }
        }
        AppendLog(hDlg, L"HKLM token promotion completed.\r\n");
    }

    // Always rescan to sync token state and reflect alias changes.
    OnRescan(hDlg, st);
}

BOOL OnInitDialog(HWND hDlg)
{
    auto* st = new SherpaDialogState();
    SetWindowLongPtrW(hDlg, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(st));

    LoadDialogPrefs(hDlg);

    if (!FindSherpaConfigToolPath(st->sherpaExePath))
    {
        MessageBoxW(hDlg, L"SherpaOnnxConfig.exe was not found.", L"Sherpa Models", MB_ICONEXCLAMATION);
        EndDialog(hDlg, IDCANCEL);
        return TRUE;
    }
    st->catalogPath = ResolveCatalogPath(st->sherpaExePath);
    st->modelsRoot = BuildModelsRoot();

    if (st->catalogPath.empty())
    {
        MessageBoxW(hDlg, L"merged_models.json was not found.", L"Sherpa Models", MB_ICONEXCLAMATION);
        EndDialog(hDlg, IDCANCEL);
        return TRUE;
    }

    SendDlgItemMessageW(hDlg, IDC_SHERPA_LANGUAGE, CB_LIMITTEXT, 120, 0);
    ReloadCatalog(hDlg, *st);
    AppendLog(hDlg, L"Sherpa manager loaded.\r\n");
    return TRUE;
}
}

INT_PTR CALLBACK SherpaManagerDlg(HWND hDlg, UINT message, WPARAM wParam, LPARAM lParam)
{
    UNREFERENCED_PARAMETER(lParam);

    auto* st = reinterpret_cast<SherpaDialogState*>(GetWindowLongPtrW(hDlg, GWLP_USERDATA));
    switch (message)
    {
    case WM_INITDIALOG:
        return OnInitDialog(hDlg);

    case WM_COMMAND:
        if (!st)
            return FALSE;

        switch (LOWORD(wParam))
        {
        case IDCANCEL:
        case IDOK:
            SaveDialogPrefs(hDlg);
            EndDialog(hDlg, LOWORD(wParam));
            return TRUE;

        case IDC_SHERPA_DOWNLOAD:
            OnDownloadSelected(hDlg, *st);
            return TRUE;

        case IDC_SHERPA_RESCAN_MANAGER:
            OnRescan(hDlg, *st);
            return TRUE;

        case IDC_SHERPA_OPEN_MODELS:
            if (!st->modelsRoot.empty())
                ShellExecuteW(hDlg, nullptr, st->modelsRoot.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
            return TRUE;

        case IDC_SHERPA_APPLY_ALL:
            OnApplyGlobal(hDlg, *st);
            return TRUE;

        case IDC_SHERPA_REFRESH:
            ReloadCatalog(hDlg, *st);
            return TRUE;

        case IDC_SHERPA_LANGUAGE:
            if (HIWORD(wParam) == CBN_SELCHANGE)
            {
                RefreshFilterList(hDlg, *st);
                return TRUE;
            }
            break;

        case IDC_SHERPA_FILTER:
            if (HIWORD(wParam) == EN_CHANGE)
            {
                RefreshFilterList(hDlg, *st);
                return TRUE;
            }
            break;
        }
        break;

    case WM_DESTROY:
        delete st;
        SetWindowLongPtrW(hDlg, GWLP_USERDATA, 0);
        return TRUE;
    }

    return FALSE;
}
