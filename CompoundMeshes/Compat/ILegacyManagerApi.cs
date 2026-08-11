using UnityEngine;

namespace CompoundSpheres.Compat
{
    /// <summary>
    /// P1 adapter-shim SEAM (see docs/adr/ADR-sota-gpu-compute-adoption.md).
    ///
    /// Defines the OLD consumer-facing surface that the main mod
    /// (Core.cs / CompoundSphereScripts.cs / Tools.cs / Mod.cs) depends on, so
    /// that when the GPU-compute rewrite (upstream ManagerBase / Dynamic* /
    /// BufferBase + the CompoundSphereCompute.compute kernels) is brought in
    /// additively, a shim implementing THIS interface keeps the main mod
    /// compiling unchanged until P4 migrates the consumers.
    ///
    /// This is intentionally a contract-only stub: no implementation is wired
    /// this phase (the new types are not imported yet), so the existing build
    /// is untouched. P1 implements <c>LegacyManagerShim : ILegacyManagerApi</c>
    /// over the new GPU manager; P4 deletes both this seam and the shim.
    ///
    /// Mapping OLD -> NEW (GPU-compute):
    ///   GetSphereTilePosition delegate  -> InputPositions (Vector2 grid X,Y) + Radius uniform, matrix on GPU
    ///   GetSphereTileRotation delegate  -> CylindricalRotation in OutputMatrices kernel (GPU)
    ///   GetSphereTileColor    delegate  -> InputColors buffer -> OutputColors kernel (GPU)
    ///   SphereTile.Matrix / .Rotation   -> recomputed CPU-side from grid+Radius when a consumer still reads them
    ///   bool Refresh*(maxPerFrame)      -> void Refresh* on new manager; shim returns "done" bool for the old callers
    ///   reflected field "SphereTiles"   -> "Tiles" on ManagerBase
    /// </summary>
    public interface ILegacyManagerApi
    {
        /// <summary>OLD: incremental scale refresh. Returns true when fully flushed (old callers gate on this).</summary>
        bool RefreshScales(int maxPerFrame = 8192);

        /// <summary>OLD: incremental color refresh. Returns true when fully flushed.</summary>
        bool RefreshColors(int maxPerFrame = 8192);

        /// <summary>OLD: incremental texture refresh. Returns true when fully flushed.</summary>
        bool RefreshTextures(int maxPerFrame = 8192);

        /// <summary>OLD: per-tile model matrix. Recomputed CPU-side from grid (X,Y)+Radius in the shim.</summary>
        Matrix4x4 GetMatrix(int index);

        /// <summary>OLD: per-tile rotation (CylindricalRotation). Recomputed CPU-side in the shim.</summary>
        Quaternion GetRotation(int index);

        /// <summary>OLD: mark one tile's color dirty (re-pulled from the legacy color delegate, packed on GPU).</summary>
        void UpdateColor(int index);

        /// <summary>P2: feed a per-tile terrain height into the GPU matrix kernel's InputHeights buffer.</summary>
        void SetHeight(int index, float height);

        /// <summary>P2: bulk-feed all per-tile heights (HeightFieldRenderer integration), then GPU re-dispatch.</summary>
        void SetHeights(System.Func<int, float> sampler);
    }
}
