using System;
using UnityEngine;

namespace CompoundSpheres
{
    /// <summary>
    /// Frustum culling utilities for CompoundSpheres.
    /// Extracts camera frustum planes and tests axis-aligned bounding boxes
    /// against them to skip off-screen tile draws.
    /// </summary>
    public class FrustumCuller
    {
        /// <summary>
        /// Number of columns grouped into a single culling chunk.
        /// Chunk-level AABB tests avoid per-tile overhead on large maps.
        /// </summary>
        public const int ChunkSize = 16;

        private readonly Plane[] _frustumPlanes = new Plane[6];
        private bool _planesValid;

        /// <summary>
        /// Recalculate frustum planes from the current camera. Call once per frame
        /// before any DrawTiles pass.
        /// </summary>
        public void UpdatePlanes(Camera camera)
        {
            if (camera == null)
            {
                _planesValid = false;
                return;
            }
            GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);
            _planesValid = true;
        }

        /// <summary>
        /// Returns true if the AABB is at least partially inside the frustum.
        /// If planes have not been updated, returns true (draws everything).
        /// </summary>
        public bool TestAABB(Bounds bounds)
        {
            if (!_planesValid) return true;
            return GeometryUtility.TestPlanesAABB(_frustumPlanes, bounds);
        }

        /// <summary>
        /// Compute the world-space AABB that encloses a contiguous range of tiles
        /// in a single row. The caller supplies the first and last tile positions
        /// and the per-tile approximate half-size so we can expand the bounds to
        /// cover the full mesh extent.
        /// </summary>
        public static Bounds BoundsFromTileRange(Vector3 first, Vector3 last, Vector3 tileHalfSize)
        {
            Vector3 min = Vector3.Min(first, last) - tileHalfSize;
            Vector3 max = Vector3.Max(first, last) + tileHalfSize;
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;
            return new Bounds(center, size);
        }

        /// <summary>
        /// For a given row, determine which column chunks are visible and output
        /// the visible column start and count. Returns false if the entire row is
        /// culled.
        /// </summary>
        /// <param name="tiles">The full SphereTiles array (row-major, index = row*cols+col)</param>
        /// <param name="rowIndex">Which row we are testing</param>
        /// <param name="cols">Number of columns per row</param>
        /// <param name="tileHalfSize">Half-size of one tile mesh in world units</param>
        /// <param name="colStart">Output: first visible column</param>
        /// <param name="colCount">Output: number of visible columns (contiguous from colStart)</param>
        /// <returns>True if any chunk in this row is visible</returns>
        public bool GetVisibleColumnRange(SphereTile[] tiles, int rowIndex, int cols,
            Vector3 tileHalfSize, out int colStart, out int colCount)
        {
            colStart = 0;
            colCount = cols;

            if (!_planesValid)
                return true;

            int baseIdx = rowIndex * cols;
            int chunkCount = (cols + ChunkSize - 1) / ChunkSize;

            int firstVisibleChunk = -1;
            int lastVisibleChunk = -1;

            for (int c = 0; c < chunkCount; c++)
            {
                int cStart = c * ChunkSize;
                int cEnd = Math.Min(cStart + ChunkSize - 1, cols - 1);

                Vector3 first = tiles[baseIdx + cStart].Position;
                Vector3 last = tiles[baseIdx + cEnd].Position;
                Bounds chunkBounds = BoundsFromTileRange(first, last, tileHalfSize);

                if (GeometryUtility.TestPlanesAABB(_frustumPlanes, chunkBounds))
                {
                    if (firstVisibleChunk < 0) firstVisibleChunk = c;
                    lastVisibleChunk = c;
                }
            }

            if (firstVisibleChunk < 0)
            {
                colCount = 0;
                return false;
            }

            colStart = firstVisibleChunk * ChunkSize;
            int lastColEnd = Math.Min((lastVisibleChunk + 1) * ChunkSize, cols);
            colCount = lastColEnd - colStart;
            return true;
        }

        // -------------------------------------------------------------------
        // P3 (GPU adoption): the GPU manager keeps tiles as pure data — the
        // legacy per-tile Vector3 SphereTile no longer exists; TileBase exposes
        // only Vector2 grid Position. So we cull on (X=row, Y=col) and recompute
        // the cylindrical WORLD position CPU-side from Radius via
        // GpuDefaults.CartesianToCylindrical — no GPU readback. Identical chunked
        // visible-column-range result as the legacy overload above.
        // -------------------------------------------------------------------
        public bool GetVisibleColumnRange(
            CompoundSpheres.Gpu.GpuSphereManager manager, int rowIndex, int cols,
            Vector3 tileHalfSize, out int colStart, out int colCount)
        {
            colStart = 0;
            colCount = cols;

            if (!_planesValid || manager == null)
                return true;

            int chunkCount = (cols + ChunkSize - 1) / ChunkSize;
            int firstVisibleChunk = -1;
            int lastVisibleChunk = -1;

            for (int c = 0; c < chunkCount; c++)
            {
                int cStart = c * ChunkSize;
                int cEnd = Math.Min(cStart + ChunkSize - 1, cols - 1);

                // Recompute world pos from grid (X=rowIndex, Y=col) for the
                // manager's active shape (cylindrical / flat / cube).
                Vector3 first = CompoundSpheres.Gpu.GpuDefaults.TileWorldPosition(manager, rowIndex, cStart);
                Vector3 last = CompoundSpheres.Gpu.GpuDefaults.TileWorldPosition(manager, rowIndex, cEnd);
                Bounds chunkBounds = BoundsFromTileRange(first, last, tileHalfSize);

                if (GeometryUtility.TestPlanesAABB(_frustumPlanes, chunkBounds))
                {
                    if (firstVisibleChunk < 0) firstVisibleChunk = c;
                    lastVisibleChunk = c;
                }
            }

            if (firstVisibleChunk < 0)
            {
                colCount = 0;
                return false;
            }

            colStart = firstVisibleChunk * ChunkSize;
            int lastColEnd = Math.Min((lastVisibleChunk + 1) * ChunkSize, cols);
            colCount = lastColEnd - colStart;
            return true;
        }
    }
}
