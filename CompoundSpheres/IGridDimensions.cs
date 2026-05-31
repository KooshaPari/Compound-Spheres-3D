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
    }
}
