# Release Notes

## What's New in v0.6.0

The complete UX overhaul: a new 3-tab guided flow, real-language SAPI tokens with alias support, the sherpa-onnx registry v2026-08-18, and a much smaller, faster installer.

### New 3-Tab Accessible UI
- **Voice Engines tab** — one selectable catalogue of every available engine (SherpaOnnx offline, Edge free cloud, 19 credentialed cloud engines) with online/offline, key-needed and language filters, search, and chunky 64px check rows
- **Credentials tab** — forms only for selected engines that need keys, each with a Verify button; the tab disables itself with a "not needed" hint when you pick only offline/Edge engines
- **Voices tab** — all voices from your selected engines aggregated in one list with engine/language/gender/quality filters, search, per-voice Preview / Download / Install, and bulk install to SAPI; includes a "Show installed voices only" filter
- **Advanced tab** — adapter registration, SAPI alias settings, offline model folder, analytics, log level and about
- **Accessibility throughout** — 44px+ touch targets, visible focus indicators, full keyboard navigation, screen-reader labels and live regions, wrapping filter rows (no more clipped/overlapping layouts)
- Onboarding walkthrough rewritten to match the new flow; onboarding v2 re-shows once for existing users

### Real-Language SAPI Tokens & Aliases
- Promoted voices now register with their **real language** instead of a hard-coded en-US (SapiLanguage maps catalog codes, ISO 639-3, piper region tags and English names to SAPI LANGIDs)
- **"Add an en-US alias for non-English voices"** (Advanced, default on) — a `…-enUS` token so English-only AAC apps see every voice
- **"Also alias right-to-left voices under Arabic"** (default on) — a `…-ar` (ar-SA) token for RTL languages: Urdu, Arabic, Hebrew, Persian, Pashto…
- Aliases rebuild on every install; uninstall removes the whole token family

### RustTtsWrapper 0.3.20 & Sherpa Registry v2026-08-18
- SherpaOnnx catalog 1399 → **1760 models** (zipvoice + pocket zero-shot cloning, fp32/int8/fp16 piper variants, language-tagged IDs)
- Zipvoice downloads auto-fetch the non-bundled vocos vocoder
- Quantization badges (int8/fp16/…) in the voice lists; tooltips carry licence, size and quality details
- **242-entry legacy-ID migration map** (C# + adapter) — installed models auto-rename through both the 2026-08-10 and 2026-08-18 registry canonicalisations

### Reliability Fixes
- **Installer was a silent no-op** — every local build shipped ProductVersion 0.5.0.0, so upgrades never replaced files. MSI is now versioned and the upgrade removes the old product before installing the new one
- **setup.exe now closes a running app** — prompts (or auto-closes in silent mode) instead of failing on locked files; a windowless zombie instance no longer makes the app unlaunchable
- **Download race fixed** — model downloads stage as `.part` and extract under an archive lock; rescans can no longer collide with downloads into "file in use" errors
- **Post-install freeze fixed** — promote/preview no longer run locked scans on the UI thread (a deadlock after installing a voice)
- Voices with duplicate display names show their model ID; errors appear in a top banner with a human-readable message and technical detail line

### Slimmer Install & Dead Code Removal
- **Azure Speech SDK chain dropped** — local Narrator/natural voices were removed for TOS reasons, so the SDK DLLs, SpeechSDKShim/Patcher and the entire dead C++ path (EnumLocalVoices, delay-load hook, ~280 lines + AzacException) are gone; the adapter now compiles with zero Azure SDK dependency
- **SherpaOnnxConfig.exe no longer ships** — VoiceGarden.UI replaced it; installer shrinks ~137 MB → ~73 MB
- CLI credential handling de-duplicated onto the shared TtsCredentialBuilder (CLI subcommands unchanged)

### Localization
- All new UI strings translated into all **36 languages**; every resource file carries the same 181 keys and `{0}`/`{1}` placeholders verified intact

---

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
