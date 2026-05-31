using System;
using UnityEngine;
using CompoundSpheres.Gpu;

namespace CompoundSpheres.Compat
{
    /// <summary>
    /// P2 adapter shim (see docs/adr/ADR-sota-gpu-compute-adoption.md).
    ///
    /// Implements the OLD consumer-facing surface (<see cref="ILegacyManagerApi"/>)
    /// ON TOP of the new GPU-compute manager <see cref="GpuSphereManager"/> +
    /// the CompoundSphereCompute.compute kernels. The legacy
    /// <see cref="CompoundSpheres.SphereManager"/> (CPU path) is untouched; this
    /// shim is the bridge that lets a caller drive the GPU path through the old
    /// delegate-style API until P4 migrates consumers and deletes the shim.
    ///
    /// OLD -> NEW translations:
    ///   GetSphereTilePosition delegate  -> NOT needed on GPU: matrices come from
    ///                                       InputPositions (grid X,Y) + Radius in
    ///                                       the OutputMatrices kernel. The shim
    ///                                       only uses the delegate to recompute a
    ///                                       Matrix4x4/Quaternion CPU-side on demand.
    ///   GetSphereTileRotation delegate  -> CylindricalRotation in the matrix
    ///                                       kernel (GPU). CPU mirror in GetRotation.
    ///   GetSphereTileColor    delegate  -> filled into InputColors buffer; the
    ///                                       OutputColors kernel packs it (GPU).
    ///   GetSphereTileScale    delegate  -> Scales buffer (GPU vertex shader).
    ///   bool Refresh*(maxPerFrame)      -> void Refresh* on the GPU manager; the
    ///                                       shim returns true (GPU refresh is a
    ///                                       single dispatch, always "fully flushed").
    ///   SphereTile.Matrix / .Rotation   -> recomputed CPU-side from grid+Radius.
    ///   reflected field "SphereTiles"   -> GpuSphereManager.Tiles.
    /// </summary>
    public sealed class LegacyManagerShim : ILegacyManagerApi
    {
        readonly GpuSphereManager _gpu;

        // Old-style geometry delegates, kept so on-demand CPU Matrix/Rotation
        // reads stay identical to the legacy path. Optional — when null the shim
        // falls back to the GPU's cylindrical geometry (GpuDefaults).
        readonly Func<float, float, float, Vector3> _position; // (x,y,height) -> world pos
        readonly Func<int, Quaternion> _rotation;              // index -> rotation
        readonly Func<int, Color32> _color;                    // index -> color
        readonly Func<int, float> _height;                     // index -> terrain height (optional)

        public GpuSphereManager Gpu => _gpu;

        public LegacyManagerShim(
            GpuSphereManager gpu,
            Func<float, float, float, Vector3> position = null,
            Func<int, Quaternion> rotation = null,
            Func<int, Color32> color = null,
            Func<int, float> height = null)
        {
            _gpu = gpu ?? throw new ArgumentNullException(nameof(gpu));
            _position = position;
            _rotation = rotation;
            _color = color;
            _height = height;

            // Seed the GPU input buffers from the old delegates, then dispatch.
            if (_color != null)
                for (int i = 0; i < _gpu.TotalTiles; i++) _gpu.SetInputColor(i, _color(i));
            if (_height != null)
                _gpu.SetHeights(_height);
            if (_color != null) _gpu.RefreshColors();
        }

        // --- bool Refresh* : drive the GPU void Refresh, report "fully flushed" ---
        public bool RefreshScales(int maxPerFrame = 8192)
        {
            _gpu.RefreshScales();
            return true; // GPU refresh is one dispatch: always done in a single call.
        }
        public bool RefreshColors(int maxPerFrame = 8192)
        {
            // Re-pull colors from the legacy delegate into InputColors, then pack.
            if (_color != null)
                for (int i = 0; i < _gpu.TotalTiles; i++) _gpu.SetInputColor(i, _color(i));
            _gpu.RefreshColors();
            return true;
        }
        public bool RefreshTextures(int maxPerFrame = 8192)
        {
            _gpu.RefreshTextures();
            return true;
        }

        /// <summary>Mark a single tile's color dirty and re-pull it from the delegate.</summary>
        public void UpdateColor(int index)
        {
            if (_color != null) _gpu.SetInputColor(index, _color(index));
            else _gpu.SetColorDirty(index);
        }

        /// <summary>Feed a per-tile terrain height into the GPU matrix kernel (P2 HeightField path).</summary>
        public void SetHeight(int index, float height) => _gpu.SetHeight(index, height);

        /// <summary>Bulk-feed all per-tile heights (HeightFieldRenderer integration).</summary>
        public void SetHeights(Func<int, float> sampler) => _gpu.SetHeights(sampler);

        // --- CPU-side Matrix / Rotation reconstruction from grid + Radius ---
        public Matrix4x4 GetMatrix(int index)
        {
            return Matrix4x4.Translate(GetPosition(index)) * Matrix4x4.Rotate(GetRotation(index));
        }

        public Quaternion GetRotation(int index)
        {
            if (_rotation != null) return _rotation(index);
            // Default: cylindrical rotation matching the GPU matrix kernel.
            Vector3 p = GetPosition(index);
            return GpuDefaults.CylindricalRotation(new Vector2(p.x, p.y));
        }

        Vector3 GetPosition(int index)
        {
            GpuSphereTile tile = _gpu.GetTile(index);
            float h = _height != null ? _height(index) : 0f;
            if (_position != null) return _position(tile.X, tile.Y, h);
            return GpuDefaults.CartesianToCylindrical(_gpu, tile.X, tile.Y, h);
        }
    }
}
