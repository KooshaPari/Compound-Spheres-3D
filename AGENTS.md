# AGENTS.md — agent collaboration guide

This repo is the **Compound-Spheres** compound-mesh library — a Unity C# renderer
that draws large numbers of gpu instances (spheres / grids) as a single compound
mesh. It is the standalone projection of the compound-mesh concern that originally
lived inside `KooshaPari/WorldSphereMod`. It runs under Phenotype-org agent
conventions, adapted for a Unity C# library.

## Stack quick reference

- **Engine target**: Unity (C#), compute-shader backed GPU management
- **Language**: C# (.NET, Unity csproj: `CompoundMeshes/CompoundMeshes.csproj`)
- **Solution**: `CompoundMeshes.sln` (library + `Tests/` projects)
- **Build**: `dotnet build CompoundMeshes.sln -c Release`
- **Tests**: `dotnet test` (two projects: `CompoundMeshes.Tests`, `CompoundSpheres.Tests`)
  - `GridDimensionsPortTests` — port-parity of the grid-dimensions math
  - `ShapeParityTests` — shape parity of sphere/compound generation
  - `PureMath.cs` — shared deterministic math helpers for tests
- **GPU compute**: `Default Assets/CompoundSphereCompute.compute`

## Agent operational rules

### Manager-style orchestration

- Delegate implementation, build/log analysis, and review to subagents.
- Reserve the orchestrator for dispatching, integrating returns, committing, pushing.
- Parallelize non-overlapping files.

### Branching + commits

- Reachable work goes to `main` **only** via a pull request that passes `ci` (lint + test) and is reviewed.
- **Conventional Commits**: `fix:`, `feat:`, `docs:`, `chore:`, `perf:`, `test:`, `refactor:`.
- Keep commits atomic and scoped to one concern. `Co-Authored-By` footer for agent-driven commits.
- Fresh feature branches: `mut/…`, `fix/…`, `perf/…` off `main`.

### Library invariants (do not regress)

- `CompoundMeshes/` must remain **agnostic of WorldSphereMod** — no `WorldSphereMod.*` types, no WorldBox `Assembly-CSharp` references. This repo is the clean re-split.
- GPU backends live under `CompoundMeshes/Gpu/`. `GpuManagerBase` is the abstraction; concrete managers (e.g. `GpuSphereManager`) build on it. Keep the compat/`ILegacyManagerApi` + `LegacyManagerShim` interface stable for consumers.
- Height-field rendering, frustum culling, and mesh merging must keep determinism — the parity tests encode the guarantee.
- Preserve the `upstream/` frozen reference (3-way merge provenance in README reveals what is ours vs upstream).

### Never idle

While agents/CI are in flight, find non-conflicting work: verify the build is green, update docs, investigate the next concern.

## What's where

| Concern | Folder |
|---|---|
| Public + internal mesh managers | `CompoundMeshes/` (`SphereManager`, `MeshManager`, `StaticHandler`, `DynamicHandler`) |
| Height-field rendering | `CompoundMeshes/HeightFieldRenderer.cs` |
| GPU management | `CompoundMeshes/Gpu/` |
| Legacy API compatibility | `CompoundMeshes/Compat/` |
| Settings | `CompoundMeshes/ManagerSettings.cs`, `IGridDimensions.cs` |
| Compute source | `Default Assets/CompoundSphereCompute.compute` |
| Tests | `Tests/CompoundMeshes.Tests/`, `Tests/CompoundSpheres.Tests/` |
| CI | `.github/workflows/` |
| Tooling config | `.trunk/`, `trunk.yaml`, `.pre-commit-config.yaml`, `.mergify.yml`, `renovate.json` |

## When in doubt

1. Check the CI gate (`.github/workflows/ci.yml`) for what a PR must pass.
2. Check the README's 3-way-merge provenance before touching a file with an `upstream/` marker.
3. Check the `.claude/commands/*` scope for the intended sub-task.
4. If still uncertain, ask a clarifying question rather than guessing.