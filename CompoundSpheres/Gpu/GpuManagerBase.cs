using System.Collections.Generic;
using UnityEngine;

namespace CompoundSpheres.Gpu
{
    // -----------------------------------------------------------------------
    // P2: upstream MelvinShwuaner/Compound-Spheres ManagerBase.cs / TileBase /
    // ManagerSettings imported ADDITIVELY into CompoundSpheres.Gpu. This is the
    // GPU-compute manager: per-tile model matrices + colors are computed on the
    // GPU by CompoundSphereCompute.compute (OutputMatrices / OutputColors
    // kernels), instead of the legacy CPU UpdateBuffer/SetBufferChunked path.
    //
    // The legacy CompoundSpheres.SphereManager (CPU path + HeightFieldRenderer +
    // FrustumCuller + water) is UNTOUCHED and still what the main mod compiles
    // against. CompoundSpheres.Compat.LegacyManagerShim drives THIS type.
    //
    // Source: git show upstream/main:CompoundSpheres/ManagerBase.cs
    // -----------------------------------------------------------------------

    public abstract class ManagerRoot : MonoBehaviour
    {
        public ComputeShader ComputeShader { get; protected set; }
        public int MatrixKernel { get; protected set; }
        public int ColorKernel { get; protected set; }
        public Material Material { protected set; get; }
        public abstract int TotalTiles { get; }
    }

    public abstract class TileBase
    {
        protected TileBase(int Index) { this.Index = Index; }
        public readonly int Index;
        /// <summary>grid coord (X=row, Y=col) — fed to the GPU InputPositions buffer.</summary>
        public Vector2 Position => new Vector2(X, Y);
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public Vector3 Scale { get; protected set; }
        public Color32 Color { get; internal set; }
        public abstract Vector3 UpdateScale();
    }

    public class ManagerSettings<T> where T : TileBase
    {
        public Mesh SphereTileMesh;
        public Material SphereTileMaterial;
        public List<IBufferData> CustomBuffers;
        public GetDisplayMode GetDisplayMode;
        public GetSphereTileScale<T> GetSphereTileScale;
        public ComputeShader ComputeShader;
        public string MatrixKernel;
        public string ColorKernel;
    }

    public abstract class ManagerBase<T> : ManagerRoot where T : TileBase
    {
        internal T[] Tiles;
        public abstract int RowCount { get; }
        public abstract T this[int x, int y] { get; }
        public override int TotalTiles => Tiles.Length;
        public Mesh SphereTileMesh { protected set; get; }

        protected GetSphereTileScale<T> getSphereTileScale;
        protected GpuComputeBuffer<Vector2> Positions;
        protected Buffer<Vector3> Scales;
        protected ComputeGraphicsBuffer<Matrix4x4> Matrixes;
        protected ComputeGraphicsBuffer<Color32> Colors;
        protected Dictionary<string, IGpuBuffer> CustomBuffers;
        protected GetDisplayMode getdisplaymode;

        // -------------------------------------------------------------------
        // Fork addition (P2): per-tile InputColors + InputHeights compute
        // buffers. The compute color kernel reads InputColors and the matrix
        // kernel reads InputHeights (gated by HasHeights). These are how the
        // LegacyManagerShim translates the old CPU color delegate and the
        // HeightFieldRenderer per-tile heights onto the GPU path.
        // -------------------------------------------------------------------
        protected GpuComputeBuffer<Color32> InputColors;
        protected GpuComputeBuffer<float> InputHeights;
        bool _hasHeights;
        /// <summary>True once heights have been supplied (sets HasHeights=1 in the compute shader).</summary>
        public bool HasHeights => _hasHeights;

        protected virtual void OnDestroy()
        {
            if (CustomBuffers != null)
            {
                foreach (var buffer in CustomBuffers)
                {
                    buffer.Value.Dispose();
                }
            }
            Matrixes?.Dispose();
            Positions?.Dispose();
            Scales?.Dispose();
            Colors?.Dispose();
            InputColors?.Dispose();
            InputHeights?.Dispose();
        }

        protected void Init(ManagerSettings<T> Settings)
        {
            SphereTileMesh = Settings.SphereTileMesh;
            Material = Settings.SphereTileMaterial;
            getdisplaymode = Settings.GetDisplayMode;

            getSphereTileScale = Settings.GetSphereTileScale;
            ComputeShader = Settings.ComputeShader;
            MatrixKernel = Settings.ComputeShader.FindKernel(Settings.MatrixKernel);
            ColorKernel = Settings.ComputeShader.FindKernel(Settings.ColorKernel);

            Positions = new GpuComputeBuffer<Vector2>(ComputeShader, MatrixKernel, "InputPositions", TotalTiles);
            // InputColors feeds the color kernel; bound to ColorKernel.
            InputColors = new GpuComputeBuffer<Color32>(ComputeShader, ColorKernel, "InputColors", TotalTiles);
            // InputHeights feeds the matrix kernel; bound to MatrixKernel. Stays
            // all-zero (flat) until SetHeights/SetHeight is called.
            InputHeights = new GpuComputeBuffer<float>(ComputeShader, MatrixKernel, "InputHeights", TotalTiles);
            ComputeShader.SetInt("HasHeights", 0);
            ComputeShader.SetInt("TotalTiles", TotalTiles);

            Matrixes = new ComputeGraphicsBuffer<Matrix4x4>(ComputeShader, Material, MatrixKernel, "OutputMatrices", "Matrixes", TotalTiles, 64);
            Colors = new ComputeGraphicsBuffer<Color32>(ComputeShader, Material, ColorKernel, "OutputColors", "Colors", TotalTiles, 64);

            Scales = new Buffer<Vector3>(GraphicsBuffer.Target.Structured, TotalTiles, Material, "Scales");

            if (Settings.CustomBuffers != null)
            {
                foreach (IBufferData buffer in Settings.CustomBuffers)
                {
                    AddCustomBuffer(buffer);
                }
            }
        }

        public void SetComputeProperty(string name, float value) => ComputeShader.SetFloat(name, value);

        public float Clamp(float Pos, float Change)
        {
            Pos += Change;
            if (Pos < 0) return RowCount + Pos;
            return Pos % RowCount;
        }

        public void Destroy() => Destroy(gameObject);

        public IGpuBuffer AddCustomBuffer(IBufferData data)
        {
            IGpuBuffer buffer = data.GetBuffer(this);
            CustomBuffers ??= new Dictionary<string, IGpuBuffer>();
            CustomBuffers.Add(data.Name, buffer);
            return buffer;
        }
        public void UpdateCustom(string Name, int I) => CustomBuffers[Name].Update(I);
        public void RefreshCustom(string Name) => CustomBuffers[Name].Refresh();

        public void UpdateScale(int I) => Scales[I] = Tiles[I].UpdateScale();
        public void SetColorDirty(int I) => Colors.Update(I);
        public void RefreshScales() => Scales.Refresh();
        public void SetMatrixDirty(int I) => Matrixes.Update(I);
        public void RefreshMatrixes() => Matrixes.Refresh();
        public void RefreshColors() => Colors.Refresh();
        public abstract void RefreshTextures();
        public abstract void UpdateTexture(int I);
        public void RefreshAll()
        {
            RefreshScales();
            RefreshColors();
            RefreshTextures();
        }
        public void RefreshAllCustom()
        {
            foreach (var buffer in CustomBuffers) buffer.Value.Refresh();
        }
        public T GetTile(int Index) => Tiles[Index];

        // -------------------------------------------------------------------
        // Color path (fork): the shim writes the legacy color delegate's value
        // into InputColors[i] and marks the color kernel's Dirty flag, then a
        // RefreshColors() dispatch packs it into the render OutputColors buffer.
        // -------------------------------------------------------------------
        public void SetInputColor(int I, Color32 c)
        {
            InputColors[I] = c;
            Colors.Update(I);
        }

        // -------------------------------------------------------------------
        // Height path (fork, P2): HeightFieldRenderer feeds per-tile terrain
        // heights so the GPU matrix kernel places tiles at their elevation
        // (CartesianToCylindrical radius += height). Marks the matrix kernel
        // Dirty flags so the next RefreshMatrixes() recomputes those tiles.
        // -------------------------------------------------------------------
        public void SetHeight(int I, float height)
        {
            EnableHeights();
            InputHeights[I] = height;
            Matrixes.Update(I);
        }
        /// <summary>Bulk-fill all per-tile heights from a sampler and recompute matrices on the GPU.</summary>
        public void SetHeights(System.Func<int, float> sampler)
        {
            EnableHeights();
            for (int i = 0; i < TotalTiles; i++) InputHeights[i] = sampler(i);
            // All tiles dirty -> full re-dispatch of the matrix kernel.
            for (int i = 0; i < TotalTiles; i++) Matrixes.Update(i);
            InputHeights.Refresh();
            RefreshMatrixes();
        }
        void EnableHeights()
        {
            if (_hasHeights) return;
            _hasHeights = true;
            ComputeShader.SetInt("HasHeights", 1);
        }

        internal virtual void Begin()
        {
            ComputeShader.SetFloat("Radius", RadiusForCompute);
            Positions.Set((int i) => Tiles[i].Position);
            InputColors.Set((int i) => Tiles[i].Color);
            InputHeights.Set((int i) => 0f);
            int groups = Mathf.CeilToInt(TotalTiles / 64f);
            ComputeShader.Dispatch(MatrixKernel, groups, 1, 1);
            ComputeShader.Dispatch(ColorKernel, groups, 1, 1);
            Scales.Set((int i) => Tiles[i].UpdateScale());
        }
        /// <summary>Radius (Rows/2PI) the compute shader needs; concrete managers override.</summary>
        protected virtual float RadiusForCompute => 1f;

        public Vector3 SphereTileScale(T SphereTile) => getSphereTileScale(SphereTile);
    }
}
