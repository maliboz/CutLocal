[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.5',

    [string]$DotNetPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installerRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = Split-Path -Parent $installerRoot
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$releaseRoot = Join-Path $allowedArtifactsRoot 'release'
$workingRoot = Join-Path $allowedArtifactsRoot 'mac-release-work'
$modelCacheRoot = Join-Path $allowedArtifactsRoot 'model-cache'
$applicationProject = Join-Path $repositoryRoot 'src\CutLocal.Mac\CutLocal.Mac.csproj'
$archiveProject = Join-Path $repositoryRoot 'tools\CutLocal.MacArchive\CutLocal.MacArchive.csproj'
$archiveAssembly = Join-Path $repositoryRoot 'tools\CutLocal.MacArchive\bin\Release\net10.0\CutLocal.MacArchive.dll'
$macInstallerRoot = Join-Path $installerRoot 'macos'
$fourPartVersion = "$Version.0"

function Assert-ControlledPath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $prefix = $allowedArtifactsRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release path resolves outside the controlled artifacts directory: $fullPath"
    }

    return $fullPath
}

function Reset-ControlledDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = Assert-ControlledPath -Path $Path
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $fullPath | Out-Null
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$FilePath' failed with exit code $LASTEXITCODE."
    }
}

function Copy-DirectoryFiles {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [switch]$ExcludeSymbols
    )

    $sourceFull = [System.IO.Path]::GetFullPath($Source).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $sourceFull -Recurse -File) {
        if ($ExcludeSymbols -and $file.Extension -eq '.pdb') {
            continue
        }

        $relative = $file.FullName.Substring($sourceFull.Length).TrimStart('\', '/')
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}

function Copy-ReleaseNotices {
    param([Parameter(Mandatory)][string]$ResourcesDirectory)

    $licenseRoot = Join-Path $ResourcesDirectory 'licenses'
    New-Item -ItemType Directory -Force -Path $licenseRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $ResourcesDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'NOTICE') -Destination $ResourcesDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ThirdPartyNotices.txt') -Destination $ResourcesDirectory
    Copy-Item -Path (Join-Path $repositoryRoot 'assets\licenses\*.txt') -Destination $licenseRoot

    $dotNetLicense = Join-Path $dotNetRoot 'LICENSE.txt'
    $dotNetNotices = Join-Path $dotNetRoot 'ThirdPartyNotices.txt'
    if (Test-Path -LiteralPath $dotNetLicense -PathType Leaf) {
        Copy-Item -LiteralPath $dotNetLicense -Destination (Join-Path $licenseRoot 'dotnet-runtime-LICENSE.txt')
    }
    if (Test-Path -LiteralPath $dotNetNotices -PathType Leaf) {
        Copy-Item -LiteralPath $dotNetNotices -Destination (Join-Path $licenseRoot 'dotnet-runtime-ThirdPartyNotices.txt')
    }

    $assetsPath = Join-Path $repositoryRoot 'src\CutLocal.Mac\obj\project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $packageRoot = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
    $onnxLibrary = $assets.libraries.PSObject.Properties.Name |
        Where-Object { $_ -like 'Microsoft.ML.OnnxRuntime/*' } |
        Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($packageRoot) -and
        -not [string]::IsNullOrWhiteSpace($onnxLibrary)) {
        $onnxNotice = Join-Path $packageRoot (($onnxLibrary -replace '/', [System.IO.Path]::DirectorySeparatorChar) +
            [System.IO.Path]::DirectorySeparatorChar + 'ThirdPartyNotices.txt')
        if (Test-Path -LiteralPath $onnxNotice -PathType Leaf) {
            Copy-Item -LiteralPath $onnxNotice -Destination (Join-Path $licenseRoot 'onnxruntime-ThirdPartyNotices.txt')
        }
    }
}

function New-ReleaseManifest {
    param(
        [Parameter(Mandatory)][string]$AppDirectory,
        [Parameter(Mandatory)][string]$RuntimeIdentifier,
        [Parameter(Mandatory)][string]$OnnxRuntimeVersion
    )

    $appFull = [System.IO.Path]::GetFullPath($AppDirectory).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $manifestPath = Join-Path $appFull 'Contents\Resources\release-manifest.json'
    $entries = Get-ChildItem -LiteralPath $appFull -Recurse -File |
        Where-Object { $_.FullName -ne $manifestPath } |
        ForEach-Object {
            [pscustomobject]@{
                Path = $_.FullName.Substring($appFull.Length).TrimStart('\', '/').Replace('\', '/')
                Bytes = $_.Length
                Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        } |
        Sort-Object Path

    $manifest = [ordered]@{
        Product = 'CutLocal'
        Version = $Version
        RuntimeIdentifier = $RuntimeIdentifier
        OnnxRuntimeVersion = $OnnxRuntimeVersion
        MinimumMacOS = '14.0'
        SelfContained = $true
        Signed = $false
        Notarized = $false
        CreatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Files = $entries
    }
    $manifest | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $manifestPath -Encoding UTF8
}

function Assert-AppBundle {
    param(
        [Parameter(Mandatory)][string]$AppDirectory,
        [Parameter(Mandatory)][string]$RuntimeIdentifier
    )

    $macOsDirectory = Join-Path $AppDirectory 'Contents\MacOS'
    $required = @(
        (Join-Path $AppDirectory 'Contents\Info.plist'),
        (Join-Path $AppDirectory 'Contents\Resources\CutLocal.icns'),
        (Join-Path $macOsDirectory 'CutLocal'),
        (Join-Path $macOsDirectory 'CutLocal.dll'),
        (Join-Path $macOsDirectory 'assets\models\manifests\u2netp.json'),
        (Join-Path $macOsDirectory 'models\u2netp\1\u2netp.onnx')
    )
    foreach ($path in $required) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required macOS bundle file is missing: $path"
        }
    }

    $forbidden = Get-ChildItem -LiteralPath $macOsDirectory -Recurse -File |
        Where-Object { $_.Name -match '(?i)directml|d3d12|dxgi' }
    if ($forbidden) {
        throw "Windows-only native files leaked into ${RuntimeIdentifier}: $($forbidden.Name -join ', ')"
    }

    $nativeNames = Get-ChildItem -LiteralPath $macOsDirectory -Recurse -File |
        Select-Object -ExpandProperty Name
    if (-not ($nativeNames -match 'libonnxruntime.*\.dylib')) {
        throw "ONNX Runtime macOS native library is missing from $RuntimeIdentifier."
    }
    if (-not ($nativeNames -contains 'libSkiaSharp.dylib')) {
        throw "SkiaSharp macOS native library is missing from $RuntimeIdentifier."
    }

    [xml]$plist = Get-Content -LiteralPath (Join-Path $AppDirectory 'Contents\Info.plist') -Raw
    if ($null -eq $plist.SelectSingleNode("/plist/dict/key[text()='CFBundleExecutable']")) {
        throw 'Info.plist does not declare CFBundleExecutable.'
    }

    $manifest = Get-Content -LiteralPath (Join-Path $macOsDirectory 'assets\models\manifests\u2netp.json') -Raw |
        ConvertFrom-Json
    $modelPath = Join-Path $macOsDirectory 'models\u2netp\1\u2netp.onnx'
    if ((Get-Item -LiteralPath $modelPath).Length -ne [long]$manifest.fileSizeBytes -or
        (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash -ne $manifest.sha256) {
        throw "Bundled U2NetP failed size or SHA-256 verification for $RuntimeIdentifier."
    }
}

if ([string]::IsNullOrWhiteSpace($DotNetPath)) {
    $localDotNet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $localDotNet -PathType Leaf) {
        $DotNetPath = $localDotNet
    }
    else {
        $DotNetPath = (Get-Command dotnet -ErrorAction Stop).Source
    }
}
$DotNetPath = [System.IO.Path]::GetFullPath($DotNetPath)
$dotNetRoot = Split-Path -Parent $DotNetPath
if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
    throw "dotnet executable is missing: $DotNetPath"
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
New-Item -ItemType Directory -Force -Path $modelCacheRoot | Out-Null
Reset-ControlledDirectory -Path $workingRoot

$defaultManifest = Join-Path $repositoryRoot 'assets\models\manifests\u2netp.json'
& (Join-Path $installerRoot 'Acquire-ReleaseModel.ps1') `
    -ManifestPath $defaultManifest `
    -DestinationRoot $modelCacheRoot | Out-Null

Invoke-Checked -FilePath $DotNetPath -ArgumentList @('restore', $archiveProject)
Invoke-Checked -FilePath $DotNetPath -ArgumentList @(
    'build', $archiveProject,
    '--configuration', 'Release',
    '--no-restore'
)

$targets = @(
    [pscustomobject]@{ Rid = 'osx-arm64'; Label = 'macos-arm64'; OrtVersion = '1.24.4' },
    [pscustomobject]@{ Rid = 'osx-x64'; Label = 'macos-x64'; OrtVersion = '1.23.2' }
)
$artifacts = @()
foreach ($target in $targets) {
    $targetRoot = Join-Path $workingRoot $target.Rid
    $publishDirectory = Join-Path $targetRoot 'publish'
    $stageRoot = Join-Path $targetRoot 'stage'
    $appDirectory = Join-Path $stageRoot 'CutLocal.app'
    $contentsDirectory = Join-Path $appDirectory 'Contents'
    $macOsDirectory = Join-Path $contentsDirectory 'MacOS'
    $resourcesDirectory = Join-Path $contentsDirectory 'Resources'
    Reset-ControlledDirectory -Path $targetRoot
    New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path $macOsDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path $resourcesDirectory | Out-Null

    Invoke-Checked -FilePath $DotNetPath -ArgumentList @(
        'restore', $applicationProject,
        '--runtime', $target.Rid,
        "-p:CutLocalOnnxRuntimeVersion=$($target.OrtVersion)"
    )
    Invoke-Checked -FilePath $DotNetPath -ArgumentList @(
        'publish', $applicationProject,
        '--configuration', 'Release',
        '--runtime', $target.Rid,
        '--self-contained', 'true',
        '--no-restore',
        "-p:PublishDir=$publishDirectory$([System.IO.Path]::DirectorySeparatorChar)",
        '-p:UseAppHost=true',
        '-p:PublishReadyToRun=false',
        '-p:PublishTrimmed=false',
        '-p:DebugSymbols=false',
        '-p:DebugType=None',
        "-p:CutLocalOnnxRuntimeVersion=$($target.OrtVersion)",
        "-p:Version=$Version",
        "-p:VersionPrefix=$Version",
        "-p:AssemblyVersion=$fourPartVersion",
        "-p:FileVersion=$fourPartVersion",
        "-p:InformationalVersion=$Version"
    )

    Copy-DirectoryFiles -Source $publishDirectory -Destination $macOsDirectory -ExcludeSymbols
    Copy-Item -LiteralPath (Join-Path $macInstallerRoot 'CutLocal.icns') -Destination $resourcesDirectory
    Copy-ReleaseNotices -ResourcesDirectory $resourcesDirectory
    New-Item -ItemType Directory -Force -Path (Join-Path $macOsDirectory 'models') | Out-Null
    Copy-Item -LiteralPath (Join-Path $modelCacheRoot 'u2netp') `
        -Destination (Join-Path $macOsDirectory 'models') -Recurse

    $plistContent = Get-Content -LiteralPath (Join-Path $macInstallerRoot 'Info.plist.template') -Raw
    $plistContent = $plistContent.Replace('@VERSION@', $Version).Replace('@BUILD_VERSION@', $Version)
    Set-Content -LiteralPath (Join-Path $contentsDirectory 'Info.plist') -Value $plistContent -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $macInstallerRoot 'FIRST-RUN.tr-en.txt') -Destination $stageRoot
    Copy-Item -LiteralPath (Join-Path $macInstallerRoot 'FIX-CUTLOCAL.command') -Destination $stageRoot

    New-ReleaseManifest `
        -AppDirectory $appDirectory `
        -RuntimeIdentifier $target.Rid `
        -OnnxRuntimeVersion $target.OrtVersion
    Assert-AppBundle -AppDirectory $appDirectory -RuntimeIdentifier $target.Rid
    if (-not (Test-Path -LiteralPath (Join-Path $stageRoot 'FIX-CUTLOCAL.command') -PathType Leaf)) {
        throw "The macOS local-signing finalizer is missing from $($target.Rid)."
    }

    $archivePath = Join-Path $releaseRoot "CutLocal-$Version-$($target.Label).tar.gz"
    $archivePath = Assert-ControlledPath -Path $archivePath
    Invoke-Checked -FilePath $DotNetPath -ArgumentList @(
        $archiveAssembly,
        '--source', $stageRoot,
        '--output', $archivePath,
        '--executable', 'CutLocal.app/Contents/MacOS/CutLocal'
    )
    $artifacts += Get-Item -LiteralPath $archivePath
}

$artifacts | Select-Object FullName, Length, @{ Name = 'Sha256'; Expression = {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
} }

$checksumPath = Join-Path $releaseRoot 'SHA256SUMS.txt'
$checksumLines = Get-ChildItem -LiteralPath $releaseRoot -File |
    Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
    Sort-Object Name |
    ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.Name
    }
$checksumLines | Set-Content -LiteralPath $checksumPath -Encoding ASCII
