[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$StageRoot,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$namespace = 'http://wixtoolset.org/schemas/v4/wxs'
$stageFull = [System.IO.Path]::GetFullPath($StageRoot).TrimEnd('\')
$outputFull = [System.IO.Path]::GetFullPath($OutputPath)

if (-not (Test-Path -LiteralPath $stageFull -PathType Container)) {
    throw "Installer stage does not exist: $stageFull"
}

$files = @(Get-ChildItem -LiteralPath $stageFull -Recurse -File | Sort-Object FullName)
if ($files.Count -eq 0) {
    throw "Installer stage is empty: $stageFull"
}

function Get-StableHash {
    param(
        [Parameter(Mandatory)][string]$Value,
        [ValidateRange(8, 64)][int]$Length = 20
    )

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
        $hash = $sha256.ComputeHash($bytes)
        $hex = ([System.BitConverter]::ToString($hash)).Replace('-', '')
        return $hex.Substring(0, $Length).ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-StableGuid {
    param([Parameter(Mandatory)][string]$Value)

    $hash = Get-StableHash -Value "cutlocal-msi-component:$Value" -Length 32
    return '{0}-{1}-5{2}-8{3}-{4}' -f `
        $hash.Substring(0, 8), `
        $hash.Substring(8, 4), `
        $hash.Substring(13, 3), `
        $hash.Substring(17, 3), `
        $hash.Substring(20, 12)
}

function Get-RelativePath {
    param([Parameter(Mandatory)][string]$FullName)

    return $FullName.Substring($stageFull.Length).TrimStart('\')
}

function Get-FileGroup {
    param([Parameter(Mandatory)][string]$RelativePath)

    if ($RelativePath.StartsWith('models\u2netp\', [System.StringComparison]::OrdinalIgnoreCase)) {
        return 'FastModelFiles'
    }

    return 'CoreFiles'
}

$directoryMap = @{}
$directoryRecords = @()
foreach ($file in $files) {
    $relativePath = Get-RelativePath -FullName $file.FullName
    $relativeDirectory = Split-Path -Parent $relativePath
    while (-not [string]::IsNullOrWhiteSpace($relativeDirectory)) {
        $key = $relativeDirectory.ToLowerInvariant()
        if (-not $directoryMap.ContainsKey($key)) {
            $parent = Split-Path -Parent $relativeDirectory
            $record = [pscustomobject]@{
                RelativePath = $relativeDirectory
                Parent = $parent
                Name = Split-Path -Leaf $relativeDirectory
                Id = 'dir_' + (Get-StableHash -Value "directory:$relativeDirectory")
            }
            $directoryMap[$key] = $record
            $directoryRecords += $record
        }

        $relativeDirectory = Split-Path -Parent $relativeDirectory
    }
}

function Write-DirectoryChildren {
    param(
        [Parameter(Mandatory)][System.Xml.XmlWriter]$Writer,
        [AllowEmptyString()][string]$Parent
    )

    $children = @($directoryRecords |
        Where-Object { $_.Parent -eq $Parent } |
        Sort-Object Name)
    foreach ($child in $children) {
        $Writer.WriteStartElement('Directory', $namespace)
        $Writer.WriteAttributeString('Id', $child.Id)
        $Writer.WriteAttributeString('Name', $child.Name)
        Write-DirectoryChildren -Writer $Writer -Parent $child.RelativePath
        $Writer.WriteEndElement()
    }
}

$outputDirectory = Split-Path -Parent $outputFull
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$writer = [System.Xml.XmlWriter]::Create($outputFull, $settings)
try {
    $writer.WriteStartDocument()
    $writer.WriteStartElement('Wix', $namespace)

    $writer.WriteStartElement('Fragment', $namespace)
    $writer.WriteStartElement('DirectoryRef', $namespace)
    $writer.WriteAttributeString('Id', 'INSTALLFOLDER')
    Write-DirectoryChildren -Writer $writer -Parent ''
    $writer.WriteEndElement()
    $writer.WriteEndElement()

    foreach ($groupName in @('CoreFiles', 'FastModelFiles')) {
        $groupFiles = @($files | Where-Object {
            (Get-FileGroup -RelativePath (Get-RelativePath -FullName $_.FullName)) -eq $groupName
        })
        if ($groupFiles.Count -eq 0) {
            continue
        }

        $writer.WriteStartElement('Fragment', $namespace)
        $writer.WriteStartElement('ComponentGroup', $namespace)
        $writer.WriteAttributeString('Id', $groupName)

        $groupedByDirectory = @($groupFiles | Group-Object {
            Split-Path -Parent (Get-RelativePath -FullName $_.FullName)
        } | Sort-Object Name)
        foreach ($directoryGroup in $groupedByDirectory) {
            $relativeDirectory = $directoryGroup.Name
            $directoryId = if ([string]::IsNullOrWhiteSpace($relativeDirectory)) {
                'INSTALLFOLDER'
            }
            else {
                $directoryMap[$relativeDirectory.ToLowerInvariant()].Id
            }
            $componentKey = "$groupName/$relativeDirectory"
            $componentId = 'cmp_' + (Get-StableHash -Value "component:$componentKey")

            $writer.WriteStartElement('Component', $namespace)
            $writer.WriteAttributeString('Id', $componentId)
            $writer.WriteAttributeString('Directory', $directoryId)
            $writer.WriteAttributeString('Guid', (Get-StableGuid -Value $componentKey))

            foreach ($file in @($directoryGroup.Group | Sort-Object FullName)) {
                $relativePath = Get-RelativePath -FullName $file.FullName
                $writer.WriteStartElement('File', $namespace)
                $writer.WriteAttributeString('Id', ('fil_' + (Get-StableHash -Value "file:$relativePath")))
                $writer.WriteAttributeString('Source', $file.FullName)
                $writer.WriteEndElement()
            }

            if ($groupName -eq 'CoreFiles' -and
                [string]::IsNullOrWhiteSpace($relativeDirectory)) {
                $cleanupDirectories = @($directoryRecords | Sort-Object {
                    $_.RelativePath.Split('\').Count
                } -Descending)
                foreach ($cleanupDirectory in $cleanupDirectories) {
                    $writer.WriteStartElement('RemoveFolder', $namespace)
                    $writer.WriteAttributeString(
                        'Id',
                        ('rmf_' + (Get-StableHash -Value "remove-folder:$($cleanupDirectory.RelativePath)")))
                    $writer.WriteAttributeString('Directory', $cleanupDirectory.Id)
                    $writer.WriteAttributeString('On', 'uninstall')
                    $writer.WriteEndElement()
                }

                foreach ($directoryId in @('INSTALLFOLDER', 'LocalProgramsFolder')) {
                    $writer.WriteStartElement('RemoveFolder', $namespace)
                    $writer.WriteAttributeString(
                        'Id',
                        ('rmf_' + (Get-StableHash -Value "remove-folder:$directoryId")))
                    $writer.WriteAttributeString('Directory', $directoryId)
                    $writer.WriteAttributeString('On', 'uninstall')
                    $writer.WriteEndElement()
                }
            }

            $writer.WriteStartElement('RegistryValue', $namespace)
            $writer.WriteAttributeString('Root', 'HKCU')
            $writer.WriteAttributeString('Key', 'Software\CutLocal\Installer\Components')
            $writer.WriteAttributeString('Name', $componentId)
            $writer.WriteAttributeString('Type', 'integer')
            $writer.WriteAttributeString('Value', '1')
            $writer.WriteAttributeString('KeyPath', 'yes')
            $writer.WriteEndElement()

            $writer.WriteEndElement()
        }

        $writer.WriteEndElement()
        $writer.WriteEndElement()
    }

    $writer.WriteEndElement()
    $writer.WriteEndDocument()
}
finally {
    $writer.Dispose()
}

[pscustomobject]@{
    Source = $outputFull
    Files = $files.Count
    Directories = $directoryRecords.Count
    CoreFiles = @($files | Where-Object {
        (Get-FileGroup -RelativePath (Get-RelativePath -FullName $_.FullName)) -eq 'CoreFiles'
    }).Count
    FastModelFiles = @($files | Where-Object {
        (Get-FileGroup -RelativePath (Get-RelativePath -FullName $_.FullName)) -eq 'FastModelFiles'
    }).Count
}
