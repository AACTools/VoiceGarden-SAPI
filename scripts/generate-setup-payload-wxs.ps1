#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory = $true)]
    [string]$PayloadDir,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

$payloadFull = (Resolve-Path $PayloadDir).Path

if (!(Test-Path $payloadFull -PathType Container)) {
    throw "Payload directory not found: $payloadFull"
}

$files = Get-ChildItem -Path $payloadFull -Recurse -File | Sort-Object FullName
if ($files.Count -eq 0) {
    throw "No payload files found in: $payloadFull"
}

function Get-HashId([string]$prefix, [string]$value) {
    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($value.ToLowerInvariant())
        $hash = [System.BitConverter]::ToString($sha1.ComputeHash($bytes)).Replace("-", "")
        return ($prefix + "_" + $hash.Substring(0, 20))
    }
    finally {
        $sha1.Dispose()
    }
}

function XmlEscape([string]$s) {
    return [System.Security.SecurityElement]::Escape($s)
}

$allDirs = New-Object System.Collections.Generic.HashSet[string]
$allDirs.Add("") | Out-Null

foreach ($f in $files) {
    $relFile = $f.FullName.Substring($payloadFull.Length).TrimStart('\')
    $relDir = Split-Path -Parent $relFile
    if ([string]::IsNullOrEmpty($relDir)) {
        continue
    }

    $parts = $relDir -split '[\\/]'
    $current = ""
    foreach ($p in $parts) {
        if ([string]::IsNullOrEmpty($p)) { continue }
        if ([string]::IsNullOrEmpty($current)) {
            $current = $p
        }
        else {
            $current = "$current\$p"
        }
        $allDirs.Add($current) | Out-Null
    }
}

$dirIds = @{}
$dirIds[""] = "INSTALLFOLDER"
foreach ($d in ($allDirs | Sort-Object)) {
    if ($d -eq "") { continue }
    $dirIds[$d] = Get-HashId "DIR" $d
}

$childMap = @{}
foreach ($d in $allDirs) {
    $childMap[$d] = @()
}
foreach ($d in $allDirs) {
    if ($d -eq "") { continue }
    $parent = Split-Path -Parent $d
    if ($parent -eq "." -or $parent -eq $null) { $parent = "" }
    $childMap[$parent] += $d
}
foreach ($k in @($childMap.Keys)) {
    $childMap[$k] = $childMap[$k] | Sort-Object
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')

function Write-DirTree([string]$parentRel, [int]$indent) {
    $spaces = " " * $indent
    foreach ($childRel in $childMap[$parentRel]) {
        $leafName = Split-Path -Leaf $childRel
        $dirId = $dirIds[$childRel]
        if ($childMap[$childRel].Count -gt 0) {
            [void]$sb.AppendLine("$spaces<Directory Id=""$dirId"" Name=""$(XmlEscape $leafName)"">")
            Write-DirTree $childRel ($indent + 2)
            [void]$sb.AppendLine("$spaces</Directory>")
        } else {
            [void]$sb.AppendLine("$spaces<Directory Id=""$dirId"" Name=""$(XmlEscape $leafName)"" />")
        }
    }
}

Write-DirTree "" 6

[void]$sb.AppendLine('    </DirectoryRef>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <ComponentGroup Id="PayloadComponents">')

foreach ($f in $files) {
    $relFile = $f.FullName.Substring($payloadFull.Length).TrimStart('\')
    $relDir = Split-Path -Parent $relFile
    if ($relDir -eq "." -or $relDir -eq $null) { $relDir = "" }
    $componentId = Get-HashId "CMP" $relFile
    $fileId = Get-HashId "FIL" $relFile
    $dirId = $dirIds[$relDir]
    $source = '$(var.PayloadDir)\' + ($relFile -replace '/', '\')

    [void]$sb.AppendLine("      <Component Id=""$componentId"" Directory=""$dirId"" Guid=""*"">")
    [void]$sb.AppendLine("        <File Id=""$fileId"" Source=""$(XmlEscape $source)"" KeyPath=""yes"" />")
    [void]$sb.AppendLine('      </Component>')
}

[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

$outputDir = Split-Path -Parent $OutputPath
if ($outputDir -and !(Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Set-Content -Path $OutputPath -Value $sb.ToString() -Encoding UTF8
Write-Host "Generated payload manifest: $OutputPath"
Write-Host "Files: $($files.Count)"
