# Find MSBuild
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $msbuildPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1
    $msbuild = "$msbuildPath\MSBuild\Current\Bin\MSBuild.exe"
    if (Test-Path $msbuild) {
        Write-Host "Using MSBuild: $msbuild"
    } else {
        $msbuild = "$msbuildPath\MSBuild\15.0\Bin\MSBuild.exe"
    }
}

if (-not (Test-Path $msbuild)) {
    # Try common locations
    $msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    if (-not (Test-Path $msbuild)) {
        $msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
    }
    if (-not (Test-Path $msbuild)) {
        $msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
    }
}

Write-Host "Using MSBuild: $msbuild"

$sln = Get-ChildItem -Path '.' -Filter '*.sln' | Select-Object -First 1
if ($sln) {
    Write-Host "Building solution: $($sln.Name)"
    & $msbuild "$($sln.FullName)" /p:Configuration=Release /p:Platform=x64 /t:VoiceGardenSAPIAdapter /m /v:minimal
} else {
    Write-Host "No solution file found"
}
