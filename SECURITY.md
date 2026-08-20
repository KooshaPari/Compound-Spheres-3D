# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability within CompoundMeshes, please send an email to KooshaPari via GitHub. All security vulnerabilities will be promptly addressed.

## Scope

This security policy covers the CompoundMeshes library code in this repository. It does not cover:

- Third-party dependencies (see `renovate.json` for automated dependency updates)
- The WorldSphereMod project (see its own SECURITY.md)

## Dependency Security

This repository uses:

- **Renovate** for automated dependency update PRs
- **OpenSSF Scorecard** for security analysis
- **GitHub Dependency Review** for PR-level dependency scanning

## Best Practices

- All PRs require at least 1 approving review
- Branch protection enforces linear history
- CI runs `dotnet format` and `dotnet test` on every PR
