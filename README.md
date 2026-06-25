# Compound-Spheres-3D Backup

This is a backup of the **Compound-Spheres-3D** submodule, which was the
GPU/water/sphere rendering engine used by WorldSphere3D.

## What this repo contains

- **Branch `wsm3d/main`** at `75c7f96` - main development branch with:
  - All upstream `KooshaPari/Compound-Spheres-3D` history (`e5b4973`)
  - **Merged** `sota/gpu-compute-golive` (308f2bb - Phase 0 + Phase 1 GPU compute migration)
  - **Merged** `perf/incremental-heightfield` (f09746d - incremental rebuild optimization)
  - **Fixed** `IGridDimensions` interface gap (HasDirtyHeights / SnapshotDirtyHeights / TotalTiles)
    that was exposed by the perf merge
- **Tagged**: see git tags

## Verification status

| Check | Result |
|-------|--------|
| Build (CompoundSpheres.csproj, net48) | 0 errors |
| Tests (CompoundSpheres.Tests, 19 tests) | 19/19 PASS in 641 ms |

## History

The original `KooshaPari/Compound-Spheres-3D` (public, archived) still exists
on GitHub at SHA `e5b4973` but is read-only. This backup is writeable and
contains the merged work that WSM3D `main` depends on as a submodule.

To sync to a fresh clone:
```bash
git clone --no-recurse-submodules https://github.com/KooshaPari/Compound-Spheres-3D-Backup.git
cd Compound-Spheres-3D-Backup
git log --oneline -10  # see all merged branches
```
