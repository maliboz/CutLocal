[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.5',

    [switch]$SkipInstaller,

    [string]$DotNetPath,

    [ValidatePattern('^[A-Fa-f0-9]{40}$')]
    [string]$SigningCertificateThumbprint,

    [ValidatePattern('^https://')]
    [string]$TimestampUrl = 'https://timestamp.digicert.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installerRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = Split-Path -Parent $installerRoot
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$releaseRoot = Join-Path $allowedArtifactsRoot 'release'
$workingRoot = Join-Path $allowedArtifactsRoot 'release-work'
$modelCacheRoot = Join-Path $allowedArtifactsRoot 'model-cache'
$publishPortable = Join-Path $workingRoot 'publish-portable'
$publishInstaller = Join-Path $workingRoot 'publish-installer'
$portableStage = Join-Path $workingRoot 'portable\CutLocal'
$installerStage = Join-Path $workingRoot 'installer\CutLocal'
$wixSource = Join-Path $workingRoot 'wix\HarvestedFiles.wxs'
$wixOutput = Join-Path $workingRoot 'wix-output'
$wixIntermediate = Join-Path $workingRoot 'wix-obj'
$fourPartVersion = "$Version.0"

function Assert-ControlledPath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $prefix = $allowedArtifactsRoot.TrimEnd('\') + '\'
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

function Copy-PublishStage {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $sourceFull = [System.IO.Path]::GetFullPath($Source).TrimEnd('\')
    foreach ($file in Get-ChildItem -LiteralPath $sourceFull -Recurse -File) {
        if ($file.Extension -eq '.pdb') {
            continue
        }

        $relative = $file.FullName.Substring($sourceFull.Length).TrimStart('\')
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}

function Copy-ReleaseNotices {
    param([Parameter(Mandatory)][string]$Stage)

    $licenseRoot = Join-Path $Stage 'licenses'
    New-Item -ItemType Directory -Force -Path $licenseRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $Stage
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'NOTICE') -Destination $Stage
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ThirdPartyNotices.txt') -Destination $Stage
    Copy-Item -Path (Join-Path $repositoryRoot 'assets\licenses\*.txt') -Destination $licenseRoot
    Copy-Item -LiteralPath (Join-Path $dotNetRoot 'LICENSE.txt') `
        -Destination (Join-Path $licenseRoot 'dotnet-runtime-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $dotNetRoot 'ThirdPartyNotices.txt') `
        -Destination (Join-Path $licenseRoot 'dotnet-runtime-ThirdPartyNotices.txt')

    $assetsPath = Join-Path $repositoryRoot 'src\CutLocal.App\obj\project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $packageRoot = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
    $onnxLibrary = $assets.libraries.PSObject.Properties.Name |
        Where-Object { $_ -like 'Microsoft.ML.OnnxRuntime.DirectML/*' } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($packageRoot) -or [string]::IsNullOrWhiteSpace($onnxLibrary)) {
        throw 'Could not resolve the ONNX Runtime package notice from project.assets.json.'
    }

    $onnxNotice = Join-Path $packageRoot (($onnxLibrary -replace '/', '\') + '\ThirdPartyNotices.txt')
    if (-not (Test-Path -LiteralPath $onnxNotice -PathType Leaf)) {
        throw "ONNX Runtime ThirdPartyNotices file is missing: $onnxNotice"
    }

    Copy-Item -LiteralPath $onnxNotice -Destination (Join-Path $licenseRoot 'onnxruntime-ThirdPartyNotices.txt')
    $runtimeReadme = Join-Path $Stage 'runtimes\README.txt'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $runtimeReadme) | Out-Null
    Set-Content -LiteralPath $runtimeReadme -Encoding UTF8 -Value @(
        'CutLocal is self-contained. The .NET, ONNX Runtime, DirectML, and Skia native files',
        'remain in the dotnet publish probing layout at the package root. Do not move them.'
    )
}

function New-ReleaseManifest {
    param([Parameter(Mandatory)][string]$Stage)

    $stageFull = [System.IO.Path]::GetFullPath($Stage).TrimEnd('\')
    $entries = Get-ChildItem -LiteralPath $stageFull -Recurse -File |
        Where-Object { $_.Name -ne 'release-manifest.json' } |
        ForEach-Object {
            [pscustomobject]@{
                Path = $_.FullName.Substring($stageFull.Length).TrimStart('\').Replace('\', '/')
                Bytes = $_.Length
                Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        } |
        Sort-Object Path

    $manifest = [ordered]@{
        Product = 'CutLocal'
        Version = $Version
        RuntimeIdentifier = 'win-x64'
        SelfContained = $true
        PublishSingleFile = $false
        PublishReadyToRun = $true
        PublishTrimmed = $false
        CreatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Files = $entries
    }
    $manifest | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $Stage 'release-manifest.json') -Encoding UTF8
}

if ([string]::IsNullOrWhiteSpace($DotNetPath)) {
    $DotNetPath = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
}
$DotNetPath = [System.IO.Path]::GetFullPath($DotNetPath)
$dotNetRoot = Split-Path -Parent $DotNetPath
if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
    throw "dotnet executable is missing: $DotNetPath"
}
if (-not (Test-Path -LiteralPath (Join-Path $dotNetRoot 'LICENSE.txt') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $dotNetRoot 'ThirdPartyNotices.txt') -PathType Leaf)) {
    throw 'The selected self-contained SDK does not expose its license and third-party notice files.'
}

Reset-ControlledDirectory -Path $releaseRoot
Reset-ControlledDirectory -Path $workingRoot
New-Item -ItemType Directory -Force -Path $modelCacheRoot | Out-Null

$applicationProject = Join-Path $repositoryRoot 'src\CutLocal.App\CutLocal.App.csproj'
Invoke-Checked -FilePath $DotNetPath -ArgumentList @(
    'publish', $applicationProject,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '-p:PublishProfile=Portable',
    "-p:PublishDir=$publishPortable\",
    "-p:Version=$Version",
    "-p:VersionPrefix=$Version",
    "-p:AssemblyVersion=$fourPartVersion",
    "-p:FileVersion=$fourPartVersion",
    "-p:InformationalVersion=$Version"
)
Invoke-Checked -FilePath $DotNetPath -ArgumentList @(
    'publish', $applicationProject,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '-p:PublishProfile=Installer',
    "-p:PublishDir=$publishInstaller\",
    "-p:Version=$Version",
    "-p:VersionPrefix=$Version",
    "-p:AssemblyVersion=$fourPartVersion",
    "-p:FileVersion=$fourPartVersion",
    "-p:InformationalVersion=$Version"
)

Copy-PublishStage -Source $publishPortable -Destination $portableStage
Copy-PublishStage -Source $publishInstaller -Destination $installerStage
Copy-ReleaseNotices -Stage $portableStage
Copy-ReleaseNotices -Stage $installerStage

$defaultManifest = Join-Path $repositoryRoot 'assets\models\manifests\u2netp.json'
& (Join-Path $installerRoot 'Acquire-ReleaseModel.ps1') `
    -ManifestPath $defaultManifest `
    -DestinationRoot $modelCacheRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $portableStage 'models') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $installerStage 'models') | Out-Null
Copy-Item -LiteralPath (Join-Path $modelCacheRoot 'u2netp') `
    -Destination (Join-Path $portableStage 'models') -Recurse
Copy-Item -LiteralPath (Join-Path $modelCacheRoot 'u2netp') `
    -Destination (Join-Path $installerStage 'models') -Recurse

if (-not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
    $signInputs = @(
        (Join-Path $portableStage 'CutLocal.exe'),
        (Join-Path $portableStage 'CutLocal.dll'),
        (Join-Path $installerStage 'CutLocal.exe'),
        (Join-Path $installerStage 'CutLocal.dll')
    )
    & (Join-Path $installerRoot 'Sign-Release.ps1') `
        -Path $signInputs `
        -CertificateThumbprint $SigningCertificateThumbprint `
        -TimestampUrl $TimestampUrl
}

& (Join-Path $installerRoot 'Validate-Release.ps1') `
    -StageRoot $portableStage -ExpectedVersion $Version | Format-Table -AutoSize
& (Join-Path $installerRoot 'Validate-Release.ps1') `
    -StageRoot $installerStage -ExpectedVersion $Version | Format-Table -AutoSize
New-ReleaseManifest -Stage $portableStage
New-ReleaseManifest -Stage $installerStage

$portableZip = Join-Path $releaseRoot "CutLocal-$Version-win-x64-portable.zip"
Compress-Archive -LiteralPath $portableStage -DestinationPath $portableZip -CompressionLevel Optimal
& (Join-Path $installerRoot 'Validate-PortableArchive.ps1') `
    -ArchivePath $portableZip `
    -ExpectedVersion $Version | Format-Table -AutoSize

$installerPath = $null
if (-not $SkipInstaller) {
    & (Join-Path $installerRoot 'Generate-WixSource.ps1') `
        -StageRoot $installerStage `
        -OutputPath $wixSource | Format-Table -AutoSize

    $wixProject = Join-Path $installerRoot 'CutLocal.Setup.wixproj'
    $wixOutputArgument = $wixOutput.Replace('\', '/') + '/'
    $wixIntermediateArgument = $wixIntermediate.Replace('\', '/') + '/'
    Invoke-Checked -FilePath $DotNetPath -ArgumentList @(
        'build', $wixProject,
        '--configuration', 'Release',
        "-p:ProductVersion=$Version",
        "-p:StageRoot=$installerStage",
        "-p:GeneratedWixSource=$wixSource",
        "-p:OutputPath=$wixOutputArgument",
        "-p:BaseIntermediateOutputPath=$wixIntermediateArgument"
    )

    $builtInstallers = @(Get-ChildItem -LiteralPath $wixOutput -Filter '*.msi' -Recurse -File)
    if ($builtInstallers.Count -ne 1) {
        throw "WiX produced $($builtInstallers.Count) MSI files; exactly one was expected."
    }

    $installerPath = Join-Path $releaseRoot "CutLocal-$Version-win-x64-setup.msi"
    Copy-Item -LiteralPath $builtInstallers[0].FullName -Destination $installerPath

    & (Join-Path $installerRoot 'Validate-Installer.ps1') `
        -InstallerPath $installerPath `
        -ExpectedVersion $Version | Format-Table -AutoSize

    if (-not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
        & (Join-Path $installerRoot 'Sign-Release.ps1') `
            -Path $installerPath `
            -CertificateThumbprint $SigningCertificateThumbprint `
            -TimestampUrl $TimestampUrl
    }
}

$hashInputs = @($portableZip)
if ($null -ne $installerPath) {
    $hashInputs += $installerPath
}
$hashLines = foreach ($artifact in $hashInputs) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash, (Split-Path -Leaf $artifact)
}
$hashLines | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ASCII

Get-ChildItem -LiteralPath $releaseRoot -File | Sort-Object Name |
    Select-Object Name, Length, @{Name='Sha256'; Expression={(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}}
