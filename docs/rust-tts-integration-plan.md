# Rust TTS Wrapper Integration Plan

## Branch: `feature/rust-tts-integration`

## Current State (as of 2026-07-07)

### What rust-tts-wrapper now provides
- **23 engines**: SherpaOnnx (all model types), Azure (WS streaming + pooling), Edge (credential-free, Sec-MS-GEC), 19 cloud (OpenAI, Google, ElevenLabs, Watson, PlayHT, Cartesia, Deepgram, etc.)
- **Real-time word boundaries**: Azure (WS metadata inline), Google (timepoints), ElevenLabs (character alignment)
- **Viseme events**: Azure WS `Type:"Viseme"` parsed and forwarded
- **Connection pooling**: Azure/Edge WS connections reused (eliminates ~300ms handshake per utterance)
- **Streaming audio**: Cloud engines deliver audio in chunks via `on_audio` callback
- **SSML passthrough**: `tts_speak_ssml(ctx, ssml)` accepts pre-built SSML from C++ `BuildSSML()`
- **Boundary v2 callback**: `CBoundaryCb2(word, charOffset, charLen, startS, endS, userdata)` — includes text offsets for SAPI word highlighting
- **SherpaOnnx**: All model types (Kokoro/Matcha/VITS/Piper/MMS/Kitten), engine caching, mid-generation cancellation, pitch/volume post-processing
- **FFI panic safety**: All `extern "C"` functions wrapped in `catch_unwind`
- **Edge voices**: Full Sec-MS-GEC token computation, browser-mimicking headers, WS synthesis

### Known blocker
- **Windows FFI `last_error` regression** — documented in `HANDOFF_WINDOWS_FFI.md`. `tts_synth_to_bytes` failure path may return empty error string on Windows. Not a blocker for the integration architecture, but C++ code should log its own errors independently.

---

## Architecture

```
VoiceGardenSAPIAdapter.dll (C++)
├── BuildSSML()                 ← KEEP (superior SSML building)
├── OnAudioData()               ← KEEP (silence compensation)
├── OnBoundary/OnViseme/OnBookmark  ← KEEP (SAPI event plumbing)
├── SherpaOnnx/                 ← KEEP as fallback (works, cached)
├── SpeechRestAPI.cpp           ← KEEP as fallback (Azure/Edge WS)
├── GenericHttpTts.cpp          ← REPLACE with RustTts
│
└── RustTts/                    ← NEW
    ├── RustTtsLoader.h/.cpp    Dynamic load tts_wrapper.dll
    ├── RustTtsEngine.h/.cpp    RAII wrapper for tts_ctx
    └── tts_wrapper.h           Copied C header
```

**Key principle:** rust-tts-wrapper is optional at runtime. If `tts_wrapper.dll` fails to load, the adapter falls back to existing C++ code paths. This allows incremental rollout.

---

## Phase 1: Infrastructure + GenericHttpTts Replacement

**Goal:** Load `tts_wrapper.dll` dynamically, use it for OpenAI/Google/ElevenLabs/Cartesia/Deepgram (currently handled by GenericHttpTts).

### Tasks

1. **Copy C header** — Copy `tts_wrapper.h` from rust-tts-wrapper to `RustTts/tts_wrapper.h` in VoiceGarden-SAPI

2. **Create `RustTtsLoader`** — Mirror `SherpaOnnxDynamic.h`:
   - `HMODULE m_hModule` — `LoadLibrary("tts_wrapper.dll")`
   - Function pointer for each `tts_*` function
   - `Initialize()` — resolves all symbols via `GetProcAddress`
   - `IsLoaded()` — true if DLL loaded successfully
   - Singleton pattern

3. **Create `RustTtsEngine`** — RAII C++ wrapper:
   - `Create(engineId, credentialsJson)` — calls `tts_create`, stores `tts_ctx*`
   - `Destroy()` — calls `tts_destroy`
   - `Speak(plainText)` — calls `tts_speak` with `on_audio` callback
   - `SpeakSsml(ssml)` — calls `tts_speak_ssml` with `on_audio` callback
   - `SetOnAudio(cb)` — registers C++ callback
   - `SetOnBoundary(cb)` — registers C++ callback with text offsets
   - `SetOnViseme(cb)` — registers C++ callback
   - `Stop()` — calls `tts_stop`
   - Static thunk functions that marshal to `CTTSEngine*` via `userdata`

4. **Integrate into TTSEngine::Speak()**:
   - In `InitVoice()`, if the engine type is one of the GenericHttp engines AND RustTts is loaded, create a `RustTtsEngine` instead of `GenericHttpTts`
   - In `Speak()`, if `m_rustTts` is initialized, use it for the speak path
   - The `on_audio` callback calls `OnAudioData()` (same path as SherpaOnnx)
   - The `on_boundary` callback calls `OnBoundary()`
   - The `on_viseme` callback calls `OnViseme()`

5. **Add `tts_wrapper.dll` to MSI payload**:
   - Add to `scripts/create-setup-payload.ps1`
   - The DLL goes into both `x64\` and `x86\` directories (native architecture only — Rust compiles per-target)

### Effort: ~2-3 days

### Gains:
- 19 cloud engines (vs current 5 in GenericHttpTts)
- Streaming audio (no buffering entire MP3 before decode)
- Real word boundaries for Google + ElevenLabs
- Eliminates `GenericHttpTts.cpp`, `Mp3Decoder.cpp` dependencies

---

## Phase 2: Edge Voices via Rust

**Goal:** Use rust-tts-wrapper's Edge voice support (Sec-MS-GEC, WS synthesis) instead of the C++ WSConnectionPool.

### Tasks

1. **Edge voice token creation**: The C++ voice enumerator currently creates in-memory Edge voice tokens with `WebsocketURL` config. Extend to also set `EngineType = "Edge"` so `InitVoice()` can route to RustTts.

2. **Edge voice init in TTSEngine**: In `InitVoice()`, if `EngineType == "Edge"` and RustTts is loaded:
   - Create `RustTtsEngine` with `engine_id = "edge"` (no credentials needed — credential-free engine)
   - Set voice via `tts_set_voice(ctx, "en-US-AriaNeural")` etc.

3. **Edge speak path**: In `Speak()`, if `m_rustTts` is an Edge context:
   - Pass plain text (Edge voices strip SSML — same as current behavior)
   - `on_audio` callback delivers PCM chunks
   - `on_boundary` fires inline (from Azure WS metadata)
   - `on_viseme` fires from Azure WS viseme events

4. **Keep WSConnectionPool as fallback**: If RustTts not loaded, use existing `SpeechRestAPI` + `WSConnectionPool` path.

### Effort: ~1-2 days

### Gains:
- Eliminates `Sec-MS-GEC` computation from C++ (was never implemented for HKLM tokens)
- Edge voices work with HKLM token promotion (Sec-MS-GEC computed by Rust)
- Connection pooling (300ms faster per utterance after first connection)
- Viseme events for Edge voices (currently not supported)

---

## Phase 3: Azure via Rust (Optional)

**Goal:** Use rust-tts-wrapper's Azure WS path instead of SpeechRestAPI. This is the riskiest phase because the C++ Azure path is the most battle-tested.

### Prerequisites
- C++ `BuildSSML()` output must be passable to `tts_speak_ssml()` — verified, the Rust ABI supports this
- Real-time boundary delivery must work — verified, Rust fires `on_boundary` inline during WS message processing
- Viseme delivery must work — verified, Rust parses `Type:"Viseme"` from WS metadata

### Tasks

1. **Azure voice init**: In `InitVoice()`, if `EngineType == "Azure"` and RustTts is loaded:
   - Create `RustTtsEngine` with `engine_id = "azure"`, credentials = `{"subscriptionKey": key, "region": region}`
   - Set voice via `tts_set_voice(ctx, voiceName)`

2. **Azure speak path**: In `Speak()`, if `m_rustTts` is an Azure context:
   - Call `BuildSSML()` as before (KEEP the superior SSML builder)
   - Pass SSML to `tts_speak_ssml(ctx, ssml)`
   - `on_audio`, `on_boundary`, `on_viseme` callbacks same as Phase 2

3. **Fallback**: If RustTts not loaded, use existing `SpeechRestAPI` + Azure SDK path.

### Effort: ~2-3 days

### Gains:
- Eliminates `websocketpp`, `asio`, `SpeechRestAPI.cpp`, `WSConnectionPool.cpp` dependencies
- Smaller DLL size
- Simpler build (fewer native dependencies)
- Connection pooling (faster per-utterance)

### Risks:
- Regression in edge cases (SSML quirks, error handling, reconnect logic)
- Must verify viseme/boundary timing is equivalent
- Must verify silence compensation still works

---

## Phase 4: SherpaOnnx via Rust (Optional, Lower Priority)

**Goal:** Use rust-tts-wrapper's SherpaOnnx path instead of the C++ SherpaOnnx dynamic loader.

### Why this is lower priority
The C++ SherpaOnnx path works well — it's model-type aware, cached, and produces correct audio. The Rust path offers:
- Better file path auto-detection
- Mid-generation cancellation (progress callback)
- Embedded model registry (no sidecar JSON file)
- Pitch/volume post-processing

But these are improvements, not fixes. The C++ path works today.

### Tasks
1. Route Sherpa voice init through RustTts when loaded
2. Map registry `SherpaOnnxModelPath` etc. to RustTts credentials JSON
3. Verify all model types produce identical audio

### Effort: ~2 days

---

## Build & CI Changes

### VoiceGarden-SAPI CI (`.github/workflows/msbuild.yml`)
- Add `build-rust-tts` job that builds `rust-tts-wrapper` for win-x64 and win-x86
- Upload `tts_wrapper.dll` (and `tts_wrapper.lib`) as artifact
- Download in `build-setup` job, include in MSI payload

### VoiceGarden-SAPI Project Structure
```
RustTts/
  RustTtsLoader.h        — DLL loading (mirrors SherpaOnnxDynamic.h)
  RustTtsLoader.cpp
  RustTtsEngine.h        — C++ RAII wrapper
  RustTtsEngine.cpp
  tts_wrapper.h          — C header (copied from rust-tts-wrapper)
```

### Payload Layout
```
x64/
  VoiceGardenSAPIAdapter.dll
  tts_wrapper.dll          ← NEW (Rust compiled DLL)
  sherpa-onnx-c-api.dll
  onnxruntime.dll
  ...
x86/
  VoiceGardenSAPIAdapter.dll
  tts_wrapper.dll          ← NEW (32-bit Rust DLL)
  sherpa-onnx-c-api.dll
  ...
```

---

## Testing Strategy

### Unit Tests
- RustTtsLoader: verify DLL loads, all function pointers resolve
- RustTtsEngine: verify tts_create/speak/destroy lifecycle
- Callback thunk: verify audio data and boundary events marshal correctly

### Integration Tests
- Verify each engine type produces audio when RustTts is loaded
- Verify fallback to C++ paths when RustTts is NOT loaded
- Verify SSML passthrough for Azure (BuildSSML → tts_speak_ssml)
- Verify word highlighting works in System.Speech (boundary timing)

### Regression Tests
- All existing SherpaOnnx voices still work
- All existing Azure voices still work
- All existing cloud engine voices still work
- Edge voices work with HKLM token promotion

---

## Decision Points

1. **Which phase first?** — Phase 1 (GenericHttpTts replacement) is lowest risk and highest immediate value.

2. **Should we ever remove the C++ fallback code?** — Not until RustTts has been tested in production for a release cycle. Keep both paths, prefer Rust when available.

3. **32-bit support?** — Rust compiles for x86. The tts_wrapper.dll will be 32-bit for the x86 payload. Both architectures supported.

4. **What about the Windows FFI last_error regression?** — Not a blocker. C++ code should log its own errors independently and not rely solely on `tts_get_last_error`. The Rust team is working on it.
