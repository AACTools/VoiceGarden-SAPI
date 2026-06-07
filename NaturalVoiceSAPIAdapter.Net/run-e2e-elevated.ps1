param(
    [string]$AzureKey = $null,
    [string]$AzureRegion = "uksouth"
)

$envFile = Join-Path $PSScriptRoot ".e2e-env.json"

$envData = @{
    Timestamp = Get-Date -Format "o"
}

if ($AzureKey) {
    $envData["MICROSOFT_TOKEN"] = $AzureKey
    $envData["MICROSOFT_REGION"] = $AzureRegion
}

$envData | ConvertTo-Json | Out-File $envFile -Encoding UTF8

$runnerScript = @'
$envFile = Join-Path $PSScriptRoot ".e2e-env.json"
if (Test-Path $envFile) {
    $env = Get-Content $envFile | ConvertFrom-Json
    foreach ($prop in $env.PSObject.Properties) {
        if ($prop.Name -ne "Timestamp") {
            [System.Environment]::SetEnvironmentVariable($prop.Name, $prop.Value, "Process")
        }
    }
}
$testScript = Join-Path $PSScriptRoot "test-e2e.ps1"
& $testScript -Full *>&1 | Out-File (Join-Path $PSScriptRoot "..\e2e-result.txt") -Encoding UTF8
Remove-Item $envFile -Force -ErrorAction SilentlyContinue
'@

$runnerFile = Join-Path $PSScriptRoot ".e2e-runner.ps1"
$runnerScript | Out-File $runnerFile -Encoding UTF8

Start-Process powershell -ArgumentList "-ExecutionPolicy", "Bypass", "-File", $runnerFile -Verb RunAs -Wait

Start-Sleep -Seconds 1
Remove-Item $runnerFile -Force -ErrorAction SilentlyContinue
Remove-Item $envFile -Force -ErrorAction SilentlyContinue
