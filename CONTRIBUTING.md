# Contributing to CompoundMeshes

Thank you for your interest in contributing to CompoundMeshes!

## Prerequisites

- .NET 8.0 SDK (for running tests)
- .NET Framework 4.8 SDK (for building the main library, requires WorldBox)

## Building

```bash
dotnet build CompoundMeshes.sln -c Release
```

## Testing

```bash
dotnet test Tests/CompoundMeshes.Tests/
```

## Code Style

- Follow the existing code conventions
- Run `dotnet format --verify-no-changes` before submitting
- CI enforces formatting via the `ci / lint` gate

## Pull Request Process

1. Create a branch from `main`
2. Make your changes
3. Ensure CI passes (`ci / lint` + `ci / test`)
4. Request a review
5. After approval, your PR will be merged with linear history

## Branch Protection

- All PRs require 1 approving review
- Linear history is enforced (no merge commits)
- Branch must be up-to-date before merge
