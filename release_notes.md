## What's New

### VoiceGarden.UI — Avalonia Configuration App
- Replaced the old C++ Installer with a modern Avalonia UI app
- Download and manage SherpaOnnx offline models
- Configure 20+ cloud TTS engines (Azure, Google, OpenAI, ElevenLabs, Polly, and more)
- Register/unregister 32-bit and 64-bit SAPI adapter DLLs
- Preview voices before installing
- Full CLI mode for automation

### SherpaOnnx Offline TTS
- Supports Kokoro, Piper, MMS, VITS, and Matcha model types
- Auto-detection of model type from directory contents (voices.bin, vocoder.onnx)
- MMS models download individual files from HuggingFace directories
- Auto-extraction of orphaned .tar.bz2 archives on rescan
- Download progress with file size and percentage

### Cloud Engine Support
- Azure Cognitive Services (REST + Speech SDK)
- Edge browser voices (WebSocket)
- Google Cloud TTS, OpenAI, ElevenLabs, AWS Polly, Cartesia, Deepgram
- 15+ additional cloud engines via generic HTTP TTS

### SAPI Voice Management
- Model type-aware promotion to HKLM (Kokoro=type 2, Matcha=type 1, VITS=type 0)
- .reg file import for fast, reliable elevation (no slow exe relaunch)
- 32-bit and 64-bit COM registration (WOW6432Node aware)
- Grid 3 / System.Speech compatibility (HKLM token promotion)

### Build & CI
- Full GitHub Actions CI pipeline (12 jobs, all green)
- MSI setup + setup.exe bootstrapper
- Per-platform release ZIPs (x86, x64, ARM64)
