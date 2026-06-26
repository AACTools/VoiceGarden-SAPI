# VoiceGarden-SAPI

> This project was forked from [NaturalVoiceSAPIAdapter](https://github.com/gexgd0419/NaturalVoiceSAPIAdapter) and is now developed at [AACTools/VoiceGarden-SAPI](https://github.com/AACTools/VoiceGarden-SAPI).

A [SAPI 5 text-to-speech (TTS) engine][1] that connects **20+ cloud TTS engines** and **offline SherpaOnnx models** to any Windows application that supports SAPI voices — including Grid 3, The Grid, Clicker, and any software using `System.Speech`.

## What voices are supported?

| Category | Engines | Cloud? |
|----------|---------|--------|
| **Offline neural** | SherpaOnnx (Kokoro, Piper, MMS, VITS, Matcha) | No — fully local |
| **Microsoft** | Azure Cognitive Services, Edge browser voices | Yes |
| **Cloud TTS** | OpenAI, Google Cloud, AWS Polly, ElevenLabs, Cartesia, Deepgram | Yes |
| **More cloud** | Watson, PlayHT, Wit.ai, Gemini, Hume AI, xAI Grok, Fish Audio, Mistral, Murf, Unreal Speech, Resemble, Uplift AI, Models Lab | Yes |

Any SAPI 5-compatible program can use these voices. Offline SherpaOnnx voices require no internet connection.

## Quick Start

### Install
1. Download the MSI from the [Releases][6] section.
2. Run `setup.exe` (or the `.msi` directly).
3. After installation, **VoiceGarden.UI.exe** launches automatically — this is the configuration app.
4. Go to the **SherpaOnnx** tab to download offline models, or the **Engine Config** tab to enter cloud API keys.
5. Click **Install to SAPI** to promote voices to the Windows registry.
6. Restart your SAPI application (e.g., Grid 3) — the new voices appear in the voice list.

### Register the adapter (if running from a build)
```
VoiceGarden.UI.exe install      # registers 64-bit DLL
VoiceGarden.UI.exe install32    # registers 32-bit DLL (for 32-bit apps)
```

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

- **OS:** Windows 7 SP1 or later (32-bit and 64-bit supported)
- **Runtime:** .NET 8.0 desktop runtime (for VoiceGarden.UI)
- **For SherpaOnnx extraction:** [7-Zip](https://www.7-zip.org/) (for `.tar.bz2` model archives)
- **For cloud voices:** Internet access + valid API key for the respective service

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│  SAPI Application (Grid 3, System.Speech, etc.)          │
│                    │ SAPI 5 COM                          │
│  ┌─────────────────▼──────────────────────────────────┐  │
│  │  VoiceGardenSAPIAdapter.dll (C++ COM DLL)          │  │
│  │  ┌──────────────┐  ┌────────────┐  ┌────────────┐ │  │
│  │  │ SherpaOnnx   │  │ Azure REST │  │GenericHttp │ │  │
│  │  │ (offline)    │  │ + SDK shim │  │(OpenAI etc)│ │  │
│  │  └──────────────┘  └────────────┘  └────────────┘ │  │
│  └────────────────────────────────────────────────────┘  │
│                                                           │
│  ┌─────────────────────────────────────────────────────┐ │
│  │  VoiceGarden.UI.exe (Avalonia .NET 8 app)           │ │
│  │  • Download/promote SherpaOnnx models               │ │
│  │  • Configure cloud engine credentials               │ │
│  │  • Register/unregister 32-bit + 64-bit DLLs         │ │
│  │  • Preview voices                                    │ │
│  └─────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
```

### Key Components

| Component | Description |
|-----------|-------------|
| `VoiceGardenSAPIAdapter/` | C++ SAPI COM DLL — the actual TTS engine. Handles SherpaOnnx (offline), Azure REST/SDK, and generic HTTP TTS |
| `VoiceGarden.UI/` | Avalonia UI app — main configuration tool. Replaces the old C++ Installer |
| `VoiceGardenSAPIAdapter.Net/` | .NET SAPI adapter (alternative runtime, currently inactive) |
| `SherpaOnnxConfig/` | CLI tool for SherpaOnnx model management |
| `EngineConfig/` | CLI tool for cloud engine voice management |
| `Setup/` + `SetupLauncher/` | WiX MSI package + setup.exe bootstrapper |

## SherpaOnnx Offline Voices

SherpaOnnx provides fully offline neural TTS — no internet, no API keys, no cloud dependency.

### Supported model types
- **Kokoro** — high-quality English models with multiple voices (`voices.bin`)
- **Piper** — fast, lightweight VITS models (many languages)
- **MMS** — Meta's Massively Multilingual Speech (1000+ languages)
- **VITS** — standard VITS models
- **Matcha** — matcha-TTS models with vocoder

### Downloading models
Models are stored in `%LOCALAPPDATA%\VoiceGardenSAPIAdapter\models\`. Download via:
- **UI:** SherpaOnnx tab → select models → Download Selected
- **CLI:** `VoiceGarden.UI.exe models download kokoro-en-en-19`

### Model type detection
The system auto-detects model type from directory contents:
- `voices.bin` present → Kokoro (type 2)
- `vocoder.onnx` present → Matcha (type 1)
- Otherwise → VITS (type 0)

### Grid 3 compatibility
Grid 3 (`System.Speech`) only selects voices from HKLM registry tokens — in-memory enumerator voices won't work with `SelectVoice`. Use **Install to SAPI** to promote voices to HKLM.

See `docs/troubleshooting-grid3-voice-activation.md` for detailed Grid 3 troubleshooting.

## Cloud Engine Configuration

### Azure Cognitive Services
1. Get a key from [Azure Portal](https://portal.azure.com/) → Speech service → Keys and Endpoint
2. In VoiceGarden.UI → Engine Config → Azure: enter key and region (e.g., `uksouth`)
3. Promote voices to SAPI

### Google Cloud TTS
1. Get an API key from [Google Cloud Console](https://console.cloud.google.com/) → APIs & Services → Credentials
2. In VoiceGarden.UI → Engine Config → Google: enter key
3. Promote a specific voice (e.g., `en-US-Wavenet-D`)

### Other engines (OpenAI, ElevenLabs, Polly, etc.)
Each cloud engine needs its API key set in the Engine Config tab. The C++ adapter handles REST calls via `GenericHttpTts` for OpenAI, ElevenLabs, Google, Cartesia, and Deepgram. Azure and Edge voices use the dedicated REST/WebSocket path.

## Testing

### End-to-end test script
```powershell
.\scripts\test-sherpa-e2e.ps1    # downloads models, promotes, speaks via System.Speech
```

### Cleanup (remove all custom voices)
```powershell
.\scripts\cleanup-voices.ps1            # removes Sherpa/Cloud/eSpeak tokens
.\scripts\cleanup-voices.ps1 -DryRun    # preview without deleting
```

### Manual test via SAPI COM
```powershell
Add-Type -AssemblyName "System.Speech"
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$s.SelectVoice("kokoro-en-en-19")       # or any voice name
$s.Speak("Hello, this is a test.")
```

## Building

### Prerequisites
- Visual Studio 2022 with C++ (MSVC v143)
- .NET 8.0 SDK
- [WiX Toolset v3](https://wixtoolset.org/) (for MSI)
- [7-Zip](https://www.7-zip.org/) (for SherpaOnnx deps extraction)

### Local build
```powershell
.\download-sherpa-deps.ps1 -Platforms x64,x86     # one-time: download SherpaOnnx native DLLs
.\scripts\build-release-local.ps1 -Configuration Release -Platforms x64,x86 -BuildSetup -SkipSherpaDeps -SkipSubmodules
```

Output: `installer-output\VoiceGardenSAPIAdapter.msi`

### CI
GitHub Actions workflow at `.github/workflows/msbuild.yml` builds all components on every push:
- C++ adapter DLL (x86, x64, ARM64)
- VoiceGarden.UI (Avalonia app)
- SherpaOnnxConfig + EngineConfig CLI tools
- .NET adapter
- MSI setup package

## Configurable Registry Values

See the [wiki page on configurable registry values][8] for advanced settings including:
- `NoEdgeVoices`, `NoAzureVoices`, `NoSherpaVoices` — toggle voice categories
- `AzureVoiceKey`, `AzureVoiceRegion` — Azure credentials
- `EdgeVoiceLanguages` — filter Edge voices by language
- `ErrorMode` — control error handling behavior

## Libraries Used

- [SherpaOnnx](https://github.com/k2-fsa/sherpa-onnx) — offline neural TTS
- [DotNetTtsWrapper](https://github.com/AACTools/dotnet-tts-wrapper) — .NET TTS client for 20+ engines
- [Avalonia UI](https://avaloniaui.net/) — cross-platform .NET UI framework
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM framework
- Microsoft.CognitiveServices.Speech — Azure Speech SDK
- [websocketpp](https://github.com/zaphoyd/websocketpp) — WebSocket client (Edge voices)
- OpenSSL — HTTPS for cloud TTS
- [nlohmann/json](https://github.com/nlohmann/json) — JSON parsing
- [spdlog](https://github.com/gabime/spdlog) — logging
- [YY-Thunks](https://github.com/Chuyu-Team/YY-Thunks) — Windows XP compatibility
- [Detours](https://github.com/microsoft/Detours) — API hooking

[1]: https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ms717037(v=vs.85)
[6]: ../../releases
[8]: ../../wiki/Configurable-registry-values
