[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('major', 'minor', 'patch')]
    [string]$Bump,

    [switch]$AllowDirty
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageJsonPath = Join-Path $repoRoot 'Packages/marble810.marbleavatartools/package.json'
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
$releaseInputPaths = @(
    'CHANGELOG.md'
)
$releaseProtectedPaths = @(
    'Packages/marble810.marbleavatartools/package.json'
)

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [switch]$AllowFailure
    )

    $output = & git -C $repoRoot @Arguments 2>&1
    $exitCode = $LASTEXITCODE

    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "git $($Arguments -join ' ') failed.`n$($output | Out-String)"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = ($output | Out-String).TrimEnd()
    }
}

function Read-PackageVersion {
    if (-not (Test-Path -LiteralPath $packageJsonPath)) {
        throw "Package manifest not found at $packageJsonPath"
    }

    $packageJson = Get-Content -LiteralPath $packageJsonPath -Raw | ConvertFrom-Json
    return [string]$packageJson.version
}

function Get-ValidatedBump {
    if ($Bump) {
        return $Bump
    }

    $interactiveBump = Read-Host 'Version bump (major/minor/patch)'
    if ($interactiveBump -notin @('major', 'minor', 'patch')) {
        throw 'Bump must be one of: major, minor, patch.'
    }

    return $interactiveBump
}

function Get-NextVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version,

        [Parameter(Mandatory = $true)]
        [string]$VersionBump
    )

    if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
        throw "Current version '$Version' is not a valid SemVer version."
    }

    [int]$major = $Matches[1]
    [int]$minor = $Matches[2]
    [int]$patch = $Matches[3]

    switch ($VersionBump) {
        'major' {
            $major += 1
            $minor = 0
            $patch = 0
        }
        'minor' {
            $minor += 1
            $patch = 0
        }
        'patch' {
            $patch += 1
        }
        default {
            throw "Unsupported bump '$VersionBump'."
        }
    }

    return "$major.$minor.$patch"
}

function Get-VersionSection {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ChangelogContent,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $escapedVersion = [regex]::Escape($Version)
    $pattern = "(?ms)^## \[$escapedVersion\] - .*?(?=^## \[|\z)"
    $match = [regex]::Match($ChangelogContent, $pattern)
    if ($match.Success) {
        return $match.Value
    }

    return $null
}

function New-VersionTemplate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $dateStamp = Get-Date -Format 'yyyy-MM-dd'
    return @"
## [$Version] - $dateStamp
### Added
- TODO: describe changes

### Changed
- TODO: describe changes

### Fixed
- TODO: describe changes
"@
}

function Add-VersionSection {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ChangelogContent,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $template = (New-VersionTemplate -Version $Version).TrimEnd()
    $firstVersionHeader = [regex]::Match($ChangelogContent, '(?m)^## \[')

    if ($firstVersionHeader.Success) {
        $prefix = $ChangelogContent.Substring(0, $firstVersionHeader.Index).TrimEnd()
        $suffix = $ChangelogContent.Substring($firstVersionHeader.Index).TrimStart()
        return ($prefix, '', $template, '', $suffix) -join [Environment]::NewLine
    }

    return ($ChangelogContent.TrimEnd(), '', $template) -join [Environment]::NewLine
}

function Test-VersionSectionReady {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Section
    )

    if ($Section -match 'TODO: describe changes') {
        return $false
    }

    return [regex]::IsMatch($Section, '(?m)^-\s+\S+')
}

function Get-DirtyPaths {
    $status = Invoke-Git -Arguments @('status', '--porcelain')
    if ([string]::IsNullOrWhiteSpace($status.Output)) {
        return @()
    }

    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($line in ($status.Output -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.Length -lt 4) {
            continue
        }

        $pathText = $line.Substring(3).Trim()
        if ([string]::IsNullOrWhiteSpace($pathText)) {
            continue
        }

        if ($pathText.Contains(' -> ')) {
            $pathText = ($pathText -split ' -> ', 2)[1]
        }

        $path = $pathText.Replace('\\', '/')
        if ($paths -notcontains $path) {
            $paths.Add($path)
        }
    }

    return $paths.ToArray()
}

function Assert-WorkingTreeReady {
    if ($AllowDirty) {
        return
    }

    $dirtyPaths = Get-DirtyPaths
    if ($dirtyPaths.Count -eq 0) {
        return
    }

    $protectedDirtyPaths = @($dirtyPaths | Where-Object { $_ -in $releaseProtectedPaths })
    $unmanagedDirtyPaths = @($dirtyPaths | Where-Object { $_ -notin ($releaseInputPaths + $releaseProtectedPaths) })

    if ($protectedDirtyPaths.Count -gt 0) {
        throw "Release-protected files already contain changes: $($protectedDirtyPaths -join ', '). Commit or stash them first, or rerun with -AllowDirty."
    }

    if ($unmanagedDirtyPaths.Count -gt 0) {
        Write-Warning "Working tree contains unrelated changes that will not be included in the release commit: $($unmanagedDirtyPaths -join ', ')"
    }
}

function Assert-TagAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $localTag = Invoke-Git -Arguments @('rev-parse', '-q', '--verify', "refs/tags/$Version") -AllowFailure
    if ($localTag.ExitCode -eq 0) {
        throw "Local tag '$Version' already exists."
    }

    $remoteTag = Invoke-Git -Arguments @('ls-remote', '--tags', 'origin', $Version) -AllowFailure
    if ($remoteTag.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($remoteTag.Output)) {
        throw "Remote tag '$Version' already exists on origin."
    }
}

if (-not (Test-Path -LiteralPath $changelogPath)) {
    throw "CHANGELOG not found at $changelogPath"
}

$bumpChoice = Get-ValidatedBump
$currentVersion = Read-PackageVersion
$targetVersion = Get-NextVersion -Version $currentVersion -VersionBump $bumpChoice
$changelogContent = Get-Content -LiteralPath $changelogPath -Raw
$versionSection = Get-VersionSection -ChangelogContent $changelogContent -Version $targetVersion

if (-not $versionSection) {
    $updatedChangelog = Add-VersionSection -ChangelogContent $changelogContent -Version $targetVersion
    Set-Content -LiteralPath $changelogPath -Value $updatedChangelog
    Write-Host "Created CHANGELOG section for $targetVersion in $changelogPath"
    Write-Host 'Fill in the new version section, then rerun this script with the same bump type to publish.'
    exit 0
}

if (-not (Test-VersionSectionReady -Section $versionSection)) {
    throw "CHANGELOG section for $targetVersion is still empty or contains TODO placeholders."
}

Assert-WorkingTreeReady
Assert-TagAvailable -Version $targetVersion

$packageJsonContent = Get-Content -LiteralPath $packageJsonPath -Raw
$versionPattern = '"version"\s*:\s*"' + [regex]::Escape($currentVersion) + '"'
$updatedPackageJsonContent = [regex]::Replace(
    $packageJsonContent,
    $versionPattern,
    '"version": "' + $targetVersion + '"',
    1
)

if ($updatedPackageJsonContent -eq $packageJsonContent) {
    throw "Failed to update package version from $currentVersion to $targetVersion."
}

Set-Content -LiteralPath $packageJsonPath -Value $updatedPackageJsonContent -NoNewline

Invoke-Git -Arguments @('add', '--', 'CHANGELOG.md', 'Packages/marble810.marbleavatartools/package.json') | Out-Null
Invoke-Git -Arguments @('commit', '-m', "release: $targetVersion") | Out-Null
Invoke-Git -Arguments @('tag', $targetVersion) | Out-Null
Invoke-Git -Arguments @('push', 'origin', 'HEAD') | Out-Null
Invoke-Git -Arguments @('push', 'origin', $targetVersion) | Out-Null

Write-Host "Released $targetVersion. The remote tag should trigger the GitHub Actions release workflow."