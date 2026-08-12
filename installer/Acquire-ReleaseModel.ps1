[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ManifestPath,

    [Parameter(Mandatory)]
    [string]$DestinationRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

function Test-ModelFile {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [long]$ExpectedLength,

        [Parameter(Mandatory)]
        [string]$ExpectedSha256
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    if ((Get-Item -LiteralPath $Path).Length -ne $ExpectedLength) {
        return $false
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $ExpectedSha256
}

function Assert-SafePathSegment {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value -in '.', '..' -or
        [System.IO.Path]::GetFileName($Value) -ne $Value -or
        $Value.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
        throw "Release model manifest $FieldName is not a safe path segment."
    }
}

function Receive-HttpsFile {
    param(
        [Parameter(Mandatory)]
        [uri]$Uri,

        [Parameter(Mandatory)]
        [string]$Path
    )

    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [System.Threading.Timeout]::InfiniteTimeSpan
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('CutLocal-ReleaseBuilder/0.1')
    $client.DefaultRequestHeaders.Accept.ParseAdd('application/octet-stream')
    try {
        $current = $Uri
        foreach ($redirect in 0..5) {
            if ($current.Scheme -ne [uri]::UriSchemeHttps) {
                throw "Release model download attempted a non-HTTPS URI."
            }

            $response = $client.GetAsync(
                $current,
                [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            try {
                $status = [int]$response.StatusCode
                if ($status -in 301, 302, 303, 307, 308) {
                    if ($redirect -eq 5 -or $null -eq $response.Headers.Location) {
                        throw "Release model download exceeded the HTTPS redirect limit."
                    }

                    $current = [uri]::new($current, $response.Headers.Location)
                    continue
                }

                $response.EnsureSuccessStatusCode() | Out-Null
                $stream = [System.IO.File]::Open(
                    $Path,
                    [System.IO.FileMode]::CreateNew,
                    [System.IO.FileAccess]::Write,
                    [System.IO.FileShare]::None)
                try {
                    $response.Content.CopyToAsync($stream).GetAwaiter().GetResult()
                    $stream.Flush($true)
                }
                finally {
                    $stream.Dispose()
                }

                return
            }
            finally {
                $response.Dispose()
            }
        }

        throw "Release model download did not reach a terminal HTTPS response."
    }
    finally {
        $client.Dispose()
        $handler.Dispose()
    }
}

function Test-TransientDownloadException {
    param([Parameter(Mandatory)][System.Exception]$Exception)

    $current = $Exception
    while ($null -ne $current) {
        if ($current -is [System.Net.Http.HttpRequestException] -or
            $current -is [System.IO.IOException] -or
            $current -is [System.Threading.Tasks.TaskCanceledException] -or
            $current -is [System.Net.Sockets.SocketException]) {
            return $true
        }

        $current = $current.InnerException
    }

    return $false
}

function Receive-HttpsFileWithRetry {
    param(
        [Parameter(Mandatory)][uri]$Uri,
        [Parameter(Mandatory)][string]$Path,
        [ValidateRange(1, 8)][int]$MaximumAttempts = 4
    )

    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        if (Test-Path -LiteralPath $Path) {
            Remove-Item -LiteralPath $Path -Force
        }

        try {
            Receive-HttpsFile -Uri $Uri -Path $Path
            return
        }
        catch {
            if ($attempt -eq $MaximumAttempts -or
                -not (Test-TransientDownloadException -Exception $_.Exception)) {
                throw
            }

            $delaySeconds = [int][Math]::Pow(2, $attempt)
            Write-Warning "Release model download attempt $attempt failed; retrying in $delaySeconds seconds."
            Start-Sleep -Seconds $delaySeconds
        }
    }
}

$manifestFullPath = [System.IO.Path]::GetFullPath($ManifestPath)
$destinationFullRoot = [System.IO.Path]::GetFullPath($DestinationRoot)
$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($manifest.id) -or
    [string]::IsNullOrWhiteSpace($manifest.version) -or
    [string]::IsNullOrWhiteSpace($manifest.fileName)) {
    throw "Release model manifest identity is incomplete."
}

Assert-SafePathSegment -Value ([string]$manifest.id) -FieldName 'id'
Assert-SafePathSegment -Value ([string]$manifest.version) -FieldName 'version'
Assert-SafePathSegment -Value ([string]$manifest.fileName) -FieldName 'fileName'
if (-not ([string]$manifest.fileName).EndsWith('.onnx', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release model manifest fileName must identify an ONNX file.'
}

$downloadUri = [uri]$manifest.downloadUrl
if (-not $downloadUri.IsAbsoluteUri -or $downloadUri.Scheme -ne [uri]::UriSchemeHttps) {
    throw "Release model manifest downloadUrl must be absolute HTTPS."
}

$expectedSha256 = ([string]$manifest.sha256).ToUpperInvariant()
if ($expectedSha256 -notmatch '^[A-F0-9]{64}$') {
    throw "Release model manifest SHA-256 is invalid."
}

$expectedLength = [long]$manifest.fileSizeBytes
if ($expectedLength -le 0) {
    throw "Release model manifest fileSizeBytes must be positive."
}

$modelDirectory = Join-Path (Join-Path $destinationFullRoot $manifest.id) $manifest.version
$finalPath = Join-Path $modelDirectory $manifest.fileName
$partialPath = "$finalPath.partial"
$destinationPrefix = $destinationFullRoot.TrimEnd('\') + '\'
if (-not [System.IO.Path]::GetFullPath($finalPath).StartsWith(
    $destinationPrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release model destination resolves outside the selected model root.'
}
New-Item -ItemType Directory -Force -Path $modelDirectory | Out-Null

if (Test-ModelFile -Path $finalPath -ExpectedLength $expectedLength -ExpectedSha256 $expectedSha256) {
    Write-Output $finalPath
    return
}

if (Test-Path -LiteralPath $finalPath) {
    Remove-Item -LiteralPath $finalPath -Force
}

if (Test-Path -LiteralPath $partialPath) {
    Remove-Item -LiteralPath $partialPath -Force
}

try {
    Receive-HttpsFileWithRetry -Uri $downloadUri -Path $partialPath
    if (-not (Test-ModelFile -Path $partialPath -ExpectedLength $expectedLength -ExpectedSha256 $expectedSha256)) {
        throw "Release model failed exact byte-length or SHA-256 verification."
    }

    Move-Item -LiteralPath $partialPath -Destination $finalPath
    Write-Output $finalPath
}
finally {
    if (Test-Path -LiteralPath $partialPath) {
        Remove-Item -LiteralPath $partialPath -Force
    }
}
