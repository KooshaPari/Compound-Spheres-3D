using System;
using System.Collections.Generic;
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
    public class HeightFieldRenderer
    {
        readonly SphereManager _manager;
        Mesh _mesh;
        Material _material;
        bool _dirty = true;
        int _lastMinRow = int.MinValue;
        int _lastMaxRow = int.MinValue;
        int _lastCameraX = int.MinValue;

        // Callbacks to retrieve per-tile height and color without coupling
        // CompoundSpheres to WorldSphereMod types. Set by the consumer.
        Func<int, int, float> _sampleHeight;
        Func<int, int, Color32> _sampleColor;
        Func<int, int, int> _sampleTexture;
        Func<float, float, float, Vector3> _projectPosition;

        // Cached arrays to avoid GC churn on rebuild.
        Vector3[] _vertices;
        Color32[] _colors;
        Vector2[] _uvs;
        int[] _triangles;

        public Mesh Mesh => _mesh;
        public bool Dirty => _dirty;

        public HeightFieldRenderer(SphereManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _mesh = new Mesh
            {
                name = "HeightFieldTerrain",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
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

        /// <summary>
        /// Mark the mesh as needing a full rebuild (e.g. after tile type change).
        /// </summary>
        public void MarkDirty()
        {
            _dirty = true;
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
            if (_dirty || _lastMinRow == int.MinValue)
            {
                Rebuild(0, fullMin, fullMax, wrapped);
                _lastMinRow = fullMin;
                _lastMaxRow = fullMax;
                _lastCameraX = 0;
                _dirty = false;
            }

            if (_material == null)
            {
                _material = _manager.Material;
            }

            if (_mesh.vertexCount > 0 && _material != null)
            {
                Graphics.DrawMesh(_mesh, Matrix4x4.identity, _material, 0);
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

            // Build vertices: for each corner, average the heights & colors of
            // the up-to-4 tiles that share it.
            for (int cr = 0; cr < cornerRows; cr++)
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
                    _colors[vi] = new Color32(
                        (byte)(rSum / count),
                        (byte)(gSum / count),
                        (byte)(bSum / count),
                        255);
                    _uvs[vi] = new Vector2(
                        (float)cc / cols,
                        (float)cr / rowCount);
                }
            }

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

            _mesh.Clear();
            _mesh.vertices = SubArray(_vertices, vertCount);
            _mesh.colors32 = SubArray(_colors, vertCount);
            _mesh.uv = SubArray(_uvs, vertCount);
            _mesh.triangles = SubArray(_triangles, triCount);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        void EnsureArrays(int vertCount, int triCount)
        {
            if (_vertices == null || _vertices.Length < vertCount)
            {
                _vertices = new Vector3[vertCount];
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
        }
    }
}
