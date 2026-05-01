# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog,
and this project adheres to Semantic Versioning.

## [1.0.2] - 2026-05-02
### Added
- Added automatic notification workflow for refreshing the external VPM list repository after a successful package release.

### Changed
- Updated the package manifest metadata to publish under the Marble Avatar Toolbox package identity.
- Updated the repository listing workflow to use the renamed package identifier.
- Removed the unused sample declaration from the package manifest.

### Fixed
- Aligned release and listing metadata so future publishes resolve the correct package name.

## [1.0.1] - 2026-05-02
### Added
- Added GitHub Actions workflows for package release builds and VPM listing deployment.
- Added a local release preparation script and Website assets for package listing output.

### Changed
- Reorganized editor tools into the Packages/marble810.marbleavatartools/Editor/Tools layout.
- Updated repository documentation for the local release workflow.

### Fixed
- Allowed the release preparation script to bootstrap CHANGELOG.md when it is missing.
