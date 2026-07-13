## What's New in v0.4.5

### Localization (36 Languages)
- **Full UI translation** — all user-facing strings extracted into `Strings.resx`
- **36 language packs** — Arabic, Bengali, Chinese (Simplified), Czech, Danish, Dutch, French, German, Greek, Hebrew, Hindi, Indonesian, Italian, Japanese, Korean, Malay, Marathi, Persian, Polish, Portuguese, Punjabi, Russian, Spanish, Swahedi, Swedish, Tamil, Telugu, Thai, Turkish, Ukrainian, Urdu, Vietnamese, and more
- **Automatic language detection** — app language follows OS locale (`CurrentUICulture`)
- **RTL support** — Arabic, Hebrew, Urdu, and Persian layouts mirror automatically
- **`{lang:Localize}` markup extension** for XAML + `Loc.GetString()` for ViewModels

### Voice Engine Fixes
- **RustTtsWrapper 0.3.12** — fixes `tts_set_voice()` selecting wrong Azure voice, `tts_speak_ssml()` for Azure, and `GetVoices()` crash for cloud engines
- **Azure voice activation fix** — Azure voice tokens now include `EngineType` in `VoiceGardenConfig`, fixing "Invalid VoiceGardenConfig configuration" errors
- **PUA sentinel for inline SSML** — web apps using Web Speech API can embed SSML via Unicode Private Use Area characters:
  - `\uE000\uE001\uE002` prefix = SSML mode
  - `\uE000\uE001\uE003` prefix = Speech Markdown mode
  - Other SAPI voices ignore the PUA characters harmlessly
- **Cloud engines off by default** — paid cloud engines (Azure, OpenAI, Google, etc.) disabled on fresh install; SherpaOnnx (offline) and Edge (free) remain on

### MMS Model Data Fix
- **1143 MMS models now searchable** — language metadata was blank due to mismatched JSON field names (`"Iso Code"` vs `"lang_code"`). Now all MMS models have proper language names, codes, and countries
- **Kurdish, Aceh, Abidji, and 1100+ other languages** now appear correctly in the model browser

### Credential Management
- **Auto-save credentials** — API keys entered in Configure Credentials or Configure Voices are saved to registry immediately
- **Dual-screen sync** — credentials entered in either screen are shared via the same registry keys
- **Azure legacy compatibility** — reads from both new (`{Engine}VoiceKey`) and legacy (`AzureVoiceKey`) registry paths

### Installer Fixes
- **Error 1721/1722 fix** — `Return="ignore"` on `regsvr32` custom actions prevents install/uninstall failure when DLL dependencies are missing
- **Upgrade ghost fix** — `IgnoreRemoveFailure="yes"` + `Schedule="afterInstallFinalize"` allows clean installs even when previous broken versions left stuck MSI registrations

### Onboarding
- **3-page wizard** — Welcome (logo + intro), Getting Started (install adapter, download models, language compatibility tip), Privacy (analytics opt-in)
- **Version-gated** — bump `CurrentOnboardingVersion` to force all users to see updated content
- **Analytics consent preserved** — PostHog analytics ID persists across reinstalls

---

## What's New in v0.4.0

### Major Architecture Changes
- **RustTtsWrapper Integration** — complete transition to Rust-based TTS wrapper
  - Removed legacy DotNetTtsWrapper and EngineConfig components
  - Full x86 and x64 support with native rust_tts_wrapper.dll
  - Improved FFI safety and thread safety in callbacks
  - Better memory management and COM reference counting

### Stability & Performance Fixes
- **Thread Safety** — fixed race conditions in cache enumeration and FFI callbacks
- **Memory Management** — fixed COM reference counting violations and event handler memory leaks
- **Performance** — reduced TTS pipeline logging overhead with conditional level checks
- **MSI Reliability** — proper COM registration with rollback and error handling
- **Async Patterns** — replaced async void methods with proper async Task patterns

### User Experience
- **Onboarding Wizard** — 3-page welcome flow with analytics opt-in
- **Application Icon** — professional icon for taskbar, title bar, and Start Menu
- **Version-Gated Onboarding** — onboarding re-appears on major version updates

### Platform Support
- **Full x86 Support** — 32-bit architecture with rust_tts_wrapper.dll
- **Dual Architecture Installer** — both x86 and x64 in single MSI

### Build System & CI/CD
- **Enhanced CI Pipeline** — all jobs green
- **Release Automation** — MSI, setup.exe, per-platform ZIPs, and debug symbols
- **NuGet Package Management** — RustTtsWrapper.Bindings from NuGet.org

### Breaking Changes
- **Removed Components** — DotNetTtsWrapper and EngineConfig no longer supported
- **Minimum Requirements** — Windows 10+
