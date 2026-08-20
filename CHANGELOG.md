# Changelog

All notable changes to CompoundMeshes will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1] - 2026-08-19

### Fixed
- Trunk Check: narrowed linter set to C#-relevant only (removed black, clippy, eslint, golangci-lint, mypy, ruff, rustfmt)
- CI: updated `actions/checkout` from `@v4` to `@v7` (Node.js 24 compatibility)

### Added
- CI-LIMITATIONS.md documenting known CI gaps
- `.editorconfig` for consistent formatting
- `CHANGELOG.md` (this file)

## [1.0.0] - 2026-08-19

### Added
- AGENTS.md governor + `.claude/` skill/commands entrypoint
- CI: C# detection + `dotnet format` + `dotnet test` via required `ci / lint` gate
- Branch protection: `ci / lint` + `ci / test`, 1 review, linear history, strict
- AgilePlus dispatch + mirror scripts (`Tools/agileplus-dispatch.ps1`, `Tools/agileplus-mirror.ps1`)
- SECURITY.md + CONTRIBUTING.md + PR template
- Renovate dependency automation
- OSSF Scorecard workflow
- Mergify auto-merge configuration

### Changed
- Removed duplicate `Tests/CompoundSpheres.Tests/` project (byte-identical to `CompoundMeshes.Tests/`)

### Upstream
- Forked from [MelvinShwuaner/CompoundMeshes](https://github.com/MelvinShwuaner/CompoundMeshes)
