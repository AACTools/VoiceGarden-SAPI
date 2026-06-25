# VoiceGarden UI Redesign Plan — Avalonia

## Executive Summary

Replace the C++ Installer.exe, SherpaOnnxConfig.exe, and EngineConfig.exe with a single Avalonia UI application (`VoiceGarden.UI.exe`). The C++ SAPI adapter DLL (`NaturalVoiceSAPIAdapter.dll`) stays unchanged. The WiX MSI continues to deploy files. The Avalonia app handles all configuration, model management, and voice promotion.

## Goals

1. **Clean, simple UI** — one window, logical sections, no nested dialogs
2. **Hide complexity** — Edge/Narrator voices hidden by default, exposed via branding.json
3. **Localization-ready** — .resx files, no hardcoded strings
4. **CLI/programmatic** — headless mode for scripted installs
5. **No regressions** — preserve all working behavior (SherpaOnnx synthesis, Azure REST, Grid3/HKLM compatibility)

## Architecture

```
VoiceGarden.UI.exe (Avalonia, self-contained, single-file)
├── References DotNetTtsWrapper NuGet (voice listing, validation, synthesis test)
├── Calls regsvr32 for COM registration (elevated via Process.Start)
├── Reads/writes HKCU\SOFTWARE\NaturalVoiceSAPIAdapter\* registry
├── Promotes voices to HKLM\SOFTWARE\Microsoft\Speech\Voices\Tokens\* (elevated)
├── Manages SherpaOnnx models (downloads, scans, promotes)
└── Saves config to registry + optional JSON export/import

Files deployed:
├── NaturalVoiceSAPIAdapter.dll (+ deps)     ← C++ SAPI adapter (unchanged)
├── NaturalVoiceSAPIAdapter.Net.*            ← .NET adapter (unchanged)
├── VoiceGarden.UI.exe (+ deps)              ← NEW: replaces Installer.exe + SherpaOnnxConfig + EngineConfig
├── merged_models.json                       ← SherpaOnnx catalog
└── branding.json                            ← UI feature flags
```

## Critical Compatibility Requirements (DO NOT REGRESS)

These are hard-won fixes that must be preserved:

### 1. DLL Version Preservation
- The C++ adapter (compiled against SherpaOnnx v1.12.23) MUST NOT have its native DLLs overwritten by .NET/DotNetTtsWrapper (v1.13.2)
- Build script already handles this; the Avalonia app doesn't deploy native DLLs

### 2. HKLM Token Promotion
- SAPI apps using `System.Speech` (Grid3, Narrator) can ONLY select registry-backed HKLM tokens
- In-memory tokens from the C++ VoiceTokenEnumerator appear in GetInstalledVoices() but SelectVoice() FAILS for them
- SherpaOnnx and cloud voices MUST be promoted to HKLM via SherpaOnnxConfig or EngineConfig promote

### 3. Duplicate Voice Prevention
- The C++ VoiceTokenEnumerator skips SherpaOnnx voices already registered in HKLM
- If both HKLM tokens AND in-memory enumerator tokens exist, System.Speech.SelectVoice fails silently
- The `NoSherpaVoices` registry flag disables enumerator SherpaOnnx scanning entirely
- **New recommendation**: Set `NoSherpaVoices=1` when using HKLM promotion as the standard flow

### 4. SherpaOnnx Voice Token Format
Token at `HKLM\SOFTWARE\Microsoft\Speech\Voices\Tokens\Sherpa-<modelId>`:
```
(Default) = "Sherpa <name>"
CLSID = {013AB33B-AD1A-401C-8BEE-F6E2B046A94E}
Attributes\Name = <short name for SelectVoice>
Attributes\Language = <hex lang ID, e.g. "409">
Attributes\Locale = <BCP-47, e.g. "en-US">
Attributes\Vendor = K2FSA
NaturalVoiceConfig\EngineType = Sherpa
NaturalVoiceConfig\SherpaOnnxModelType = <0=Vits, 1=Matcha, 2=Kokoro>
NaturalVoiceConfig\SherpaOnnxModelPath = <full path to .onnx file>
NaturalVoiceConfig\SherpaOnnxTokens = <full path to tokens.txt>
NaturalVoiceConfig\SherpaOnnxDataDir = <path to espeak-ng-data, if present>
```

### 5. Cloud Engine Token Format
Token at `HKLM\SOFTWARE\Microsoft\Speech\Voices\Tokens\Cloud-<engine>-<voiceId>`:
```
(Default) = "<engine> <voiceId>"
CLSID = {013AB33B-AD1A-401C-8BEE-F6E2B046A94E}
Attributes\Name = <voice name>
Attributes\Language = 409
Attributes\Locale = <locale>
Attributes\Vendor = <EngineName>
NaturalVoiceConfig\EngineType = <OpenAI|ElevenLabs|Google|Cartesia|DeepGram>
NaturalVoiceConfig\Voice = <voice ID>
NaturalVoiceConfig\Key = <API key>
NaturalVoiceConfig\Region = <region, if applicable>
```

### 6. Azure Key Backward Compatibility
- Azure keys must also be saved to `HKCU\SOFTWARE\NaturalVoiceSAPIAdapter\Enumerator\AzureVoiceKey` and `AzureVoiceRegion`
- The C++ VoiceTokenEnumerator reads these to enumerate Azure voices via WebSocket
- EngineConfig promote already handles this; the Avalonia app must do the same

### 7. C++ Adapter Init Flow
```
TTSEngine::SetObjectToken()
  → InitVoice()
    → InitSherpaOnnxVoice()     (checks SherpaOnnxModelType/SherpaOnnxModelPath)
    → InitLocalVoice()          (Azure Speech SDK, checks "Path")
    → InitCloudVoiceSynthesizer() (Azure SDK, checks "Key"+"Region"+"Voice")
    → InitCloudVoiceRestAPI()   (Azure/Edge REST, checks "Voice"+"Key"+"Region")
    → InitGenericHttpVoice()    (OpenAI/ElevenLabs/etc., checks "EngineType"+"Voice"+"Key")
    → throw "Invalid NaturalVoiceConfig"
```
**Gotcha**: `GetStringValue(L"SherpaOnnxModelPath", nullptr)` throws STG_E_INVALIDPOINTER on registry-backed tokens. Must use a valid buffer. Already fixed on the branch.

### 8. Branding Configuration
`config/branding.json` (deployed alongside exe):
```json
{
  "appName": "VoiceGarden",
  "installDir": "VoiceGardenSAPI",
  "showEdgeVoices": false,
  "showNarratorVoices": false,
  "showAdvancedSection": false,
  "defaultSherpaEnabled": true,
  "defaultAzureEnabled": false
}
```

## UI Layout (Single Window, No Nested Dialogs)

### Main Window

```
┌─ VoiceGarden ─────────────────────────────────────────┐
│                                                        │
│  ┌─ Adapter Installation ──────────────────────────┐  │
│  │  64-bit: ✓ Installed    [Uninstall]              │  │
│  │  32-bit: Not installed  [Install]                │  │
│  └──────────────────────────────────────────────────┘  │
│                                                        │
│  ┌─ Voice Engines ─────────────────────────────────┐  │
│  │                                                   │  │
│  │  ┌─ Offline ─────────────────────────────────┐  │  │
│  │  │ ☑ SherpaOnnx Neural TTS                   │  │  │
│  │  │   📦 3 models installed                   │  │  │
│  │  │   [Manage Models...]                      │  │  │
│  │  └───────────────────────────────────────────┘  │  │
│  │                                                   │  │
│  │  ┌─ Cloud ───────────────────────────────────┐  │  │
│  │  │ ☑ Azure    🔑 ••••••  🌍 uksouth          │  │  │
│  │  │ ☐ OpenAI   🔑 ___________                  │  │  │
│  │  │ ☐ Google   🔑 ___________                  │  │  │
│  │  │ ☐ Polly    🔑 ___ / ___ 🌍 us-east-1       │  │  │
│  │  │ ☐ ElevenLabs 🔑 ___________                │  │  │
│  │  │ ☐ Cartesia 🔑 ___________                  │  │  │
│  │  │ ☐ Deepgram 🔑 ___________                  │  │  │
│  │  │                                           │  │  │
│  │  │ [Configure Voices...]                     │  │  │
│  │  └───────────────────────────────────────────┘  │  │
│  │                                                   │  │
│  └──────────────────────────────────────────────────┘  │
│                                                        │
│  ▶ Advanced                                            │
│  ┌──────────────────────────────────────────────────┐  │
│  │ ☐ Edge browser voices                            │  │
│  │ ☐ Windows Narrator voices                        │  │
│  │ ☐ Show raw token registry                        │  │
│  │ Log level: [Normal ▼]    [Open log folder]       │  │
│  └──────────────────────────────────────────────────┘  │
│                                                        │
│  [About]                                    [Close]    │
└────────────────────────────────────────────────────────┘
```

### Voice Configuration Panel (Slide-in or Tab)

When user clicks "Configure Voices...", a panel slides in or replaces the main content:

```
┌─ Configure Voices ─────────────────────────────────────┐
│                                                          │
│  ← Back to Main                                          │
│                                                          │
│  Engine: [Azure ▼]     🔍 Search: [en-US ________]      │
│  [✓ Validate Key]                                        │
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │ ☑ Jenny (en-US)        Female   ✓ Installed      │   │
│  │ ☑ Davis (en-US)        Male     ✓ Installed      │   │
│  │ ☐ Aria (en-US)         Female   Not installed    │   │
│  │ ☐ Sonia (en-GB)        Female   Not installed    │   │
│  │ ☐ Adri (af-ZA)         Female   Not installed    │   │
│  │ ... (scrollable, virtualized for 500+ items)     │   │
│  └──────────────────────────────────────────────────┘   │
│                                                          │
│  [Select All] [Select None]    [Install Selected →]      │
│  [Uninstall Selected]                                    │
│                                                          │
│  Status: 2 selected, 2 installed, 556 total              │
└────────────────────────────────────────────────────────┘
```

### SherpaOnnx Model Manager (Same slide-in pattern)

```
┌─ SherpaOnnx Models ────────────────────────────────────┐
│                                                          │
│  ← Back to Main                                          │
│                                                          │
│  Language: [All ▼]   🔍 Filter: [__________] [Refresh]   │
│  ☑ Show installed only                                   │
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │ Kokoro English (19 voices)    63MB   ✓ Installed │   │
│  │ Piper Amy (Low Quality)       64MB   ✓ Installed │   │
│  │ MMS Armenian (hyw)            114MB  ✓ Installed │   │
│  │ Piper Alan (Low Quality)      64MB   Not installed│   │
│  │ ...                                               │   │
│  └──────────────────────────────────────────────────┘   │
│                                                          │
│  [Download Selected]  [Rescan]                           │
│  [Install Selected to SAPI →]                            │
│  [Open Models Folder]                                    │
│                                                          │
│  Log output:                                             │
│  ┌──────────────────────────────────────────────────┐   │
│  │ Downloaded piper-en-alan-low...                  │   │
│  │ Installed piper-en-alan-low to HKLM...           │   │
│  └──────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────┘
```

## Project Structure

```
VoiceGarden.UI/
├── VoiceGarden.UI.csproj          # Avalonia app, self-contained, single-file
├── Program.cs                     # Entry point, CLI dispatch, Avalonia launch
├── App.axaml                      # Styles, themes, resources
├── ViewModels/
│   ├── MainViewModel.cs           # Adapter install/uninstall, engine checkboxes
│   ├── VoiceConfigViewModel.cs    # Voice listing, selection, promotion
│   ├── SherpaModelsViewModel.cs   # Model catalog, download, promote
│   └── ViewModelBase.cs           # INotifyPropertyChanged, helpers
├── Views/
│   ├── MainWindow.axaml           # Main window layout
│   ├── VoiceConfigView.axaml      # Voice configuration panel
│   ├── SherpaModelsView.axaml     # SherpaOnnx model manager panel
│   └── Controls/
│       ├── EngineRow.axaml        # Reusable: checkbox + key field per engine
│       └── VoiceListItem.axaml    # Reusable: voice row in list
├── Models/
│   ├── EngineConfig.cs            # Engine definition, credentials, enable state
│   ├── VoiceToken.cs              # SAPI voice token representation
│   ├── SherpaModel.cs             # SherpaOnnx model catalog entry
│   └── BrandingConfig.cs          # branding.json model
├── Services/
│   ├── ComRegistrationService.cs  # regsvr32 calls, elevated process
│   ├── RegistryService.cs         # HKCU/HKLM read/write
│   ├── VoicePromotionService.cs   # HKLM token creation (elevated)
│   ├── SherpaModelService.cs      # Download, scan, promote models
│   └── BrandingService.cs         # Load branding.json feature flags
├── Assets/
│   ├── Icons/                     # Standard icons (speaker, cloud, download, etc.)
│   └── merged_models.json         # SherpaOnnx catalog (embedded or sidecar)
├── Resources/
│   ├── Strings.resx               # English (default)
│   └── Strings.fr.resx            # French (future localization)
└── Localization/
    └── LocalizationExtension.cs   # Avalonia MarkupExtension for .resx
```

## CLI Interface

```
VoiceGarden.UI.exe [command] [options]

Commands:
  (none)                    Launch GUI (default)
  install                   Install COM adapter
    --platform x64|x86|all
  uninstall                 Uninstall COM adapter
    --platform x64|x86|all
  engines                   List enabled engines
  voices                    List/promote voices
    --engine <id>
    --key <key>
    --region <region>
    --promote <voice-id>    Promote to HKLM
    --json                  Output as JSON
  models                    SherpaOnnx model management
    list [--language <lang>]
    download <model-id>
    promote-all
    rescan
  config                    Import/export configuration
    --import <file.json>
    --export <file.json>
  validate                  Validate credentials
    --engine <id> --key <key> [--region <region>]

Examples:
  VoiceGarden.UI.exe
  VoiceGarden.UI.exe install --platform all
  VoiceGarden.UI.exe voices --engine azure --key X --region Y --promote en-US-JennyNeural
  VoiceGarden.UI.exe models download kokoro-en-en-19
  VoiceGarden.UI.exe models promote-all
  VoiceGarden.UI.exe config --import engines.json
```

## Implementation Phases

### Phase 1: Core Shell (Week 1)
- Create Avalonia project
- Main window with adapter install/uninstall
- Engine checkboxes (save to registry)
- CLI dispatch (install/uninstall commands)
- Branding.json loading
- Basic styling (Fluent theme, dark/light)

### Phase 2: Voice Configuration (Week 2)
- Engine key input fields
- VoiceConfigViewModel with DotNetTtsWrapper integration
- Voice list with search/filter (DataGrid or ListBox with virtualization)
- Validate key button
- Promote voices to HKLM (elevated Process.Start)
- CLI: voices, validate commands

### Phase 3: SherpaOnnx Integration (Week 3)
- Port SherpaOnnxConfig logic into SherpaModelService
- Model catalog loading (merged_models.json)
- Model download (with progress)
- Model scanning/validation
- promote-all command
- CLI: models commands

### Phase 4: Polish (Week 4)
- Localization framework (.resx)
- Error handling (bad keys, network issues)
- Help text/tooltips
- Advanced section (Edge/Narrator toggles, log level)
- Config import/export
- CI pipeline integration
- Replace Installer.exe/SherpaOnnxConfig.exe/EngineConfig.exe in build

## Registry Schema (Consolidated)

### HKCU\SOFTWARE\NaturalVoiceSAPIAdapter\Enumerator
```
NoSherpaVoices = 0|1          (0 = SherpaOnnx enumerator enabled)
NoAzureVoices = 0|1           (0 = Azure enumerator enabled)
NoEdgeVoices = 0|1            (0 = Edge enumerator enabled)
NoOpenAIVoices = 0|1          (NEW: generic engine toggles)
NoElevenLabsVoices = 0|1
NoGoogleVoices = 0|1
NoPollyVoices = 0|1
NoCartesiaVoices = 0|1
NoDeepgramVoices = 0|1
AzureVoiceKey = <key>         (Azure REST/WebSocket auth)
AzureVoiceRegion = <region>   (Azure region)
LogLevel = 0|1|2              (Trace=2, Info=1, Normal=0)
```

### HKLM\SOFTWARE\Microsoft\Speech\Voices\Tokens\*
```
Sherpa-<modelId>              (SherpaOnnx voices, promoted by app)
Cloud-<engine>-<voiceId>      (Cloud engine voices, promoted by app)
```

### HKCU\SOFTWARE\NaturalVoiceSAPIAdapter\CloudEngines (NEW)
```
<EngineName>\ApiKey = <key>
<EngineName>\Region = <region>
<EngineName>\Enabled = 0|1
```

## Migration Strategy

1. **Phase 1**: Avalonia app coexists with existing tools. Build deploys both.
2. **Phase 2-3**: Avalonia app gains feature parity with SherpaOnnxConfig + EngineConfig.
3. **Phase 4**: Remove SherpaOnnxConfig.exe and EngineConfig.exe from payload. Only VoiceGarden.UI.exe remains.

Users with existing installs keep working — the registry format and HKLM tokens are unchanged.

## Build Pipeline Changes

### Current:
```
Step 3:   Build SherpaOnnxConfig
Step 3.5: Build EngineConfig
Step 4:   Build C++ Installer.exe + utilities
Step 5:   Build NaturalVoiceSAPIAdapter.dll
Step 5.5: Build .NET adapter
Step 7:   Compose payload
Step 8:   Build MSI
```

### Target:
```
Step 3:   Build VoiceGarden.UI (Avalonia, self-contained single-file)
Step 4:   Build C++ utilities (AzureSpeechSDKShim only)
Step 5:   Build NaturalVoiceSAPIAdapter.dll
Step 5.5: Build .NET adapter
Step 7:   Compose payload (VoiceGarden.UI.exe replaces Installer.exe + SherpaOnnxConfig + EngineConfig)
Step 8:   Build MSI
```

## Risk Assessment

| Risk | Mitigation |
|------|-----------|
| Avalonia self-contained too large | Single-file publish, LZMA compression, same as SherpaOnnxConfig |
| COM registration from .NET | Use Process.Start(regsvr32, runas) — same as current Installer.exe |
| Grid3 HKLM compatibility | Preserve exact token format, same CLSID, same registry paths |
| SherpaOnnx catalog logic | Port from SherpaOnnxConfig (C# already), reference merged_models.json |
| DotNetTtsWrapper version mismatch | Don't deploy native DLLs from DotNetTtsWrapper — only use it for API calls |
| Localization | Start with English, .resx files ready for translation |

## Non-Goals (Explicitly Out of Scope)

- Rewriting the C++ SAPI adapter (NaturalVoiceSAPIAdapter.dll)
- Changing the COM CLSIDs or token format
- Supporting non-Windows SAPI (the adapter is Windows-only)
- Building a SAPI voice synthesis engine in .NET (the .NET adapter comhost issue is unresolved)
- Replacing the WiX MSI build system
