# Grid3 Voice Activation Troubleshooting

## Summary

Grid3 (a SAPI-based AAC app by Smartbox) could see SherpaOnnx voices in its voice list but produced no audio when selecting them. Microsoft built-in voices (David, Zira) worked fine. This document covers the root causes found during v0.2.0 development and how to diagnose similar issues.

## Root Causes

### 1. DLL Version Mismatch (v0.13.2 vs v1.12.23)

**Symptom:** SherpaOnnx engine creation crashes with access violation (0xC0000005) or "resource deadlock would occur".

**Cause:** The .NET adapter (via DotNetTtsWrapper NuGet) bundles SherpaOnnx native DLLs v1.13.2 (`sherpa-onnx-c-api.dll`, `onnxruntime.dll`). The build script copied these to the payload, overwriting the C++ adapter's v1.12.23 DLLs. The struct layout between versions differs, causing a crash when the C++ adapter (compiled against v1.12.23 headers) calls into v1.13.2 DLLs.

**Fix:** `build-release-local.ps1` and CI workflow now exclude native SherpaOnnx DLLs from the .NET adapter output, preserving the C++ adapter's matched v1.12.23 versions.

**Diagnosis:** Check `sherpa_loader.log` — if it shows successful DLL loading but the adapter crashes during `SherpaOnnxCreateOfflineTts`, compare DLL hashes between the build output and the install directory.

### 2. Missing onnxruntime.dll

**Symptom:** SherpaOnnx engine creation fails with error 0x7E (`ERROR_MOD_NOT_FOUND`).

**Cause:** The MSI installer didn't replace `onnxruntime.dll` if the file was locked by a running process. The clean uninstall/reinstall cycle should fix this, but manual DLL deployment may be needed.

**Diagnosis:** Check the C++ adapter log for:
```
Failed to preload onnxruntime.dll from C:\...\onnxruntime.dll (error 0x7e).
```

### 3. Duplicate Voice Tokens

**Symptom:** Grid3 can see voices but `SelectVoice` fails silently. No "TTS init: begin" entry in the C++ adapter log. No audio produced.

**Cause:** Two sources create SherpaOnnx voice tokens:

1. **C++ VoiceTokenEnumerator** — scans the local models directory and creates **in-memory** COM tokens. These appear in `GetInstalledVoices()` but CANNOT be used with `System.Speech.Synthesis.SpeechSynthesizer.SelectVoice()`.

2. **SherpaOnnxConfig "promote-hklm"** — registers voices as **HKLM registry tokens** at `HKLM\SOFTWARE\Microsoft\Speech\Voices\Tokens\Sherpa-<modelId>`. These work with ALL SAPI apps including System.Speech.

When both sources exist, duplicate voices with the same ID but different names confuse `System.Speech.SelectVoice()`, which fails silently.

**Fix:** The C++ VoiceTokenEnumerator now checks if a SherpaOnnx voice is already registered in HKLM before adding it. If the HKLM token exists, the enumerator skips it. This prevents duplicates.

### 4. System.Speech Requires Registry-Backed Tokens

**Symptom:** Native SAPI apps (Balabolka) work fine. Managed apps using `System.Speech` (Grid3) cannot select the voice.

**Cause:** `System.Speech.Synthesis.SpeechSynthesizer.SelectVoice()` internally creates the TTS engine COM object from the voice token. For in-memory tokens (created by the VoiceTokenEnumerator), the COM activation fails because System.Speech requires registry-backed token paths.

Registry-backed tokens (HKLM or HKCU) work correctly because System.Speech can resolve the token path to a real registry key and read the CLSID for COM activation.

**Key insight:** Always promote SherpaOnnx voices to HKLM (via SherpaOnnxConfig's "Install for Admin Apps" or `promote-hklm` CLI) for System.Speech compatibility.

## Diagnostic Steps

### Step 1: Check the C++ adapter log

```
%LOCALAPPDATA%\NaturalVoiceSAPIAdapter\log.txt
```

- **Voice enumeration only, no "TTS init: begin"** → SelectVoice is failing. Check for duplicate voice tokens.
- **"TTS init: begin" + "Sherpa init" + crash** → DLL version mismatch or missing onnxruntime.dll.
- **"TTS init: begin" + "Sherpa init" + "Sherpa engine instance created" + "Speak" + audio samples** → Adapter is working. Issue is in the app's audio output path.

### Step 2: Test with PowerShell

```powershell
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.GetInstalledVoices() | Where-Object { $_.VoiceInfo.Id -match "Sherpa" } | ForEach-Object {
    Write-Host "Name='$($_.VoiceInfo.Name)' Id=$($_.VoiceInfo.Id)"
    try {
        $synth.SelectVoice($_.VoiceInfo.Name)
        Write-Host "  SelectVoice OK"
    } catch {
        Write-Host "  SelectVoice FAILED: $($_.Exception.Message)"
    }
}
```

If SelectVoice fails here, the issue is with voice registration (duplicates or non-registry-backed tokens).

### Step 3: Test with a native SAPI app

Use **Balabolka** or **SherpaOnnxConfig.exe sapi-probe** to test native SAPI activation. If native SAPI works but System.Speech doesn't, the issue is specifically with in-memory vs registry-backed tokens.

### Step 4: Check for duplicate voices

```powershell
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$ids = $synth.GetInstalledVoices() | ForEach-Object { $_.VoiceInfo.Id }
$dupes = $ids | Group-Object | Where-Object { $_.Count -gt 1 }
if ($dupes) { Write-Host "Duplicate IDs found!" }
```

## Solution Summary

| Issue | Fix |
|-------|-----|
| DLL version mismatch | Build script preserves C++ adapter's native DLLs |
| Missing onnxruntime.dll | Ensure clean reinstall or manual DLL copy |
| Duplicate voice tokens | VoiceTokenEnumerator skips voices already in HKLM |
| System.Speech can't select in-memory tokens | Always promote SherpaOnnx voices to HKLM via SherpaOnnxConfig |

## Affected Apps

- **Grid3** (Smartbox) — uses `System.Speech` → requires HKLM tokens
- **Balabolka** — uses native SAPI COM → works with both in-memory and HKLM tokens
- **Narrator** (Windows) — uses native SAPI → works with both
- **PowerShell System.Speech** — same as Grid3

## Related Files

- `NaturalVoiceSAPIAdapter/VoiceTokenEnumerator.cpp` — SherpaOnnx voice enumeration + HKLM dedup check
- `SherpaOnnxConfig/MainForm.cs` — `PromoteModelTokenToHklm()` writes HKLM tokens
- `scripts/build-release-local.ps1` — Prevents .NET adapter DLL overwrite
- `.github/workflows/msbuild.yml` — CI equivalent of the build script fix
