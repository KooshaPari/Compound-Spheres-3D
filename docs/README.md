# Compound-Spheres-3D Documentation

## Overview
CompoundMeshes is a C# library providing compound-mesh management for
WorldSphereMod terrain and GPU sphere rendering.

## Architecture
- `CompoundMeshes/` — core library (SphereManager, GpuSphereManager, MeshManager)
- `Tests/CompoundMeshes.Tests/` — unit tests (GridDimensionsPortTests, ShapeParityTests)

## Build
See [BUILD.md](../BUILD.md) or run:
```bash
dotnet build CompoundMeshes.sln -c Release
```

## Testing
```bash
dotnet test Tests/CompoundMeshes.Tests/
```
