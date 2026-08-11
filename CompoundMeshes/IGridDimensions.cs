namespace CompoundSpheres
{
    /// <summary>
    /// Minimal grid-geometry port shared by the CPU <see cref="SphereManager"/> and the
    /// GPU <c>GpuSphereManager</c>. Extracted for issue #199 so that
    /// <see cref="HeightFieldRenderer"/> (the standalone terrain surface) can depend on an
    /// abstract grid rather than the concrete CPU manager, enabling a parallel GPU
    /// actor/voxel render path without a unified-manager rewrite.
    /// </summary>
    public interface IGridDimensions
    {
        int Rows { get; }
        int Cols { get; }
        UnityEngine.Material Material { get; }
        UnityEngine.Vector3 SphereTilePosition(float X, float Y, float Height);

        // Incremental-heightfield (2026-06-04 perf fix). The HeightFieldRenderer asks
        // the manager which tiles have height-dirty state so it can prefer a per-tile
        // incremental mesh update over a full 256² rebuild when only a small set
        // changed. Implementations return false / empty to force a full rebuild.
        bool HasDirtyHeights { get; }
        int[] SnapshotDirtyHeights();
        int TotalTiles { get; }
    }
}
