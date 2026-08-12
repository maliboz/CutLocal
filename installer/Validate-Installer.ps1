[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installerFull = [System.IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $installerFull -PathType Leaf)) {
    throw "Installer does not exist: $installerFull"
}

function Invoke-ComMethod {
    param(
        [Parameter(Mandatory)][object]$Target,
        [Parameter(Mandatory)][string]$Name,
        [AllowNull()][object[]]$Arguments
    )

    return $Target.GetType().InvokeMember(
        $Name,
        [System.Reflection.BindingFlags]::InvokeMethod,
        $null,
        $Target,
        $Arguments)
}

$installer = $null
$database = $null
try {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = Invoke-ComMethod -Target $installer -Name 'OpenDatabase' -Arguments @($installerFull, 0)

    function Get-MsiColumn {
        param([Parameter(Mandatory)][string]$Query)

        $view = $null
        try {
            $view = Invoke-ComMethod -Target $database -Name 'OpenView' -Arguments @($Query)
            [void](Invoke-ComMethod -Target $view -Name 'Execute' -Arguments $null)
            $values = @()
            while ($true) {
                $record = Invoke-ComMethod -Target $view -Name 'Fetch' -Arguments $null
                if ($null -eq $record) {
                    break
                }

                try {
                    $values += [string]$record.GetType().InvokeMember(
                        'StringData',
                        [System.Reflection.BindingFlags]::GetProperty,
                        $null,
                        $record,
                        @(1))
                }
                finally {
                    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
                }
            }

            return $values
        }
        finally {
            if ($null -ne $view) {
                try {
                    [void](Invoke-ComMethod -Target $view -Name 'Close' -Arguments $null)
                }
                finally {
                    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
                }
            }
        }
    }

    function Get-SingleMsiValue {
        param([Parameter(Mandatory)][string]$Query)

        $values = @(Get-MsiColumn -Query $Query)
        if ($values.Count -ne 1) {
            throw "MSI query did not return exactly one scalar value: $Query"
        }

        return [string]$values[0]
    }

    $version = Get-SingleMsiValue -Query "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = 'ProductVersion'"
    if ($version -ne $ExpectedVersion) {
        throw "MSI version '$version' does not match expected '$ExpectedVersion'."
    }

    $upgradeCode = Get-SingleMsiValue -Query "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = 'UpgradeCode'"
    if ($upgradeCode -ne '{23B936FD-DCB0-4A4F-9B8A-7C1720DD43C6}') {
        throw "MSI UpgradeCode is unexpected: $upgradeCode"
    }

    $fileNames = @(Get-MsiColumn -Query 'SELECT `FileName` FROM `File`')
    $fileCount = $fileNames.Count
    if ($fileCount -lt 450) {
        throw "MSI contains only $fileCount files; the self-contained payload is incomplete."
    }

    $componentCount = @(Get-MsiColumn -Query 'SELECT `Component` FROM `Component`').Count
    $shortcutCount = @(Get-MsiColumn -Query 'SELECT `Shortcut` FROM `Shortcut`').Count
    $upgradeRows = @(Get-MsiColumn -Query 'SELECT `UpgradeCode` FROM `Upgrade`').Count
    if ($shortcutCount -ne 2) {
        throw "MSI must define Start Menu and optional Desktop shortcuts; found $shortcutCount."
    }
    if ($upgradeRows -lt 1) {
        throw 'MSI has no major-upgrade detection row.'
    }

    $features = @(Get-MsiColumn -Query 'SELECT `Feature` FROM `Feature`')
    foreach ($requiredFeature in @('ApplicationFeature', 'FastModelFeature', 'DesktopShortcutFeature')) {
        if ($features -notcontains $requiredFeature) {
            throw "MSI is missing required feature '$requiredFeature'."
        }
    }

    if ($features -contains 'BalancedModelFeature') {
        throw 'The installer must not expose a restricted BiRefNet weight as a bundled feature.'
    }

    $fastModelRows = @($fileNames | Where-Object { $_ -like '*u2netp.onnx*' }).Count
    if ($fastModelRows -ne 1) {
        throw "MSI must contain exactly one U2NetP model payload; found $fastModelRows."
    }

    $balancedModelRows = @($fileNames | Where-Object { $_ -like '*birefnet-general-lite.onnx*' }).Count
    if ($balancedModelRows -ne 0) {
        throw "Restricted BiRefNet model weights must not be bundled; found $balancedModelRows payload rows."
    }

    $cabinet = Get-SingleMsiValue -Query 'SELECT `Cabinet` FROM `Media` WHERE `DiskId` = 1'
    if (-not $cabinet.StartsWith('#', [System.StringComparison]::Ordinal)) {
        throw "MSI cabinet is not embedded: $cabinet"
    }

    [pscustomobject]@{
        Installer = $installerFull
        Version = $version
        Files = $fileCount
        Components = $componentCount
        Shortcuts = $shortcutCount
        MajorUpgradeRows = $upgradeRows
        RestrictedModelsBundled = $false
        EmbeddedCabinet = $true
    }
}
finally {
    if ($null -ne $database) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    }
    if ($null -ne $installer) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
    }
}
