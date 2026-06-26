# SherpaOnnx Dependencies

This directory contains the SherpaOnnx static libraries required for building VoiceGardenSAPIAdapter with offline TTS support.

## Downloading Dependencies

The SherpaOnnx libraries are large (~200MB per platform per configuration) and are not included in the git repository.

### Quick Start

From the `VoiceGardenSAPIAdapter` directory, run:

```powershell
# Download x64 Release libraries (for CI/CD and production builds)
.\download-sherpa-deps.ps1

# Download both x86 and x64 Release builds
.\download-sherpa-deps.ps1 -Platforms all

# Download Debug libraries for local development
.\download-sherpa-deps.ps1 -Configuration Debug

# Download both Debug and Release for all platforms (full development setup)
.\download-sherpa-deps.ps1 -Platforms all -Configuration all

# Force re-download
.\download-sherpa-deps.ps1 -Platforms x64 -Force
```

### What Gets Downloaded

| Platform | Config | Size | Description |
|----------|--------|------|-------------|
| x64 | Release | ~197MB | 64-bit Release libraries (required for modern systems) |
| x64 | Debug | ~250MB | 64-bit Debug libraries (for development) |
| x86 | Release | ~150MB | 32-bit Release libraries (for older Windows systems) |
| x86 | Debug | ~190MB | 32-bit Debug libraries (for development) |
| ARM64 | Release | ~180MB | ARM64 Release libraries (for Windows on ARM) |
| ARM64 | Debug | ~230MB | ARM64 Debug libraries (for development) |

### Download Source

Libraries are downloaded from the official SherpaOnnx GitHub releases:
- https://github.com/k2-fsa/sherpa-onnx/releases/tag/v1.12.23

The script downloads static libraries with MT runtime linking:
- `*-MT-Release.tar.bz2` for Release builds
- `*-MT-Debug.tar.bz2` for Debug builds

## Directory Structure

```
SherpaOnnx/
├── README.md                    # This file
├── SherpaOnnxEngine.cpp         # TTS engine wrapper
├── SherpaOnnxEngine.h
├── SherpaOnnxModels.cpp         # Model discovery
├── SherpaOnnxModels.h
└── libs/                        # Downloaded dependencies (gitignored)
    ├── sherpa-onnx-v1.12.23-win-x64-static-Release/
    │   ├── include/
    │   │   └── sherpa-onnx/
    │   │       ├── c-api/
    │   │       └── cxx-api/
    │   └── lib/
    │       ├── sherpa-onnx-c-api.lib
    │       ├── sherpa-onnx-core.lib
    │       ├── onnxruntime.lib
    │       └── ... (other dependencies)
    └── sherpa-onnx-v1.12.23-win-x64-static-Debug/  (if downloaded)
        └── ...
```

## Building After Download

Once dependencies are downloaded, build VoiceGardenSAPIAdapter normally in Visual Studio or using MSBuild:

### Release Build (CI/CD)
```cmd
msbuild VoiceGardenSAPIAdapter.sln /p:Configuration=Release /p:Platform=x64
```

### Debug Build (Local Development)
```cmd
msbuild VoiceGardenSAPIAdapter.sln /p:Configuration=Debug /p:Platform=x64
```

**Important**: The project configuration must match the downloaded SherpaOnnx configuration:
- **Debug** builds require `*-Debug` libraries
- **Release** builds require `*-Release` libraries

## Troubleshooting

### "Cannot open input file 'sherpa-onnx-c-api.lib'"

Run the download script for the appropriate configuration:
```powershell
# For Release builds
.\download-sherpa-deps.ps1 -Configuration Release

# For Debug builds
.\download-sherpa-deps.ps1 -Configuration Debug
```

### "RuntimeLibrary mismatch detected"

You're building with a configuration that doesn't match the downloaded libraries:
- Building in **Debug** mode requires `*-Debug` libraries
- Building in **Release** mode requires `*-Release` libraries

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

Or use the batch file wrapper:
```cmd
download-sherpa-deps.bat
```

## Version Information

- **SherpaOnnx Version**: v1.12.23
- **Supported Platforms**: Windows x64, x86 (Win32), ARM64
- **Supported Configurations**: Debug, Release
- **Runtime Library**: MT (Multi-threaded static runtime)
- **Last Updated**: 2025-02-10

## More Information

- [SherpaOnnx GitHub](https://github.com/k2-fsa/sherpa-onnx)
- [SherpaOnnx Documentation](https://k2-fsa.github.io/sherpa/onnx/index.html)
- [TTS Models](https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models)
