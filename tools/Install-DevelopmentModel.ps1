[CmdletBinding()]
param(
    [string]$DestinationRoot = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CutLocal\models')
)

$ErrorActionPreference = 'Stop'
$modelId = 'u2netp'
$modelVersion = '1'
$fileName = 'u2netp.onnx'
$expectedSha256 = '309C8469258DDA742793DCE0EBEA8E6DD393174F89934733ECC8B14C76F4DDD8'
$downloadUrl = 'https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx'
$modelDirectory = Join-Path (Join-Path $DestinationRoot $modelId) $modelVersion
$finalPath = Join-Path $modelDirectory $fileName
$partialPath = "$finalPath.partial"

New-Item -ItemType Directory -Force -Path $modelDirectory | Out-Null

if (Test-Path -LiteralPath $finalPath) {
    $installedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $finalPath).Hash
    if ($installedHash -eq $expectedSha256) {
        Write-Host "U2NetP is already installed and verified: $finalPath"
        return
    }

    $quarantinePath = "$finalPath.$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()).quarantine"
    Move-Item -LiteralPath $finalPath -Destination $quarantinePath
    Write-Warning "The existing model failed verification and was quarantined: $quarantinePath"
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $partialPath
$downloadedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $partialPath).Hash
if ($downloadedHash -ne $expectedSha256) {
    $quarantinePath = "$partialPath.$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()).quarantine"
    Move-Item -LiteralPath $partialPath -Destination $quarantinePath
    throw "U2NetP SHA-256 mismatch. The download was quarantined at $quarantinePath"
}

Move-Item -LiteralPath $partialPath -Destination $finalPath
Write-Host "U2NetP installed and verified: $finalPath"
