[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath($RepositoryRoot)
$noticePath = Join-Path $root 'ThirdPartyNotices.txt'
$projectLicensePath = Join-Path $root 'LICENSE'
$projectNoticePath = Join-Path $root 'NOTICE'
$assetsPaths = @(
    (Join-Path $root 'src\CutLocal.App\obj\project.assets.json'),
    (Join-Path $root 'src\CutLocal.Mac\obj\project.assets.json')
)

if (-not (Test-Path -LiteralPath $noticePath -PathType Leaf)) {
    throw 'ThirdPartyNotices.txt is missing.'
}
if (-not (Test-Path -LiteralPath $projectLicensePath -PathType Leaf) -or
    (Get-Content -LiteralPath $projectLicensePath -Raw).IndexOf(
        'Apache License',
        [System.StringComparison]::Ordinal) -lt 0) {
    throw 'The root Apache-2.0 LICENSE is missing or invalid.'
}
if (-not (Test-Path -LiteralPath $projectNoticePath -PathType Leaf) -or
    (Get-Content -LiteralPath $projectNoticePath -Raw).IndexOf(
        'rembg',
        [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw 'The root NOTICE file is missing the rembg attribution.'
}
foreach ($assetsPath in $assetsPaths) {
    if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
        throw "Restore all production applications before validating licenses: $assetsPath"
    }
}

$notice = Get-Content -LiteralPath $noticePath -Raw
$productionPackages = @(
    $assetsPaths |
        ForEach-Object { Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json } |
        ForEach-Object { $_.libraries.PSObject.Properties.Name } |
        ForEach-Object { ($_ -split '/')[0] } |
        Sort-Object -Unique
)

$noticeTokens = @{
    'CommunityToolkit.Mvvm' = 'CommunityToolkit.Mvvm'
    'Microsoft.Extensions.DependencyInjection' = 'Microsoft.Extensions.*'
    'Microsoft.Extensions.Hosting' = 'Microsoft.Extensions.*'
    'Microsoft.ML.OnnxRuntime.DirectML' = 'ONNX Runtime DirectML'
    'Microsoft.ML.OnnxRuntime' = 'ONNX Runtime CPU'
    'Avalonia' = 'Avalonia UI 12.1.0'
    'Avalonia.Desktop' = 'Avalonia UI 12.1.0'
    'Avalonia.Themes.Fluent' = 'Avalonia UI 12.1.0'
    'HarfBuzzSharp' = 'HarfBuzzSharp 8.3.1.3'
    'MicroCom.Runtime' = 'MicroCom.Runtime 0.11.6'
    'Tmds.DBus.Protocol' = 'Tmds.DBus.Protocol 0.94.1'
    'Serilog' = 'Serilog 4.3.0'
    'Serilog.Extensions.Hosting' = 'Serilog.Extensions.Hosting 10.0.0'
    'Serilog.Sinks.File' = 'Serilog.Sinks.File 7.0.0'
    'SkiaSharp' = 'SkiaSharp / Skia 4.150.1'
    'SkiaSharp.NativeAssets.Win32' = 'SkiaSharp / Skia 4.150.1'
}

$missingNotices = @()
foreach ($package in $productionPackages) {
    if ($noticeTokens.ContainsKey($package) -and
        $notice.IndexOf($noticeTokens[$package], [System.StringComparison]::Ordinal) -lt 0) {
        $missingNotices += $package
    }
}
if ($missingNotices.Count -gt 0) {
    throw "Production package notices are missing: $($missingNotices -join ', ')"
}

foreach ($license in @('MIT.txt', 'Apache-2.0.txt', 'BSD-3-Clause.txt', 'MS-RL.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root "assets\licenses\$license") -PathType Leaf)) {
        throw "Canonical license text is missing: $license"
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $root 'assets\licenses\BRIA-RMBG-2.0-NOTICE.txt') -PathType Leaf) -or
    $notice.IndexOf('BRIA RMBG-2.0 optional non-commercial model', [System.StringComparison]::Ordinal) -lt 0) {
    throw 'The optional BRIA model notice or third-party inventory entry is missing.'
}

if (-not (Test-Path -LiteralPath (Join-Path $root 'assets\licenses\BiRefNet-Weights-NOTICE.txt') -PathType Leaf) -or
    $notice.IndexOf('LicenseRef-BiRefNet-Weights-NonCommercial', [System.StringComparison]::Ordinal) -lt 0) {
    throw 'The restricted BiRefNet weight notice or third-party inventory entry is missing.'
}

$wixProject = Get-Content -LiteralPath (Join-Path $root 'installer\CutLocal.Setup.wixproj') -Raw
if ($wixProject.IndexOf('WixToolset.Sdk/5.0.2', [System.StringComparison]::Ordinal) -lt 0 -or
    $notice.IndexOf('WiX Toolset SDK and UI extension 5.0.2', [System.StringComparison]::Ordinal) -lt 0) {
    throw 'WiX 5.0.2 and its MS-RL notice must remain pinned together.'
}

[pscustomobject]@{
    ProductionPackages = $productionPackages.Count
    CanonicalLicenses = 4
    ProjectLicense = 'Apache-2.0'
    WixVersion = '5.0.2'
    Result = 'PASS'
}
