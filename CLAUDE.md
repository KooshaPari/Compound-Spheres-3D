# Compound-Spheres-3D — Agent Context

## What This Repo Is
A C# compound-meshes library (`CompoundMeshes.dll`) providing sphere management,
GpuSphereManager, MeshManager, height-field renderer, and frustum culler for
WorldSphereMod terrain/GPU sphere management.

## Build
```bash
dotnet build CompoundMeshes.sln -c Release
```

## Test
```bash
dotnet test Tests/CompoundMeshes.Tests/
```

## Key Files
- `CompoundMeshes/CompoundMeshes.csproj` — main library (net4.8)
- `CompoundMeshes.sln` — solution file
- `Tests/CompoundMeshes.Tests/` — xunit tests (net8.0, no Unity dependency)
- `Tools/agileplus-dispatch.ps1` — DAG-driven agent dispatch
- `Tools/agileplus-mirror.ps1` — master tracker mirror

## Agent Rules
- All changes go through `ci / lint` + `ci / test` gates
- Branch protection: strict up-to-date, 1 review, linear history
- Run `dotnet format --verify-no-changes` before committing
