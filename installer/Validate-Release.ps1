[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$StageRoot,

    [string]$ExpectedVersion = '0.1.5'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath($StageRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Release stage does not exist: $root"
}

$requiredFiles = @(
    'CutLocal.exe',
    'CutLocal.dll',
    'CutLocal.deps.json',
    'CutLocal.runtimeconfig.json',
    'coreclr.dll',
    'hostfxr.dll',
    'hostpolicy.dll',
    'onnxruntime.dll',
    'DirectML.dll',
    'libSkiaSharp.dll',
    'ThirdPartyNotices.txt',
    'assets\models\manifests\u2netp.json',
    'licenses\MIT.txt',
    'licenses\Apache-2.0.txt',
    'licenses\BSD-3-Clause.txt',
    'licenses\BiRefNet-Weights-NOTICE.txt',
    'licenses\BRIA-RMBG-2.0-NOTICE.txt',
    'licenses\dotnet-runtime-LICENSE.txt',
    'licenses\dotnet-runtime-ThirdPartyNotices.txt',
    'licenses\onnxruntime-ThirdPartyNotices.txt',
    'runtimes\README.txt'
)

foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relativePath) -PathType Leaf)) {
        throw "Release stage is missing required file: $relativePath"
    }
}

$forbidden = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
    $_.Extension -eq '.pdb' -or
    $_.Name.EndsWith('.partial', [System.StringComparison]::OrdinalIgnoreCase) -or
    $_.Name.EndsWith('.seeding', [System.StringComparison]::OrdinalIgnoreCase)
})
if ($forbidden.Count -ne 0) {
    throw "Release stage contains forbidden symbols or temporary files: $($forbidden.Name -join ', ')"
}

$applicationVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
    (Join-Path $root 'CutLocal.exe')).FileVersion
if (-not $applicationVersion.StartsWith($ExpectedVersion, [System.StringComparison]::Ordinal)) {
    throw "Published executable version '$applicationVersion' does not match '$ExpectedVersion'."
}

$runtimeConfig = Get-Content -LiteralPath (Join-Path $root 'CutLocal.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($null -eq $runtimeConfig.runtimeOptions.includedFrameworks -or
    $runtimeConfig.runtimeOptions.includedFrameworks.Count -eq 0) {
    throw "Release runtimeconfig does not describe a self-contained deployment."
}

$manifestDirectory = Join-Path $root 'assets\models\manifests'
$modelRoot = Join-Path $root 'models'
$bundledModels = @(Get-ChildItem -LiteralPath $modelRoot -Recurse -Filter '*.onnx' -File)
if ($bundledModels.Count -eq 0) {
    throw "Release stage has no bundled offline model."
}

foreach ($model in $bundledModels) {
    $matchingManifest = Get-ChildItem -LiteralPath $manifestDirectory -Filter '*.json' -File |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } |
        Where-Object { $_.fileName -eq $model.Name } |
        Select-Object -First 1
    if ($null -eq $matchingManifest) {
        throw "Bundled model '$($model.Name)' has no release manifest."
    }

    if (-not [bool]$matchingManifest.license.commercialUseAllowed) {
        throw "Non-commercial model '$($model.Name)' must never be bundled in a CutLocal release."
    }

    if ($model.Length -ne [long]$matchingManifest.fileSizeBytes) {
        throw "Bundled model '$($model.Name)' has an unexpected byte length."
    }

    $hash = (Get-FileHash -LiteralPath $model.FullName -Algorithm SHA256).Hash
    if ($hash -ne ([string]$matchingManifest.sha256).ToUpperInvariant()) {
        throw "Bundled model '$($model.Name)' failed SHA-256 validation."
    }
}

$allFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File)
$totalBytes = ($allFiles | Measure-Object -Property Length -Sum).Sum
[pscustomobject]@{
    StageRoot = $root
    Version = $ExpectedVersion
    FileCount = $allFiles.Count
    TotalBytes = $totalBytes
    BundledModels = $bundledModels.Count
    SelfContained = $true
    SymbolsExcluded = $true
}
