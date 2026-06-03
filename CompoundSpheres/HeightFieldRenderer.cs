using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace CompoundSpheres
{
    /// <summary>
    /// Generates a continuous height-field mesh from the tile grid, where each
    /// vertex sits at a tile CORNER with height = average of the 4 adjacent
    /// tiles and color = average of the 4 adjacent biome colors. Replaces
    /// the per-tile instanced-quad draw with a single unified mesh per visible
    /// region. ADR-0017 M0 — flat shape (CurrentShape=0) only.
    /// </summary>
    /// <summary>
    /// Liquid surface type. Each kind gets its own corner-averaged sub-mesh at its
    /// own level with a per-type material. Water is the original, pixel-verified
    /// surface; the others reuse the identical corner-average + analytic-normal +
    /// depth-shading math, differing only in classification + material.
    /// </summary>
    public enum LiquidKind
    {
        Water = 0,
        Lava = 1,
        Swamp = 2,
        Acid = 3,
    }

    public class HeightFieldRenderer
    {
        readonly SphereManager _manager;
        Mesh _mesh;
        Material _material;
        // Liquid surfaces live in the fork as ADDITIONAL corner-averaged sub-meshes built
        // the same way as terrain, but at each liquid's per-tile level (below sand), not as
        // a main-mod billboard/2D-overlay. Each LiquidKind has its own layer. Empty until
        // ConfigureLiquid (or the ConfigureWater shim) is called.
        //
        // The original single-water fields below are retained as the Water layer's storage
        // so the verified water path is byte-for-byte unchanged; additional kinds get their
        // own LiquidLayer entries in _liquids.
        Mesh _waterMesh;
        Material _waterMaterial;
        bool _dirty = true;
        int _lastMinRow = int.MinValue;
        int _lastMaxRow = int.MinValue;
        int _lastCameraX = int.MinValue;

        // Frame-coalesced rebuild throttle. Brush tools dirty hundreds of tiles
        // per frame; each rebuild is a 1M-vert mesh on a 316² map. Without
        // throttling the game freezes for the entire brush stroke. We defer
        // rebuild until either (a) no new dirty arrived for QuietMs (brush
        // stopped) or (b) we've been deferring for MaxStallMs (don't starve
        // user feedback during a sustained drag).
        float _lastDirtyTime = -1f;
        float _lastRebuildTime = -1f;
        const float QuietMs = 0.20f;      // seconds — wait 200ms after last dirty
        const float MaxStallMs = 0.50f;   // seconds — force rebuild after 500ms of continuous dirty
        const float MinIntervalMs = 0.10f; // seconds — minimum 100ms between rebuilds during an active brush stroke

        // PERF (residual rebuild-storm fix): the world's displayed tile-height
        // (render_z) flickers continuously on a LIVE sim — open water animates,
        // shore foam toggles, fire/lava spreads, biome morph — so the scale dirty
        // queue is almost never empty even when the terrain SHAPE is effectively
        // stable. Each such dirty previously forced a full 256² mesh re-bake at the
        // 100ms floor => the recurring "HEIGHTFIELD SLOW: 295-896ms" storm. The goal
        // is "bake ONCE on world-load, then stay clean". We can't make WorldBox stop
        // re-dirtying, so we DEBOUNCE steady-state churn hard: after the first few
        // post-load rebuilds the surface is settled, so we raise the minimum interval
        // between full rebuilds to SteadyStateIntervalMs. Genuine terraforming still
        // lands (within the interval) but cosmetic render_z flicker can no longer
        // storm. Brush strokes are unaffected (they coalesce via QuietMs/MaxStallMs
        // and still honour the tight MinIntervalMs while the stroke is active).
        const float SteadyStateIntervalMs = 2.0f; // seconds between rebuilds once settled
        // How many rebuilds count as the initial settle window (world-gen dirties
        // the whole map; it converges over a handful of frames before flicker takes
        // over). After this many, the steady-state interval applies.
        const int SettleRebuildBudget = 6;
        // realtime of the last NEW dirty that arrived AFTER the previous rebuild —
        // used to detect a fresh terraform vs. the same tiles re-flickering.
        float _lastResolvedRebuildTime = -1f;
        // Diagnostic: why the most recent rebuild ran (logged under ProfileRebuild).
        string _lastRebuildReason = "init";

        // PERF: when set by the consumer (gated behind SavedSettings.ProfilerDump so
        // it never spams the WorldBox debug-console overlay), each full Rebuild logs
        // a Stopwatch breakdown of the corner-bake / triangle / normal passes. Off by
        // default — per-frame logging is the documented overlay-spam trap.
        public static bool ProfileRebuild = false;

        // PERF instrumentation: counts how many times Rebuild() has actually run
        // this session. Logged (under ProfileRebuild) on every rebuild so the
        // deployed Player.log shows at a glance whether the terrain is baking ONCE
        // on world-load (goal) or storming every frame (the bug). If this number
        // climbs continuously while the camera is still, the dirty-gate is broken.
        static int _rebuildCount;

        // Callbacks to retrieve per-tile height and color without coupling
        // CompoundSpheres to WorldSphereMod types. Set by the consumer.
        Func<int, int, float> _sampleHeight;
        Func<int, int, Color32> _sampleColor;
        Func<int, int, int> _sampleTexture;
        Func<float, float, float, Vector3> _projectPosition;

        // Terrain-overlay callback (nullable; when unset terrain bakes base biome
        // color only). Drives the TERRAIN-OVERLAY region below; independent of the
        // liquid/water sub-mesh path so the two can be edited concurrently.
        Func<int, int, TerrainOverlay> _sampleOverlay;

        // Water callbacks (nullable; when unset no water surface is emitted).
        Func<int, int, bool> _sampleIsWater;   // (tx,ty) => tile is open water
        Func<int, int, float> _sampleWaterLevel; // (tx,ty) => absolute water-surface height
        Func<int, int, float> _sampleSeabed;     // (tx,ty) => terrain floor under the water column

        /// <summary>
        /// A single non-water liquid surface (lava/swamp/acid). Mirrors the water
        /// fields above but bundled per-kind so any number of liquids coexist. The
        /// rebuild math is shared with water via <see cref="RebuildLiquidLayer"/>.
        /// </summary>
        sealed class LiquidLayer
        {
            public LiquidKind Kind;
            public Mesh Mesh;
            public Material Material;
            public Func<int, int, bool> IsLiquid;
            public Func<int, int, float> Level;
            public Func<int, int, float> Seabed;
            // Per-layer scratch (separate topology: only corners touching this liquid).
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public float[] CornerHeights;
            public float[] Depth;
            public Color32[] Colors;
            public int[] Triangles;
        }

        // Extra liquid layers keyed by kind (Water stays in the dedicated fields above
        // so the verified path is untouched). Lava/Swamp/Acid land here.
        readonly Dictionary<LiquidKind, LiquidLayer> _liquids = new Dictionary<LiquidKind, LiquidLayer>();

        // Cached arrays to avoid GC churn on rebuild.
        Vector3[] _vertices;
        Vector3[] _normals;
        float[] _cornerHeights;
        Color32[] _colors;
        Vector2[] _uvs;
        int[] _triangles;

        // Separate scratch arrays for the water sub-mesh (different topology: only
        // corners touching a water tile are emitted).
        Vector3[] _wVertices;
        Vector3[] _wNormals;
        float[] _wCornerHeights;
        float[] _wDepth;
        Color32[] _wColors;
        int[] _wTriangles;

        public Mesh Mesh => _mesh;
        public Mesh WaterMesh => _waterMesh;
        public bool Dirty => _dirty;

        // -------------------------------------------------------------------
        // P2 GPU-compute path. When a GPU shim is bound, every height-field
        // rebuild also pushes per-tile heights into the new GPU manager's
        // InputHeights compute buffer (HasHeights=1), so the OutputMatrices
        // kernel places terrain tiles at their elevation on the GPU instead of
        // relying on the CPU matrix path. This is additive: the legacy
        // SphereManager mesh path is unchanged; binding is optional.
        // -------------------------------------------------------------------
        Compat.LegacyManagerShim _gpuShim;
        /// <summary>
        /// Bind a GPU-compute shim so terrain heights drive the GPU matrix
        /// kernel's InputHeights buffer. The shim's GpuSphereManager must have
        /// the same Rows/Cols grid as this renderer's SphereManager.
        /// </summary>
        public void BindGpu(Compat.LegacyManagerShim shim)
        {
            _gpuShim = shim;
            PushHeightsToGpu();
        }

        /// <summary>
        /// Fill the GPU InputHeights buffer from the per-tile sampleHeight
        /// callback (indexed X*Cols+Y to match the GPU tile order) and trigger a
        /// GPU matrix re-dispatch. No-op when no GPU shim is bound or not yet
        /// configured.
        /// </summary>
        void PushHeightsToGpu()
        {
            if (_gpuShim == null || _sampleHeight == null) return;
            int rows = _manager.Rows;
            int cols = _manager.Cols;
            _gpuShim.SetHeights(i =>
            {
                int x = i / cols;
                int y = i % cols;
                return _sampleHeight(x, y);
            });
        }

        public HeightFieldRenderer(SphereManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _mesh = new Mesh
            {
                name = "HeightFieldTerrain",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            // The land mesh is re-uploaded on every terrain change. MarkDynamic
            // tells Unity to keep it in CPU-writable memory so repeated uploads
            // skip the expensive static-mesh re-residency path on the GPU.
            _mesh.MarkDynamic();
        }

        /// <summary>
        /// Configure the callbacks that provide per-tile data. Must be called
        /// before the first Rebuild.
        /// </summary>
        /// <param name="sampleHeight">
        /// (tileX, tileY) => terrain height at that tile center.
        /// Out-of-bounds coords should be clamped/wrapped by the caller.
        /// </param>
        /// <param name="sampleColor">
        /// (tileX, tileY) => biome Color32 at that tile center.
        /// </param>
        /// <param name="sampleTexture">
        /// (tileX, tileY) => texture-array index for that tile.
        /// </param>
        /// <param name="projectPosition">
        /// (worldX, worldY, height) => 3D world-space position.
        /// For flat mode this is (x, height, y + ZDisplacement).
        /// </param>
        public void Configure(
            Func<int, int, float> sampleHeight,
            Func<int, int, Color32> sampleColor,
            Func<int, int, int> sampleTexture,
            Func<float, float, float, Vector3> projectPosition)
        {
            _sampleHeight = sampleHeight ?? throw new ArgumentNullException(nameof(sampleHeight));
            _sampleColor = sampleColor ?? throw new ArgumentNullException(nameof(sampleColor));
            _sampleTexture = sampleTexture ?? throw new ArgumentNullException(nameof(sampleTexture));
            _projectPosition = projectPosition ?? throw new ArgumentNullException(nameof(projectPosition));
        }

        #region TERRAIN-OVERLAY
        // ====================================================================
        // TERRAIN-OVERLAY family. Tints/layers the LAND height-field mesh per
        // WorldBox tile overlay state — snow coverage, tumor/corruption creep,
        // burnt char, and frozen ice. This is intentionally SEPARATE from the
        // liquid/water sub-mesh path (RebuildWater / RebuildLiquidLayer): the
        // overlay only touches the terrain vertex-color bake in Rebuild via the
        // corner-averaged _sampleOverlay callback and ApplyTerrainOverlay below.
        //
        // The overlay is a CHEAP vertex-color blend over the averaged biome
        // color. No extra mesh, no extra draw call — it rides the existing land
        // mesh. TUMOR is dynamic (spreads over time); the consumer dirties the
        // renderer when corruption changes so affected corners re-bake.
        // ====================================================================

        /// <summary>
        /// Per-tile overlay state fed from WorldBox tile data. Plain POD so the
        /// fork stays decoupled from WorldSphereMod / Assembly-CSharp types — the
        /// main mod reads the real tile fields and fills this in.
        /// Amounts are 0..1 coverage fractions; they are averaged over the
        /// up-to-4 tiles that share a mesh corner.
        /// </summary>
        public struct TerrainOverlay
        {
            /// <summary>Snow coverage 0..1 (heavier on cold biomes / peaks).</summary>
            public float Snow;
            /// <summary>Tumor/corruption creep 0..1 (dynamic; emissive violet).</summary>
            public float Corruption;
            /// <summary>Burnt/char 0..1 (after fire).</summary>
            public float Burnt;
            /// <summary>Frozen/ice 0..1 (pale-blue).</summary>
            public float Frozen;

            internal void Accumulate(TerrainOverlay o)
            {
                Snow += o.Snow;
                Corruption += o.Corruption;
                Burnt += o.Burnt;
                Frozen += o.Frozen;
            }

            internal void Normalize(int count)
            {
                if (count <= 0) return;
                float inv = 1f / count;
                Snow *= inv;
                Corruption *= inv;
                Burnt *= inv;
                Frozen *= inv;
            }
        }

        /// <summary>
        /// Configure the terrain-overlay callback. Optional — when not called the
        /// land mesh bakes base biome color only. <paramref name="sampleOverlay"/>
        /// returns the overlay coverage for a tile; out-of-bounds coords should be
        /// clamped/wrapped by the caller (same convention as the other callbacks).
        /// </summary>
        public void ConfigureTerrainOverlay(Func<int, int, TerrainOverlay> sampleOverlay)
        {
            _sampleOverlay = sampleOverlay;
            MarkDirty();
        }

        /// <summary>
        /// Blend the overlay tints over a base biome color. Ordering matters:
        /// burnt darkens first (it sits under everything), then corruption creep
        /// (emissive violet, the most visually dominant living overlay), then snow
        /// (white coverage, boosted by elevation), then frozen ice (pale blue,
        /// applied last so a frozen surface always reads cold even under snow).
        /// Each layer is a simple lerp by its coverage fraction — cheap, no shader.
        /// </summary>
        static Color32 ApplyTerrainOverlay(Color32 baseColor, TerrainOverlay ov, float elevation)
        {
            // Work in float RGB 0..1.
            float r = baseColor.r / 255f;
            float g = baseColor.g / 255f;
            float b = baseColor.b / 255f;

            // BURNT — dark char. Crush toward near-black with a faint warm ember.
            if (ov.Burnt > 0.001f)
            {
                float t = Mathf.Clamp01(ov.Burnt);
                r = Mathf.Lerp(r, 0.10f, t);
                g = Mathf.Lerp(g, 0.07f, t);
                b = Mathf.Lerp(b, 0.06f, t);
            }

            // CORRUPTION / TUMOR — emissive purple/violet creep. Push strongly
            // toward violet and brighten slightly so it reads as glowing biomass.
            if (ov.Corruption > 0.001f)
            {
                float t = Mathf.Clamp01(ov.Corruption);
                r = Mathf.Lerp(r, 0.55f, t);
                g = Mathf.Lerp(g, 0.08f, t);
                b = Mathf.Lerp(b, 0.70f, t);
            }

            // SNOW — white coverage. Stronger at high elevation: peaks accumulate
            // more snow than valleys for the same tile coverage value. Elevation
            // is the corner's averaged terrain height (post micro-displacement).
            if (ov.Snow > 0.001f)
            {
                // Map elevation to a 0.6..1.0 multiplier so high ground gets full
                // snow while lowland snow stays partial. Heights are unscaled
                // (pre-HeightMult) tile heights, typically ~0..30; clamp gently.
                float elevBoost = 0.6f + 0.4f * Mathf.Clamp01(elevation / 24f);
                float t = Mathf.Clamp01(ov.Snow * elevBoost);
                r = Mathf.Lerp(r, 0.95f, t);
                g = Mathf.Lerp(g, 0.96f, t);
                b = Mathf.Lerp(b, 1.00f, t);
            }

            // FROZEN — pale-blue ice. Applied last; tints whatever is below cold.
            if (ov.Frozen > 0.001f)
            {
                float t = Mathf.Clamp01(ov.Frozen);
                r = Mathf.Lerp(r, 0.72f, t);
                g = Mathf.Lerp(g, 0.85f, t);
                b = Mathf.Lerp(b, 0.98f, t);
            }

            return new Color32(
                (byte)(Mathf.Clamp01(r) * 255f),
                (byte)(Mathf.Clamp01(g) * 255f),
                (byte)(Mathf.Clamp01(b) * 255f),
                255);
        }
        #endregion

        /// <summary>
        /// Configure the water surface callbacks. Optional — when not called (or
        /// passed nulls) the height field emits land only. The water sub-mesh is
        /// built corner-averaged exactly like terrain, but each corner sits at the
        /// averaged <paramref name="sampleWaterLevel"/> (which the caller derives
        /// from WorldBox sea level / shore data), so it sits AT/BELOW the shore
        /// rather than floating above it. Per-corner depth = waterLevel - seabed
        /// is baked into vertex color for shore/depth shading.
        /// </summary>
        /// <param name="sampleIsWater">(tileX, tileY) => true if the tile is open water.</param>
        /// <param name="sampleWaterLevel">(tileX, tileY) => absolute water-surface height (world units).</param>
        /// <param name="sampleSeabed">(tileX, tileY) => terrain floor height under the water column.</param>
        public void ConfigureWater(
            Func<int, int, bool> sampleIsWater,
            Func<int, int, float> sampleWaterLevel,
            Func<int, int, float> sampleSeabed)
        {
            _sampleIsWater = sampleIsWater;
            _sampleWaterLevel = sampleWaterLevel;
            _sampleSeabed = sampleSeabed;
            if (_waterMesh == null)
            {
                _waterMesh = new Mesh
                {
                    name = "HeightFieldWater",
                    indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
                };
            }
            MarkDirty();
        }

        /// <summary>
        /// Set the material used to draw the water sub-mesh. Should be a translucent
        /// (alpha-blended) material so terrain reads through the water column.
        /// </summary>
        public void SetWaterMaterial(Material mat)
        {
            _waterMaterial = mat;
        }

        /// <summary>
        /// Generalized liquid-surface configuration. Each <see cref="LiquidKind"/> gets
        /// its own corner-averaged sub-mesh at its own level, built with the IDENTICAL
        /// corner-average + analytic-normal + depth-shading math as water. Water is
        /// special-cased to the dedicated (verified) fields/mesh so it never regresses;
        /// every other kind is stored in its own <see cref="LiquidLayer"/>.
        /// </summary>
        /// <param name="kind">Which liquid this surface represents.</param>
        /// <param name="sampleIsLiquid">(tileX, tileY) => true if the tile is this liquid.</param>
        /// <param name="sampleLevel">(tileX, tileY) => absolute liquid-surface height (world units).</param>
        /// <param name="sampleSeabed">(tileX, tileY) => terrain floor height under the liquid column.</param>
        public void ConfigureLiquid(
            LiquidKind kind,
            Func<int, int, bool> sampleIsLiquid,
            Func<int, int, float> sampleLevel,
            Func<int, int, float> sampleSeabed)
        {
            if (kind == LiquidKind.Water)
            {
                // Route water through the original dedicated path (no behavioural change).
                ConfigureWater(sampleIsLiquid, sampleLevel, sampleSeabed);
                return;
            }

            if (!_liquids.TryGetValue(kind, out var layer))
            {
                layer = new LiquidLayer
                {
                    Kind = kind,
                    Mesh = new Mesh
                    {
                        name = "HeightField" + kind,
                        indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
                    },
                };
                _liquids[kind] = layer;
            }
            layer.IsLiquid = sampleIsLiquid;
            layer.Level = sampleLevel;
            layer.Seabed = sampleSeabed;
            MarkDirty();
        }

        /// <summary>
        /// Set the material for a non-water liquid layer. (Water uses
        /// <see cref="SetWaterMaterial"/>.) The layer must have been configured first.
        /// </summary>
        public void SetLiquidMaterial(LiquidKind kind, Material mat)
        {
            if (kind == LiquidKind.Water) { _waterMaterial = mat; return; }
            if (_liquids.TryGetValue(kind, out var layer)) layer.Material = mat;
        }

        /// <summary>
        /// Mark the mesh as needing a full rebuild (e.g. after tile type change).
        /// </summary>
        public void MarkDirty()
        {
            _dirty = true;
            _lastDirtyTime = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Rebuild the height-field mesh for the visible row range and draw it.
        /// Call this from DrawTiles instead of the per-row instanced path.
        /// </summary>
        /// <param name="cameraX">Camera row position (same as DrawTiles param).</param>
        /// <param name="minRow">Signed offset from cameraX for the first visible row.</param>
        /// <param name="maxRow">Signed offset from cameraX for the last visible row (exclusive).</param>
        /// <param name="wrapped">True if the X axis wraps (cylindrical).</param>
        public void RebuildAndDraw(int cameraX, int minRow, int maxRow, bool wrapped)
        {
            if (_sampleHeight == null || _projectPosition == null)
            {
                Debug.LogWarning("[WSM3D] HeightFieldRenderer: not configured, skipping draw.");
                return;
            }

            // PERF: rebuild only when world tiles change (_dirty), NOT on every
            // camera pan. Camera range changes do not require a CPU mesh rebuild
            // — the GPU clips off-screen geometry trivially. The mesh now spans
            // the full world; we ignore minRow/maxRow except on first build.
            int rows = _manager.Rows;
            int fullMin = 0;
            int fullMax = rows;

            bool isFirstBuild = _lastMinRow == int.MinValue;
            bool shouldRebuild = isFirstBuild;
            if (isFirstBuild) _lastRebuildReason = "first-build";

            if (_dirty && !isFirstBuild)
            {
                // Brush-stroke coalescing: defer rebuild until brush goes quiet
                // (no new dirty for QuietMs) OR we've been stalling for MaxStallMs.
                // Also enforce a minimum interval between full rebuilds so
                // rapid-fire dirties can't cause back-to-back 1M-vert rebuilds.
                float now = Time.realtimeSinceStartup;
                float sinceLastDirty = now - _lastDirtyTime;
                float sinceLastRebuild = now - _lastRebuildTime;
                bool brushQuiet = sinceLastDirty >= QuietMs;
                bool stalledTooLong = sinceLastRebuild >= MaxStallMs;

                // Steady-state debounce: once the world has settled (past the
                // initial post-load convergence window), a full rebuild may run at
                // most every SteadyStateIntervalMs. This is what kills the residual
                // 295-896ms storm: continuous render_z flicker on the live sim keeps
                // _dirty set, but the rebuild is now rate-limited to once per ~2s
                // instead of every 100ms. During the settle window (or an active
                // brush stroke, signalled by recent QuietMs-bounded dirties) we keep
                // the tight 100ms floor so terraforming stays responsive.
                bool settled = _rebuildCount >= SettleRebuildBudget;
                float minInterval = settled ? SteadyStateIntervalMs : MinIntervalMs;
                bool intervalElapsed = sinceLastRebuild >= minInterval;

                if ((brushQuiet || stalledTooLong) && intervalElapsed)
                {
                    shouldRebuild = true;
                    _lastRebuildReason = settled
                        ? (brushQuiet ? "steady-quiet" : "steady-stall")
                        : (brushQuiet ? "settle-quiet" : "settle-stall");
                }
            }

            if (shouldRebuild)
            {
                Rebuild(0, fullMin, fullMax, wrapped);
                // P2: keep the GPU matrix kernel's InputHeights in sync with the
                // CPU corner-averaged mesh after every terrain rebuild.
                PushHeightsToGpu();
                _lastMinRow = fullMin;
                _lastMaxRow = fullMax;
                _lastCameraX = 0;
                _dirty = false;
                _lastRebuildTime = Time.realtimeSinceStartup;
            }

            if (_material == null)
            {
                _material = _manager.Material;
            }

            if (_mesh.vertexCount > 0 && _material != null)
            {
                Graphics.DrawMesh(_mesh, Matrix4x4.identity, _material, 0);
            }

            // Draw the water sub-mesh on top (translucent, queued after opaque land).
            if (_waterMesh != null && _waterMesh.vertexCount > 0 && _waterMaterial != null)
            {
                Graphics.DrawMesh(_waterMesh, Matrix4x4.identity, _waterMaterial, 0);
            }

            // Draw the other liquid surfaces (lava/swamp/acid) the same way.
            foreach (var kv in _liquids)
            {
                var layer = kv.Value;
                if (layer.Mesh != null && layer.Mesh.vertexCount > 0 && layer.Material != null)
                {
                    Graphics.DrawMesh(layer.Mesh, Matrix4x4.identity, layer.Material, 0);
                }
            }
        }

        /// <summary>
        /// Overrides the material. If not called, the SphereManager's material is used.
        /// </summary>
        public void SetMaterial(Material mat)
        {
            _material = mat;
        }

        void Rebuild(int cameraX, int minRow, int maxRow, bool wrapped)
        {
            int rows = _manager.Rows;
            int cols = _manager.Cols;
            int rowCount = maxRow - minRow;
            if (rowCount <= 0 || cols <= 0) return;

            // Resolve the actual tile-row indices (may wrap).
            int[] rowIndices = new int[rowCount];
            for (int i = 0; i < rowCount; i++)
            {
                int raw = cameraX + minRow + i;
                if (wrapped)
                {
                    raw = ((raw % rows) + rows) % rows;
                }
                else
                {
                    raw = Mathf.Clamp(raw, 0, rows - 1);
                }
                rowIndices[i] = raw;
            }

            // Vertex grid: (rowCount+1) x (cols+1) corners.
            // Each corner (cr, cc) sits at the junction of up to 4 tiles.
            int cornerRows = rowCount + 1;
            int cornerCols = cols + 1;
            int vertCount = cornerRows * cornerCols;
            int quadCount = rowCount * cols;
            int triCount = quadCount * 6;

            EnsureArrays(vertCount, triCount);

            var sw = ProfileRebuild ? System.Diagnostics.Stopwatch.StartNew() : null;

            // Build vertices: for each corner, average the heights & colors of
            // the up-to-4 tiles that share it. PARALLELIZED over corner ROWS:
            // each iteration writes ONLY vertex indices in its own row
            // (vi = cr*cornerCols+cc), so rows never collide and the per-corner
            // work (sampleHeight/Color/Texture callbacks + averaging) is the
            // single biggest cost in the rebuild. On an 8-core box this drops the
            // O(corners*4) main-thread bake by ~Ncores. The sample callbacks must
            // be pure reads of already-committed tile data (true here: RefreshSphere
            // drains all dirty queues BEFORE MarkDirty, so no tile mutation races).
            Parallel.For(0, cornerRows, cr =>
            {
                for (int cc = 0; cc < cornerCols; cc++)
                {
                    int vi = cr * cornerCols + cc;

                    // The 4 tiles adjacent to corner (cr, cc) in local row-offset
                    // coords are at row offsets (cr-1, cr) and col offsets (cc-1, cc).
                    float hSum = 0f;
                    int rSum = 0, gSum = 0, bSum = 0;
                    int count = 0;
                    int dominantTex = 0;
                    float maxContrib = -1f;

                    // TERRAIN-OVERLAY: accumulate per-tile overlay state over the
                    // same up-to-4 adjacent tiles so the corner tint is averaged
                    // exactly like base color/height. See the TERRAIN-OVERLAY region.
                    TerrainOverlay ovAccum = default;
                    int ovCount = 0;

                    for (int dr = -1; dr <= 0; dr++)
                    {
                        int localRow = cr + dr;
                        if (localRow < 0 || localRow >= rowCount) continue;
                        int tileX = rowIndices[localRow];

                        for (int dc = -1; dc <= 0; dc++)
                        {
                            int localCol = cc + dc;
                            if (localCol < 0 || localCol >= cols) continue;
                            int tileY = localCol;

                            float h = _sampleHeight(tileX, tileY);
                            Color32 c = _sampleColor(tileX, tileY);
                            hSum += h;
                            rSum += c.r;
                            gSum += c.g;
                            bSum += c.b;
                            count++;

                            if (_sampleOverlay != null)
                            {
                                ovAccum.Accumulate(_sampleOverlay(tileX, tileY));
                                ovCount++;
                            }

                            // Dominant texture: pick the tile with highest height
                            // contribution (simple heuristic).
                            if (h > maxContrib)
                            {
                                maxContrib = h;
                                dominantTex = _sampleTexture(tileX, tileY);
                            }
                        }
                    }

                    if (count == 0) count = 1;
                    float avgH = hSum / count;

                    // Micro-displacement: tile corners would otherwise sit at exactly
                    // averaged heights, producing perfectly flat shaded quads with
                    // visible biome-color blobs. A small Perlin offset breaks the
                    // uniformity so adjacent quads tilt differently and pick up
                    // distinct lighting, giving the surface "roughness" without
                    // requiring a detail-texture shader. Frequency 0.1 covers ~10
                    // tiles per noise cycle; amplitude 0.15 stays well under the
                    // typical 1.0-unit height step between biomes.
                    // Use cc/cr-driven noise (stable across rebuilds, not world-x
                    // because cylindrical wrapping would seam at the dateline).
                    float noise = Mathf.PerlinNoise(cc * 0.1f, cr * 0.1f);
                    avgH += (noise - 0.5f) * 0.3f;

                    // World position of this corner: it's at tile-grid coords
                    // (tileX + 0.5 or -0.5, tileY + 0.5 or -0.5) but the
                    // simpler formulation: corner (cr, cc) in local space
                    // corresponds to world X at the edge between localRow and
                    // localRow-1, world Y at cc.
                    //
                    // For flat mode the tile at grid (x, y) has its center at
                    // world position (x, 0, y+ZDisplacement). Corners are offset
                    // by -0.5 from each tile center they're adjacent to.
                    float worldX;
                    if (cr < rowCount)
                    {
                        worldX = rowIndices[cr] - 0.5f;
                    }
                    else if (cr > 0)
                    {
                        worldX = rowIndices[cr - 1] + 0.5f;
                    }
                    else
                    {
                        worldX = rowIndices[0] - 0.5f;
                    }

                    float worldY = cc - 0.5f;

                    _vertices[vi] = _projectPosition(worldX, worldY, avgH);
                    _cornerHeights[vi] = avgH;

                    Color32 baseColor = new Color32(
                        (byte)(rSum / count),
                        (byte)(gSum / count),
                        (byte)(bSum / count),
                        255);
                    // TERRAIN-OVERLAY: blend snow/tumor/burnt/frozen tint over the
                    // averaged biome color (cheap vertex-color layering). Driven by
                    // the corner-averaged overlay state; elevation passed so snow
                    // reads heavier on peaks.
                    if (ovCount > 0)
                    {
                        ovAccum.Normalize(ovCount);
                        baseColor = ApplyTerrainOverlay(baseColor, ovAccum, avgH);
                    }
                    _colors[vi] = baseColor;
                    _uvs[vi] = new Vector2(
                        (float)cc / cols,
                        (float)cr / rowCount);
                }
            });

            long bakeMs = sw?.ElapsedMilliseconds ?? 0;

            // Build triangles: two tris per quad.
            int ti = 0;
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int bl = r * cornerCols + c;
                    int br = bl + 1;
                    int tl = bl + cornerCols;
                    int tr = tl + 1;

                    _triangles[ti++] = bl;
                    _triangles[ti++] = tl;
                    _triangles[ti++] = br;

                    _triangles[ti++] = br;
                    _triangles[ti++] = tl;
                    _triangles[ti++] = tr;
                }
            }

            // Analytic normals from the height-field gradient. Corner spacing is
            // 1 world unit in both grid axes; tangents are (2, dh/dx, 0) and
            // (0, dh/dz, 2) using central differences, so the normal is their
            // cross product. This gives smooth per-vertex lighting on slopes
            // instead of the flat-shaded-per-triangle look RecalculateNormals
            // produces on a low-poly height field.
            long triMs = sw?.ElapsedMilliseconds ?? 0;

            // Normals are also per-corner-row independent: each iteration reads
            // _cornerHeights (now fully populated) and writes only _normals[cr*..].
            Parallel.For(0, cornerRows, cr =>
            {
                int crM = cr > 0 ? cr - 1 : cr;
                int crP = cr < cornerRows - 1 ? cr + 1 : cr;
                for (int cc = 0; cc < cornerCols; cc++)
                {
                    int ccM = cc > 0 ? cc - 1 : cc;
                    int ccP = cc < cornerCols - 1 ? cc + 1 : cc;

                    float hxPlus  = _cornerHeights[crP * cornerCols + cc];
                    float hxMinus = _cornerHeights[crM * cornerCols + cc];
                    float hzPlus  = _cornerHeights[cr  * cornerCols + ccP];
                    float hzMinus = _cornerHeights[cr  * cornerCols + ccM];

                    Vector3 tx = new Vector3(2f, hxPlus - hxMinus, 0f);
                    Vector3 tz = new Vector3(0f, hzPlus - hzMinus, 2f);
                    Vector3 n = Vector3.Cross(tx, tz).normalized;
                    if (n.y < 0f) n = -n;
                    _normals[cr * cornerCols + cc] = n;
                }
            });

            long normMs = sw?.ElapsedMilliseconds ?? 0;

            // GPU upload. PERF: the previous path was
            //     _mesh.vertices = SubArray(...);  // x5 channels
            // Each `SubArray` allocated + Array.Copy'd a fresh ~vertCount array
            // (5 allocations of ~100k elems => GC churn every rebuild), and each
            // Mesh.<channel> SETTER does its own managed->native marshal + bounds
            // recompute. We now:
            //   (1) keep MarkDynamic() (set in ctor) so the buffer stays
            //       CPU-writable across re-uploads;
            //   (2) use the Set*(array, start, length) overloads (Unity 2022.3+)
            //       which upload directly from the cached scratch arrays with NO
            //       SubArray allocation/copy, in one marshalled call per channel;
            //   (3) pass MeshUpdateFlags.DontRecalculateBounds and set bounds once
            //       at the end, instead of an implicit recompute per setter.
            // This collapses the per-rebuild allocation to zero and removes the
            // redundant per-setter work. (Full single-buffer interleaved upload via
            // SetVertexBufferData is the next step but needs a struct vertex layout;
            // tracked alongside the GPU-compute offload #199.)
            const UnityEngine.Rendering.MeshUpdateFlags NoRecalc =
                UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds |
                UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices;

            _mesh.Clear();
            // SetVertices must precede SetTriangles (index validation needs the
            // vertex count); bounds recompute deferred to the explicit call below.
            _mesh.SetVertices(_vertices, 0, vertCount, NoRecalc);
            _mesh.SetNormals(_normals, 0, vertCount, NoRecalc);
            _mesh.SetColors(_colors, 0, vertCount, NoRecalc);
            _mesh.SetUVs(0, _uvs, 0, vertCount);
            _mesh.SetTriangles(_triangles, 0, triCount, 0, calculateBounds: false);
            _mesh.RecalculateBounds();

            // Always advance the rebuild counter (not just under ProfileRebuild) so
            // the settle-window debounce and the reason log stay consistent whether
            // or not profiling is on.
            _rebuildCount++;

            if (sw != null)
            {
                sw.Stop();
                long totalMs = sw.ElapsedMilliseconds;
                Debug.LogWarning($"[WSM3D][PERF] HeightField.Rebuild #{_rebuildCount} {totalMs}ms " +
                    $"reason={_lastRebuildReason} " +
                    $"(bake={bakeMs}ms tri={triMs - bakeMs}ms norm={normMs - triMs}ms " +
                    $"upload={totalMs - normMs}ms verts={vertCount} tris={triCount / 3}) " +
                    "[steady-* reason climbing while idle => sim render_z flicker; " +
                    "settle-* only during world-load convergence]");
            }

            RebuildWater(rowIndices, rowCount, cornerRows, cornerCols, wrapped);

            // Rebuild every non-water liquid layer with the SAME corner-average +
            // analytic-normal + depth-shading math (factored into RebuildLiquidMesh).
            foreach (var kv in _liquids)
            {
                RebuildLiquidLayer(kv.Value, rowIndices, rowCount, cornerRows, cornerCols);
            }
        }

        /// <summary>
        /// Build one non-water liquid layer's sub-mesh. Identical algorithm to
        /// <see cref="RebuildWater"/> (corner-averaged level over adjacent liquid tiles,
        /// quads only where all 4 corners are liquid, depth baked into vertex color,
        /// analytic normals from the level gradient) but operating on the layer's own
        /// scratch arrays + callbacks.
        /// </summary>
        void RebuildLiquidLayer(LiquidLayer layer, int[] rowIndices, int rowCount, int cornerRows, int cornerCols)
        {
            if (layer.Mesh == null || layer.IsLiquid == null || layer.Level == null)
            {
                if (layer.Mesh != null) layer.Mesh.Clear();
                return;
            }

            int vertCount = cornerRows * cornerCols;
            int maxTri = rowCount * (cornerCols - 1) * 6;
            if (layer.Vertices == null || layer.Vertices.Length < vertCount)
            {
                layer.Vertices = new Vector3[vertCount];
                layer.Normals = new Vector3[vertCount];
                layer.CornerHeights = new float[vertCount];
                layer.Depth = new float[vertCount];
                layer.Colors = new Color32[vertCount];
            }
            if (layer.Triangles == null || layer.Triangles.Length < maxTri)
                layer.Triangles = new int[maxTri];

            float maxDepth = 0.0001f;
            for (int cr = 0; cr < cornerRows; cr++)
            {
                for (int cc = 0; cc < cornerCols; cc++)
                {
                    int vi = cr * cornerCols + cc;
                    float lvlSum = 0f, depthSum = 0f;
                    int liquidCount = 0;
                    bool touchesLand = false;

                    for (int dr = -1; dr <= 0; dr++)
                    {
                        int localRow = cr + dr;
                        if (localRow < 0 || localRow >= rowCount) continue;
                        int tileX = rowIndices[localRow];
                        for (int dc = -1; dc <= 0; dc++)
                        {
                            int localCol = cc + dc;
                            if (localCol < 0 || localCol >= _manager.Cols) continue;
                            int tileY = localCol;
                            if (layer.IsLiquid(tileX, tileY))
                            {
                                float lvl = layer.Level(tileX, tileY);
                                float seabed = layer.Seabed != null ? layer.Seabed(tileX, tileY) : lvl;
                                float depth = lvl - seabed;
                                if (depth < 0f) depth = 0f;
                                lvlSum += lvl;
                                depthSum += depth;
                                liquidCount++;
                                if (depth > maxDepth) maxDepth = depth;
                            }
                            else touchesLand = true;
                        }
                    }

                    if (liquidCount == 0)
                    {
                        layer.CornerHeights[vi] = float.NaN;
                        layer.Colors[vi] = new Color32(0, 0, 0, 0);
                        layer.Vertices[vi] = Vector3.zero;
                        continue;
                    }

                    float avgLvl = lvlSum / liquidCount;
                    layer.Depth[vi] = depthSum / liquidCount;

                    float worldX;
                    if (cr < rowCount) worldX = rowIndices[cr] - 0.5f;
                    else if (cr > 0) worldX = rowIndices[cr - 1] + 0.5f;
                    else worldX = rowIndices[0] - 0.5f;
                    float worldY = cc - 0.5f;

                    layer.Vertices[vi] = _projectPosition(worldX, worldY, avgLvl);
                    layer.CornerHeights[vi] = avgLvl;
                    layer.Colors[vi] = new Color32(0, (byte)(touchesLand ? 255 : 0), 0, 255);
                }
            }

            // Bake normalized depth into R/B and translucency into A.
            for (int vi = 0; vi < vertCount; vi++)
            {
                if (float.IsNaN(layer.CornerHeights[vi])) continue;
                float frac = Mathf.Clamp01(layer.Depth[vi] / maxDepth);
                byte f = (byte)(frac * 255f);
                layer.Colors[vi] = new Color32(f, layer.Colors[vi].g, f, (byte)(140f + frac * 80f));
            }

            int ti = 0;
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < cornerCols - 1; c++)
                {
                    int bl = r * cornerCols + c;
                    int br = bl + 1;
                    int tl = bl + cornerCols;
                    int tr = tl + 1;
                    if (float.IsNaN(layer.CornerHeights[bl]) || float.IsNaN(layer.CornerHeights[br]) ||
                        float.IsNaN(layer.CornerHeights[tl]) || float.IsNaN(layer.CornerHeights[tr]))
                        continue;
                    layer.Triangles[ti++] = bl; layer.Triangles[ti++] = tl; layer.Triangles[ti++] = br;
                    layer.Triangles[ti++] = br; layer.Triangles[ti++] = tl; layer.Triangles[ti++] = tr;
                }
            }

            for (int cr = 0; cr < cornerRows; cr++)
            {
                int crM = cr > 0 ? cr - 1 : cr;
                int crP = cr < cornerRows - 1 ? cr + 1 : cr;
                for (int cc = 0; cc < cornerCols; cc++)
                {
                    int idx = cr * cornerCols + cc;
                    if (float.IsNaN(layer.CornerHeights[idx])) { layer.Normals[idx] = Vector3.up; continue; }
                    int ccM = cc > 0 ? cc - 1 : cc;
                    int ccP = cc < cornerCols - 1 ? cc + 1 : cc;
                    float hxP = Safe(layer.CornerHeights[crP * cornerCols + cc], layer.CornerHeights[idx]);
                    float hxM = Safe(layer.CornerHeights[crM * cornerCols + cc], layer.CornerHeights[idx]);
                    float hzP = Safe(layer.CornerHeights[cr * cornerCols + ccP], layer.CornerHeights[idx]);
                    float hzM = Safe(layer.CornerHeights[cr * cornerCols + ccM], layer.CornerHeights[idx]);
                    Vector3 tx = new Vector3(2f, hxP - hxM, 0f);
                    Vector3 tz = new Vector3(0f, hzP - hzM, 2f);
                    Vector3 n = Vector3.Cross(tx, tz).normalized;
                    if (n.y < 0f) n = -n;
                    layer.Normals[idx] = n;
                }
            }

            layer.Mesh.Clear();
            if (ti == 0) return;
            layer.Mesh.vertices = SubArray(layer.Vertices, vertCount);
            layer.Mesh.normals = SubArray(layer.Normals, vertCount);
            layer.Mesh.colors32 = SubArray(layer.Colors, vertCount);
            layer.Mesh.triangles = SubArray(layer.Triangles, ti);
            layer.Mesh.RecalculateBounds();
        }

        /// <summary>
        /// Build the water sub-mesh from the same corner grid as the land mesh.
        /// A corner emits a water vertex iff any of its up-to-4 adjacent tiles is
        /// water; quads are emitted only when all 4 of their corners are water (so
        /// the surface stops at the shore rather than overshooting onto land).
        /// Corner height uses a single sampled water level (flat across the ocean),
        /// so the water surface is not pulled down by nearby seabed elevation.
        /// Depth = waterLevel - seabed is baked into vertex color (R/B =
        /// depth fraction, G = shore flag) reusing the scheme the old overlay
        /// proved.
        /// </summary>
        void RebuildWater(int[] rowIndices, int rowCount, int cornerRows, int cornerCols, bool wrapped)
        {
            if (_waterMesh == null || _sampleIsWater == null || _sampleWaterLevel == null)
            {
                if (_waterMesh != null) _waterMesh.Clear();
                return;
            }

            int vertCount = cornerRows * cornerCols;
            int maxTri = rowCount * (cornerCols - 1) * 6;
            EnsureWaterArrays(vertCount, maxTri);

            // First pass: find a constant water level for the rebuild (from the
            // first seen water tile) and average depth over adjacent water tiles
            // only; flag shore corners (touch a non-water tile).
            // -1 marks a corner with no adjacent water (not part of the surface).
            float maxDepth = 0.0001f;
            float constantWaterLevel = float.NaN;
            for (int cr = 0; cr < cornerRows; cr++)
            {
                for (int cc = 0; cc < cornerCols; cc++)
                {
                    int vi = cr * cornerCols + cc;
                    float depthSum = 0f;
                    int waterCount = 0;
                    bool touchesLand = false;

                    for (int dr = -1; dr <= 0; dr++)
                    {
                        int localRow = cr + dr;
                        if (localRow < 0 || localRow >= rowCount) continue;
                        int tileX = rowIndices[localRow];
                        for (int dc = -1; dc <= 0; dc++)
                        {
                            int localCol = cc + dc;
                            if (localCol < 0 || localCol >= _manager.Cols) continue;
                            int tileY = localCol;

                            if (_sampleIsWater(tileX, tileY))
                            {
                                float sampledLevel = _sampleWaterLevel(tileX, tileY);
                                if (float.IsNaN(constantWaterLevel))
                                {
                                    constantWaterLevel = sampledLevel;
                                }
                                float seabed = _sampleSeabed != null ? _sampleSeabed(tileX, tileY) : sampledLevel;
                                float depth = sampledLevel - seabed;
                                if (depth < 0f) depth = 0f;
                                depthSum += depth;
                                waterCount++;
                                if (depth > maxDepth) maxDepth = depth;
                            }
                            else
                            {
                                touchesLand = true;
                            }
                        }
                    }

                    if (waterCount == 0)
                    {
                        _wCornerHeights[vi] = float.NaN; // no water here
                        _wColors[vi] = new Color32(0, 0, 0, 0);
                        _wVertices[vi] = Vector3.zero;
                        continue;
                    }

                    if (float.IsNaN(constantWaterLevel))
                    {
                        continue;
                    }

                    float wl = constantWaterLevel;
                    _wDepth[vi] = depthSum / waterCount;

                    float worldX;
                    if (cr < rowCount) worldX = rowIndices[cr] - 0.5f;
                    else if (cr > 0) worldX = rowIndices[cr - 1] + 0.5f;
                    else worldX = rowIndices[0] - 0.5f;
                    float worldY = cc - 0.5f;

                    _wVertices[vi] = _projectPosition(worldX, worldY, wl);
                    _wCornerHeights[vi] = wl;
                    // Stash shore flag in G; R/B/A finalized once maxDepth is known.
                    _wColors[vi] = new Color32(0, (byte)(touchesLand ? 255 : 0), 0, 255);
                    _uvs[vi] = new Vector2((float)cc / Mathf.Max(1, _manager.Cols), (float)cr / Mathf.Max(1, rowCount));
                }
            }

            // Bake normalized depth into R/B and translucency into A (shallow water
            // is clearer, deep water more opaque), now that maxDepth is known.
            for (int vi = 0; vi < vertCount; vi++)
            {
                if (float.IsNaN(_wCornerHeights[vi])) continue;
                float frac = Mathf.Clamp01(_wDepth[vi] / maxDepth);
                byte f = (byte)(frac * 255f);
                _wColors[vi] = new Color32(f, _wColors[vi].g, f, (byte)(140f + frac * 80f));
            }

            // Triangles: emit a quad only when all 4 corners are water vertices.
            int ti = 0;
            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < cornerCols - 1; c++)
                {
                    int bl = r * cornerCols + c;
                    int br = bl + 1;
                    int tl = bl + cornerCols;
                    int tr = tl + 1;
                    if (float.IsNaN(_wCornerHeights[bl]) || float.IsNaN(_wCornerHeights[br]) ||
                        float.IsNaN(_wCornerHeights[tl]) || float.IsNaN(_wCornerHeights[tr]))
                        continue;

                    _wTriangles[ti++] = bl; _wTriangles[ti++] = tl; _wTriangles[ti++] = br;
                    _wTriangles[ti++] = br; _wTriangles[ti++] = tl; _wTriangles[ti++] = tr;
                }
            }

            // Analytic normals from the water-level gradient (same math as land).
            for (int cr = 0; cr < cornerRows; cr++)
            {
                int crM = cr > 0 ? cr - 1 : cr;
                int crP = cr < cornerRows - 1 ? cr + 1 : cr;
                for (int cc = 0; cc < cornerCols; cc++)
                {
                    int idx = cr * cornerCols + cc;
                    if (float.IsNaN(_wCornerHeights[idx])) { _wNormals[idx] = Vector3.up; continue; }
                    int ccM = cc > 0 ? cc - 1 : cc;
                    int ccP = cc < cornerCols - 1 ? cc + 1 : cc;
                    float hxP = Safe(_wCornerHeights[crP * cornerCols + cc], _wCornerHeights[idx]);
                    float hxM = Safe(_wCornerHeights[crM * cornerCols + cc], _wCornerHeights[idx]);
                    float hzP = Safe(_wCornerHeights[cr * cornerCols + ccP], _wCornerHeights[idx]);
                    float hzM = Safe(_wCornerHeights[cr * cornerCols + ccM], _wCornerHeights[idx]);
                    Vector3 tx = new Vector3(2f, hxP - hxM, 0f);
                    Vector3 tz = new Vector3(0f, hzP - hzM, 2f);
                    Vector3 n = Vector3.Cross(tx, tz).normalized;
                    if (n.y < 0f) n = -n;
                    _wNormals[idx] = n;
                }
            }

            _waterMesh.Clear();
            if (ti == 0) return; // no water tiles in view
            _waterMesh.vertices = SubArray(_wVertices, vertCount);
            _waterMesh.normals = SubArray(_wNormals, vertCount);
            _waterMesh.colors32 = SubArray(_wColors, vertCount);
            _waterMesh.triangles = SubArray(_wTriangles, ti);
            _waterMesh.RecalculateBounds();
        }

        // NaN-safe gradient sample: a corner with no water uses its own height so
        // the gradient at the shore stays finite.
        static float Safe(float v, float fallback) => float.IsNaN(v) ? fallback : v;

        void EnsureWaterArrays(int vertCount, int triCount)
        {
            if (_wVertices == null || _wVertices.Length < vertCount)
            {
                _wVertices = new Vector3[vertCount];
                _wNormals = new Vector3[vertCount];
                _wCornerHeights = new float[vertCount];
                _wDepth = new float[vertCount];
                _wColors = new Color32[vertCount];
            }
            if (_wTriangles == null || _wTriangles.Length < triCount)
            {
                _wTriangles = new int[triCount];
            }
        }

        void EnsureArrays(int vertCount, int triCount)
        {
            if (_vertices == null || _vertices.Length < vertCount)
            {
                _vertices = new Vector3[vertCount];
                _normals = new Vector3[vertCount];
                _cornerHeights = new float[vertCount];
                _colors = new Color32[vertCount];
                _uvs = new Vector2[vertCount];
            }
            if (_triangles == null || _triangles.Length < triCount)
            {
                _triangles = new int[triCount];
            }
        }

        static T[] SubArray<T>(T[] source, int length)
        {
            if (source.Length == length) return source;
            T[] result = new T[length];
            Array.Copy(source, result, length);
            return result;
        }

        /// <summary>
        /// Release GPU resources.
        /// </summary>
        public void Dispose()
        {
            if (_mesh != null)
            {
                UnityEngine.Object.Destroy(_mesh);
                _mesh = null;
            }
            if (_waterMesh != null)
            {
                UnityEngine.Object.Destroy(_waterMesh);
                _waterMesh = null;
            }
            foreach (var kv in _liquids)
            {
                if (kv.Value.Mesh != null) UnityEngine.Object.Destroy(kv.Value.Mesh);
            }
            _liquids.Clear();
        }
    }
}
