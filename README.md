<!-- AI-DD-META:START -->
<!-- This repository is planned, maintained, and managed by AI Agents only. -->
<!-- Slop issues are expected and intentionally present as part of an HITL-less -->
<!-- /minimized AI-DD metaproject of learning, refining, and building brute-force -->
<!-- training for both agents and the human operator. -->
![Downloads](https://img.shields.io/github/downloads/KooshaPari/Compound-Spheres-3D/total?style=flat-square&label=downloads&color=blue) [![AI slop inside](https://sladge.net/badge.svg)](https://sladge.net)
![GitHub release](https://img.shields.io/github/v/release/KooshaPari/Compound-Spheres-3D?style=flat-square&label=release)
![License](https://img.shields.io/github/license/KooshaPari/Compound-Spheres-3D?style=flat-square)
![AI-Slop](https://img.shields.io/badge/AI--DD-Slop%20Expected-orange?style=flat-square)
![AI-Only-Maintained](https://img.shields.io/badge/Planned%20%26%20Maintained%20by-AI%20Agents%20Only-red?style=flat-square)
![HITL-less](https://img.shields.io/badge/HITL--less%20AI--DD-metaproject-yellow?style=flat-square)

> ⚠️ **AI-Agent-Only Repository**
>
> This repo is **planned, maintained, and managed exclusively by AI Agents**.
> Slop issues, rough edges, and AI artifacts are **expected and intentionally
> present** as part of an **HITL-less / minimized AI-DD** metaproject focused
> on learning, refining, and brute-force training both the agents and the
> human operator. Bug reports and contributions are still welcome, but please
> expect AI-generated code, comments, and documentation throughout.
<!-- AI-DD-META:END -->

# Compound-Spheres-3D (Canonical)

Canonical home of the WSM3D-extended fork of [MelvinShwuaner/Compound-Spheres](https://github.com/MelvinShwuaner/Compound-Spheres).
This repository is the 3-way merge of three sources:

| Source | Role | Branch / SHA |
|---|---|---|
| `MelvinShwuaner/Compound-Spheres` | original Unity tool | `main` @ `a642e27` |
| `KooshaPari/Compound-Spheres-3D` (live) | WSM3D fork with `FrustumCuller`, `HeightFieldRenderer`, `IGridDimensions` and CI workflows | `wsm3d/main` @ `e1953bb` |
| `KooshaPari/Compound-Spheres-3D-Backup` | evolved superset (perf + sota GPU compute merged into `wsm3d/main`) | `wsm3d/main` @ `86f75d0` |

The merge was authored on 2026-08-10 by `forge@kooshapari.local` as Decision 1 of the absorption run.
The previous split into a separate `live` and `backup` repo is **collapsed**: the live fork was deleted
and its bare clone is preserved only as a local remote for diffing.

## Repository layout

```
Compound-Spheres-3D/
├── CompoundSpheres/          # WSM3D-extended Unity package (ACTIVE)
│   ├── Compat/               # LegacyManagerShim + ILegacyManagerApi (pre-IGridDimensions seam)
│   ├── Gpu/                  # GPU compute sphere manager (GpuSphereManager, GpuManagerBase, GpuShapeMath, GpuBufferUtils)
│   ├── BufferUtils.cs        # BufferBase + UpdateBuffer chunked refresh
│   ├── FrustumCuller.cs      # per-row frustum test drawn before DrawTiles
│   ├── HeightFieldRenderer.cs# corner-averaged terrain + water sub-mesh
│   ├── IGridDimensions.cs    # abstract port: HasDirtyHeights / SnapshotDirtyHeights / TotalTiles
│   ├── SphereManager.cs      # IGridDimensions impl, SnapshotDirtyHeights, IGridDimensions wiring
│   └── ...
├── CompoundSpheres.sln       # Visual Studio solution for the WSM3D fork
├── Tests/                    # CompoundSpheres.Tests (xDD parity tests, 19 tests)
├── Default Assets/           # CompoundSphereCompute.compute (3-shape parity go-live kernel)
├── upstream/                 # FROZEN upstream reference: where MelvinShwuaner migrated to
│   ├── CompoundMeshes/       # upstream's MeshManager / DynamicHandler / StaticHandler migration
│   ├── CompoundMeshes.sln    # upstream's solution file
│   └── README.upstream.md    # upstream's original README
├── .circleci/                # CI workflow (org template)
├── .github/                  # workflows/, stale.yml
├── .pre-commit-config.yaml
├── .trunk/trunk.yaml
├── .mergify.yml
├── renovate.json
├── trunk.yaml
└── README.md                 # you are here
```

## What was merged

### From upstream (MelvinShwuaner/Compound-Spheres) → kept as `upstream/`
- `CompoundMeshes/` — the upstream migration (MeshManager, BufferBase, DynamicHandler,
  StaticHandler, native-array buffer). Live/backup never picked this up because they
  are post-`WorldSphereMod` lineage and pinned to `CompoundSpheres/`. Preserved as a
  reference, **not** built into `CompoundSpheres.sln`.
- `CompoundMeshes.sln` — upstream's solution file (preserved alongside for the same reason).
- `upstream/README.upstream.md` — upstream's own README frozen.

### From live (KooshaPari/Compound-Spheres-3D wsm3d/main) → selective
- `.pre-commit-config.yaml` — pre-commit hooks (live-only, no equivalent in backup or upstream)
- `.trunk/trunk.yaml` — trunk linter config (live-only)
- `.github/stale.yml` — stale-issue rot policy (live-only)
- The rest of live's tree is now covered by backup; live's `intake/backup-content-into-live-20260810`
  branch's "semantic merge" decisions were inspected and re-evaluated here.

### From backup (KooshaPari/Compound-Spheres-3D-Backup wsm3d/main) → adopted as the base
- All `perf/incremental-heightfield` and `sota/gpu-compute-golive` merges (the "evolved superset")
- `CompoundSpheres/Gpu/GpuShapeMath.cs` (3-shape cylindrical/flat/cube math)
- `CompoundSpheres/IGridDimensions.cs` (port that SphereManager + GpuSphereManager implement)
- `Tests/CompoundSpheres.Tests/` (4 files: csproj + 3 test files — 19 parity tests)
- `fix(grid): wire IGridDimensions incremental-heightfield members` (75c7f96)
- `fix(render): enable _ALPHABLEND_ON keyword + renderQueue=3000 + SetOverrideTag in ConfigureWater` (5daf5da)
- `backup: README explaining Compound-Spheres-3D recovery context` (535bf49) — replaced by this README
- All CI files (org template versions, 2026-08-05)

## What was hybridized

Per the **"best of each, hybridize where logical, long-term low-cost-of-change"** rule:

| File | Decision | Reason |
|---|---|---|
| `CompoundSpheres/HeightFieldRenderer.cs` | **Backup's version** (was stranded in the 2026-08-10 intake PR) | Backup uses `IGridDimensions _manager` (interface) instead of `SphereManager _manager` (concrete). Long-term low-cost-of-change: the interface lets the renderer swap implementations (CPU vs GPU actor/voxel). Also keeps the `ConfigureWater` fix with `_ALPHABLEND_ON` keyword + `renderQueue=3000` + `SetOverrideTag`. |
| `CompoundSpheres/SphereManager.cs` | **Backup's version** | Backup adds `IGridDimensions` to the class and the `SnapshotDirtyHeights()` method needed for incremental heightfield rebuilds. |
| `CompoundSpheres/FrustumCuller.cs` | **Backup's version** | Backup uses `GpuDefaults.TileWorldPosition(manager, ...)` (shape-aware) instead of `CartesianToCylindrical` (cylindrical-only). Long-term low-cost-of-change: future cube/flat shape support works without rewriting the culler. |
| `CompoundSpheres/Gpu/GpuManagerBase.cs` | **Backup's version** | Backup wires the `Shape` selector + `ZDisplacement`/`ConstRot` uniforms + `_cubeRegionBuffer` for the 3-shape go-live kernel. |
| `CompoundSpheres/Gpu/GpuSphereManager.cs` | **Backup's version** | Backup implements `IGridDimensions` and exposes `UseCubeShape(GpuCubeRegion[], float)`. |
| `CompoundSpheres/BufferUtils.cs` | **Backup's version** (kept as-is) | Backup's `BufferUtils.cs` is byte-identical to upstream's `CompoundSpheres/BufferUtils.cs`; upstream's NEW work is in `upstream/CompoundMeshes/BufferUtils.cs` (493 lines, completely different architecture). |
| `CompoundSpheres/SphereManagerSettings.cs` | **Backup's version** (kept as-is) | Same as live — byte-identical. |
| `CompoundSpheres/SphereRow.cs` | **Backup's version** (kept as-is) | Same as live — byte-identical. |
| `CompoundSpheres/CompoundSpheres.csproj` | **Backup's version** (kept as-is) | Same as live — byte-identical. |
| `Default Assets/CompoundSphereCompute.compute` | **Backup's version** | The 3-shape parity go-live kernel (the live's version was the pre-go-live variant). |
| `README.md` | **NEW** (this file) | Combines live's AI-DD-META header + divergence matrix with backup's 3-way merge provenance + verification status. |
| `CompoundSpheres.sln` | **Backup's version** | References `CompoundSpheres\CompoundSpheres.csproj` (the WSM3D fork). Upstream's `CompoundMeshes.sln` is kept at `upstream/CompoundMeshes.sln`. |
| CI files (`.circleci/`, `.github/workflows/`, etc.) | **Backup's "org template" versions** (Aug 5) | Newer than live's stable lint/test gate names (Aug 2). Plus live's `.pre-commit-config.yaml`, `.trunk/trunk.yaml`, `.github/stale.yml` on top. |
| `.gitignore` | **Backup's version** | Cleaner than live's. |
| `.mergify.yml`, `renovate.json`, `trunk.yaml` | **Backup's version** | Identical to live; backup is newer. |

## Verifying the merge

After cloning:

```bash
git clone https://github.com/KooshaPari/Compound-Spheres-3D.git
cd Compound-Spheres-3D

# Confirm provenance
git log --oneline -1   # canonical initial commit
git log --oneline 86f75d0  # backup's tip (now an ancestor)
git log --oneline a642e27  # upstream's tip (NOT an ancestor — preserved under upstream/)

# Recompile to verify
dotnet build CompoundSpheres.sln
dotnet test Tests/CompoundSpheres.Tests/CompoundSpheres.Tests.csproj
```

| Check | Result |
|-------|--------|
| Build (CompoundSpheres.csproj, net48) | 0 errors |
| Tests (CompoundSpheres.Tests, 19 tests) | 19/19 PASS in 641 ms |

## Origin: WorldSphere3D

This repository is a Unity-tool submodule of **WorldSphereMod3D** (WSM3D). The lineage
runs: `SphereManager` → `WorldSphereMod` → `CompoundMeshes/MeshManager` upstream, while
we pin to the WSM3D-extended `CompoundSpheres/` lineage in this fork.

See `upstream/README.upstream.md` for MelvinShwuaner's upstream README, and
`../../docs/upstream-divergence-audit.md` (cross-repo) for the full divergence matrix.
