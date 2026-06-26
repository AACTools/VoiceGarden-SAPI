#pragma once

#include <windows.h>
#include <Shlwapi.h>

#include <algorithm>
#include <cwctype>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>

#include <nlohmann/json.hpp>

namespace AppDataLayout
{
constexpr wchar_t kLegacyRootName[] = L"VoiceGardenSAPIAdapter";
constexpr wchar_t kLegacyAltRootName[] = L"VoiceGardensSAPIAdapter";

inline std::wstring Trim(const std::wstring& value)
{
    size_t start = 0;
    while (start < value.size() && iswspace(value[start]))
        ++start;
    size_t end = value.size();
    while (end > start && iswspace(value[end - 1]))
        --end;
    return value.substr(start, end - start);
}

inline bool IsSafeFolderName(const std::wstring& value)
{
    if (value.empty())
        return false;
    static constexpr wchar_t invalidChars[] = L"<>:\"/\\|?*";
    return value.find_first_of(invalidChars) == std::wstring::npos;
}

inline std::wstring ReadInstallFolderNameFromBranding(const std::filesystem::path& brandingPath)
{
    try
    {
        if (!std::filesystem::exists(brandingPath))
            return {};

        std::ifstream in(brandingPath, std::ios::binary);
        if (!in.is_open())
            return {};

        nlohmann::json j;
        in >> j;
        if (!j.is_object() || !j.contains("install_folder_name") || !j["install_folder_name"].is_string())
            return {};

        const std::string utf8 = j["install_folder_name"].get<std::string>();
        if (utf8.empty())
            return {};

        int len = MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), static_cast<int>(utf8.size()), nullptr, 0);
        if (len <= 0)
            return {};

        std::wstring out(static_cast<size_t>(len), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), static_cast<int>(utf8.size()), out.data(), len);
        out = Trim(out);
        if (!IsSafeFolderName(out))
            return {};
        return out;
    }
    catch (...)
    {
        return {};
    }
}

inline std::wstring ResolveInstallFolderNameNearModule(HMODULE moduleHandle)
{
    WCHAR modulePath[MAX_PATH] = {};
    DWORD len = GetModuleFileNameW(moduleHandle, modulePath, MAX_PATH);
    if (len == 0 || len >= MAX_PATH)
        return {};

    std::filesystem::path dir(modulePath);
    dir = dir.parent_path();

    std::vector<std::filesystem::path> candidates = {
        dir / L"branding.json",
        dir.parent_path() / L"branding.json",
        dir.parent_path().parent_path() / L"branding.json"
    };

    for (const auto& c : candidates)
    {
        std::wstring name = ReadInstallFolderNameFromBranding(c);
        if (!name.empty())
            return name;
    }

    return {};
}

inline std::vector<std::wstring> CandidateRootNames(const std::wstring& preferred)
{
    std::vector<std::wstring> names;
    auto addUnique = [&names](const std::wstring& value)
    {
        if (value.empty())
            return;
        if (std::none_of(names.begin(), names.end(), [&](const std::wstring& existing)
            {
                return _wcsicmp(existing.c_str(), value.c_str()) == 0;
            }))
        {
            names.push_back(value);
        }
    };

    addUnique(preferred);
    addUnique(kLegacyRootName);
    addUnique(kLegacyAltRootName);
    return names;
}

inline std::wstring ChooseExistingRootName(const std::wstring& basePath, const std::wstring& preferred)
{
    const auto names = CandidateRootNames(preferred);
    for (const auto& name : names)
    {
        std::filesystem::path p = std::filesystem::path(basePath) / name;
        std::error_code ec;
        if (std::filesystem::exists(p, ec) && std::filesystem::is_directory(p, ec))
            return name;
    }
    if (!names.empty())
        return names.front();
    return kLegacyRootName;
}
}
