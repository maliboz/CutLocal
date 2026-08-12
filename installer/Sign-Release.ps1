[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$Path,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{40}$')]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory)]
    [ValidatePattern('^https://')]
    [string]$TimestampUrl,

    [string]$SignToolPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($SignToolPath)) {
    $candidate = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' `
        -Filter signtool.exe -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName.EndsWith('\x64', [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw 'signtool.exe was not found. Install the Windows SDK or pass -SignToolPath.'
    }

    $SignToolPath = $candidate.FullName
}

$signTool = [System.IO.Path]::GetFullPath($SignToolPath)
if (-not (Test-Path -LiteralPath $signTool -PathType Leaf)) {
    throw "signtool.exe does not exist: $signTool"
}

foreach ($item in $Path) {
    $fullPath = [System.IO.Path]::GetFullPath($item)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Signing input does not exist: $fullPath"
    }

    & $signTool sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 /d CutLocal $fullPath
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode signing failed for '$fullPath' with exit code $LASTEXITCODE."
    }

    & $signTool verify /pa /v $fullPath
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode verification failed for '$fullPath' with exit code $LASTEXITCODE."
    }
}
