# Scripts

## Core scripts (keep)

- `build-all.ps1` (repo root): local build + packaging + verification flow.
- `scripts/sapi-probe.ps1`: SAPI probe for token visibility, module load paths, and `Speak` success.
  - Add `-Audible` for synchronous audible playback.
- `scripts/verify-sapi-registration.ps1`: checks COM/TokenEnums registration health.
- `scripts/verify-sherpa-integration.ps1`: quick Sherpa integration check used by local/CI flows.
- `scripts/run-sherpa-smoke-test.ps1`: compiles/runs native Sherpa smoke test.
- `scripts/clean-naturalvoice-state.ps1`: clean/reset local registry + app data state (self-elevating).
- `scripts/promote-hkcu-sherpa-tokens-to-hklm.ps1`: exports HKCU Sherpa tokens and imports them into HKLM (self-elevating).

## Diagnostic/reference scripts (optional)

- `scripts/run-sherpa-vanilla-amy-test.ps1`
- `scripts/sherpa-vanilla-amy-test.c`
- `scripts/sherpa-smoke-test.cpp`

These are useful for engine parity/debugging, but not required for normal install/use.

## Typical recovery sequence

```powershell
# 1) Clean state
.\scripts\clean-naturalvoice-state.ps1

# 2) Build + package
.\build-all.ps1 -Configuration Release -Platform x64

# 3) Register adapter (elevated shell)
& "$env:WINDIR\System32\regsvr32.exe" /s "C:\github\NaturalVoiceSAPIAdapter\out\NaturalVoiceSAPIAdapter.dll"

# 4) Sync model tokens
C:\github\NaturalVoiceSAPIAdapter\out\SherpaOnnxConfig.exe rescan

# 5) Probe
.\scripts\sapi-probe.ps1 -VoiceId piper-en-alan-low -TimeoutSeconds 20
.\scripts\sapi-probe.ps1 -VoiceId piper-en-alan-low -Audible -TimeoutSeconds 30
```
