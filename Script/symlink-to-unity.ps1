Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-UnityPackagesPath {
	$envPath = Join-Path $PSScriptRoot 'dev.env'
	if (-not (Test-Path -LiteralPath $envPath -PathType Leaf)) {
		throw "Config file was not found: $envPath"
	}

	$unityProjectLine = Get-Content -LiteralPath $envPath |
		Where-Object { $_ -match '^\s*UNITYPROJECT\s*=' } |
		Select-Object -First 1

	if ($null -eq $unityProjectLine) {
		throw "Required setting 'UNITYPROJECT' was not found in $envPath"
	}

	$unityProjectPath = ($unityProjectLine -replace '^\s*UNITYPROJECT\s*=\s*', '').Trim().Trim('"', "'")
	if ([string]::IsNullOrWhiteSpace($unityProjectPath)) {
		throw "UNITYPROJECT is empty in $envPath"
	}

	if (-not (Test-Path -LiteralPath $unityProjectPath -PathType Container)) {
		throw "UNITYPROJECT does not exist: $unityProjectPath"
	}

	$unityPackagesPath = Join-Path (Resolve-Path -LiteralPath $unityProjectPath).ProviderPath 'Packages'
	if (-not (Test-Path -LiteralPath $unityPackagesPath -PathType Container)) {
		throw "Unity project's Packages directory was not found: $unityPackagesPath"
	}

	return $unityPackagesPath
}

function Resolve-LinkTargetPath {
	param(
		[Parameter(Mandatory = $true)]
		[System.IO.FileSystemInfo]$Item
	)

	$linkTarget = $Item.Target
	if ($linkTarget -is [System.Array]) {
		$linkTarget = $linkTarget[0]
	}

	if ([string]::IsNullOrWhiteSpace([string]$linkTarget)) {
		throw "Unable to determine the current link target for $($Item.FullName)"
	}

	if (-not [System.IO.Path]::IsPathRooted($linkTarget)) {
		$linkTarget = Join-Path $Item.Directory.FullName $linkTarget
	}

	return [System.IO.Path]::GetFullPath($linkTarget).TrimEnd([char]'\', [char]'/')
}

try {
	$sourcePackagePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Packages\marble810.marbleavatartools'
	if (-not (Test-Path -LiteralPath $sourcePackagePath -PathType Container)) {
		throw "Source package directory was not found: $sourcePackagePath"
	}

	$sourcePackagePath = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $sourcePackagePath).ProviderPath).TrimEnd([char]'\', [char]'/')
	$targetPackagePath = Join-Path (Get-UnityPackagesPath) 'marble810.marbleavatartools'
	$targetItem = Get-Item -LiteralPath $targetPackagePath -Force -ErrorAction SilentlyContinue

	if ($null -ne $targetItem) {
		if ($targetItem.LinkType -ne 'SymbolicLink') {
			throw "Target already exists and is not a symbolic link: $targetPackagePath"
		}

		$existingTargetPath = Resolve-LinkTargetPath -Item $targetItem
		if ($existingTargetPath -ne $sourcePackagePath) {
			throw "Target already exists as a symbolic link, but it points to a different location: $existingTargetPath"
		}

		Write-Host "Symbolic link already exists and points to the expected package."
		Write-Host "Source: $sourcePackagePath"
		Write-Host "Target: $targetPackagePath"
		return
	}

	New-Item -ItemType SymbolicLink -Path $targetPackagePath -Target $sourcePackagePath | Out-Null
	Write-Host "Created symbolic link."
	Write-Host "Source: $sourcePackagePath"
	Write-Host "Target: $targetPackagePath"
}
catch {
	Write-Error $_.Exception.Message
	exit 1
}