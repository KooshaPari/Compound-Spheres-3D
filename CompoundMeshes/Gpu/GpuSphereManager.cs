using System;
using System.Collections;
using UnityEngine;

namespace CompoundSpheres.Gpu
{
    // -----------------------------------------------------------------------
    // P2: upstream SphereManager / SphereTile / SphereRow imported ADDITIVELY
    // under CompoundSpheres.Gpu as GpuSphereManager / GpuSphereTile /
    // GpuSphereRow. GPU-compute matrices + colors via CompoundSphereCompute.
    // Source: git show upstream/main:CompoundSpheres/{SphereManager,SphereTile,SphereRow}.cs
    // -----------------------------------------------------------------------

    public delegate int GetGpuTileTexture(GpuSphereTile tile);
    public delegate void GetGpuCameraRange(GpuSphereManager manager, out Range Rows, out Range Cols);
    public delegate void GpuInitiation(GpuSphereManager manager);

    public class GpuSphereManagerSettings : ManagerSettings<GpuSphereTile>
    {
        public GetGpuTileTexture GetSphereTileTexture;
        public Texture2DArray TextureArray;
        public GetGpuCameraRange GetCameraRange;
        public GpuInitiation Initiation;
    }

    public class GpuSphereTile : TileBase
    {
        public GpuSphereRow Row { get; private set; }
        public GpuSphereManager Manager => Row.Manager;
        public int TextureIndex { get; private set; }
        internal GpuSphereTile(int X, int Y, GpuSphereRow row) : base((row.Cols * X) + Y)
        {
            Row = row;
            this.X = X;
            this.Y = Y;
            Color = default;
            Scale = Vector3.one;
        }
        public override Vector3 UpdateScale()
        {
            Scale = Manager.SphereTileScale(this);
            return Scale;
        }
        public int UpdateTexture()
        {
            TextureIndex = Manager.GpuTileTexture(this);
            return TextureIndex;
        }
    }

    public class GpuSphereRow : IEnumerable, IDisposable
    {
        public readonly GpuSphereManager Manager;
        public GpuSphereTile this[int i] => Manager[Row, i];
        public int Cols => Manager.Cols;
        public readonly int Row;
        public MaterialPropertyBlock Properties => _rp.matProps;
        internal GpuSphereRow(GpuSphereManager manager, int Row)
        {
            Manager = manager;
            this.Row = Row;
            _rp = new RenderParams(manager.Material)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 10000),
                matProps = new MaterialPropertyBlock()
            };
            Properties.SetInteger("Row", Row * Cols);

            // P3 (GPU adoption): per-row indirect command buffer so each row can
            // draw only its visible column range (frustum cull). The shared
            // manager.CommandBuf can only express one instanceCount for the whole
            // frame; per-row buffers let DrawTiles(colStart,colCount) vary it.
            _cmd = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
            _cmdData[0].indexCountPerInstance = manager.SphereTileMesh.GetIndexCount(0);
            _cmdData[0].instanceCount = (uint)Cols;
            _cmd.SetData(_cmdData);
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            for (int i = 0; i < Cols; i++) yield return this[i];
        }

        /// <summary>Draw the whole row (no cull).</summary>
        public void DrawTiles()
        {
            Properties.SetInteger("Row", Row * Cols);
            if (_lastCount != Cols)
            {
                _cmdData[0].instanceCount = (uint)Cols;
                _cmd.SetData(_cmdData);
                _lastCount = Cols;
            }
            Graphics.RenderMeshIndirect(_rp, Manager.SphereTileMesh, _cmd, 1);
        }

        /// <summary>P3: draw only the visible contiguous column range [colStart, colStart+colCount).</summary>
        public void DrawTiles(int colStart, int colCount)
        {
            if (colCount <= 0) return;
            // Shift the instance base so SV_InstanceID 0 maps to colStart.
            Properties.SetInteger("Row", (Row * Cols) + colStart);
            if (_lastCount != colCount)
            {
                _cmdData[0].instanceCount = (uint)colCount;
                _cmd.SetData(_cmdData);
                _lastCount = colCount;
            }
            Graphics.RenderMeshIndirect(_rp, Manager.SphereTileMesh, _cmd, 1);
        }

        /// <summary>Re-sync the cached mesh index count after the manager swaps meshes.</summary>
        internal void RefreshMeshIndexCount()
        {
            _cmdData[0].indexCountPerInstance = Manager.SphereTileMesh.GetIndexCount(0);
            _cmd.SetData(_cmdData);
        }

        public void Dispose() => _cmd?.Release();

        private RenderParams _rp;
        private readonly GraphicsBuffer _cmd;
        private readonly GraphicsBuffer.IndirectDrawIndexedArgs[] _cmdData =
            new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        private int _lastCount = -1;
    }

    public class GpuSphereManager : ManagerBase<GpuSphereTile>, IEnumerable, IGridDimensions
    {
        public override GpuSphereTile this[int x, int y] => Tiles[(x * Cols) + y];
        public GpuSphereRow this[int x] => SphereRows[x];

        internal GpuSphereRow[] SphereRows;
        public int Rows { private set; get; }
        public int Cols { private set; get; }
        public float Radius { private set; get; }
        public float Diameter => 2 * Radius;
        public override int RowCount => Rows;
        /// <summary>
        /// IGridDimensions: total tile count on the GPU grid. The renderer
        /// uses this for full-rebuild budget sizing when incremental rebuilds
        /// are not available.
        /// </summary>
        public override int TotalTiles => Rows * Cols;
        /// <summary>
        /// IGridDimensions: GPU path keeps all tile scales in a structured
        /// buffer managed by compute kernels; we don't maintain a CPU dirty
        /// queue, so incremental rebuilds are not (yet) supported. Returning
        /// false forces the heightfield renderer down its full-rebuild path
        /// for the GPU actor/voxel pipeline.
        /// </summary>
        public bool HasDirtyHeights => false;
        /// <summary>
        /// IGridDimensions: see <see cref="HasDirtyHeights"/>. Returns an
        /// empty array — the GPU path rebuilds the whole heightfield when
        /// anything changes.
        /// </summary>
        public int[] SnapshotDirtyHeights() => System.Array.Empty<int>();
        protected override float RadiusForCompute => Radius;

        internal GraphicsBuffer CommandBuf;
        readonly GraphicsBuffer.IndirectDrawIndexedArgs[] commandData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];

        GetGpuCameraRange GetCameraRange;
        protected Buffer<float> Textures;
        protected GetGpuTileTexture getSphereTileTexture;

        // P3 (GPU adoption): CPU frustum culler operating on grid (X,Y) +
        // Radius-recomputed world positions (no GPU readback). Mirrors the
        // legacy SphereManager.Culler path.
        public readonly FrustumCuller Culler = new FrustumCuller();
        public bool FrustumCullingEnabled = true;
        Vector3 _tileHalfSize = Vector3.one;
        int _lastCulledTiles;
        public int LastCulledTiles => _lastCulledTiles;

        protected override void OnDestroy()
        {
            base.OnDestroy();
            CommandBuf?.Release();
            Textures?.Dispose();
            if (SphereRows != null)
                foreach (var row in SphereRows) row?.Dispose();
        }

        internal GpuSphereManager Init(int rows, int cols, GpuSphereManagerSettings settings)
        {
            Cols = cols;
            Rows = rows;
            Tiles = new GpuSphereTile[rows * cols];

            base.Init(settings);
            GetCameraRange = settings.GetCameraRange;

            if (settings.TextureArray != null) Material.SetTexture("TextureArray", settings.TextureArray);
            Textures = new Buffer<float>(GraphicsBuffer.Target.Structured, TotalTiles, Material, "Textures");
            getSphereTileTexture = settings.GetSphereTileTexture;

            Radius = Rows / (2 * Mathf.PI);
            ComputeShader.SetFloat("Radius", Radius);
            SphereRows = new GpuSphereRow[rows];

            CommandBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            commandData[0].indexCountPerInstance = SphereTileMesh.GetIndexCount(0);
            commandData[0].instanceCount = (uint)Cols;
            CommandBuf.SetData(commandData);

            if (SphereTileMesh != null)
                _tileHalfSize = SphereTileMesh.bounds.extents * 2f;

            return this;
        }

        internal override void Begin()
        {
            base.Begin();
            if (getSphereTileTexture != null) Textures.Set((int i) => Tiles[i].UpdateTexture());
        }

        public void SetMesh(Mesh mesh)
        {
            SphereTileMesh = mesh;
            commandData[0].indexCountPerInstance = SphereTileMesh.GetIndexCount(0);
            CommandBuf.SetData(commandData);
            if (SphereTileMesh != null) _tileHalfSize = SphereTileMesh.bounds.extents * 2f;
            if (SphereRows != null)
                foreach (var row in SphereRows) row?.RefreshMeshIndexCount();
        }
        void SetRenderAmount(uint amount)
        {
            commandData[0].instanceCount = amount;
            CommandBuf.SetData(commandData);
        }
        private GpuSphereManager() { }

        public void DrawTiles(int cameraX)
        {
            if (getdisplaymode != null) Material.SetFloat("ShouldRenderTextures", (int)getdisplaymode());
            GetCameraRange(this, out Range Row, out Range Col);

            if (FrustumCullingEnabled)
            {
                Culler.UpdatePlanes(Camera.main);
                int totalCulled = 0;
                for (int i = Row.Min; i < Row.Max; i++)
                {
                    int I = (int)Clamp(cameraX, i);
                    if (Culler.GetVisibleColumnRange(this, I, Cols, _tileHalfSize,
                            out int colStart, out int colCount))
                    {
                        SphereRows[I].DrawTiles(colStart, colCount);
                        totalCulled += Cols - colCount;
                    }
                    else
                    {
                        totalCulled += Cols;
                    }
                }
                _lastCulledTiles = totalCulled;
            }
            else
            {
                for (int i = Row.Min; i < Row.Max; i++)
                {
                    int I = (int)Clamp(cameraX, i);
                    SphereRows[I].DrawTiles();
                }
            }
        }
        public void DrawAllTiles()
        {
            if (getdisplaymode != null) Material.SetFloat("ShouldRenderTextures", (int)getdisplaymode());
            SetRenderAmount((uint)Cols);
            foreach (GpuSphereRow row in this) row.DrawTiles();
        }

        // Cube geometry mirror (set by ConfigureCube) so the CPU frustum culler
        // can compute cube-face world positions without a GPU readback.
        internal GpuCubeRegion[] CubeRegions = System.Array.Empty<GpuCubeRegion>();
        internal float CubeSize;

        /// <summary>Select cube shape and cache the face regions for CPU culling.</summary>
        public void UseCubeShape(GpuCubeRegion[] regions, float cubeSize)
        {
            CubeRegions = regions;
            CubeSize = cubeSize;
            ConfigureCube(regions, cubeSize);
        }

        public Vector3 SphereTilePosition(float X, float Y, float Height = 0)
            => GpuDefaults.TileWorldPosition(this, X, Y, Height);

        public IEnumerator GetEnumerator() => SphereRows.GetEnumerator();

        public void UpdateColor(int X, int Y) => SetColorDirty((X * Cols) + Y);
        public void UpdateScale(int X, int Y) => UpdateScale((X * Cols) + Y);
        public void UpdateTexture(int X, int Y) => UpdateTexture((X * Cols) + Y);

        public int GpuTileTexture(GpuSphereTile tile) => getSphereTileTexture(tile);
        public override void UpdateTexture(int I) => Textures[I] = Tiles[I].UpdateTexture();
        public override void RefreshTextures() => Textures.Refresh();

        public static class Creator
        {
            public static GpuSphereManager CreateSphereManager(int rows, int cols, GpuSphereManagerSettings settings, string Name = "GpuSphereManager")
            {
                if (cols <= 0 || rows <= 0)
                    throw new ArgumentException("Cols And Rows must be above 0 when creating a sphere manager");
                GameObject go = new GameObject(Name);
                GpuSphereManager manager = go.AddComponent<GpuSphereManager>().Init(rows, cols, settings);
                for (int X = 0; X < rows; X++)
                {
                    GpuSphereRow row = manager.SphereRows[X] = new GpuSphereRow(manager, X);
                    for (int Y = 0; Y < cols; Y++)
                        manager.Tiles[(X * cols) + Y] = new GpuSphereTile(X, Y, row);
                }
                manager.Begin();
                settings.Initiation?.Invoke(manager);
                return manager;
            }

            /// <summary>
            /// Frame-yielding wrapper around <see cref="CreateSphereManager"/> mirroring
            /// the CPU SphereManager.Creator.CreateSphereManagerAsync coroutine pattern
            /// (issue #199 Phase 1). The heavy per-tile build loop yields every
            /// <paramref name="chunkSize"/> tiles to avoid a single-frame spike; the
            /// manager is handed back via <paramref name="onCreated"/> before the
            /// synchronous Begin() so callers can store the reference immediately.
            /// </summary>
            public static IEnumerator CreateSphereManagerAsync(int rows, int cols, GpuSphereManagerSettings settings, Action<GpuSphereManager> onCreated, int chunkSize = 4096, string Name = "GpuSphereManager")
            {
                if (cols <= 0 || rows <= 0)
                    throw new ArgumentException("Cols And Rows must be above 0 when creating a sphere manager");
                GameObject go = new GameObject(Name);
                GpuSphereManager manager = go.AddComponent<GpuSphereManager>().Init(rows, cols, settings);
                int count = 0;
                for (int X = 0; X < rows; X++)
                {
                    GpuSphereRow row = manager.SphereRows[X] = new GpuSphereRow(manager, X);
                    for (int Y = 0; Y < cols; Y++)
                    {
                        manager.Tiles[(X * cols) + Y] = new GpuSphereTile(X, Y, row);
                        if (++count % chunkSize == 0)
                            yield return null;
                    }
                }
                onCreated?.Invoke(manager);
                manager.Begin();
                settings.Initiation?.Invoke(manager);
            }
        }
    }

    /// <summary>GPU-namespace default geometry helpers (mirror CompoundSpheres.DefaultSettings).</summary>
    public static class GpuDefaults
    {
        public static Vector3 CartesianToCylindrical(GpuSphereManager manager, float X, float Y, float Height = 0)
            => GpuShapeMath.PosCylindrical(manager.Radius, X, Y, Height);

        /// <summary>World position for the manager's active shape (used by frustum culling).</summary>
        public static Vector3 TileWorldPosition(GpuSphereManager manager, float X, float Y, float Height = 0)
        {
            switch (manager.Shape)
            {
                case TileShape.Flat: return GpuShapeMath.PosFlat(X, Y, Height);
                case TileShape.Cube: return GpuShapeMath.PosCube(manager.CubeRegions, manager.CubeSize, X, Y, Height);
                default: return GpuShapeMath.PosCylindrical(manager.Radius, X, Y, Height);
            }
        }
        public static Quaternion CylindricalRotation(Vector2 position)
            => GpuShapeMath.RotCylindrical(new Vector3(position.x, position.y, 0f));
        public static DisplayMode DefaultMode() => DisplayMode.ColoredTexture;
        public static Vector3 DefaultScale(GpuSphereTile tile) => Vector3.one;
    }
}
