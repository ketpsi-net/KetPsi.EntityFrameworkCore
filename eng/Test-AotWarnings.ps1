<#
.SYNOPSIS
    Checks the trim and AOT warnings of a Native AOT publish against a checked-in baseline.

.DESCRIPTION
    Any application that uses EF Core gets trim and AOT warnings from EF Core and SqlClient that it cannot fix. The
    baseline lists those, so the publish stays a gate: it fails on any warning that is not in the baseline, and on any
    warning that mentions KetPsi, which must have none. Entries that no longer occur are reported so the baseline can
    shrink. Warnings are compared without file paths, line numbers or project names.

.PARAMETER LogPath
    The output of `dotnet publish`.

.PARAMETER BaselinePath
    The baseline file: one warning per line; lines starting with '#' are comments.

.PARAMETER Update
    Writes the warnings found in the log as the new baseline instead of checking them.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $LogPath,
    [Parameter(Mandatory)] [string] $BaselinePath,
    [switch] $Update
)

$ErrorActionPreference = 'Stop'

$warningPattern = 'warning (IL\d{4}): (.*?)(\s+\[[^\]]*\])?\s*$'
$baselineHeader = @(
    '# Trim and AOT warnings the AOT sample is allowed to produce, as reported by eng/Test-AotWarnings.ps1.',
    '# All of them come from EF Core and SqlClient. Regenerate with -Update after an EF Core or SDK upgrade.'
)

function Get-Warnings([string] $path) {
    $found = foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -match $warningPattern) {
            "$($Matches[1]): $($Matches[2].Trim())"
        }
    }

    return @($found | Sort-Object -Unique)
}

$actual = Get-Warnings $LogPath

if ($Update) {
    $directory = Split-Path -Parent $BaselinePath
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }

    Set-Content -LiteralPath $BaselinePath -Value ($baselineHeader + $actual) -Encoding utf8
    Write-Host "Wrote $($actual.Count) warning(s) to $BaselinePath"
    exit 0
}

if (-not (Test-Path $BaselinePath)) {
    Write-Error "The baseline $BaselinePath does not exist. Create it with -Update."
}

$baseline = @(Get-Content -LiteralPath $BaselinePath | Where-Object { $_ -and -not $_.StartsWith('#') })

$ketPsi = @($actual | Where-Object { $_ -match 'KetPsi' })
$unexpected = @($actual | Where-Object { $baseline -notcontains $_ })
$resolved = @($baseline | Where-Object { $actual -notcontains $_ })

foreach ($warning in $resolved) {
    Write-Host "No longer reported, remove from the baseline: $warning"
}

foreach ($warning in $ketPsi) {
    Write-Host "KetPsi must produce no trim or AOT warnings: $warning"
}

foreach ($warning in $unexpected) {
    Write-Host "Not in the baseline: $warning"
}

if ($ketPsi.Count -gt 0 -or $unexpected.Count -gt 0) {
    Write-Host "Failed: $($unexpected.Count) unexpected warning(s), $($ketPsi.Count) from KetPsi."
    exit 1
}

Write-Host "Passed: $($actual.Count) warning(s), all in the baseline and none from KetPsi."
