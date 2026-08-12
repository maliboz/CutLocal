[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ArchivePath,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archiveFull = [System.IO.Path]::GetFullPath($ArchivePath)
if (-not (Test-Path -LiteralPath $archiveFull -PathType Leaf)) {
    throw "Portable archive does not exist: $archiveFull"
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($archiveFull)
try {
    $entries = [System.Collections.Generic.Dictionary[string, System.IO.Compression.ZipArchiveEntry]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries) {
        $normalized = $entry.FullName.Replace('\', '/')
        if ($normalized.EndsWith('/', [System.StringComparison]::Ordinal)) {
            continue
        }
        if (-not $normalized.StartsWith('CutLocal/', [System.StringComparison]::Ordinal) -or
            $normalized.Contains('../') -or
            $normalized.Contains(':')) {
            throw "Unsafe or unexpected portable entry path: $normalized"
        }
        if ($normalized.EndsWith('.pdb', [System.StringComparison]::OrdinalIgnoreCase) -or
            $normalized.EndsWith('.partial', [System.StringComparison]::OrdinalIgnoreCase) -or
            $normalized.EndsWith('.seeding', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Portable archive contains a forbidden file: $normalized"
        }
        if ($entries.ContainsKey($normalized)) {
            throw "Portable archive contains duplicate entry: $normalized"
        }
        $entries.Add($normalized, $entry)
    }

    foreach ($required in @(
        'CutLocal/CutLocal.exe',
        'CutLocal/ThirdPartyNotices.txt',
        'CutLocal/release-manifest.json',
        'CutLocal/models/u2netp/1/u2netp.onnx',
        'CutLocal/runtimes/README.txt',
        'CutLocal/licenses/MIT.txt',
        'CutLocal/licenses/MS-RL.txt'
    )) {
        if (-not $entries.ContainsKey($required)) {
            throw "Portable archive is missing required entry: $required"
        }
    }

    $manifestEntry = $entries['CutLocal/release-manifest.json']
    $manifestStream = $manifestEntry.Open()
    try {
        $reader = [System.IO.StreamReader]::new($manifestStream, [System.Text.Encoding]::UTF8, $true)
        try {
            $manifest = $reader.ReadToEnd() | ConvertFrom-Json
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $manifestStream.Dispose()
    }

    if ($manifest.Product -ne 'CutLocal' -or
        $manifest.Version -ne $ExpectedVersion -or
        $manifest.RuntimeIdentifier -ne 'win-x64' -or
        -not $manifest.SelfContained) {
        throw 'Portable release manifest identity or deployment mode is invalid.'
    }

    $manifestFiles = @($manifest.Files)
    if ($entries.Count -ne $manifestFiles.Count + 1) {
        throw "Portable entry count '$($entries.Count)' does not match manifest '$($manifestFiles.Count + 1)'."
    }

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        foreach ($file in $manifestFiles) {
            $entryPath = 'CutLocal/' + ([string]$file.Path).Replace('\', '/')
            if (-not $entries.ContainsKey($entryPath)) {
                throw "Manifest entry is absent from portable archive: $entryPath"
            }

            $entry = $entries[$entryPath]
            if ($entry.Length -ne [long]$file.Bytes) {
                throw "Portable entry length mismatch: $entryPath"
            }

            $stream = $entry.Open()
            try {
                $hash = ([System.BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '')
            }
            finally {
                $stream.Dispose()
            }
            if ($hash -ne ([string]$file.Sha256).ToUpperInvariant()) {
                throw "Portable entry SHA-256 mismatch: $entryPath"
            }
        }
    }
    finally {
        $sha256.Dispose()
    }

    [pscustomobject]@{
        Archive = $archiveFull
        Version = $ExpectedVersion
        Files = $entries.Count
        ManifestHashes = $manifestFiles.Count
        Bytes = (Get-Item -LiteralPath $archiveFull).Length
        Result = 'PASS'
    }
}
finally {
    $archive.Dispose()
}
