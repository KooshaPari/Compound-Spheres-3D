# Compound-Spheres Skill

Compound-Spheres is a Unity C# **compound-mesh library** (GPU-instanced sphere/grid
rendering via a single merged mesh + compute shader). It is the clean re-split of
the compound-mesh concern out of `KooshaPari/WorldSphereMod`.

## Core invariants

- **No WorldSphereMod / WorldBox coupling.** `CompoundMeshes/` only references
  Unity + .NET. Do not introduce `WorldSphereMod.*` or `Assembly-CSharp` types.
- **Determinism.** `MeshManager` + GPU managers must stay deterministic; the
  `ShapeParityTests` / `GridDimensionsPortTests` in `Tests/` encode this.
- **Backend abstraction.** GPU work goes through `Gpu/GpuManagerBase`;
  concrete managers (e.g. `GpuSphereManager`) build on it. Keep
  `Compat/ILegacyManagerApi` + `LegacyManagerShim` backwards-compatible.

## Commands

- `!build` → `dotnet build CompoundMeshes.sln -c Release`
- `!test` → `dotnet test CompoundMeshes.sln -c Release`
- `!validate` → run `dotnet test` + the parity/port NUnit suites, confirm 0 failures

## Layout

| Path | Purpose |
|---|---|
| `CompoundMeshes/` | library: SphereManager, MeshManager, Static/Dynamic handlers |
| `CompoundMeshes/Gpu/` | GPU backends (GpuManagerBase, GpuSphereManager, GpuShapeMath) |
| `CompoundMeshes/Compat/` | legacy API shim (ILegacyManagerApi, LegacyManagerShim) |
| `Default Assets/` | Unity compute shader |
| `Tests/` | NUnit parity + port tests (pure, deterministic) |
| `.github/workflows/` | ci + scorecard + infisical + trunk-check |

CI gate is `.github/workflows/ci.yml` (lint + test). Follow Conventional Commits;
changes land via reviewed PRs on protected `main`.