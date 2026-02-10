# SherpaOnnx Dependencies

This directory contains the SherpaOnnx static libraries required for building NaturalVoiceSAPIAdapter with offline TTS support.

## Downloading Dependencies

The SherpaOnnx libraries are large (~200MB per platform) and are not included in the git repository.

### Quick Start

From the `NaturalVoiceSAPIAdapter` directory, run:

```powershell
# Download x64 (64-bit) libraries only (most common)
.\download-sherpa-deps.ps1

# Download both x86 and x64
.\download-sherpa-deps.ps1 -Platforms all

# Force re-download
.\download-sherpa-deps.ps1 -Platforms x64 -Force
```

### What Gets Downloaded

| Platform | Size | Description |
|----------|------|-------------|
| x64 | ~197MB | 64-bit libraries (required for modern systems) |
| x86 | ~150MB | 32-bit libraries (for older Windows systems) |

### Download Source

Libraries are downloaded from the official SherpaOnnx SourceForge mirror:
- https://sourceforge.net/projects/sherpa-onnx.mirror/

## Directory Structure

```
SherpaOnnx/
├── README.md                    # This file
├── SherpaOnnxEngine.cpp         # TTS engine wrapper
├── SherpaOnnxEngine.h
├── SherpaOnnxModels.cpp         # Model discovery
├── SherpaOnnxModels.h
└── libs/                        # Downloaded dependencies (gitignored)
    └── sherpa-onnx-v1.12.23-win-x64-static/
        ├── include/
        │   └── sherpa-onnx/
        │       ├── c-api/
        │       └── cxx-api/
        └── lib/
            ├── sherpa-onnx-c-api.lib
            ├── sherpa-onnx-core.lib
            ├── onnxruntime.lib
            └── ... (other dependencies)
```

## Building After Download

Once dependencies are downloaded, build NaturalVoiceSAPIAdapter normally in Visual Studio or using MSBuild:

```cmd
msbuild NaturalVoiceSAPIAdapter.sln /p:Configuration=Release /p:Platform=x64
```

## Troubleshooting

### "Cannot open input file 'sherpa-onnx-c-api.lib'"

Run the download script:
```powershell
.\download-sherpa-deps.ps1
```

### "Library machine type 'x86' conflicts with target machine type 'x64'"

You downloaded the wrong platform. Download the x64 version:
```powershell
.\download-sherpa-deps.ps1 -Platforms x64 -Force
```

### PowerShell execution policy error

If you get "cannot be loaded because running scripts is disabled", run:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

## Version Information

- **SherpaOnnx Version**: v1.12.23
- **Supported Platforms**: Windows x64, x86 (Win32), ARM64
- **Last Updated**: 2025-02-10

## More Information

- [SherpaOnnx GitHub](https://github.com/k2-fsa/sherpa-onnx)
- [SherpaOnnx Documentation](https://k2-fsa.github.io/sherpa/onnx/index.html)
- [TTS Models](https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models)
