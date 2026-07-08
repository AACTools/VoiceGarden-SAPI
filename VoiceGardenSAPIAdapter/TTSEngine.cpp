// TTSEngine.cpp: CTTSEngine 的实现

#include "pch.h"
#include "TTSEngine.h"
#include "NetUtils.h"
#include "SpeechServiceConstants.h"
#include <VersionHelpers.h>
#include "RegKey.h"
#include "wrappers.h"
#include <mutex>
#include <filesystem>

// CTTSEngine

// GetTickCount() is deprecated and can overflow every 49 days.
// But GetTickCount64() isn't supported on XP,
// and we are only using the ticks to calculate intervals by subtracting two ticks,
// so overflowing does not matter, and we use this to disable the warning
static inline DWORD _GetTickCount()
{
#pragma warning (disable: 28159)
    return GetTickCount();
#pragma warning (default: 28159)
}

static std::wstring TrimWhitespace(const std::wstring& text)
{
    size_t start = text.find_first_not_of(L" \t\r\n");
    if (start == std::wstring::npos)
        return {};
    size_t end = text.find_last_not_of(L" \t\r\n");
    return text.substr(start, end - start + 1);
}

static std::wstring ExtractSherpaPlainText(const SPVTEXTFRAG* pTextFragList)
{
    std::wstring out;
    for (auto pTextFrag = pTextFragList; pTextFrag; pTextFrag = pTextFrag->pNext)
    {
        if (!pTextFrag->pTextStart || pTextFrag->ulTextLen == 0)
            continue;

        switch (pTextFrag->State.eAction)
        {
        case SPVA_Speak:
        case SPVA_SpellOut:
        case SPVA_Pronounce:
            if (!out.empty() && !iswspace(out.back()))
                out.push_back(L' ');
            out.append(pTextFrag->pTextStart, pTextFrag->ulTextLen);
            break;
        default:
            break;
        }
    }
    return TrimWhitespace(out);
}

// ISpObjectWithToken Implementation

// Initializes this instance of CTTSEngine to use the voice specified in registry
STDMETHODIMP CTTSEngine::SetObjectToken(ISpObjectToken* pToken) noexcept
{
    ScopeTracer tracer("TTS init: begin", "TTS init: end");
    LogInfo("TTS init: SetObjectToken invoked");
    try
    {
        if (SP_IS_BAD_INTERFACE_PTR(pToken))
            return E_POINTER;
        // SpGenericSetObjectToken can re-enter token resolution and stall when using
        // virtual TokenEnum-backed tokens. Keep a direct reference instead.
        m_cpToken = pToken;
        LogInfo("TTS init: SpGenericSetObjectToken completed");

        LogInfo("TTS init: InitVoice starting");
        InitVoice();
        LogInfo("TTS init: InitVoice completed");

        if (m_rustTtsUseSsml == false && m_rustTts)
        {
            // Sherpa/cloud non-Azure path synthesizes plain text directly.
            LogInfo("TTS init: skipping InitPhoneConverter for non-SSML voice");
        }
        else
        {
            LogInfo("TTS init: InitPhoneConverter starting");
            InitPhoneConverter();
            LogInfo("TTS init: InitPhoneConverter completed");
        }

        return S_OK;
    }
    catch (const std::bad_alloc&)
    {
        LogCritical("Out of memory");
        return E_OUTOFMEMORY;
    }
    catch (const std::system_error& ex)
    {
        return OnException(ex, "TTS init: voice '{}' cannot be initialized: {}", pToken);
    }
    catch (const std::invalid_argument& ex)
    {
        return OnException(ex, "TTS init: voice '{}' cannot be initialized: {}", pToken);
    }
    catch (const std::exception& ex)
    {
        return OnException(ex, "TTS init: voice '{}' cannot be initialized: {}", pToken);
    }
    catch (...) // C++ exceptions should not cross COM boundary
    {
        LogErr("TTS init: voice '{}' cannot be initialized: Unknown error", pToken);
        return E_FAIL;
    }
}


// ISpTTSEngine Implementation 

STDMETHODIMP CTTSEngine::Speak(DWORD /*dwSpeakFlags*/,
    REFGUID /*rguidFormatId*/,
    const WAVEFORMATEX* /*pWaveFormatEx*/,
    const SPVTEXTFRAG* pTextFragList,
    ISpTTSEngineSite* pOutputSite) noexcept
{
    ScopeTracer tracer("Speak: begin", "Speak: end");
    LogInfo("Speak: entered");
    try
    {
        LogInfo("Speak: state rustTts={} cancelFuture={}",
            m_rustTts ? 1 : 0,
            m_lastCancellingFuture.valid() ? 1 : 0);
        LogErr("SpeakDiag: stage=after-state-log");

        // Check args (avoid legacy SP_IS_BAD_* probes which can fault in modern processes).
        if (!pOutputSite || !pTextFragList)
        {
            LogWarn("Speak: bad input pointers");
            return E_INVALIDARG;
        }
        LogInfo("Speak: pointer validation passed");

        if (!m_rustTts)
        {
            LogErr("Speak: no RustTts engine initialized");
            return SPERR_UNINITIALIZED;
        }
        LogInfo("Speak: engine presence check passed (RustTts)");

        if (m_lastCancellingFuture.valid())
        {
            LogInfo("Speak: waiting previous cancellation");
            // The previous cancellation is still in progress. Wait for it.
            while (m_lastCancellingFuture.wait_for(std::chrono::milliseconds(0)) == std::future_status::timeout)
            {
                if (pOutputSite->GetActions() & SPVES_ABORT)
                {
                    // The current speech is cancelled.
                    // We can return immediately, since nothing has been done yet.
                    return S_OK;
                }
                Sleep(0);  // Reduce cancellation latency
            }
            // Cancellation completed. Clear the future.
            m_lastCancellingFuture = {};
            LogInfo("Speak: previous cancellation completed");
        }

        // Clear m_pOutputSite automatically when Speak is completed
        ScopeGuard siteDeleter([this]()
            {
                std::lock_guard lock(m_outputSiteMutex);
                m_pOutputSite = nullptr;
            });
        LogInfo("Speak: scope guard created");
        LogErr("SpeakDiag: stage=after-scopeguard");
        m_pOutputSite = pOutputSite;
        LogInfo("Speak: output site assigned");

        LogInfo("Speak: pre-branch RustTts");
        LogErr("SpeakDiag: stage=before-rusttts-branch");

        if (!m_rustTts)
        {
            LogErr("Speak: no RustTts engine initialized");
            return E_FAIL;
        }

        LogInfo("Speak: RustTts path selected (ssml={})", m_rustTtsUseSsml ? 1 : 0);

        m_compensatedSilenceWritten = false;
        m_compensatedSilentBytes = 0;
        m_lastSilentBytes = 0;
        m_thisSpeakStartedTicks = _GetTickCount();
        m_sherpaAbortRequested.store(false, std::memory_order_relaxed);

        // Clear boundary queue for this utterance
        m_pendingBoundaries.clear();
        m_boundaryIndex = 0;
        m_totalAudioBytesWritten = 0;

        std::string speakText;
        if (m_rustTtsUseSsml)
        {
            if (!BuildSSML(pTextFragList))
            {
                LogDebug("Speak: RustTts SSML built with no speech content");
                FinishSimulatingBookmarkEvents(m_compensatedSilentBytes);
                return S_OK;
            }
            speakText = WStringToUTF8(m_ssml);
        }
        else
        {
            std::wstring plainTextW = ExtractSherpaPlainText(pTextFragList);
            if (plainTextW.empty())
            {
                FinishSimulatingBookmarkEvents(m_compensatedSilentBytes);
                return S_OK;
            }
            speakText = WStringToUTF8(plainTextW);
        }

        // Synchronous synthesis — prevents race condition where boundary events
        // from a previous Speak() are processed against new text by System.Speech.
        // Abort checking via SPVES_ABORT is not possible during synthesis.
        LogInfo("Speak: RustTts generation begin");
        try {
            if (m_rustTtsUseSsml)
                m_rustTts->SpeakSsml(speakText);
            else
                m_rustTts->Speak(speakText);
        } catch (const std::exception& ex) {
            LogErr("RustTts synthesis failed: {}", ex.what());
        }

        LogInfo("Speak: RustTts generation end");
        m_lastSpeakCompletedTicks = _GetTickCount();

        return S_OK;
    }
    catch (const std::bad_alloc&)
    {
        LogCritical("Out of memory");
        return E_OUTOFMEMORY;
    }
    catch (const std::system_error& ex)
    {
        return OnException(ex, "Speak: {}");
    }
    catch (const std::exception& ex)
    {
        return OnException(ex, "Speak: {}");
    }
    catch (...) // C++ exceptions should not cross COM boundary
    {
        LogErr("Speak: Unknown error");
        return E_FAIL;
    }
} /* CTTSEngine::Speak */

STDMETHODIMP CTTSEngine::GetOutputFormat(const GUID* /*pTargetFormatId*/, const WAVEFORMATEX* /*pTargetWaveFormatEx*/,
    GUID* pDesiredFormatId, WAVEFORMATEX** ppCoMemDesiredWaveFormatEx) noexcept
{
    // For offline voices, prefer model sample rate from registry metadata.
    {
        DWORD sampleRate = 0;

        // Token metadata from model catalog.
        if (m_cpToken)
        {
            CComPtr<ISpDataKey> pConfigKey;
            if (SUCCEEDED(m_cpToken->OpenKey(L"VoiceGardenConfig", &pConfigKey)) && pConfigKey)
                (void)pConfigKey->GetDWORD(L"SampleRate", &sampleRate);
        }

        if (sampleRate > 0)
        {
            auto pickFormat = [](DWORD sr) -> SPSTREAMFORMAT {
                switch (sr)
                {
                case 8000: return SPSF_8kHz16BitMono;
                case 11025: return SPSF_11kHz16BitMono;
                case 12000: return SPSF_12kHz16BitMono;
                case 16000: return SPSF_16kHz16BitMono;
                case 22050: return SPSF_22kHz16BitMono;
                case 24000: return SPSF_24kHz16BitMono;
                case 32000: return SPSF_32kHz16BitMono;
                case 44100: return SPSF_44kHz16BitMono;
                case 48000: return SPSF_48kHz16BitMono;
                default:
                    if (sr <= 9512) return SPSF_8kHz16BitMono;
                    if (sr <= 11512) return SPSF_11kHz16BitMono;
                    if (sr <= 14000) return SPSF_12kHz16BitMono;
                    if (sr <= 19025) return SPSF_16kHz16BitMono;
                    if (sr <= 23025) return SPSF_22kHz16BitMono;
                    if (sr <= 28000) return SPSF_24kHz16BitMono;
                    if (sr <= 38050) return SPSF_32kHz16BitMono;
                    if (sr <= 46050) return SPSF_44kHz16BitMono;
                    return SPSF_48kHz16BitMono;
                }
            };

            const SPSTREAMFORMAT fmt = pickFormat(sampleRate);
            return SpConvertStreamFormatEnum(fmt, pDesiredFormatId, ppCoMemDesiredWaveFormatEx);
        }
    }

    // Default
    return SpConvertStreamFormatEnum(SPSF_24kHz16BitMono, pDesiredFormatId, ppCoMemDesiredWaveFormatEx);
}


// Other Member Functions

void CTTSEngine::InitPhoneConverter()
{
    LANGID lang = 0;
    HRESULT hr = SpGetLanguageFromToken(m_cpToken, &lang);
    if (FAILED(hr))
        throw std::system_error(hr, sapi_category(), "Attribute 'Language' is missing");

    CComPtr<ISpDataKey> pAttrKey;
    CSpDynamicString locale;
    if (SUCCEEDED(m_cpToken->OpenKey(SPTOKENKEY_ATTRIBUTES, &pAttrKey))
        && SUCCEEDED(pAttrKey->GetStringValue(L"Locale", &locale)))
    {
        m_localeName = locale;
    }
    else
    {
        m_localeName = L"en-US";
    }

    CheckSapiHr(SpCreatePhoneConverter(lang, nullptr, nullptr, &m_phoneConverter));
}

void CTTSEngine::InitVoice()
{
    CComPtr<ISpDataKey> pConfigKey;

    LogInfo("TTS init: opening VoiceGardenConfig key");
    HRESULT hr = m_cpToken->OpenKey(L"VoiceGardenConfig", &pConfigKey); // this key must exist
    LogInfo("TTS init: OpenKey VoiceGardenConfig returned hr={:#x}", static_cast<unsigned int>(hr));
    if (FAILED(hr))
        throw std::system_error(hr, sapi_category(), "Subkey 'VoiceGardenConfig' is missing");

    DWORD dwErrorMode;
    hr = pConfigKey->GetDWORD(L"ErrorMode", &dwErrorMode);
    if (FAILED(hr)) dwErrorMode = 0;
    m_errorMode = (ErrorMode)std::clamp(dwErrorMode, 0UL, 2UL);

    // All voices route through rust-tts-wrapper (tts_wrapper.dll).
    if (InitRustTtsVoice(pConfigKey))
        return;

    throw std::invalid_argument("Invalid VoiceGardenConfig configuration.");
}

// Returns true if hr indicates that the value is not found. Throws on other error.
inline static bool CheckHrNotFound(HRESULT hr)
{
    if (hr == SPERR_NOT_FOUND)
        return true;
    CheckSapiHr(hr);
    return false;
}

LSTATUS TryLoadAzureSpeechSDK();

bool CTTSEngine::InitRustTtsVoice(ISpDataKey* pConfigKey)
{
    // Try to use rust-tts-wrapper for cloud engines.
    // Falls through (returns false) if tts_wrapper.dll isn't loaded,
    // so the existing GenericHttpTts / SpeechRestAPI paths are used instead.

    auto& loader = RustTts::Loader::Instance();
    if (!loader.Initialize() || !loader.IsLoaded())
    {
        LogErr("TTS init: tts_wrapper.dll not loaded — cannot initialize voice");
        return false;
    }

    // Determine engine type from registry config.
    // Cloud voices have EngineType (Azure, Edge, OpenAI, Google, etc.)
    // SherpaOnnx voices have SherpaOnnxModelPath (no EngineType).
    CSpDynamicString pszEngineType;
    CSpDynamicString pszSherpaModelPath;
    bool hasEngineType = !CheckHrNotFound(pConfigKey->GetStringValue(L"EngineType", &pszEngineType));
    bool hasSherpaPath = !CheckHrNotFound(pConfigKey->GetStringValue(L"SherpaOnnxModelPath", &pszSherpaModelPath));

    std::string engineType;
    std::string lowerType;

    if (hasEngineType)
    {
        engineType = pszEngineType.m_psz ? WStringToUTF8(std::wstring(pszEngineType.m_psz)) : "";
        lowerType = engineType;
        std::transform(lowerType.begin(), lowerType.end(), lowerType.begin(), ::tolower);
        // Sherpa EngineType maps to "sherpaonnx" in rust-tts-wrapper
        if (lowerType == "sherpa")
            lowerType = "sherpaonnx";
    }
    else if (hasSherpaPath)
    {
        // SherpaOnnx voice detected by SherpaOnnxModelPath (no EngineType field)
        engineType = "Sherpa";
        lowerType = "sherpaonnx"; // Rust engine ID is "sherpaonnx"
    }
    else
    {
        return false;
    }

    // All engine types are handled by rust-tts-wrapper.
    // Read credentials for the engine.

    // Read credentials from the token config
    CSpDynamicString pszVoice, pszKey, pszRegion;
    CheckHrNotFound(pConfigKey->GetStringValue(L"Voice", &pszVoice));
    CheckHrNotFound(pConfigKey->GetStringValue(L"Key", &pszKey));
    CheckHrNotFound(pConfigKey->GetStringValue(L"Region", &pszRegion));

    // Build credentials JSON for rust-tts-wrapper
    std::string credsJson;
    if (lowerType == "google" || lowerType == "openai" || lowerType == "elevenlabs" ||
        lowerType == "cartesia" || lowerType == "deepgram" || lowerType == "fishaudio" ||
        lowerType == "hume" || lowerType == "mistral" || lowerType == "murf" ||
        lowerType == "resemble" || lowerType == "unrealspeech" || lowerType == "upliftai" ||
        lowerType == "xai" || lowerType == "modelslab")
    {
        std::string key = pszKey.m_psz ? WStringToUTF8(std::wstring(pszKey.m_psz)) : "";
        credsJson = "{\"apiKey\":\"" + key + "\"}";
    }
    else if (lowerType == "azure")
    {
        std::string key = pszKey.m_psz ? WStringToUTF8(std::wstring(pszKey.m_psz)) : "";
        std::string region = pszRegion.m_psz ? WStringToUTF8(std::wstring(pszRegion.m_psz)) : "";
        credsJson = "{\"subscriptionKey\":\"" + key + "\",\"region\":\"" + region + "\"}";
    }
    else if (lowerType == "watson")
    {
        std::string key = pszKey.m_psz ? WStringToUTF8(std::wstring(pszKey.m_psz)) : "";
        std::string region = pszRegion.m_psz ? WStringToUTF8(std::wstring(pszRegion.m_psz)) : "";
        credsJson = "{\"apiKey\":\"" + key + "\",\"region\":\"" + region + "\"}";
    }
    else if (lowerType == "playht")
    {
        std::string key = pszKey.m_psz ? WStringToUTF8(std::wstring(pszKey.m_psz)) : "";
        std::string userId = pszRegion.m_psz ? WStringToUTF8(std::wstring(pszRegion.m_psz)) : "";
        credsJson = "{\"apiKey\":\"" + key + "\",\"userId\":\"" + userId + "\"}";
    }
    else if (lowerType == "witai")
    {
        std::string key = pszKey.m_psz ? WStringToUTF8(std::wstring(pszKey.m_psz)) : "";
        credsJson = "{\"token\":\"" + key + "\"}";
    }
    else if (lowerType == "edge")
    {
        // Edge is credential-free
        credsJson = "{}";
    }
    else if (lowerType == "sherpaonnx")
    {
        // SherpaOnnx via Rust. Derive modelId and modelPath from the registry.
        // The Rust wrapper expects: modelPath=<base dir containing modelId dirs>,
        // modelId=<directory name matching the registry entry>.
        //
        // Path structures:
        //   MMS:    models/mms_eng/model.onnx          (flat — modelId = mms_eng)
        //   Kokoro: models/kokoro-en-en-19/sub/model.onnx  (nested — modelId = kokoro-en-en-19)
        //   Piper:  models/piper-en-amy-low/sub/file.onnx  (nested — modelId = piper-en-amy-low)
        //
        // Solution: walk up from the .onnx file to find the "models" directory,
        // then the first directory after it is the modelId.
        if (hasSherpaPath && pszSherpaModelPath.m_psz)
        {
            std::filesystem::path onnxPath(pszSherpaModelPath.m_psz);
            auto p = onnxPath.parent_path();
            while (p.has_parent_path() && p.filename() != L"models")
                p = p.parent_path();

            if (p.filename() == L"models")
            {
                auto rel = std::filesystem::relative(onnxPath.parent_path(), p);
                std::string modelId = rel.begin()->string();
                std::string basePath = p.string();
                std::replace(basePath.begin(), basePath.end(), '\\', '/');
                credsJson = "{\"modelId\":\"" + modelId + "\",\"modelPath\":\"" + basePath + "\"}";
                LogInfo("RustTts: SherpaOnnx credentials: {}", credsJson);
            }
            else
            {
                LogWarn("RustTts: Could not find 'models' directory in path: {}", onnxPath.string());
                return false;
            }
        }
        else
        {
            LogWarn("RustTts: SherpaOnnx voice has no SherpaOnnxModelPath");
            return false;
        }
    }
    else
    {
        // Generic: pass apiKey
        std::string key = pszKey.m_psz ? WStringToUTF8(std::wstring(pszKey.m_psz)) : "";
        credsJson = "{\"apiKey\":\"" + key + "\"}";
    }

    // Create the RustTts engine
    m_rustTts = std::make_unique<RustTts::Engine>();
    if (!m_rustTts->Create(lowerType, credsJson))
    {
        LogWarn("RustTts: failed to create engine '{}', falling back", lowerType);
        m_rustTts.reset();
        return false;
    }

    // Set voice if specified
    if (pszVoice.m_psz && *pszVoice.m_psz)
    {
        m_rustTts->SetVoice(WStringToUTF8(std::wstring(pszVoice.m_psz)));
    }

    // Register callbacks that route audio and events back to CTTSEngine
    m_rustTts->SetOnAudio([this](const uint8_t* data, uint32_t len) {
        if (m_pOutputSite && len > 0)
        {
            // Write audio to the SAPI output site
            OnAudioData(const_cast<uint8_t*>(data), len);
        }
    });

    m_rustTts->SetOnBoundary([](const char*, int32_t, int32_t, float, float) {
        // Boundary events disabled — System.Speech crashes when character
        // offsets from Rust (plain text) don't match System.Speech's internal
        // text tracking (SSML/prompt text). Re-enable once text mapping is fixed.
    });

    m_rustTts->SetOnViseme([this](int32_t visemeId, float offsetS) {
        uint64_t offsetTicks = static_cast<uint64_t>(offsetS * 1e7);
        OnViseme(offsetTicks, static_cast<uint32_t>(visemeId));
    });

    m_rustTts->SetOnError([](const char* msg) {
        LogErr("RustTts engine error: {}", msg ? msg : "(null)");
    });

    m_onlineVoiceName = pszVoice.m_psz ? pszVoice.m_psz : L"";
    m_rustTtsUseSsml = false; // All engines use plain text — Rust builds SSML internally
    LogInfo("RustTts voice created: {} / {}", engineType, pszVoice.m_psz ? pszVoice.m_psz : L"(default)");
    return true;
}

// Returns the trailing silence (zero) wave data length, in bytes
template <typename SampleType>
static size_t GetTrailingSilenceLengthMono(BYTE* waveData, size_t length)
{
    constexpr size_t bytesPerSample = sizeof(SampleType);
    if (length < bytesPerSample)
        return 0;

    // Check each sample in reverse order
    BYTE* p = waveData + (length - (length % bytesPerSample));
    SampleType smp;
    do
    {
        p -= bytesPerSample;
        memcpy(&smp, p, bytesPerSample);
        // this sample is non-zero, so the trailing silence starts at the next sample
        if (smp != SampleType())
            return length - (p - waveData) - bytesPerSample;
    } while (p != waveData);

    // The whole data block is silence
    return length;
}

int CTTSEngine::OnAudioData(uint8_t* data, uint32_t len)
{
    std::lock_guard lock(m_outputSiteMutex);
    if (!m_pOutputSite)
    {
        LogWarn("Speak: Audio write with invalid OutputSite, ignored");
        return len; // ignore the data
    }

    ULONG written = 0;

    if (m_onlineDelayOptimization)
    {
        if (!m_compensatedSilenceWritten)
        {
            DWORD currentTicks = _GetTickCount();
            DWORD passedMs = currentTicks - m_thisSpeakStartedTicks;  // delay of this connection
            LogDebug("Speak: Connection delay: {}ms", passedMs);
            // Speak() usually returns before the audio finishes.
            // Therefore, if the previous Speak() ends no more than 5 seconds ago,
            // we will compensate for the full silence duration
            if (m_lastSpeakCompletedTicks != 0 && currentTicks - m_lastSpeakCompletedTicks < 5000)
            {
                // Compensate for the previous removed trailing silence
                DWORD silenceMs = m_lastSilentBytes / nWaveBytesPerMSec;  // last slience duration
                m_compensatedSilentBytes = silenceMs > passedMs ? (silenceMs - passedMs) * nWaveBytesPerMSec : 0;

                if (m_compensatedSilentBytes != 0)
                {
                    LogDebug("Speak: Compensate for the previous trailing {}ms silence", silenceMs - passedMs);
                    // Write the compensated silence
                    auto mem = std::make_unique<BYTE[]>(m_compensatedSilentBytes);  // zeroed mem
                    m_pOutputSite->Write(mem.get(), m_compensatedSilentBytes, &written);
                }
            }
            m_lastSilentBytes = 0;
            m_compensatedSilenceWritten = true;
        }

        // assume 16bit mono
        ULONG silentBytes = (ULONG)GetTrailingSilenceLengthMono<USHORT>(data, len);
        if (silentBytes == len)
        {
            // This chunk is completely silent
            // Hold the silence data for no more than a second
            if (m_lastSilentBytes < nWaveBytesPerMSec * 1000)
            {
                // Hold and accumulate the silence length
                m_lastSilentBytes += silentBytes;
                return len;
            }
        }
        else
        {
            // This chunk is not completely silent, so send the previous silent data
            if (m_lastSilentBytes != 0)
            {
                auto mem = std::make_unique<BYTE[]>(m_lastSilentBytes);  // zeroed mem
                m_pOutputSite->Write(mem.get(), m_lastSilentBytes, &written);
            }
            m_lastSilentBytes = silentBytes;
        }
    }

    HRESULT hr = m_pOutputSite->Write(data, len - m_lastSilentBytes, &written);

    // Assumes that the data can be either entirely written or not written at all
    // because some implementations do not set the written bytes correctly
    if (SUCCEEDED(hr))
        return len;
    else
    {
        if (logger.should_log(spdlog::level::debug))
            LogDebug("Speak: Could not write {} bytes of audio data, {}", len, std::system_error(hr, sapi_category()));
        return 0;
    }
}
void CTTSEngine::OnBookmark(uint64_t offsetTicks, const std::wstring& bookmark)
{
    std::lock_guard lock(m_outputSiteMutex);
    if (!m_pOutputSite) return;
    SPEVENT ev = { 0 };
    ev.ullAudioStreamOffset = WaveTicksToBytes(offsetTicks);
    ev.eEventId = SPEI_TTS_BOOKMARK;
    ev.elParamType = SPET_LPARAM_IS_STRING;
    ev.lParam = reinterpret_cast<LPARAM>(bookmark.c_str());
    ev.wParam = _wtol(bookmark.c_str());
    m_pOutputSite->AddEvents(&ev, 1);
}
void CTTSEngine::OnBoundary(uint64_t audioOffsetTicks, uint32_t textOffset, uint32_t textLength, SPEVENTENUM boundaryType)
{
    std::lock_guard lock(m_outputSiteMutex);
    if (!m_pOutputSite) return;
    SPEVENT ev = { 0 };
    ev.ullAudioStreamOffset = WaveTicksToBytes(audioOffsetTicks);
    ev.eEventId = boundaryType;
    ev.elParamType = SPET_LPARAM_IS_UNDEFINED;
    ULONG offset = textOffset, length = textLength;
    MapTextOffset(offset, length);
    ev.lParam = offset;
    ev.wParam = length;
    m_pOutputSite->AddEvents(&ev, 1);
}
void CTTSEngine::OnViseme(uint64_t offsetTicks, uint32_t visemeId)
{
    std::lock_guard lock(m_outputSiteMutex);
    if (!m_pOutputSite) return;
    SPEVENT ev = { 0 };
    ev.ullAudioStreamOffset = WaveTicksToBytes(offsetTicks);
    ev.eEventId = SPEI_VISEME;
    ev.elParamType = SPET_LPARAM_IS_UNDEFINED;
    ev.wParam = 0;
    // Cognitive Speech uses the same viseme ID values as SAPI 
    ev.lParam = MAKELONG(visemeId, 0);
    m_pOutputSite->AddEvents(&ev, 1);
}

void CTTSEngine::AppendTextFragToSsml(const SPVTEXTFRAG* pTextFrag)
{
    // entities are converted to characters before passing in, so we have to convert it back to XML
    // these entities are processed: &lt;&gt;&amp;&quot;&apos;

    LPCWSTR pEnd = pTextFrag->pTextStart + pTextFrag->ulTextLen;
    m_ssml.reserve(m_ssml.size() + pTextFrag->ulTextLen);

    for (LPCWSTR pCh = pTextFrag->pTextStart; pCh != pEnd && *pCh; pCh++)
    {
        switch (*pCh)
        {
        case '<': m_ssml.append(L"&lt;"); break;
        case '>': m_ssml.append(L"&gt;"); break;
        case '&': m_ssml.append(L"&amp;"); break;
        case '"': m_ssml.append(L"&quot;"); break;
        case '\'': m_ssml.append(L"&apos;"); break;
        default: m_ssml.push_back(*pCh); continue;
        }

        // match the next character in SAPI text with the character after the inserted entity in SSML text
        m_offsetMappings.emplace_back(pTextFrag->ulTextSrcOffset + (ULONG)(pCh - pTextFrag->pTextStart) + 1, (ULONG)m_ssml.size());
    }
}

void CTTSEngine::AppendPhonemesToSsml(const SPPHONEID* pPhoneIds)
{
    WCHAR phoneme[SP_MAX_PRON_LENGTH * 8];
    HRESULT hr = m_phoneConverter->IdToPhone(pPhoneIds, phoneme);
    if (FAILED(hr))
        return;

    for (LPCWSTR pCh = phoneme; *pCh; pCh++)
    {
        switch (*pCh)
        {
        case '<': m_ssml.append(L"&lt;"); break;
        case '>': m_ssml.append(L"&gt;"); break;
        case '&': m_ssml.append(L"&amp;"); break;
        case '"': m_ssml.append(L"&quot;"); break;
        case '\'': m_ssml.append(L"&apos;"); break;
        default: m_ssml.push_back(*pCh); break;
        }
    }
}

void CTTSEngine::AppendSAPIContextToSsml(const SPVCONTEXT& context)
{
    // map <context id='xxx'>...</context>
    // to <say-as interpret-as='xxx' format='xxx'>...</say-as>

    m_ssml.append(L"<say-as interpret-as='");

    std::wstring_view cat = context.pCategory;

    // standard: 'date_xxx'
    // when parsed from SSML, 'date:xxx' is used when format isn't standard
    if (EqualsIgnoreCase(cat.substr(0, 4), L"date")
        && (cat[4] == '_' || cat[4] == ':')) 
    {
        // <context id='date_dmy'> to <say-as interpret-as='date' format='dmy'>
        m_ssml.append(L"date' format='");
        auto fmt = cat.substr(5);
        if (EqualsIgnoreCase(fmt, L"year"))
            m_ssml.push_back('y');
        else
            m_ssml.append(fmt);
    }
    else if (EqualsIgnoreCase(cat, L"number_cardinal"))
        m_ssml.append(L"cardinal");
    else if (EqualsIgnoreCase(cat, L"number_fraction"))
        m_ssml.append(L"fraction");
    else if (EqualsIgnoreCase(cat, L"phone_number"))
        m_ssml.append(L"telephone");
    else
        m_ssml.append(cat); // other category IDs are passed as-is

    m_ssml.append(L"'>");
}

// Returns whether we need a space between the existing SSML and the text to be appended.
static bool NeedAddingSpace(std::wstring_view ssmlBefore, std::wstring_view strAfter)
{
    // Different text fragments belongs to different words.
    // Sometimes XML parsing removes leading & trailing spaces,
    // and we have to add it back between fragments,
    // so words in adjacent fragments don't merge together.
    // XML tags themselves can also separate words,
    // so if there's already an XML tag, spaces are not needed.

    if (strAfter.empty())  // Nothing is being appended
        return false;

    wchar_t chBefore = ssmlBefore.back();
    if (chBefore == L'>')  // An XML tag just ended (common case), no space needed
        return false;
    if (iswspace(chBefore))  // already a space
        return false;
    if (chBefore < 128)  // assume other ASCII characters are English and a space is needed
        return true;
    
    wchar_t chAfter = strAfter.front();
    if (iswspace(chAfter))  // already a space
        return false;
    if (chAfter < 128)  // assume other ASCII characters are English and a space is needed
        return true;

    // None of them are English characters, so check their types
    WORD wTypeBefore = 0, wTypeAfter = 0;
    GetStringTypeW(CT_CTYPE3, &chBefore, 1, &wTypeBefore);
    GetStringTypeW(CT_CTYPE3, &chAfter, 1, &wTypeAfter);

    // Check if both characters are one of: ideographs (e.g. Chinese), Japanese Katakanas or Hiraganas
    // If so, no space needed.
    // We do this check because Chinese voices add extra pauses during speaking when you add extra spaces.
    if ((wTypeBefore & wTypeAfter) & (C3_IDEOGRAPH | C3_KATAKANA | C3_HIRAGANA))
        return false;

    return true;
}

static std::wstring_view GetXMLTagName(const std::wstring& tag)
{
    // from the first non-space character after the first '<',
    // to the first space character after that
    auto begin = std::find_if_not(tag.begin() + 1, tag.end() - 1, iswspace);
    auto end = std::find_if(begin, tag.end() - 1, iswspace); // find the first space until the last '>'

    return std::wstring_view(begin, end);
}

static std::wstring_view GetXMLClosingTagName(std::wstring_view tag)
{
    // from the first non-space character after the first '/',
    // to the first space character after that
    auto slash = std::find(tag.begin() + 1, tag.end() - 1, L'/');
    auto begin = std::find_if_not(slash + 1, tag.end() - 1, iswspace);
    auto end = std::find_if(begin, tag.end() - 1, iswspace); // find the first space until the last '>'

    return std::wstring_view(begin, end);
}

static bool IsXMLClosingTag(std::wstring_view tag)  // </xxx>
{
    if (tag.size() < 4) return false;  // malformed
    auto it = std::find_if_not(tag.begin() + 1, tag.end() - 1, iswspace);
    return it != tag.end() - 1 && *it == L'/';
}

static bool IsXMLSelfClosingTag(std::wstring_view tag)  // <xxx/>
{
    if (tag.size() < 4) return false;  // malformed
    auto it = std::find_if_not(tag.rbegin() + 1, tag.rend() - 1, iswspace);
    return it != tag.rend() - 1 && *it == L'/';
}

// returns false if no actual text will be spoken
bool CTTSEngine::BuildSSML(const SPVTEXTFRAG* pTextFragList)
{
    m_ssml.assign(L"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='http://www.w3.org/2001/mstts' xml:lang='");
    m_ssml.append(m_localeName);
    m_ssml.append(L"'>");

    USHORT mainVolume;
    if (FAILED(m_pOutputSite->GetVolume(&mainVolume)))
        mainVolume = 100;
    long mainRate;
    if (FAILED(m_pOutputSite->GetRate(&mainRate)))
        mainRate = 0;

    bool isInProsodyTag = false, isInEmphasisTag = false, isInSayAsTag = false;

    // A list of currently open custom XML tags from SPVA_ParseUnknownTag.
    // We keep a list so that we can close and reopen the tags when appropriate.
    std::vector<std::wstring> customTags;
    bool isInCustomTags = false;

    // Edge online voices only support a limited subset of SSML
    bool isEdgeVoice = m_isEdgeVoice;
    // Edge voices do not allow more than two prosody tags (changes).
    int prosodyCount = 0;

    // Some clients send Speak requests with no text, only bookmarks,
    // supposedly to track positions.
    // This will add some delay when using online voices,
    // so we track if there's actually text to be spoken.
    bool hasText = false;

    m_offsetMappings.clear();
    m_mappingIndex = 0;

    m_bookmarks.clear();
    m_bookmarkIndex = 0;

    ULONG lastSAPIOffset = 0;

    // online voices requires a <voice> tag, even after calling SetSpeechSynthesisVoiceName
    if (!m_onlineVoiceName.empty())
    {
        m_ssml.append(L"<voice name='");
        m_ssml.append(m_onlineVoiceName);
        m_ssml.append(L"'>");
    }

    constexpr auto IsSpeakableEdgeFrag = [](const SPVTEXTFRAG* pTextFrag)
        {
            auto action = pTextFrag->State.eAction;
            return action == SPVA_Speak || action == SPVA_SpellOut || action == SPVA_Pronounce;
        };

    for (auto pTextFrag = pTextFragList; pTextFrag; pTextFrag = pTextFrag->pNext)
    {
        if (pTextFrag->State.eAction != SPVA_Bookmark && pTextFrag->ulTextLen != 0)
            hasText = true;

        // tag structure: <prosody><emphasis><custom-tags...><say-as></say-as></custom-tags...></emphasis></prosody>
        // <say-as> cannot contain tags

        if (!isInProsodyTag
            // avoid introducing empty prosody tags for non-speakable fragments
            && (!isEdgeVoice || IsSpeakableEdgeFrag(pTextFrag))
            )
        {
            USHORT volume = (USHORT)std::clamp(mainVolume * pTextFrag->State.Volume / 100, 0UL, 100UL);
            long rate = std::clamp(mainRate + pTextFrag->State.RateAdj, -10L, 10L);
            long pitch = std::clamp(pTextFrag->State.PitchAdj.MiddleAdj, -10L, 10L);

            if (volume != 100 || rate != 0 || pitch != 0) // if not default value, add a prosody tag
            {
                prosodyCount++;
                m_ssml.append(L"<prosody");
                if (volume != 100)
                {
                    m_ssml.append(L" volume='");
                    m_ssml.append(std::to_wstring(volume - 100)); // 0~100 => -100%~0%
                    m_ssml.append(L"%'");
                }
                if (rate != 0)
                {
                    m_ssml.append(L" rate='");
                    m_ssml.append(std::to_wstring(rate >= 0 ? rate * 20 : rate * 20 / 3)); // -10~10 => -(2/3)~+200%
                    m_ssml.append(L"%'");
                }
                if (pitch != 0)
                {
                    m_ssml.append(L" pitch='");
                    m_ssml.append(std::to_wstring(pitch * 5)); // -10~10 => -50%~+50%
                    m_ssml.append(L"%'");
                }
                m_ssml.push_back('>');
                isInProsodyTag = true;
            }
        }

        if (!isInEmphasisTag && pTextFrag->State.EmphAdj && !isEdgeVoice)
        {
            m_ssml.append(L"<emphasis>"); // (not supported by offline TTS)
            isInEmphasisTag = true;
        }

        // reopen all custom tags
        if (!isInCustomTags)
        {
            for (const auto& customTag : customTags)
                m_ssml.append(customTag);
            isInCustomTags = true;
        }

        // NOTE: <say-as> tag cannot contain child tags.
        // if eAction is not Speak, a child tag will be added, which is incompatible with <say-as>
        // so only add <say-as> when eAction is Speak
        if (!isInSayAsTag && pTextFrag->State.Context.pCategory
            && pTextFrag->State.eAction == SPVA_Speak
            && !isEdgeVoice)
        {
            // map <context id='xxx'>...</context>
            // to <say-as interpret-as='xxx' format='xxx'>...</say-as>
            AppendSAPIContextToSsml(pTextFrag->State.Context);
            isInSayAsTag = true;
        }

        if (isEdgeVoice)
        {
            // Edge online voices only support some SSML tags
            // SSML that contains unrecognized tags will be rejected by the server
            // so we only keep the text that can be processed, and ignore the XML tags around it
            switch (pTextFrag->State.eAction)
            {
            case SPVA_Speak:
            case SPVA_SpellOut:
            case SPVA_Pronounce:
                if (NeedAddingSpace(m_ssml, std::wstring_view(pTextFrag->pTextStart, pTextFrag->ulTextLen)))
                    m_ssml.push_back(L' ');
                m_offsetMappings.emplace_back(pTextFrag->ulTextSrcOffset, (ULONG)m_ssml.size());
                AppendTextFragToSsml(pTextFrag);
                m_offsetMappings.emplace_back(pTextFrag->ulTextSrcOffset + pTextFrag->ulTextLen, (ULONG)m_ssml.size());
                lastSAPIOffset = pTextFrag->ulTextSrcOffset + pTextFrag->ulTextLen;
                break;

            case SPVA_Bookmark:
                // mark the position before this bookmark
                m_offsetMappings.emplace_back(lastSAPIOffset, (ULONG)m_ssml.size());
                // keep track of every bookmark, so we can simulate bookmark events later
                m_bookmarks.emplace_back(pTextFrag->ulTextSrcOffset, std::wstring(pTextFrag->pTextStart, pTextFrag->ulTextLen));
                break;
            }
        }
        else
        {
            switch (pTextFrag->State.eAction)
            {
            case SPVA_Speak:
                if (NeedAddingSpace(m_ssml, std::wstring_view(pTextFrag->pTextStart, pTextFrag->ulTextLen)))
                    m_ssml.push_back(L' ');
                m_offsetMappings.emplace_back(pTextFrag->ulTextSrcOffset, (ULONG)m_ssml.size());
                AppendTextFragToSsml(pTextFrag);
                m_offsetMappings.emplace_back(pTextFrag->ulTextSrcOffset + pTextFrag->ulTextLen, (ULONG)m_ssml.size());
                break;

            case SPVA_Silence: // insert a <break time='xxms'/> (not supported by offline TTS)
                m_ssml.append(L"<break time='");
                m_ssml.append(std::to_wstring(pTextFrag->State.SilenceMSecs));
                m_ssml.append(L"ms'/>");
                break;

            case SPVA_Bookmark: // insert a <bookmark mark='xx'/>
                m_ssml.append(L"<bookmark mark='");
                AppendTextFragToSsml(pTextFrag);
                m_ssml.append(L"'/>");
                // keep track of every bookmark, so when there's no text, we can simulate bookmark events instead
                m_bookmarks.emplace_back(pTextFrag->ulTextSrcOffset, std::wstring(pTextFrag->pTextStart, pTextFrag->ulTextLen));
                break;

            case SPVA_SpellOut: // insert a <say-as interpret-as='characters'>...</say-as>
                m_ssml.append(L"<say-as interpret-as='characters'>");
                m_offsetMappings.emplace_back(pTextFrag->ulTextSrcOffset, (ULONG)m_ssml.size());
                AppendTextFragToSsml(pTextFrag);
                m_offsetMappings.emplace_back(pTextFrag->ulTextSrcOffset + pTextFrag->ulTextLen, (ULONG)m_ssml.size());
                m_ssml.append(L"</say-as>");
                break;

            case SPVA_Pronounce: // insert a <phoneme alphabet='sapi' ph='xx'>...</phoneme>
                m_ssml.append(L"<phoneme alphabet='sapi' ph='");
                AppendPhonemesToSsml(pTextFrag->State.pPhoneIds);
                m_ssml.append(L"'>");
                m_offsetMappings.emplace_back(pTextFrag->ulTextSrcOffset, (ULONG)m_ssml.size());
                AppendTextFragToSsml(pTextFrag);
                m_offsetMappings.emplace_back(pTextFrag->ulTextSrcOffset + pTextFrag->ulTextLen, (ULONG)m_ssml.size());
                m_ssml.append(L"</phoneme>");
                break;

            case SPVA_ParseUnknownTag: // insert it into SSML as-is
            {
                // The string should always start with '<' and end with '>',
                // and may contain trailing spaces,
                // but no further warranty is given, as SAPI does no further check.
                // So the actual XML tag might be malformed.

                std::wstring_view tag(pTextFrag->pTextStart, pTextFrag->ulTextLen);
                // trim spaces
                tag.remove_prefix(tag.find('<'));
                tag.remove_suffix(tag.size() - tag.rfind(L'>') - 1);

                if (IsXMLSelfClosingTag(tag))
                {
                    m_ssml.append(pTextFrag->pTextStart, pTextFrag->ulTextLen);
                }
                else if (IsXMLClosingTag(tag))
                {
                    std::wstring_view tagName = GetXMLClosingTagName(tag);

                    auto tagToClose = std::find_if(customTags.rbegin(), customTags.rend(),
                        [tagName](const std::wstring& tag)
                        { return EqualsIgnoreCase(GetXMLTagName(tag), tagName); });

                    // if there's no matching opening tag, ignore this closing tag
                    if (tagToClose != customTags.rend())
                    {
                        if (tagToClose != customTags.rbegin())
                        {
                            LogWarn("Speak: XML tag '{}' closed in the wrong order", tag);
                            // close all previous unclosed tags in reverse order
                            for (auto it = customTags.rbegin(); it != tagToClose; ++it)
                            {
                                m_ssml.append(L"</");
                                m_ssml.append(GetXMLTagName(*it));
                                m_ssml.push_back(L'>');
                            }
                        }
                        m_ssml.append(pTextFrag->pTextStart, pTextFrag->ulTextLen);
                        customTags.erase(tagToClose.base() - 1, customTags.end());  // remove from tag list
                    }
                    else
                    {
                        LogWarn("Speak: Unmatched closing tag '{}', ignored", tag);
                    }
                }
                else if (pTextFrag->ulTextLen >= 3) // opening tag
                {
                    m_ssml.append(pTextFrag->pTextStart, pTextFrag->ulTextLen);
                    customTags.emplace_back(pTextFrag->pTextStart, pTextFrag->ulTextLen);  // add to tag list
                }
                else
                {
                    LogWarn("Speak: Malformed XML tag '{}', ignored", tag);
                }

                break;
            }
            }
        }

        int preserveTagLevel = 0;
        auto pNextTextFrag = pTextFrag->pNext;

        if (isEdgeVoice)
        {
            // skip fragments that are ignored for Edge voices
            // to avoid introducing empty prosody tags
            for (; pNextTextFrag; pNextTextFrag = pNextTextFrag->pNext)
            {
                if (IsSpeakableEdgeFrag(pNextTextFrag))
                    break;
            }
        }

        if (pNextTextFrag)
        {
            auto& curState = pTextFrag->State;
            auto& nextState = pNextTextFrag->State;

            bool sameProsody = (curState.Volume == nextState.Volume
                && curState.RateAdj == nextState.RateAdj
                && curState.PitchAdj.MiddleAdj == nextState.PitchAdj.MiddleAdj);

			if (sameProsody || (isEdgeVoice && prosodyCount >= 2))
			{
                if (!sameProsody && isEdgeVoice)
                    LogWarn("Speak: Edge voices do not support more than two prosody tags. Some prosody changes may be lost.");

                preserveTagLevel = 1; // if prosody is the same, no need to close it

                if (curState.EmphAdj == nextState.EmphAdj)
                {
                    preserveTagLevel = 2; // if emphasis is the same, no need to close it

                    if ((curState.Context.pCategory == nextState.Context.pCategory ||
                        (curState.Context.pCategory && nextState.Context.pCategory
                            && _wcsicmp(curState.Context.pCategory, nextState.Context.pCategory) == 0))
                        && nextState.eAction == SPVA_Speak)
                    {
                        // if context is the same, and the next fragment is still Speak, no need to close it
                        // if the next fragment isn't Speak, a child tag will be added, and we should close <say-as>
                        preserveTagLevel = 3;
                    }
                }
            }
        }

        // close tags
        if (isInSayAsTag && preserveTagLevel < 3)
        {
            m_ssml.append(L"</say-as>");
            isInSayAsTag = false;
        }
        if (isInCustomTags && preserveTagLevel < 2)
        {
            // close all custom tags in reverse order
            for (auto it = customTags.rbegin(); it != customTags.rend(); ++it)
            {
                m_ssml.append(L"</");
                m_ssml.append(GetXMLTagName(*it));
                m_ssml.push_back(L'>');
            }
            isInCustomTags = false;
        }
        if (isInEmphasisTag && preserveTagLevel < 2)
        {
            m_ssml.append(L"</emphasis>");
            isInEmphasisTag = false;
        }
        if (isInProsodyTag && preserveTagLevel < 1)
        {
            m_ssml.append(L"</prosody>");
            isInProsodyTag = false;
        }
    }

    if (!m_onlineVoiceName.empty())
    {
        m_ssml.append(L"</voice>");
    }

    m_ssml.append(L"</speak>");

    return hasText;
}

std::wstring CTTSEngine::StripSSML(const std::wstring& ssml)
{
    // Simple SSML stripper - removes tags but keeps text content
    // For production, consider using a proper XML parser

    std::wstring result;
    result.reserve(ssml.size());

    bool inTag = false;
    bool inComment = false;

    for (size_t i = 0; i < ssml.size(); ++i)
    {
        if (inComment)
        {
            if (i + 2 < ssml.size() && ssml[i] == L'-' && ssml[i + 1] == L'-' && ssml[i + 2] == L'>')
            {
                inComment = false;
                i += 2;
            }
            continue;
        }

        if (ssml[i] == L'<')
        {
            // Check for comment start
            if (i + 3 < ssml.size() && ssml[i + 1] == L'!' && ssml[i + 2] == L'-' && ssml[i + 3] == L'-')
            {
                inComment = true;
                i += 3;
                continue;
            }
            inTag = true;
            continue;
        }

        if (ssml[i] == L'>')
        {
            inTag = false;
            continue;
        }

        if (!inTag)
        {
            // Decode common XML entities
            if (ssml[i] == L'&')
            {
                if (ssml.substr(i, 4) == L"&lt;")
                {
                    result += L'<';
                    i += 3;
                }
                else if (ssml.substr(i, 4) == L"&gt;")
                {
                    result += L'>';
                    i += 3;
                }
                else if (ssml.substr(i, 5) == L"&amp;")
                {
                    result += L'&';
                    i += 4;
                }
                else if (ssml.substr(i, 6) == L"&quot;")
                {
                    result += L'"';
                    i += 5;
                }
                else if (ssml.substr(i, 6) == L"&apos;")
                {
                    result += L'\'';
                    i += 5;
                }
                else
                {
                    result += ssml[i];
                }
            }
            else
            {
                result += ssml[i];
            }
        }
    }

    // Trim whitespace
    size_t start = result.find_first_not_of(L" \t\n\r");
    if (start == std::wstring::npos)
        return L"";

    size_t end = result.find_last_not_of(L" \t\n\r");
    return result.substr(start, end - start + 1);
}

void CTTSEngine::FinishSimulatingBookmarkEvents(ULONGLONG streamOffset)
{
    const auto size = m_bookmarks.size();
    SPEVENT ev = { 0 };
    ev.ullAudioStreamOffset = streamOffset;
    ev.eEventId = SPEI_TTS_BOOKMARK;
    ev.elParamType = SPET_LPARAM_IS_STRING;
    for (auto i = m_bookmarkIndex; i < size; i++)
    {
        auto& bookmark = m_bookmarks[i];
        ev.lParam = reinterpret_cast<LPARAM>(bookmark.name.c_str());
        ev.wParam = _wtol(bookmark.name.c_str());
        m_pOutputSite->AddEvents(&ev, 1);
    }
}

// Convert offset and length in SSML text to those in SAPI text
void CTTSEngine::MapTextOffset(ULONG& ulSSMLOffset, ULONG& ulTextLen)
{
    if (m_offsetMappings.empty())
        return;

    ULONG endOffset = ulSSMLOffset + ulTextLen;
    const auto size = m_offsetMappings.size();

    // all mapping pairs in m_offsetMappings go from low offset to high offset,
    // so we just move the index forward as the speaking progresses
    // but if index goes beyond border, or the current offset surpasses the actual offset, reset index to 0
    if (m_mappingIndex >= size || m_offsetMappings[m_mappingIndex].ulSSMLTextOffset > ulSSMLOffset)
        m_mappingIndex = 0;

    // if we surpass the next offset, move forward, until the actual offset is between current and next
    // if there are multiple items with the same SSML offset, this will find the last item
    while (m_mappingIndex + 1 < size && ulSSMLOffset >= m_offsetMappings[m_mappingIndex + 1].ulSSMLTextOffset)
        m_mappingIndex++;

    const auto& mapping = m_offsetMappings[m_mappingIndex];
    // the same parameter is used for input & output
    auto& ulSAPIOffset = ulSSMLOffset;
    // if offset falls below zero, set it to zero
    if (mapping.ulSSMLTextOffset > mapping.ulSAPITextOffset && ulSSMLOffset < mapping.ulSSMLTextOffset - mapping.ulSAPITextOffset)
        ulSAPIOffset = 0;
    else
        ulSAPIOffset = ulSSMLOffset - mapping.ulSSMLTextOffset + mapping.ulSAPITextOffset;

    // if the end position (ulSSMLOffset + ulTextLen) also get remapped
    // (for example when '&' becomes '&amp;')
    // adjust ulTextLen as well
    // first find which range the end position is in, just like the above
    auto index = m_mappingIndex;
    while (index + 1 < size && endOffset >= m_offsetMappings[index + 1].ulSSMLTextOffset)
        index++;

    // If there are multiple items with the same SSML offset, go backwards and choose the first item.
    // This is because when simulating bookmark events for Edge voices,
    // the bookmark itself does not appear in the SSML text, only in the SAPI text,
    // so the same SSML offset will be paired with both the beginning and the end of the bookmark tag.
    // 
    // When mapping the starting offset, we need the last pair;
    // and when mapping the end offset, we need the first pair,
    // so that we can exclude the bookmark tag in boundary events.
    // 
    // However, if the bookmark tag is inside a word,
    // as Edge voices know nothing about bookmarks, they will still assume it's a whole word,
    // so we have to include the bookmark tag in the word.
    // Use the end offset of SAPI text to determine if the bookmark is inside the word.
    auto endSAPIOffset = ulSAPIOffset + ulTextLen;
    while (index > 0
        && m_offsetMappings[index - 1].ulSSMLTextOffset == m_offsetMappings[index].ulSSMLTextOffset  // previous same offset
        && m_offsetMappings[index - 1].ulSAPITextOffset >= endSAPIOffset  // previous not inside the word
        )
        index--;
    if (index <= m_mappingIndex)
        return;

    const auto& endMapping = m_offsetMappings[index];
    endOffset = endOffset - endMapping.ulSSMLTextOffset + endMapping.ulSAPITextOffset;
    ulTextLen = endOffset - ulSSMLOffset;
}

// Checks the result from speech operation, and throws if error happened
