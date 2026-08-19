// VoiceTokenEnumerator.cpp: CVoiceTokenEnumerator 的实现
#include "pch.h"
#include "VoiceTokenEnumerator.h"
#include <VersionHelpers.h>
#include "SpeechServiceConstants.h"
#include "NetUtils.h"
#include "StringTokenizer.h"
#include "LangUtils.h"
#include <condition_variable>
#include "wrappers.h"
#include "TaskScheduler.h"
#include "RegKey.h"
#include "SapiException.h"
#include "Logger.h"

// SherpaOnnx support
#include "../SherpaOnnx/SherpaOnnxModels.h"
#include "../SherpaOnnx/SherpaOnnxConfig.h"


// CVoiceTokenEnumerator

inline static void CheckHr(HRESULT hr)
{
    if (FAILED(hr))
        throw std::system_error(hr, std::system_category());
}

static std::vector<std::shared_ptr<DataKeyData>> s_cachedTokens;

static std::mutex s_cacheMutex;
static bool s_isCacheTaskScheduled = false;
extern TaskScheduler g_taskScheduler;


enum LanguageFlags
{
    Lang_AllLanguages = 1,
    Lang_AllMultilingual = 2
};


HRESULT CVoiceTokenEnumerator::FinalConstruct() noexcept
{
    // Exception handling in enumerator:
    //   Returning an error code will make the whole SAPI voice enumeration process fail,
    //   instead of just ignoring this faulty enumerator.
    //   As a result, no SAPI clients can enumerate voices.
    //   To prevent this, if an enumeration function fails, it should silently return without throwing.
    //   Only critical situations such as no memory or failing to create an enumerator object at all can be reported,
    //   others should be silently ignored and return S_OK.

    ScopeTracer tracer("Voice enum: Constructor begin", "Voice enum: Constructor end");
    try
    {
        // Some programs assume that creating an enumerator is a low-cost operation,
        // and re-create enumerators frequently during eumeration.
        // Here we try to cache the created tokens for a short period (10 seconds) to improve performance

        std::lock_guard lock(s_cacheMutex);

        CComPtr<ISpObjectTokenEnumBuilder> pEnumBuilder;
        CheckSapiHr(pEnumBuilder.CoCreateInstance(CLSID_SpObjectTokenEnum));
        CheckSapiHr(pEnumBuilder->SetAttribs(nullptr, nullptr));

        if (!s_cachedTokens.empty())
        {
            for (auto& token : s_cachedTokens)
            {
                CComPtr<ISpObjectToken> pToken;
                CheckSapiHr(CVoiceKey::CreateToken(token, &pToken));
                CheckSapiHr(pEnumBuilder->AddTokens(1, &pToken.p));
            }
            CheckSapiHr(pEnumBuilder->QueryInterface(&m_pEnum));
            return S_OK;
        }

        // Failing to open the key will make all query methods return default values
        RegKey key = RegOpenEnumeratorConfigKey();

        DWORD langFlags = 0;

        if (key.GetDword(L"EdgeVoiceAllLanguages"))
            langFlags |= Lang_AllLanguages;
        if (key.GetDword(L"EdgeVoiceAllMultilingual"))
            langFlags |= Lang_AllMultilingual;

        std::vector<std::wstring> languages = key.GetMultiStringList(L"EdgeVoiceLanguages");
        std::wstring narratorVoicePath = key.GetString(L"NarratorVoicePath");
        if (narratorVoicePath.empty())
        {
            WCHAR szDefaultPath[MAX_PATH];
            DWORD len = GetModuleFileNameW((HMODULE)&__ImageBase, szDefaultPath, MAX_PATH);
            if (len != 0 && len != MAX_PATH)
            {
                PathRemoveFileSpecW(szDefaultPath);
                // try DLLPath\NarratorVoices
                if (PathAppendW(szDefaultPath, L"NarratorVoices"))
                {
                    if (PathFileExistsW(szDefaultPath))
                        narratorVoicePath = szDefaultPath;
                    PathRemoveFileSpecW(szDefaultPath);
                }
                if (narratorVoicePath.empty())
                {
                    // try DLLPath\..\NarratorVoices
                    PathRemoveFileSpecW(szDefaultPath);
                    if (PathAppendW(szDefaultPath, L"NarratorVoices"))
                    {
                        if (PathFileExistsW(szDefaultPath))
                            narratorVoicePath = szDefaultPath;
                    }
                }
            }
        }
        ErrorMode errorMode = static_cast<ErrorMode>(std::clamp(key.GetDword(L"DefaultErrorMode", 0UL), 0UL, 2UL));

        if (!key.GetDword(L"Disable"))
        {
            // Narrator natural voices are no longer supported — the original hack
            // (extracting encryption keys from system files) is broken on modern
            // Windows 11 builds. SherpaOnnx provides better offline alternatives.

            // Cloud voices (Azure, Edge, etc.) are only registered via HKLM promotion
            // (VoiceGarden.UI "Install Selected"). We do NOT enumerate them dynamically
            // because in-memory tokens don't work with System.Speech apps like Grid3.
            // Registry-backed tokens are the only source for cloud voices.

            // Enumerate SherpaOnnx offline voices (similar to narrator voices - local models)
            TokenMap sherpaTokens;
            if (!key.GetDword(L"NoSherpaVoices"))
            {
                EnumSherpaVoices(sherpaTokens, langFlags, languages);
                for (auto& token : sherpaTokens)
                    s_cachedTokens.push_back(std::move(token.second));
            }
        }

        // Protect the cache expiry scheduling — mutex already held from line 98
        {
            if (!s_isCacheTaskScheduled)
            {
                s_isCacheTaskScheduled = true;
                g_taskScheduler.StartNewTask(10000, []()
                    {
                        std::lock_guard clearLock(s_cacheMutex);
                        s_cachedTokens.clear();
                        s_isCacheTaskScheduled = false;
                    });
            }
        }

        for (auto& token : s_cachedTokens)
        {
            CComPtr<ISpObjectToken> pToken;
            CheckSapiHr(CVoiceKey::CreateToken(token, &pToken));
            CheckSapiHr(pEnumBuilder->AddTokens(1, &pToken.p));
        }
        CheckSapiHr(pEnumBuilder->QueryInterface(&m_pEnum));

        if (logger.should_log(spdlog::level::info))
        {
            LogInfo("Voice enum: Enumerated {} voice(s)", s_cachedTokens.size());
        }

        return S_OK;
    }
    // All exceptions caught here are critical. They will prevent other voices from being enumerated.
    catch (const std::bad_alloc&)
    {
        LogCritical("Out of memory");
        return E_OUTOFMEMORY;
    }
    catch (const std::system_error& ex)
    {
        LogCritical("Voice enum: Cannot create enumerator: {}", ex);
        return HRESULT_FROM_WIN32(ex.code().value());
    }
    catch (const std::exception& ex)
    {
        LogCritical("Voice enum: Cannot create enumerator: {}", ex);
        return E_FAIL;
    }
    catch (...) // C++ exceptions should not cross COM boundary
    {
        LogCritical("Voice enum: Cannot create enumerator: Unknown error");
        return E_FAIL;
    }
}

static std::wstring LanguageIDsFromLocaleName(const std::wstring& locale)
{
    LANGID lang = LangIDFromLocaleName(locale.c_str());
    if (lang == 0 || lang == LOCALE_CUSTOM_UNSPECIFIED)
    {
        static std::wstring fallbackstr = RegOpenEnumeratorConfigKey().GetString(L"LanguageForUnknownLocales");
        if (fallbackstr.empty())
            LogDebug("Voice enum: locale '{}' cannot be converted to LCID, ignored", locale);
        return fallbackstr;
    }

    std::wstring ret = LangIDToHexLang(lang);

    for (LANGID fallback : GetLangIDFallbacks(lang))
    {
        ret += L';';
        ret += LangIDToHexLang(fallback);
    }

    return ret;
}

// "Microsoft Aria (Natural) - English (United States)" to "Microsoft Aria"
static void TrimVoiceName(std::wstring& longName)
{
    LPCWSTR pStr = longName.c_str();
    LPCWSTR pCh = pStr;
    while (*pCh && !iswpunct(*pCh)) // Go to the first punctuation: '(', '-', etc.
        pCh++;
    if (pCh != pStr) // we advanced at least one character
    {
        pCh--; // Back to the space before punctuation
        while (pCh != pStr && iswspace(*pCh)) // Remove the spaces
            pCh--;
        if (pCh != pStr) // If not trimmed to the starting point
            longName.erase(pCh - pStr + 1); // Trim the string
    }
}

static const nlohmann::json& GetVoiceExtraAttrs()
{
    static nlohmann::json json = nlohmann::json::parse(GetResData(L"VoiceExtraAttrs.json", L"JSON"));
    return json;
}

static std::wstring GetVoiceAge(const std::string& shortName)
{
    auto& json = GetVoiceExtraAttrs();
    auto voice = json.find(shortName);
    if (voice == json.end())
        return {};
    auto age = voice->find("Age");
    if (age == voice->end())
        return {};
    return UTF8ToWString(age->get<std::string>());
}

static std::shared_ptr<DataKeyData> MakeEdgeVoiceToken(
    const nlohmann::json& json,
    ErrorMode errorMode = ErrorMode::ProbeForError
)
{
    std::wstring localeName = UTF8ToWString(json.at("Locale"));
    std::wstring languageIds = LanguageIDsFromLocaleName(localeName);
    if (languageIds.empty())
        return {};

    std::wstring shortName = UTF8ToWString(json.at("ShortName"));

    std::wstring friendlyName = UTF8ToWString(json.at("FriendlyName"));
    std::wstring shortFriendlyName = friendlyName;
    TrimVoiceName(shortFriendlyName);

    std::wstring regName = L"Edge-" + shortName; // registry key name format: Edge-en-US-AriaNeural

    return std::shared_ptr<DataKeyData>(new DataKeyData {
        .path = regName,
        .values = {
            { L"", std::move(friendlyName) },
            { L"CLSID", L"{013AB33B-AD1A-401C-8BEE-F6E2B046A94E}" }
        },
        .subkeys = {
            { L"Attributes", {
                .path = regName + L"\\Attributes",
                .values = {
                    { L"Name", std::move(shortFriendlyName) },
                    { L"Gender", UTF8ToWString(json.at("Gender")) },
                    { L"Age", GetVoiceAge(json.at("ShortName")) },
                    { L"Language", std::move(languageIds) },
                    { L"Locale", std::move(localeName) },
                    { L"Vendor", L"Microsoft" },
                    { L"VoiceGardenType", L"Edge;Cloud" }
                }
            } },
            { L"VoiceGardenConfig", {
                .path = regName + L"\\VoiceGardenConfig",
                .values = {
                    { L"ErrorMode", std::to_wstring(static_cast<UINT>(errorMode)) },
                    { L"WebsocketURL", EDGE_WEBSOCKET_URL },
                    { L"Voice", shortName },
                    { L"IsEdgeVoice", L"1" },
                    { L"EngineType", L"Edge" }
                }
            } }
        }
    });
}

static std::shared_ptr<DataKeyData> MakeAzureVoiceToken(
    const nlohmann::json& json,
    const std::wstring& key,
    const std::wstring& region,
    ErrorMode errorMode = ErrorMode::ProbeForError
)
{
    std::wstring localeName = UTF8ToWString(json.at("Locale"));
    std::wstring languageIds = LanguageIDsFromLocaleName(localeName);
    if (languageIds.empty())
        return {};

    std::wstring shortName = UTF8ToWString(json.at("ShortName"));

    // Make Azure voice names begin with "Azure"
    std::wstring shortFriendlyName = L"Azure " + UTF8ToWString(json.at("DisplayName"));
    std::wstring localeDisplayName = UTF8ToWString(json.at("LocaleName"));
    std::wstring friendlyName = shortFriendlyName + L" - " + localeDisplayName;

    std::wstring regName = L"Azure-" + shortName; // registry key name format: Azure-en-US-AriaNeural

    return std::shared_ptr<DataKeyData>(new DataKeyData {
        .path = regName,
        .values = {
            { L"", std::move(friendlyName) },
            { L"CLSID", L"{013AB33B-AD1A-401C-8BEE-F6E2B046A94E}" }
        },
        .subkeys = {
            { L"Attributes", {
                .path = regName + L"\\Attributes",
                .values = {
                    { L"Name", std::move(shortFriendlyName) },
                    { L"Gender", UTF8ToWString(json.at("Gender")) },
                    { L"Age", GetVoiceAge(json.at("ShortName")) },
                    { L"Language", std::move(languageIds) },
                    { L"Locale", std::move(localeName) },
                    { L"Vendor", L"Microsoft" },
                    { L"VoiceGardenType", L"Azure;Cloud" }
                }
            } },
            { L"VoiceGardenConfig", {
                .path = regName + L"\\VoiceGardenConfig",
                .values = {
                    { L"EngineType", L"Azure" },
                    { L"ErrorMode", std::to_wstring(static_cast<UINT>(errorMode)) },
                    { L"Voice", shortName },
                    { L"Key", key },
                    { L"Region", region }
                }
            } }
        }
    });
}

// Create a SAPI voice token for a SherpaOnnx model
static std::shared_ptr<DataKeyData> MakeSherpaVoiceToken(
    const SherpaOnnx::VoiceInfo& model)
{
    auto deriveKokoroLang = [](std::wstring locale) -> std::wstring
    {
        if (locale.empty())
            return L"en-us";

        size_t delim = locale.find_first_of(L",; ");
        if (delim != std::wstring::npos)
            locale = locale.substr(0, delim);

        std::replace(locale.begin(), locale.end(), L'_', L'-');
        std::transform(locale.begin(), locale.end(), locale.begin(), ::towlower);
        return locale.empty() ? L"en-us" : locale;
    };

    // Convert language from model (e.g., "en-US", "zh-CN")
    std::wstring language = UTF8ToWString(model.language);

    // Create a friendly display name
    std::wstring displayName = UTF8ToWString(model.displayName);
    if (displayName.empty())
    {
        // Fallback to model name if display name is empty
        displayName = UTF8ToWString(model.name);
        // Capitalize first letter
        if (!displayName.empty())
        {
            displayName[0] = towupper(displayName[0]);
        }
    }

    // Add model type prefix to display name
    std::wstring typePrefix;
    switch (model.modelType) {
        case SherpaOnnx::ModelType::Matcha:
            typePrefix = L"Matcha ";
            break;
        case SherpaOnnx::ModelType::Kokoro:
            typePrefix = L"Kokoro ";
            break;
        case SherpaOnnx::ModelType::Piper:
            typePrefix = L"Piper ";
            break;
        case SherpaOnnx::ModelType::MMS:
            typePrefix = L"MMS ";
            break;
        case SherpaOnnx::ModelType::Vits:
        default:
            typePrefix = L"VITS ";
            break;
    }

    std::wstring friendlyName = L"Sherpa " + typePrefix + displayName;

    // Create registry key name: Sherpa-model-name
    std::wstring regName = L"Sherpa-" + UTF8ToWString(model.name);

    // Parse language for SAPI (e.g., "en-US" -> "0409")
    std::wstring languageIds = LanguageIDsFromLocaleName(language);
    if (languageIds.empty())
    {
        // Fallback: try the primary language subtag as a neutral locale and build
        // the language + fallback chain dynamically via LangUtils.
        std::wstring langCode = language;
        size_t dashPos = language.find(L'-');
        if (dashPos != std::wstring::npos)
            langCode = language.substr(0, dashPos);

        LANGID langid = LangIDFromLocaleName(langCode.c_str());
        if (langid != 0 && langid != LOCALE_CUSTOM_UNSPECIFIED)
        {
            languageIds = LangIDToHexLang(langid);
            for (LANGID fallback : GetLangIDFallbacks(langid))
            {
                languageIds += L';';
                languageIds += LangIDToHexLang(fallback);
            }
        }

        if (languageIds.empty())
        {
            LogWarn("Skipping Sherpa model '{}' due to unknown locale '{}'", model.name, model.language);
            return {};
        }
    }

    // Determine gender from voice name if possible. Use Neutral unless there is a strong hint.
    std::wstring gender = L"Neutral";
    std::wstring nameLower = UTF8ToWString(model.name);
    std::transform(nameLower.begin(), nameLower.end(), nameLower.begin(), ::towlower);
    if (nameLower.find(L"female") != std::wstring::npos ||
        nameLower.find(L"woman") != std::wstring::npos ||
        nameLower.find(L"girl") != std::wstring::npos)
    {
        gender = L"Female";
    }
    else if (nameLower.find(L"male") != std::wstring::npos ||
        nameLower.find(L"man") != std::wstring::npos ||
        nameLower.find(L"boy") != std::wstring::npos)
    {
        gender = L"Male";
    }

    // Build config values based on model type
    std::vector<std::pair<std::wstring, std::wstring>> configValues = {
        { L"EngineType", L"Sherpa" },
        { L"SherpaOnnxModelType", std::to_wstring(static_cast<int>(model.modelType)) },
        { L"SampleRate", std::to_wstring(model.sampleRate) },
        { L"SpeakerCount", std::to_wstring(model.speakerCount) },
        { L"IsSherpaVoice", L"1" }
    };

    // Add model-type-specific paths
    switch (model.modelType) {
        case SherpaOnnx::ModelType::Matcha:
            configValues.push_back({ L"SherpaOnnxAcousticModel", UTF8ToWString(model.acousticModelPath) });
            configValues.push_back({ L"SherpaOnnxVocoder", UTF8ToWString(model.vocoderPath) });
            configValues.push_back({ L"SherpaOnnxTokens", UTF8ToWString(model.tokensPath) });
            if (!model.dataDir.empty()) {
                configValues.push_back({ L"SherpaOnnxDataDir", UTF8ToWString(model.dataDir) });
            }
            break;

        case SherpaOnnx::ModelType::Kokoro:
            configValues.push_back({ L"SherpaOnnxModelPath", UTF8ToWString(model.modelPath) });
            configValues.push_back({ L"SherpaOnnxVoices", UTF8ToWString(model.voicesPath) });
            configValues.push_back({ L"SherpaOnnxTokens", UTF8ToWString(model.tokensPath) });
            configValues.push_back({ L"SherpaOnnxLang", deriveKokoroLang(language) });
            if (!model.dataDir.empty()) {
                configValues.push_back({ L"SherpaOnnxDataDir", UTF8ToWString(model.dataDir) });
            }
            break;

        case SherpaOnnx::ModelType::Vits:
        case SherpaOnnx::ModelType::Piper:
        case SherpaOnnx::ModelType::MMS:
        default:
            configValues.push_back({ L"SherpaOnnxModelPath", UTF8ToWString(model.modelPath) });
            configValues.push_back({ L"SherpaOnnxTokens", UTF8ToWString(model.tokensPath) });
            if (!model.dataDir.empty()) {
                configValues.push_back({ L"SherpaOnnxDataDir", UTF8ToWString(model.dataDir) });
            }
            break;
    }

    return std::shared_ptr<DataKeyData>(new DataKeyData {
        .path = regName,
        .values = {
            { L"", std::move(friendlyName) },
            { L"CLSID", L"{013AB33B-AD1A-401C-8BEE-F6E2B046A94E}" }
        },
        .subkeys = {
            { L"Attributes", {
                .path = regName + L"\\Attributes",
                .values = {
                    { L"Name", std::move(displayName) },
                    { L"Gender", std::move(gender) },
                    { L"Age", L"Adult" },
                    { L"Language", std::move(languageIds) },
                    { L"Locale", std::move(language) },
                    { L"Vendor", L"K2FSA" },
                    { L"VoiceGardenType", L"Sherpa;Offline" },
                    { L"SherpaModelName", UTF8ToWString(model.name) }
                }
            } },
            { L"VoiceGardenConfig", {
                .path = regName + L"\\VoiceGardenConfig",
                .values = std::move(configValues)
            } }
        }
    });
}

// Enumerate all language IDs of installed phoneme converters
static std::set<LANGID> GetSupportedLanguageIDs()
{
    std::set<LANGID> langids;
    CComPtr<IEnumSpObjectTokens> pEnum;
    CheckSapiHr(SpEnumTokens(SPCAT_PHONECONVERTERS, nullptr, nullptr, &pEnum));
    for (CComPtr<ISpObjectToken> pToken; pEnum->Next(1, &pToken, nullptr) == S_OK; pToken.Release())
    {
        CComPtr<ISpDataKey> pKey;
        if (FAILED(pToken->OpenKey(SPTOKENKEY_ATTRIBUTES, &pKey)))
            continue;
        CSpDynamicString languages;
        if (FAILED(pKey->GetStringValue(L"Language", &languages)))
            continue;

        for (auto& langstr : TokenizeString(std::wstring_view(languages.m_psz), L';'))
        {
            langids.insert(HexLangToLangID(langstr));
        }
    }
    return langids;
}

static bool IsUniversalPhoneConverterSupported()
{
    CComPtr<ISpPhoneConverter> converter;
    CheckSapiHr(converter.CoCreateInstance(CLSID_SpPhoneConverter));
    CComPtr<ISpPhoneticAlphabetSelection> alphaSelector;
    return SUCCEEDED(converter.QueryInterface(&alphaSelector));
}

static std::set<LANGID> GetUserPreferredLanguageIDs(bool includeFallbacks)
{
    std::set<LANGID> langids;
    ULONG numLangs = 0, cchBuffer = 0;
    
    static const auto pfnGetUserPreferredUILanguages
        = reinterpret_cast<decltype(GetUserPreferredUILanguages)*>
        (GetProcAddress(GetModuleHandleW(L"kernel32"), "GetUserPreferredUILanguages"));

    if (!pfnGetUserPreferredUILanguages)
    {
        LANGID langid = GetUserDefaultLangID();
        langids.insert(langid);
        if (includeFallbacks)
            langids.insert_range(GetLangIDFallbacks(langid));
        langids.insert(MAKELANGID(LANG_ENGLISH, SUBLANG_ENGLISH_US)); // always included
        return langids;
    }

    if (!pfnGetUserPreferredUILanguages(MUI_LANGUAGE_ID, &numLangs, nullptr, &cchBuffer))
        throw std::system_error(GetLastError(), std::system_category());
    auto pBuffer = std::make_unique_for_overwrite<WCHAR[]>(cchBuffer);
    if (!pfnGetUserPreferredUILanguages(MUI_LANGUAGE_ID, &numLangs, pBuffer.get(), &cchBuffer))
        throw std::system_error(GetLastError(), std::system_category());

    for (const auto& langidstr : TokenizeString(std::wstring_view(pBuffer.get(), cchBuffer - 2), L'\0'))
    {
        LANGID langid = HexLangToLangID(langidstr);
        langids.insert(langid);
        if (includeFallbacks)
            langids.insert_range(GetLangIDFallbacks(langid));
    }

    static const auto pfnResolveLocaleName
        = reinterpret_cast<decltype(ResolveLocaleName)*>
        (GetProcAddress(GetModuleHandleW(L"kernel32"), "ResolveLocaleName"));

    if (pfnResolveLocaleName)
    {
        try
        {
            for (const auto& langstr :
                winrt::Windows::System::UserProfile::GlobalizationPreferences::Languages())
            {
                WCHAR resolvedLocale[LOCALE_NAME_MAX_LENGTH] = {};
                if (pfnResolveLocaleName(langstr.c_str(), resolvedLocale, LOCALE_NAME_MAX_LENGTH) == 0)
                    continue;
                LANGID langid = LangIDFromLocaleName(resolvedLocale);
                if (langid == LOCALE_CUSTOM_UNSPECIFIED)
                    continue;
                langids.insert(langid);
                if (includeFallbacks)
                    langids.insert_range(GetLangIDFallbacks(langid));
            }
        }
        catch (const winrt::hresult_error&)
        {
        }
    }

    langids.insert(MAKELANGID(LANG_ENGLISH, SUBLANG_ENGLISH_US)); // always included
    return langids;
}

static bool IsLanguageInList(const std::wstring& language, const std::vector<std::wstring>& languages)
{
    // A voice's language should be able to match a broader list item
    // e.g. "en-US" can match list item "en"
    for (auto& langInList : languages)
    {
        if (langInList.size() > language.size())
            continue;
        if (language.size() == langInList.size() && EqualsIgnoreCase(language, langInList))
            return true;
        wchar_t prefixEndChar = *(language.data() + langInList.size());
        if (prefixEndChar != '-' && prefixEndChar != '\0')
            continue;
        std::wstring_view langPrefix(language.data(), langInList.size());
        if (EqualsIgnoreCase(langPrefix, langInList))
            return true;
    }
    return false;
}

nlohmann::json GetCachedJson(LPCWSTR cacheName, LPCSTR downloadUrl, LPCSTR downloadHeaders);

template <class TokenMaker>
    requires std::is_invocable_r_v<std::shared_ptr<DataKeyData>, TokenMaker, const nlohmann::json&>
void EnumOnlineVoices(std::map<std::string, std::shared_ptr<DataKeyData>>& tokens,
    LPCWSTR cacheName, LPCSTR downloadUrl, LPCSTR downloadHeaders,
    DWORD langFlags, const std::vector<std::wstring>& languages,
    TokenMaker&& tokenMaker)
{
    try
    {
        const auto json = GetCachedJson(cacheName, downloadUrl, downloadHeaders);

        // Universal (IPA) phoneme converter has been supported since SAPI 5.3, which supports most other languages
        // SAPI on older systems (XP) does not have this universal converter, so each language must have its corresponding phoneme converter
        // For systems not supporting the universal converter, we check for each voice if a phoneme converter for its language is present
        // If not, hide the voice from the list
        bool universalSupported = IsUniversalPhoneConverterSupported();
        std::set<LANGID> supportedLangs;
        if (!universalSupported)
            supportedLangs = GetSupportedLanguageIDs();

        std::set<LANGID> userLangs;
        if (!(langFlags & Lang_AllLanguages) && languages.empty())
            userLangs = GetUserPreferredLanguageIDs(false);

        for (const auto& voice : json)
        {
            auto locale = UTF8ToWString(voice.at("Locale"));
            LANGID langid = LangIDFromLocaleName(locale.c_str());
            if (!universalSupported && !supportedLangs.contains(langid))
                continue;
            std::string shortName = voice.at("ShortName");
            // If "AllLanguages" is set, or "AllMultilingual" is set and "Multilingual" is in the name,
            // then no need to check the languages.
            if (!(
                (langFlags & Lang_AllLanguages) || ((langFlags & Lang_AllMultilingual) && shortName.contains("Multilingual"))
                ))
            {
                if (languages.empty())
                {
                    // the language list is empty, use the display languages
                    if (!userLangs.contains(langid))
                        continue;
                }
                else
                {
                    if (!IsLanguageInList(locale, languages))
                        continue;
                }
            }
            auto token = tokenMaker(voice);
            if (token)
                tokens.try_emplace(std::move(shortName), std::move(token));
        }
    }
    catch (const std::bad_alloc&)
    {
        throw;
    }
    catch (const std::system_error& ex)
    {
        LogWarn("Voice enum: Cannot get online voice list: {}", ex);
    }
    catch (const std::exception& ex)
    {
        LogWarn("Voice enum: Cannot get online voice list: {}", ex);
    }
}

void CVoiceTokenEnumerator::EnumEdgeVoices(TokenMap& tokens, DWORD langFlags, const std::vector<std::wstring>& languages,
    ErrorMode errorMode)
{
    EnumOnlineVoices(tokens, L"EdgeVoiceListCache.json", EDGE_VOICE_LIST_URL, "",
        langFlags, languages,
        [errorMode](const nlohmann::json& json)
        {
            return MakeEdgeVoiceToken(json, errorMode);
        }
    );
}

void CVoiceTokenEnumerator::EnumAzureVoices(TokenMap& tokens, DWORD langFlags, const std::vector<std::wstring>& languages,
    const std::wstring& key, const std::wstring& region, ErrorMode errorMode)
{
    EnumOnlineVoices(tokens, L"AzureVoiceListCache.json",
        (std::string("https://") + WStringToUTF8(region) + AZURE_TTS_HOST_AFTER_REGION + AZURE_VOICE_LIST_PATH).c_str(),
        (std::string("Ocp-Apim-Subscription-Key: ") + WStringToUTF8(key) + "\r\n").c_str(),
        langFlags, languages,
        [key, region, errorMode](const nlohmann::json& json)
        {
            return MakeAzureVoiceToken(json, key, region, errorMode);
        });
}

void CVoiceTokenEnumerator::EnumSherpaVoices(TokenMap& tokens, DWORD langFlags, const std::vector<std::wstring>& languages)
{
    try
    {
        // Get default model search paths
        std::vector<std::wstring> searchPaths = SherpaOnnx::Models::GetDefaultModelPaths();

        // Discover SherpaOnnx models
        auto [models, errors] = SherpaOnnx::Models::DiscoverModelsWithErrors(searchPaths);

        if (models.empty())
        {
            logger.debug("No SherpaOnnx models found");
            for (const auto& err : errors)
            {
                logger.warn("Sherpa model scan issue [{}]: {}", err.modelName, err.message);
            }
            return;
        }

        logger.info("Found " + std::to_string(models.size()) + " SherpaOnnx models");
        for (const auto& err : errors)
        {
            logger.warn("Sherpa model scan issue [{}]: {}", err.modelName, err.message);
        }

        // Process each discovered model
        for (const auto& model : models)
        {
            std::wstring language = UTF8ToWString(model.language);

            // Simple language filtering if specified
            if (!(langFlags & Lang_AllLanguages))
            {
                if (!languages.empty() && !IsLanguageInList(language, languages))
                    continue;
            }

            // Skip if this voice is already registered as a persistent HKLM token
            // (by SherpaOnnxConfig's promote-hklm). Duplicates break System.Speech's SelectVoice.
            {
                std::wstring hklmTokenPath = L"SOFTWARE\\Microsoft\\Speech\\Voices\\Tokens\\Sherpa-" + UTF8ToWString(model.name);
                HKEY hKey = nullptr;
                if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, hklmTokenPath.c_str(), 0, KEY_READ, &hKey) == ERROR_SUCCESS)
                {
                    RegCloseKey(hKey);
                    logger.debug("Skipping Sherpa voice '{}' - already registered in HKLM", model.name);
                    continue;
                }
            }

            // Create the voice token
            auto token = MakeSherpaVoiceToken(model);
            if (token)
            {
                // Use model name as the key (unique identifier)
                tokens[model.name] = std::move(token);
                logger.debug("Added Sherpa voice: " + model.name);
            }
            else
            {
                logger.warn("Skipped Sherpa voice due to incomplete metadata: {}", model.name);
            }
        }
    }
    catch (const std::exception& ex)
    {
        logger.error("Error enumerating Sherpa voices: " + std::string(ex.what()));
    }
}
