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

try {
	$targetPackagePath = Join-Path (Get-UnityPackagesPath) 'marble810.marbleavatartools'
	$targetItem = Get-Item -LiteralPath $targetPackagePath -Force -ErrorAction SilentlyContinue

	if ($null -eq $targetItem) {
		Write-Host "Target package link does not exist. Nothing to remove."
		Write-Host "Target: $targetPackagePath"
		return
	}

	if ($targetItem.LinkType -ne 'SymbolicLink') {
		throw "Target exists but is not a symbolic link: $targetPackagePath"
	}

	Remove-Item -LiteralPath $targetPackagePath -Force
	Write-Host "Removed symbolic link."
	Write-Host "Target: $targetPackagePath"
}
catch {
	Write-Error $_.Exception.Message
	exit 1
}