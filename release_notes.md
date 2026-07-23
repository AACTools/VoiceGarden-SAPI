## What's New in v0.5.4

### MMS Model Sample Rate Fix
- **MMS models play at correct speed** — sample rate defaulted to 22050 Hz instead of 16000 Hz, causing wrong pitch/rate. Now defaults per model type: MMS=16000, Kokoro=24000, Vits/Piper=22050

### Stable Download URL
- **Permalink** — `https://github.com/AACTools/VoiceGarden-SAPI/releases/latest/download/VoiceGarden-release-layout.zip` always points to the latest release

---

## What's New in v0.5.2

### Grid3 & System.Speech Compatibility
- **Cloud voices only via registry promotion** — Azure/Edge voices are no longer enumerated dynamically. Only voices promoted via "Install Selected" (HKLM registry tokens) appear in SAPI. This fixes Grid3 and other `System.Speech` apps that can't activate in-memory tokens
- **Voice promotion fixed** — `PromoteElevated` was silently failing because EngineConfig.exe was removed. Now uses `reg.exe import` with UAC elevation, creating tokens in both legacy SAPI and Speech_OneCore paths
- **Voice enumerator deadlock fixed** — double mutex lock in `VoiceTokenEnumerator::EnumTokens` caused "resource deadlock would occur" crash on first enumeration or after 10s cache expiry
- **No more voice flood** — previously 500+ Azure voices flooded SAPI voice lists; now only promoted voices appear

### SSML & Speech Markdown via Web Speech API
- **PUA sentinel** — web apps using Chrome/Firefox Web Speech API can embed SSML or Speech Markdown via Unicode Private Use Area characters:
  - `\uE000\uE001\uE002` prefix = SSML mode
  - `\uE000\uE001\uE003` prefix = Speech Markdown mode
  - Other SAPI voices ignore the PUA characters harmlessly
- **SSML normalization** — adapter automatically injects required `<speak version="1.0" xmlns="..." xml:lang="...">` attributes when missing
- **Speech Markdown syntax** — use `(text)[rate:"slow"]` format (standard Speech Markdown spec)

### Chrome & Edge Support
- **Speech_OneCore registration** — VoiceGarden voices now appear in Chrome and Edge, not just legacy SAPI applications
- Voices registered in both `SOFTWARE\Microsoft\Speech\Voices\Tokens` (Firefox, Grid3, Balabolka) and `SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens` (Chrome, Edge) when promoted

### RustTtsWrapper 0.3.14
- **SSML pass-through fixed** — `tts_speak_ssml()` correctly detects and processes `<speak>` documents
- **Speech Markdown converter fixed** — produces valid SSML with required Azure attributes
- **Voice selection fixed** — `tts_set_voice()` correctly selects the requested Azure voice
- **GetVoices crash fixed** — cloud engine voice listing no longer crashes with access violation

### Voice Engine Fixes
- **Azure voice activation** — Azure voice tokens now include `EngineType` in `VoiceGardenConfig`
- **Cloud engines off by default** — paid cloud engines disabled on fresh install; SherpaOnnx (offline) and Edge (free) remain on

### MMS Model Data Fix
- **1143 MMS models now searchable** — language metadata was blank due to mismatched JSON field names. Kurdish, Aceh, Abidji, and 1100+ other languages now appear correctly

### Localization (36 Languages)
- **Full UI translation** — all user-facing strings in `Strings.resx`
- **36 language packs** — Arabic, Bengali, Chinese, Czech, Danish, Dutch, French, German, Greek, Hebrew, Hindi, Indonesian, Italian, Japanese, Korean, Malay, Persian, Polish, Portuguese, Punjabi, Russian, Spanish, Swahili, Swedish, Tamil, Telugu, Thai, Turkish, Ukrainian, Urdu, Vietnamese, and more
- **Automatic language detection** — app language follows OS locale
- **RTL support** — Arabic, Hebrew, Urdu, and Persian layouts mirror automatically

### Credential Management
- **Auto-save credentials** — API keys saved to registry immediately
- **Dual-screen sync** — credentials shared between Configure Credentials and Configure Voices

### Installer Fixes
- **Error 1721/1722** — `Return="ignore"` on `regsvr32` prevents install/uninstall failure
- **Upgrade ghost fix** — `IgnoreRemoveFailure` + `Schedule="afterInstallFinalize"` allows clean installs over broken previous versions

### Onboarding
- **3-page wizard** — Welcome, Getting Started, Privacy (analytics opt-in)
- **Version-gated** — bump `CurrentOnboardingVersion` to force all users to see updated content

---

## What's New in v0.4.0

### Major Architecture Changes
- **RustTtsWrapper Integration** — complete transition to Rust-based TTS wrapper
  - Removed legacy DotNetTtsWrapper and EngineConfig components
  - Full x86 and x64 support with native rust_tts_wrapper.dll

### Breaking Changes
- **Removed Components** — DotNetTtsWrapper and EngineConfig no longer supported
- **Minimum Requirements** — Windows 10+
