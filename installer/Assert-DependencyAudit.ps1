[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$vulnerablePackages = @()
foreach ($path in $ReportPath) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Dependency audit report does not exist: $fullPath"
    }

    $report = Get-Content -LiteralPath $fullPath -Raw | ConvertFrom-Json
    $problemProperty = $report.PSObject.Properties['problems']
    if ($null -ne $problemProperty -and @($problemProperty.Value).Count -gt 0) {
        $messages = @($problemProperty.Value | ForEach-Object { $_.text })
        throw "Dependency audit could not evaluate '$fullPath': $($messages -join '; ')"
    }

    foreach ($project in @($report.projects)) {
        $frameworkProperty = $project.PSObject.Properties['frameworks']
        if ($null -eq $frameworkProperty) {
            continue
        }

        foreach ($framework in @($frameworkProperty.Value)) {
            $topLevelProperty = $framework.PSObject.Properties['topLevelPackages']
            $transitiveProperty = $framework.PSObject.Properties['transitivePackages']
            $packages = @()
            if ($null -ne $topLevelProperty) {
                $packages += @($topLevelProperty.Value)
            }
            if ($null -ne $transitiveProperty) {
                $packages += @($transitiveProperty.Value)
            }
            foreach ($package in $packages) {
                $vulnerabilityProperty = if ($null -ne $package) {
                    $package.PSObject.Properties['vulnerabilities']
                }
                else {
                    $null
                }
                if ($null -ne $vulnerabilityProperty -and @($vulnerabilityProperty.Value).Count -gt 0) {
                    $vulnerablePackages += [pscustomobject]@{
                        Project = $project.path
                        Framework = $framework.framework
                        Package = $package.id
                        Version = $package.resolvedVersion
                        Advisories = @($vulnerabilityProperty.Value).Count
                    }
                }
            }
        }
    }
}

if ($vulnerablePackages.Count -gt 0) {
    $vulnerablePackages | Format-Table -AutoSize | Out-Host
    throw "$($vulnerablePackages.Count) vulnerable dependency entries were reported."
}

[pscustomobject]@{
    Reports = $ReportPath.Count
    VulnerablePackages = 0
    Result = 'PASS'
}
