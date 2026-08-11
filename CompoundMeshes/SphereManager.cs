using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CompoundSpheres
{
    /// <summary>
    /// The Manager For Your Compound Sphere
    /// </summary>
    public class SphereManager : MonoBehaviour, IEnumerable, IGridDimensions
    {
        /// <summary>
        /// a spheretile at x and y coordinates
        /// </summary>
        public SphereTile this[int x, int y] => SphereTiles[(x*Cols)+y];
        /// <summary>
        /// a Row at an X position
        /// </summary>
        public SphereRow this[int x] => SphereRows[x];
        internal SphereTile[] SphereTiles;
        internal SphereRow[] SphereRows;
        /// <summary>
        /// The X Axis
        /// </summary>
        public int Rows { private set; get; }
        /// <summary>
        /// The Y Axis
        /// </summary>
        public int Cols { private set; get; }
        /// <summary>
        /// the total amount of tiles on the sphere
        /// </summary>
        public int TotalTiles => Cols * Rows;
        /// <summary>
        /// Rows / 2PI
        /// </summary>
        public float Radius { private set; get; }
        /// <summary>
        /// 2 * Radius
        /// </summary>
        public float Diameter => 2 * Radius;

        #region MeshStuff
        /// <summary>
        /// The Mesh used, every tile has the same mesh
        /// </summary>
        public Mesh SphereTileMesh { private set; get; }
        /// <summary>
        /// The material used, every tile has the same material
        /// </summary>
        /// <remarks>dont add custom buffers directly to this, instead use addcustombuffer, since the manager will manage the buffer for you</remarks>
        public Material Material { private set; get; }
        internal GraphicsBuffer commandBuf;
        private GraphicsBuffer Scales, Colors, Textures, Matrixes;
        internal HashSet<int> _scales, _colors, _textures;
        private Dictionary<string, IBuffer> CustomBuffers;
        /// <summary>
        /// True iff at least one of the core dirty queues (scales, colors,
        /// textures) currently has an entry. Used by consumers to gate
        /// downstream mesh rebuilds — e.g. the heightfield only needs to
        /// rebuild when actual tile data changed, not on every redraw tick.
        /// </summary>
        public bool HasDirtyTiles
        {
            get
            {
                if (_scales != null && _scales.Count > 0) return true;
                if (_colors != null && _colors.Count > 0) return true;
                if (_textures != null && _textures.Count > 0) return true;
                return false;
            }
        }
        /// <summary>
        /// True iff the SCALE dirty queue has pending entries. Scale == per-tile
        /// elevation in 3D, so this is the ONLY queue whose changes alter the
        /// height-field mesh GEOMETRY. Color/texture churn (water-sim recolor,
        /// fire flicker, mob tint) must NOT trigger a full geometry rebuild — that
        /// is the per-frame 5s storm. The heightfield gates its expensive rebuild
        /// on this instead of <see cref="HasDirtyTiles"/>. Color-only changes are
        /// picked up by the renderer's own color re-bake without a geometry pass.
        /// </summary>
        public bool HasDirtyHeights => _scales != null && _scales.Count > 0;
        #endregion
        #region HeightField
        HeightFieldRenderer _heightField;
        bool _useHeightField;
        /// <summary>
        /// When true, DrawTiles uses a continuous height-field mesh instead
        /// of per-tile instanced quads. Set via SavedSettings.UseHeightFieldTerrain.
        /// </summary>
        public bool UseHeightFieldTerrain
        {
            get => _useHeightField;
            set => _useHeightField = value;
        }
        /// <summary>
        /// Access the height-field renderer for configuration (callbacks etc.).
        /// Lazily created on first access.
        /// </summary>
        public HeightFieldRenderer HeightField
        {
            get
            {
                if (_heightField == null)
                {
                    _heightField = new HeightFieldRenderer(this);
                }
                return _heightField;
            }
        }
        #endregion
        #region FrustumCulling
        /// <summary>
        /// Frustum culler instance used to skip off-screen rows/columns.
        /// </summary>
        public readonly FrustumCuller Culler = new FrustumCuller();
        /// <summary>
        /// Enable or disable frustum culling. When disabled, all visible rows
        /// draw every column (original behaviour).
        /// </summary>
        public bool FrustumCullingEnabled = true;
        private Vector3 _tileHalfSize;
        private int _lastCulledTiles;
        /// <summary>
        /// Number of tiles culled in the most recent DrawTiles call. Useful for profiling.
        /// </summary>
        public int LastCulledTiles => _lastCulledTiles;
        #endregion
        #region Settings
        GetSphereTilePosition SphereTilePos;
        GetSphereTileRotation getSphereTileRotation;
        GetSphereTileScale getSphereTileScale;
        GetSphereTileTexture getSphereTileTexture;
        GetSphereTileColor getSphereTileColor;
        GetDisplayMode getdisplaymode;
        GetCameraRange GetCameraRange;
        #endregion
        private void OnDestroy()
        {
            _heightField?.Dispose();
            _heightField = null;
            if (CustomBuffers != null)
            {
                foreach (var buffer in CustomBuffers)
                {
                    buffer.Value.Dispose();
                }
            }
            if (SphereRows != null)
            {
                foreach (var row in SphereRows)
                {
                    row?.ReleaseRangeBuffer();
                }
            }
            Scales.Release();
            Colors.Release();
            Matrixes.Release();
            Textures.Release();
            commandBuf.Release();
            SphereRows = null;
            SphereTiles = null;
        }
        /// <summary>
        /// destroys the sphere manager and its game object, and frees up all memory
        /// </summary>
        public void Destroy()
        {
            Destroy(gameObject);
        }
            internal SphereManager Init(int rows, int cols, SphereManagerSettings sphereManagerSettings)
            {
                var sw = Stopwatch.StartNew();
                Cols = cols;
                Rows = rows;

            SphereTileMesh = sphereManagerSettings.SphereTileMesh;
            Material = sphereManagerSettings.SphereTileMaterial;
            getSphereTileColor = sphereManagerSettings.GetSphereTileColor;
            getSphereTileTexture = sphereManagerSettings.GetSphereTileTexture;
            getSphereTileRotation = sphereManagerSettings.GetSphereTileRotation;
            getSphereTileScale = sphereManagerSettings.GetSphereTileScale;
            SphereTilePos = sphereManagerSettings.getspheretileposition;
            getdisplaymode = sphereManagerSettings.GetDisplayMode;
            GetCameraRange = sphereManagerSettings.GetCameraRange;
            Material.SetTexture("TextureArray", sphereManagerSettings.TextureArray);
            Radius = Rows / (2 * Mathf.PI);
            SphereTiles = new SphereTile[rows * cols];
            SphereRows = new SphereRow[rows];

            commandBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            GraphicsBuffer.IndirectDrawIndexedArgs[] commandData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
            commandData[0].indexCountPerInstance = SphereTileMesh.GetIndexCount(0);
            commandData[0].instanceCount = (uint)Cols;
            commandBuf.SetData(commandData);
            Matrixes = new GraphicsBuffer(GraphicsBuffer.Target.Structured, TotalTiles, 64);
            Colors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, TotalTiles, 4);
            Scales = new GraphicsBuffer(GraphicsBuffer.Target.Structured, TotalTiles, 12);
            Textures = new GraphicsBuffer(GraphicsBuffer.Target.Structured, TotalTiles, 4);
            _colors = new HashSet<int>();
            _scales = new HashSet<int>();
            _textures = new HashSet<int>();
            Material.SetBuffer("Matrixes", Matrixes);
            Material.SetBuffer("Colors", Colors);
            Material.SetBuffer("Scales", Scales);
            Material.SetBuffer("Textures", Textures);

                if(sphereManagerSettings.CustomBuffers != null)
                {
                    foreach(IBufferData buffer in sphereManagerSettings.CustomBuffers)
                    {
                        AddCustomBuffer(buffer);
                    }
                }
                Debug.Log($"[WSM3D][PERF] CompoundSpheres.SphereManager.Init.Setup={sw.Elapsed.TotalMilliseconds:F3}ms");
                return this;
            }
        private SphereManager() { }
        /// <summary>
        /// clamps a position + change to the X Axis
        /// </summary>
        public float Clamp(float Pos, float Change)
        {
            int Max = Rows;
            Pos += Change;
            if (Pos < 0)
            {
                return Max + Pos;
            }
            return Pos % Max;
        }
        /// <summary>
        /// all rows that are in the camera range draw their tiles
        /// </summary>
        /// <remarks>the camera x and y positions are the camera's position on the 2d grid, NOT its actual coordinates</remarks>
        public void DrawTiles(int CameraX)
        {
            GetCameraRange(this, out int Min, out int Max);
            Material.SetFloat("ShouldRenderTextures", (int)getdisplaymode(this));
            int rowCount = Max - Min;
            var sw = Stopwatch.StartNew();

            if (_useHeightField && _heightField != null)
            {
                _heightField.RebuildAndDraw(CameraX, Min, Max, false);
                long hfMs = sw.ElapsedMilliseconds;
                if (hfMs > 16)
                {
                    Debug.LogWarning($"[WSM3D][PERF] DrawTiles HEIGHTFIELD SLOW: {hfMs}ms rows={rowCount} cols={Cols}");
                }
                return;
            }

            if (FrustumCullingEnabled)
            {
                Camera cam = Camera.main;
                Culler.UpdatePlanes(cam);
                int totalCulled = 0;

                for (int i = Min; i < Max; i++)
                {
                    int I = (int)Clamp(CameraX, i);
                    SphereRow row = SphereRows[I];

                    if (Culler.GetVisibleColumnRange(SphereTiles, I, Cols,
                            _tileHalfSize, out int colStart, out int colCount))
                    {
                        row.DrawTiles(colStart, colCount);
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
                for (int i = Min; i < Max; i++)
                {
                    int I = (int)Clamp(CameraX, i);
                    SphereRows[I].DrawTiles();
                }
            }
            long ms = sw.ElapsedMilliseconds;
            if (ms > 16)
            {
                Debug.LogWarning($"[WSM3D][PERF] DrawTiles SLOW: {ms}ms rows={rowCount} " +
                    $"cols={Cols} total={Rows}x{Cols} culled={_lastCulledTiles} " +
                    $"frustum={FrustumCullingEnabled}");
            }
        }
        /// <summary>
        /// draws all tiles, even if they are not visible, NOT RECOMMENDED!
        /// </summary>
        public void DrawAllTiles()
        {
            Material.SetFloat("ShouldRenderTextures", (int)getdisplaymode(this));
            foreach (SphereRow row in this)
            {
                row.DrawTiles();
            }
        }
        /// <summary>
        /// the 3D Position of a Position on the grid, along with a Height
        /// </summary>
        public Vector3 SphereTilePosition(float X, float Y, float Height = 0)
        {
            return SphereTilePos(this, X, Y, Height);
        }
        /// <summary>
        /// gets the rotation of a spheretile
        /// </summary>
        public Quaternion GetSphereTileRotation(SphereTile SphereTile)
        {
            return getSphereTileRotation(SphereTile);
        }
        /// <summary>
        /// the scale of a spheretile
        /// </summary>
        public Vector3 SphereTileScale(SphereTile SphereTile)
        {
            return getSphereTileScale(SphereTile);
        }
        /// <summary>
        /// the color of a spheretile
        /// </summary>
        /// <remarks>the Alpha Component is NOT USED.</remarks>
        public Color32 SphereTileColor(SphereTile SphereTile)
        {
            return getSphereTileColor(SphereTile);
        }
        /// <summary>
        /// the Index of the texture in the textures array that this spheretile has
        /// </summary>
        /// <param name="SphereTile"></param>
        /// <returns></returns>
        public int SphereTileTexture(SphereTile SphereTile)
        {
            return getSphereTileTexture(SphereTile);
        }
        /// <summary>
        /// the sphere manager acts as a list of sphere rows
        /// </summary>
        public IEnumerator GetEnumerator()
        {
            return SphereRows.GetEnumerator();
        }
        /// <summary>
        /// refresh all of the scales, textures and colors
        /// </summary>
        public void RefreshAll()
        {
            RefreshScales();
            RefreshColors();
            RefreshTextures();
        }
        internal void Begin()
        {
            Matrixes.SetBuffer(TotalTiles, (int i) => SphereTiles[i].Matrix);
            Scales.SetBuffer(TotalTiles, (int i) => SphereTiles[i].UpdateScale());
            Colors.SetBuffer(TotalTiles, (int i) => SphereTiles[i].UpdateColor());
            Textures.SetBuffer<float>(TotalTiles, (int i) => SphereTiles[i].UpdateTexture());
        }
        /// <summary>
        /// Whether the sphere manager has finished its buffer initialisation.
        /// False while <see cref="BeginCoroutine"/> is still running.
        /// </summary>
        public bool IsReady { get; private set; }
        /// <summary>
        /// Coroutine version of <see cref="Begin"/> that spreads the heavy
        /// buffer fills across multiple frames so the main thread stays
        /// responsive during world load.
        /// </summary>
        internal IEnumerator BeginCoroutine(int chunkSize = 4096)
        {
            IsReady = false;
            int done = 0;
            yield return Matrixes.SetBufferChunked(TotalTiles, (int i) => SphereTiles[i].Matrix, chunkSize, () => done++);
            yield return Scales.SetBufferChunked(TotalTiles, (int i) => SphereTiles[i].UpdateScale(), chunkSize, () => done++);
            yield return Colors.SetBufferChunked(TotalTiles, (int i) => SphereTiles[i].UpdateColor(), chunkSize, () => done++);
            yield return Textures.SetBufferChunked<float>(TotalTiles, (int i) => SphereTiles[i].UpdateTexture(), chunkSize, () => done++);
            IsReady = true;
        }
        /// <summary>
        /// refresh the scale buffer, processing at most maxPerFrame dirty entries.
        /// Returns true when all entries are processed.
        /// </summary>
        public bool RefreshScales(int maxPerFrame = 8192)
        {
            return Scales.UpdateBuffer(_scales, (int i) => SphereTiles[i].UpdateScale(), TotalTiles, maxPerFrame);
        }
        /// <summary>
        /// Snapshot the current dirty scale indices before the queue is drained.
        /// HeightFieldRenderer uses this to patch only the affected vertices when
        /// the dirty set is small, instead of forcing a full 256x256 mesh rebuild.
        /// </summary>
        public int[] SnapshotDirtyHeights()
        {
            if (_scales == null || _scales.Count == 0)
            {
                return Array.Empty<int>();
            }

            int[] snapshot = new int[_scales.Count];
            _scales.CopyTo(snapshot);
            return snapshot;
        }
        /// <summary>
        /// refresh the color buffer, processing at most maxPerFrame dirty entries.
        /// Returns true when all entries are processed.
        /// </summary>
        public bool RefreshColors(int maxPerFrame = 8192)
        {
            return Colors.UpdateBuffer(_colors, (int i) => SphereTiles[i].UpdateColor(), TotalTiles, maxPerFrame);
        }
        /// <summary>
        /// refresh the texture buffer, processing at most maxPerFrame dirty entries.
        /// Returns true when all entries are processed.
        /// </summary>
        public bool RefreshTextures(int maxPerFrame = 8192)
        {
            return Textures.UpdateBuffer<float>(_textures, (int i) => SphereTiles[i].UpdateTexture(), TotalTiles, maxPerFrame);
        }
        /// <summary>
        /// marks a tile's color to be refreshed
        /// </summary>
        public void UpdateColor(int X, int Y)
        {
            _colors.Add((X*Cols) +Y);
        }
        /// <summary>
        /// marks a tile's color to be refreshed
        /// </summary>
        public void UpdateColor(int I)
        {
            _colors.Add(I);
        }
        /// <summary>
        /// marks a tile's matrix to be refreshed
        /// </summary>
        public void UpdateScale(int X, int Y)
        {
            _scales.Add((X * Cols) + Y);
        }
        /// <summary>
        /// marks a tile's matrix to be refreshed
        /// </summary>
        public void UpdateScale(int I)
        {
            _scales.Add(I);
        }
        /// <summary>
        /// marks a tile's texture to be refreshed
        /// </summary>
        public void UpdateTexture(int X, int Y)
        {
            _textures.Add((X * Cols) + Y);
        }
        /// <summary>
        /// marks a tile's texture to be refreshed
        /// </summary>
        public void UpdateTexture(int I)
        {
            _textures.Add(I);
        }
        /// <summary>
        /// refreshes a custom buffer, processing at most maxPerFrame dirty entries.
        /// Returns true when all entries are processed.
        /// </summary>
        public bool RefreshCustom(string Name, int maxPerFrame = 8192)
        {
            return CustomBuffers[Name].Refresh(maxPerFrame);
        }
        /// <summary>
        /// marks a tile's custom property to be updated
        /// </summary>
        public void UpdateCustom(string Name, int X, int Y)
        {
            CustomBuffers[Name].Update((X * Cols) + Y);
        }
        /// <summary>
        /// marks a tile's custom property to be updated
        /// </summary>
        public void UpdateCustom(string Name, int I)
        {
            CustomBuffers[Name].Update(I);
        }
        /// <summary>
        /// adds a custom buffer to this sphererow, which is then accessed by the GPU
        /// </summary>
        /// <param name="Name">the name of the buffer in the custom shader, must be unique</param>
        /// <param name="getcustomdata">a function that returns a struct to be stored in the buffer</param>
        /// <returns>a compute buffer, to update it call buffer.update and buffer.refresh</returns>
        /// <remarks>your compute buffer will be automatically released from memory once sphere is destroyed</remarks>
        public CustomBuffer<T> AddCustomBuffer<T>(string Name, GetCustomData<T> getcustomdata) where T : struct
        {
            CustomBufferData<T> data = new CustomBufferData<T>(Name, getcustomdata);
            return (CustomBuffer<T>)AddCustomBuffer(data);
        }
        /// <summary>
        /// a non-generic method of adding a custom buffer
        /// </summary>
        public IBuffer AddCustomBuffer(IBufferData data)
        {
            IBuffer buffer = data.GetBuffer(this);
            CustomBuffers ??= new Dictionary<string, IBuffer>();
            CustomBuffers.Add(data.GetName(), buffer);
            return buffer;
        }
        /// <summary>
        /// Creates Spheremanagers
        /// </summary>
        public static class Creator
        {
            /// <summary>
            /// Creates a sphere manager, cols and rows represent the size of the grid the manager displays
            /// </summary>
            /// <exception cref="ArgumentException">cols and rows must be above 0</exception>
            /// <remarks>this will create a game object with a sphere manager attached to it!</remarks>
            public static SphereManager CreateSphereManager(int rows, int cols, SphereManagerSettings sphereManagerSettings, string Name = "SphereManager")
            {
                if(cols <= 0 || rows <= 0)
                {
                    throw new ArgumentException("Cols And Rows must be above 0 when creating a sphere manager");
                }
                var sw = Stopwatch.StartNew();
                GameObject SphereManager = new GameObject(Name);
                SphereManager Manager = SphereManager.AddComponent<SphereManager>().Init(rows, cols, sphereManagerSettings);
                Debug.Log($"[WSM3D][PERF] CompoundSpheres.CreateSphereManager.InitOnly={sw.Elapsed.TotalMilliseconds:F3}ms");
                sw.Restart();
                BuildTilesAndRows(Manager, rows, cols);
                Debug.Log($"[WSM3D][PERF] CompoundSpheres.CreateSphereManager.TileBuild={sw.Elapsed.TotalMilliseconds:F3}ms");
                sw.Restart();
                FinalizeRows(Manager, rows);
                Manager.Begin();
                Manager.IsReady = true;
                Debug.Log($"[WSM3D][PERF] CompoundSpheres.CreateSphereManager.ManagerBegin={sw.Elapsed.TotalMilliseconds:F3}ms");
                sw.Restart();
                sphereManagerSettings.Initiation(Manager);
                Debug.Log($"[WSM3D][PERF] CompoundSpheres.CreateSphereManager.Initiation={sw.Elapsed.TotalMilliseconds:F3}ms");
                return Manager;
            }
            /// <summary>
            /// Coroutine factory that creates the sphere manager and spreads
            /// the heavy buffer fills across multiple frames.  The returned
            /// <see cref="SphereManager"/> is available immediately but
            /// <see cref="SphereManager.IsReady"/> will be false until the
            /// coroutine finishes.  Callers should gate
            /// <see cref="SphereManager.DrawTiles"/> on IsReady.
            /// </summary>
            public static IEnumerator CreateSphereManagerAsync(int rows, int cols, SphereManagerSettings sphereManagerSettings, Action<SphereManager> onCreated, int chunkSize = 4096, string Name = "SphereManager")
            {
                if (cols <= 0 || rows <= 0)
                {
                    throw new ArgumentException("Cols And Rows must be above 0 when creating a sphere manager");
                }
                var sw = Stopwatch.StartNew();
                GameObject go = new GameObject(Name);
                SphereManager Manager = go.AddComponent<SphereManager>().Init(rows, cols, sphereManagerSettings);
                Debug.Log($"[WSM3D][PERF] CompoundSpheres.CreateSphereManagerAsync.InitOnly={sw.Elapsed.TotalMilliseconds:F3}ms");
                sw.Restart();
                yield return BuildTilesAndRowsAsync(Manager, rows, cols, chunkSize);
                Debug.Log($"[WSM3D][PERF] CompoundSpheres.CreateSphereManagerAsync.TileBuild={sw.Elapsed.TotalMilliseconds:F3}ms");
                sw.Restart();
                FinalizeRows(Manager, rows);
                // Hand the manager back immediately so the caller can store
                // the reference; buffer fill happens over subsequent frames.
                onCreated?.Invoke(Manager);
                yield return Manager.BeginCoroutine(chunkSize);
                Debug.Log($"[WSM3D][PERF] CompoundSpheres.CreateSphereManagerAsync.ManagerBegin={sw.Elapsed.TotalMilliseconds:F3}ms");
                sw.Restart();
                sphereManagerSettings.Initiation(Manager);
                Debug.Log($"[WSM3D][PERF] CompoundSpheres.CreateSphereManagerAsync.Initiation={sw.Elapsed.TotalMilliseconds:F3}ms");
            }
            private static void BuildTilesAndRows(SphereManager Manager, int rows, int cols)
            {
                for (int X = 0; X < rows; X++)
                {
                    SphereRow row = Manager.SphereRows[X] = new SphereRow(Manager, X);
                    for (int Y = 0; Y < cols; Y++)
                    {
                        Manager.SphereTiles[(X * cols) + Y] = new SphereTile(X, Y, row);
                    }
                }
            }
            private static IEnumerator BuildTilesAndRowsAsync(SphereManager Manager, int rows, int cols, int chunkSize = 4096)
            {
                int count = 0;
                for (int X = 0; X < rows; X++)
                {
                    SphereRow row = Manager.SphereRows[X] = new SphereRow(Manager, X);
                    for (int Y = 0; Y < cols; Y++)
                    {
                        Manager.SphereTiles[(X * cols) + Y] = new SphereTile(X, Y, row);
                        if (++count % chunkSize == 0)
                        {
                            yield return null;
                        }
                    }
                }
            }
            private static void FinalizeRows(SphereManager Manager, int rows)
            {
                for (int r = 0; r < rows; r++)
                {
                    Manager.SphereRows[r].InitRangeBuffer();
                }
                Bounds meshBounds = Manager.SphereTileMesh.bounds;
                Manager._tileHalfSize = meshBounds.extents * 2f;
            }
        }
    }
}
