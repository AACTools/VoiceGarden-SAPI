## What's New in v0.4.0

### Major Architecture Changes
- **RustTtsWrapper Integration** — complete transition to Rust-based TTS wrapper (RustTtsWrapper.Bindings 0.3.8)
  - Removed legacy DotNetTtsWrapper and EngineConfig components
  - Full x86 and x64 support with native rust_tts_wrapper.dll
  - Improved FFI safety and thread safety in callbacks
  - Better memory management and COM reference counting

### Stability & Performance Fixes (Comprehensive Codebase Review)
- **Thread Safety Improvements** — fixed race conditions in cache enumeration and FFI callbacks
- **Memory Management** — fixed COM reference counting violations and event handler memory leaks
- **Performance Optimization** — reduced TTS pipeline logging overhead with conditional level checks
- **MSI Installation Reliability** — deferred COM registration for proper rollback and error handling
- **Async Pattern Fixes** — replaced async void methods with proper async Task patterns

### User Experience Improvements
- **Onboarding Wizard** — new 3-page welcome/onboarding flow (welcome, how-to, privacy)
  - Shows on fresh install and version bumps
  - Clear explanations of VoiceGarden's purpose and privacy practices
  - Analytics opt-in with proper consent
- **Application Icon** — professional icon for taskbar, title bar, and Start Menu
- **Version-Gated Onboarding** — onboarding re-appears on major version updates

### Platform Support
- **Full x86 Support** — complete 32-bit architecture support with rust_tts_wrapper.dll
- **Dual Architecture Installer** — both x86 and x64 versions in single MSI
- **Improved Platform Detection** — better handling of WOW6432Node registry hive

### Build System & CI/CD
- **Simplified Dependencies** — removed 7-Zip dependency, using built-in SharpCompress
- **Enhanced CI Pipeline** — all 12 jobs green, including C++ DLL, UI, CLI tools, and MSI
- **Release Automation** — MSI, setup.exe, per-platform ZIPs, and debug symbols
- **NuGet Package Management** — RustTtsWrapper.Bindings from NuGet.org instead of source builds

### Voice Engine Enhancements
- **Model Download Improvements** — streaming downloads with progress indication
- **SherpaOnnx Integration** — better model type detection and Kokoro model support
- **Azure Voice Preview** — fixed format conversion (MP3 to WAV) for audio playback
- **Voice Promotion** — improved registry token management and cleanup

### Technical Debt Cleanup
- **Code Quality** — addressed memory leaks, thread safety issues, and COM interface violations
- **Error Handling** — improved exception handling in async operations and COM calls
- **Resource Management** — proper IDisposable implementation in ViewModels
- **Logging Optimization** — conditional compilation to reduce performance overhead

### Breaking Changes
- **Removed Components** — DotNetTtsWrapper and EngineConfig no longer supported
- **Minimum Requirements** — Windows 10+ required (removed XP compatibility code paths)

### Migration Notes
- **Existing Installations** — seamless upgrade path with proper COM registration/unregistration
- **Configuration Migration** — existing registry settings preserved during upgrade
- **Model Downloads** — SherpaOnnx models automatically downloaded on first use