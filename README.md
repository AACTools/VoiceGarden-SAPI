# VoiceGarden-SAPI

> Forked from [NaturalVoiceSAPIAdapter](https://github.com/gexgd0419/NaturalVoiceSAPIAdapter). Developed at [AACTools/VoiceGarden-SAPI](https://github.com/AACTools/VoiceGarden-SAPI).

A [SAPI 5 text-to-speech engine][1] that connects **23 TTS engines** to any Windows application that supports SAPI voices — including Grid 3, Mind Express, Balabolka, Clicker, and any software using `System.Speech`.

Powered by [rust-tts-wrapper](https://github.com/AACTools/rust-tts-wrapper) for all synthesis, voice listing, and word boundary events.

## What voices are supported?

| Category | Engines | Cloud? |
|----------|---------|--------|
| **Offline neural** | floravox (piper/MMS VITS, Matcha, Kokoro — measured word timings, SSML bookmarks, lexicon + Phonetisaurus + ByT5 G2P) and SherpaOnnx (Kokoro, Piper, MMS, VITS, Matcha, Kitten) as fallback | No — fully local* |
| **Microsoft** | Azure Cognitive Services, Edge browser voices (credential-free) | Yes |
| **Cloud TTS** | OpenAI, Google Cloud, AWS Polly, ElevenLabs, Cartesia, Deepgram | Yes |
| **More cloud** | Watson, PlayHT, Wit.ai, Gemini, Hume AI, xAI Grok, Fish Audio, Mistral, Murf, Unreal Speech, Resemble, Uplift AI, Models Lab | Yes |

All engines support **word boundary events** for word highlighting in AAC software. Offline voices route to **floravox** wherever it's supported (piper/MMS VITS, Matcha, Kokoro — self-served from sherpa layout; duration-tensor timings on patched voices, SSML `<mark>` → `SPEI_TTSBOOKMARK`, published per-language G2P bundles cached locally, synthesis still works offline). **SherpaOnnx** is the automatic fallback (32-bit hosts, engine failure) and the only engine for the flow/diffusion families (zipvoice, supertonic, pocket, kitten).

\* floravox on x64 needs Windows 10 1903+ (the static onnxruntime DirectML floor); older systems fall back to SherpaOnnx.

## Quick Start

### Install
1. Download `setup.exe` from the [Releases][6] section.
2. Run `setup.exe` — it installs the MSI and **auto-registers** the SAPI adapter.
3. After installation, **VoiceGarden.UI.exe** launches automatically.
4. Go to the **SherpaOnnx** tab to download offline models, or the **Engine Config** tab to enter cloud API keys.
5. Click **Install to SAPI** to promote voices to the Windows registry.
6. Restart your SAPI application (e.g., Grid 3) — the new voices appear in the voice list.

### CLI mode
VoiceGarden.UI.exe doubles as a command-line tool:

```
VoiceGarden.UI.exe status                    # show registration status
VoiceGarden.UI.exe voices                    # list all SAPI voices
VoiceGarden.UI.exe models list               # list downloaded SherpaOnnx models
VoiceGarden.UI.exe models download <id>      # download a model
VoiceGarden.UI.exe models promote-all        # promote all models to SAPI (needs admin)
VoiceGarden.UI.exe promote --engine google --voice en-US-Wavenet-D --key API_KEY
VoiceGarden.UI.exe validate --engine azure --voice en-US-JennyNeural --key KEY --region uksouth
```

## System Requirements

- **OS:** Windows 7 SP1 or later (64-bit recommended; 32-bit supported with limitations)
- **Runtime:** .NET 8.0 desktop runtime (for VoiceGarden.UI)
- **For cloud voices:** Internet access + valid API key for the respective service

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│  SAPI Application (Grid 3, Mind Express, Balabolka)     │
│                    │ SAPI 5 COM                          │
│  ┌─────────────────▼──────────────────────────────────┐  │
│  │  VoiceGardenSAPIAdapter.dll (C++ COM DLL)          │  │
│  │  • BuildSSML (SAPI fragments → SSML)               │  │
│  │  • Word boundary offset mapping                     │  │
│  │  • SSML marks → SPEI_TTSBOOKMARK (floravox)         │  │
│  │  • Audio streaming + silence compensation           │  │
│  │         │ loads via LoadLibrary                     │  │
│  │  ┌────────▼─────────────────────────────────┐   │  │
│  │  │  rust_tts_wrapper.dll (Rust)              │   │  │
│  │  │  • 23 engines (floravox, SherpaOnnx,      │   │  │
│  │  │    Azure, Edge, OpenAI, Google, ...)      │   │  │
│  │  │  • Word boundaries (Azure/Google: real,   │   │  │
│  │  │    floravox: measured, others: estimated) │   │  │
│  │  │  • Viseme events (Azure/Edge)             │   │  │
│  │  │  • Connection pooling (Azure/Edge WS)     │   │  │
│  │  │  • Sec-MS-GEC token (Edge voices)         │   │  │
│  │  │  • SherpaOnnx model auto-detection        │   │  │
│  │  │  • ABI canary: refuses pre-0.5 DLLs       │   │  │
│  │  └───────────────────────────────────────────┘   │  │
│  └────────────────────────────────────────────────────┘  │
│                                                           │
│  ┌─────────────────────────────────────────────────────┐ │
│  │  VoiceGarden.UI.exe (Avalonia .NET 8 app)           │ │
│  │  • Download/promote SherpaOnnx models               │ │
│  │  • Configure cloud engine credentials               │ │
│  │  • Register/unregister 32-bit + 64-bit DLLs         │ │
│  │  • Preview voices (via RustTtsWrapper.Bindings)      │ │
│  │  • Anonymous usage analytics (opt-in, PostHog EU)   │ │
│  └─────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
```

### Key Components

| Component | Description |
|-----------|-------------|
| `VoiceGardenSAPIAdapter/` | C++ SAPI COM DLL — SSML building, offset mapping, audio streaming. Loads `rust_tts_wrapper.dll` for all synthesis |
| `VoiceGardenSAPIAdapter/RustTts/` | C++ wrapper for the Rust DLL (dynamic loading, ABI canary, callback marshalling) |
| `VoiceGarden.UI/` | Avalonia UI app — configuration, model management, voice preview, analytics |
| `VoiceGarden.UI/Services/PiperSidecarGenerator.cs` | Generates piper `*.onnx.json` sidecars for sherpa-layout voices so they route through floravox |
| `VoiceGarden.UI.Tests/` | xunit tests: floravox integration (boundaries, marks, G2P) + sidecar generator units |
| `SherpaOnnx/` | Model discovery (voice enumerator scans for installed models) |
| `Setup/` + `SetupLauncher/` | WiX MSI package + setup.exe bootstrapper |

## SherpaOnnx Offline Voices

Fully offline neural TTS — no internet, no API keys, no cloud dependency.

### Supported model types
- **Kokoro** — high-quality English models with multiple voices (`voices.bin`)
- **Piper** — fast, lightweight VITS models (many languages)
- **MMS** — Meta's Massively Multilingual Speech (1000+ languages)
- **VITS** — standard VITS models
- **Matcha** — matcha-TTS models with vocoder

### Downloading models
Models are stored in `%LOCALAPPDATA%\VoiceGardenSAPIAdapter\models\`. Download via:
- **UI:** SherpaOnnx tab → select models → Download Selected (built-in extraction, no 7-Zip needed)
- **CLI:** `VoiceGarden.UI.exe models download kokoro-en-v0_19`

### Grid 3 compatibility
Grid 3 (`System.Speech`) only selects voices from HKLM registry tokens. Use **Install to SAPI** to promote voices to HKLM. Word boundary events work with Grid3 (tested, no crash).

See `docs/troubleshooting-grid3-voice-activation.md` for detailed Grid 3 troubleshooting.

## Cloud Engine Configuration

### Azure Cognitive Services
1. Get a key from [Azure Portal](https://portal.azure.com/) → Speech service → Keys and Endpoint
2. In VoiceGarden.UI → Engine Config → Azure: enter key and region (e.g., `uksouth`)
3. Fetch voices → select → Install Selected

### Edge browser voices
Free, credential-free voices from Microsoft Edge's Read Aloud feature. Enable in Advanced settings → "Enable Edge browser voices". The Rust engine computes the Sec-MS-GEC token automatically.

### Other engines (OpenAI, Google, ElevenLabs, etc.)
Each cloud engine needs its API key set in the Engine Config tab. Search voices by language name (e.g., "arabic", "gujarati") or voice ID.

## Testing

### Unit + integration (.NET)
```powershell
dotnet test VoiceGarden.UI.Tests\VoiceGarden.UI.Tests.csproj   # floravox integration (skips w/o local models) + sidecar generator units
.\scripts\test-rust-dll-pin.ps1                                 # CI ships the csproj-pinned rust DLL
.\scripts\test-rust-abi.ps1                                     # export/ABI canary check incl. negative control
```

### Boundary crash test (Grid3 pattern)
```powershell
.\scripts\test-boundary-crash.ps1    # PromptBuilder with rate changes — reproduces Grid3 crash
```

### Word boundary events
```powershell
.\scripts\test-word-boundaries.ps1   # checks word text + position for each voice
```

### Cleanup (remove all custom voices)
```powershell
.\scripts\cleanup-voices.ps1            # removes Sherpa/Cloud/Edge tokens from HKLM + HKCU
.\scripts\cleanup-voices.ps1 -DryRun    # preview without deleting
```

### Manual test via SAPI COM
```powershell
Add-Type -AssemblyName "System.Speech"
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$s.SelectVoice("kokoro-en-v0_19")       # or any voice name
$s.Speak("Hello, this is a test.")
```

## Building

### Prerequisites
- Visual Studio 2022 with C++ (MSVC v143)
- .NET 8.0 SDK
- [WiX Toolset v4](https://wixtoolset.org/) (for MSI)

### Local build
```powershell
.\download-sherpa-deps.ps1 -Platforms x64     # one-time: download SherpaOnnx native DLLs
.\scripts\build-release-local.ps1 -Configuration Release -Platforms x64 -BuildSetup -SkipSherpaDeps -SkipSubmodules
```

Output: `installer-output\VoiceGardenSAPIAdapter.msi`

### CI
GitHub Actions workflow at `.github/workflows/msbuild.yml` builds all components on every push:
- C++ adapter DLL (x64 + x86)
- VoiceGarden.UI (Avalonia app with RustTtsWrapper.Bindings)
- SherpaOnnxConfig + EngineConfig CLI tools
- .NET adapter
- MSI setup package (auto-registers DLL on install)

## Privacy

VoiceGarden.UI includes optional, anonymous usage analytics (PostHog, EU-hosted). Disabled by default. See `docs/PRIVACY.md` for full details.

## Libraries Used

**Primary:**
- [rust-tts-wrapper](https://github.com/AACTools/rust-tts-wrapper) — Rust TTS engine for all synthesis (21 engines, streaming, word boundaries)
- [RustTtsWrapper.Bindings](https://www.nuget.org/packages/RustTtsWrapper.Bindings) — .NET P/Invoke bindings for the Rust DLL
- [Avalonia UI](https://avaloniaui.net/) — .NET desktop UI framework
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM framework
- [SharpCompress](https://github.com/adamhathcock/sharpcompress) — built-in model archive extraction

**C++ adapter dependencies:**
- [SherpaOnnx](https://github.com/k2-fsa/sherpa-onnx) — offline neural TTS native runtime
- Microsoft Azure Speech SDK — embedded TTS (DLL shim)
- OpenSSL, websocketpp, nlohmann/json, spdlog — legacy C++ networking (used by Azure SDK)

[1]: https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ms717037(v=vs.85)
[6]: ../../releases
