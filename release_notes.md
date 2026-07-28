## What's New in v0.5.8

### RustTtsWrapper 0.3.16
- **Online model voice support** — OpenAI, Google, and other cloud engines now support voice listing (`GetVoices`), previewing, and promotion to SAPI
- All 22 engines confirmed working with dynamic credential discovery

### Dynamic Engine Discovery
- **No more hardcoded engine lists** — `EngineDefinition.DiscoverAll()` calls `TtsClient.ListEngines()` to discover all available engines with their credential requirements at runtime
- Adding a new engine in rust-tts-wrapper automatically makes it appear in VoiceGarden UI
- Credentials built dynamically from each engine's `credential_keys` — no engine-specific switch statements

### Credential Management Rework
- **Single source of truth** — API keys entered only in Configure Credentials (removed from Configure Voices)
- **Verify button** — each provider has a Verify button that tests credentials and shows ✓/✗ with voice count
- **Configure Voices reads from registry** — no duplicate key entry; dropdown shows only enabled providers

### Screen Reader Accessibility
- **Live regions** — status updates, download progress, and verification results announced automatically via `LiveSetting=Polite`
- **View change announcements** — switching between Configure Voices, Configure Credentials, etc. is announced
- **Onboarding page announcements** — "Page 2 of 3: Getting Started" announced on navigation
- **Password field descriptions** — HelpText on all API key fields ("Enter your Azure API key")
- **Control naming** — all buttons, checkboxes, textboxes have descriptive accessible names
- **ListBox items** — model/voice items describe name, language, and status

### Manage Models Crash Fix
- **Async catalog loading** — 1300+ model catalog now loads off the UI thread with `Parallel.ForEach`, preventing UI freeze and crash

### Single Instance Guard
- **Mutex prevents duplicate instances** — launching a second VoiceGarden.UI brings the existing window to the foreground

### Avalonia 12.1.0
- Upgraded from 11.2.7 to 12.1.0 — compiled bindings, performance, accessibility improvements
- Removed deprecated `Avalonia.Diagnostics` package

### CI Fixes
- **Stable download URL** — `https://github.com/AACTools/VoiceGarden-SAPI/releases/latest/download/VoiceGarden-release-layout.zip`

---

## What's New in v0.5.2

### Grid3 & System.Speech Compatibility
- **Cloud voices only via registry promotion** — Azure/Edge voices are no longer enumerated dynamically. Only voices promoted via "Install Selected" (HKLM registry tokens) appear in SAPI
- **Voice promotion fixed** — `PromoteElevated` now uses `reg.exe import` with UAC elevation
- **Voice enumerator deadlock fixed** — double mutex lock caused crash on first enumeration
- **No more voice flood** — only promoted voices appear in SAPI

### SSML & Speech Markdown via Web Speech API
- **PUA sentinel** — embed SSML or Speech Markdown via Unicode Private Use Area characters
- **SSML normalization** — adapter injects required `<speak>` attributes when missing
- **Speech Markdown syntax** — use `(text)[rate:"slow"]` format

### Chrome & Edge Support
- **Speech_OneCore registration** — voices appear in Chrome and Edge when promoted

### RustTtsWrapper 0.3.14
- SSML pass-through, Speech Markdown converter, voice selection, GetVoices crash all fixed

### MMS Model Data Fix
- 1143 MMS models now searchable with proper language names

### Localization (36 Languages)
- Full UI translation, automatic OS language detection, RTL support

### Onboarding & Installer
- 3-page wizard, version-gated, installer error fixes

---

## What's New in v0.4.0

### Major Architecture Changes
- **RustTtsWrapper Integration** — complete transition to Rust-based TTS wrapper
- Removed legacy DotNetTtsWrapper and EngineConfig components

### Breaking Changes
- **Removed Components** — DotNetTtsWrapper and EngineConfig no longer supported
- **Minimum Requirements** — Windows 10+
