## What's New in v0.3.4

### Installation Fixes
- **setup.exe now requests elevation via UAC** — fixes error 1625 ("installation forbidden by system policy") on devices where Windows Installer requires admin
- **No more 7-Zip dependency** — model extraction now uses built-in SharpCompress library. Works on locked-down devices (Grid Pads) without needing 7-Zip installed
- **merged_models.json search paths fixed** — resolves "No Download URL" error when running from an MSI install

### Voice Engine Fixes
- **Azure voice preview** — fixed silent preview (Azure returns MP3, SoundPlayer needs WAV; now uses SynthToFileAsync with WAV format)
- **Kokoro model type detection** — voices.bin presence now correctly sets SherpaOnnxModelType=2 (Kokoro) in registry tokens
- **MMS models** — download individual files from HuggingFace directories instead of trying to fetch a directory URL (was 404)
- **Download progress** — streaming download with percentage, file size (MB), and throttled UI updates
- **SherpaOnnx model promotion** — uses .reg file import for fast, reliable elevation instead of relaunching the 116MB exe

### SAPI Compatibility
- **32-bit registration** — correctly checks WOW6432Node registry hive and uses SysWOW64\regsvr32.exe
- **Grid 3 / System.Speech** — model type-aware HKLM token promotion with SherpaOnnxVoices for Kokoro models
- **HKCU voice cleanup** — cleanup-voices.ps1 now covers HKLM, WOW6432Node, AND HKCU voice tokens

### CI / Build
- **Full CI pipeline** — all 12 jobs green (C++ DLL, VoiceGarden.UI, CLI tools, .NET adapter, MSI)
- **Release assets** — MSI, setup.exe, per-platform ZIPs (x86/x64), debug symbols, full release layout
- **DotNetTtsWrapper from NuGet.org** — no longer clones from source during CI
