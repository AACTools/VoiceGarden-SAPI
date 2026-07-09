# Privacy Policy

**VoiceGarden-SAPI** is an open-source, non-profit project.

## Analytics

VoiceGarden.UI can send anonymous usage analytics to help us understand which TTS engines and features are used. This is **opt-in** — disabled by default.

### What we collect

- App launch events
- Which TTS engine types are enabled/disabled (e.g., "azure", "google", "sherpaonnx")
- Number of models downloaded or voices promoted to SAPI
- Whether the adapter DLL was registered (32-bit or 64-bit)
- App version
- Anonymous random ID (generated locally, not linked to any account)

### What we do NOT collect

- ❌ Text spoken by the user
- ❌ API keys or credentials
- ❌ Voice names (only engine type + count)
- ❌ Personal information (name, email, IP is not stored)
- ❌ Audio content
- ❌ File system paths

### How to control it

- **Enable/disable**: Advanced section in VoiceGarden.UI → "Send anonymous usage analytics"
- **Stored setting**: `HKCU\SOFTWARE\VoiceGardenSAPIAdapter\AnalyticsEnabled` (DWORD)
- **Anonymous ID**: `HKCU\SOFTWARE\VoiceGardenSAPIAdapter\AnalyticsId` (random UUID, can be deleted to reset)

### Data processor

Analytics are processed by [PostHog](https://posthog.com) (EU-hosted, GDPR compliant).

### Open source

The analytics code is in `VoiceGarden.UI/Services/AnalyticsService.cs` — fully auditable. You can verify exactly what is sent.
