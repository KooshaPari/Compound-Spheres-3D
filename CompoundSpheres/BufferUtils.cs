using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CompoundSpheres
{
    /// <summary>
    /// An interface meant so the manager can control custom buffers of different types
    /// </summary>
    public interface IBuffer : IDisposable
    {
        /// <summary>
        /// marks a tile to be refreshed
        /// </summary>
        public void Update(int I);
        /// <summary>
        /// refreshes the buffer, processing up to maxPerFrame dirty entries.
        /// Returns true when all dirty entries have been processed,
        /// false if more remain for the next call.
        /// </summary>
        public bool Refresh(int maxPerFrame = 8192);
    }
    /// <summary>
    /// a class for managing a buffer efficiently
    /// </summary>
    public class CustomBuffer<T> : IBuffer where T : struct
    {
        /// <summary>
        /// the manager managing this custom buffer
        /// </summary>
        public SphereManager Manager;
        /// <summary>
        /// the buffer this is managing
        /// </summary>
        public GraphicsBuffer Buffer;
        readonly HashSet<int> ToUpdate;
        readonly GetCustomData<T> getCustomData;
        /// <summary>
        /// refreshes all of the data
        /// </summary>
        public bool Refresh(int maxPerFrame = 8192)
        {
            return Buffer.UpdateBuffer(ToUpdate, (int i) => getCustomData(Manager.SphereTiles[i]), Manager.TotalTiles, maxPerFrame);
        }
        internal CustomBuffer(SphereManager Manager, GraphicsBuffer Buffer, GetCustomData<T> getdata)
        {
            getCustomData = getdata;
            this.Manager = Manager;
            ToUpdate = new HashSet<int>();
            this.Buffer = Buffer;
        }
        /// <inheritdoc/>
        public void Update(int I)
        {
            ToUpdate.Add(I);
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            Buffer.Dispose();
        }
    }
    /// <summary>
    /// a class for managing buffers
    /// </summary>
    public static class BufferUtils
    {
        /// <summary>
        /// sets a buffer, updating values in the list ToUpdate, NOT efficent for updating buffers, only call this to create buffers
        /// </summary>
        /// <remarks>calling this function many times at once may lead to lag</remarks>
        public static void SetBuffer<T>(this GraphicsBuffer Buffer, int Count, Func<int, T> function) where T : struct
        {
            T[] Array = new T[Count];
            for (int i = 0; i < Count; i++)
            {
                Array[i] = function(i);
            }
            Buffer.SetData(Array);
        }
        /// <summary>
        /// Coroutine that fills a buffer in chunks across multiple frames.
        /// Each frame processes up to <paramref name="chunkSize"/> elements,
        /// then yields to keep the main thread responsive.
        /// Call <paramref name="onComplete"/> when finished (or check the
        /// coroutine's completion).
        /// </summary>
        public static IEnumerator SetBufferChunked<T>(this GraphicsBuffer Buffer, int Count, Func<int, T> function, int chunkSize = 4096, Action onComplete = null) where T : struct
        {
            T[] Array = new T[Count];
            for (int i = 0; i < Count; i++)
            {
                Array[i] = function(i);
                if (i > 0 && (i % chunkSize) == 0)
                {
                    yield return null;
                }
            }
            Buffer.SetData(Array);
            onComplete?.Invoke();
        }
        /// <summary>
        /// Updates a buffer. When more than half the tiles are dirty, skips
        /// sort and does a single full-buffer SetData (faster than sorting
        /// 331K entries). Otherwise processes at most <paramref name="maxPerFrame"/>
        /// dirty entries per call, leaving the rest for the next frame.
        /// </summary>
        /// <returns>true if all dirty entries were processed; false if more remain.</returns>
        public static bool UpdateBuffer<T>(this GraphicsBuffer buffer, HashSet<int> ToUpdate, Func<int, T> Function, int totalTiles = 0, int maxPerFrame = 8192) where T : struct
        {
            if (ToUpdate == null || ToUpdate.Count == 0) return true;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int count = ToUpdate.Count;

            // Full rebuild path: when more than half the buffer is dirty,
            // a single SetData of the entire array is cheaper than sorting.
            if (totalTiles > 0 && count > totalTiles / 2)
            {
                T[] fullArray = new T[totalTiles];
                for (int i = 0; i < totalTiles; i++)
                {
                    fullArray[i] = Function(i);
                }
                buffer.SetData(fullArray);
                ToUpdate.Clear();
                long fullMs = sw.ElapsedMilliseconds;
                if (fullMs > 8)
                {
                    Debug.LogWarning($"[WSM3D][PERF] UpdateBuffer<{typeof(T).Name}> FULL REBUILD: {fullMs}ms count={count}/{totalTiles}");
                }
                return true;
            }

            // Chunked incremental path: process at most maxPerFrame entries.
            int toProcess = count;
            bool complete = true;
            if (maxPerFrame > 0 && count > maxPerFrame)
            {
                toProcess = maxPerFrame;
                complete = false;
            }

            var sorted = UnityEngine.Pool.ListPool<int>.Get();
            sorted.AddRange(ToUpdate);
            sorted.Sort();
            long sortMs = sw.ElapsedMilliseconds;

            // Trim to the chunk we'll actually process this frame.
            if (toProcess < sorted.Count)
            {
                sorted.RemoveRange(toProcess, sorted.Count - toProcess);
            }

            sw.Restart();
            T[] Array = new T[toProcess];
            for (int i = 0; i < toProcess; i++)
            {
                Array[i] = Function(sorted[i]);
            }
            long fillMs = sw.ElapsedMilliseconds;

            sw.Restart();
            // PERF (death-by-a-thousand-uploads fix): the dirty indices are usually
            // SCATTERED (sim recolors random tiles), so the per-contiguous-run
            // SetData loop below issued one GPU upload per run — measured at
            // setDataCalls=1055 for ~9.6k items. Thousands of tiny SetData calls
            // are far slower than one bulk upload of the spanned region. When the
            // run count would be high relative to the spanned range, coalesce into
            // a SINGLE SetData over [minIdx..maxIdx]. We fill the gap tiles (clean
            // tiles inside the span) with their CURRENT value via Function(idx) so
            // the bulk upload doesn't clobber them. This trades a few extra
            // Function() reads for collapsing 1000+ GPU calls into one.
            int minIdx = sorted[0];
            int maxIdx = sorted[sorted.Count - 1];
            int span = maxIdx - minIdx + 1;
            int setDataCalls = 0;
            long uploadMs;
            // Coalesce when the dirty set is fragmented (many runs) but spatially
            // dense (span not hugely larger than the dirty count). One bulk upload
            // of `span` contiguous elements beats `runs` scattered uploads once the
            // run count climbs past a small threshold.
            bool coalesce = sorted.Count >= 64 && span <= sorted.Count * 4;
            if (coalesce)
            {
                T[] spanArray = new T[span];
                for (int s = 0; s < span; s++)
                {
                    spanArray[s] = Function(minIdx + s);
                }
                buffer.SetData(spanArray, 0, minIdx, span);
                setDataCalls = 1;
                uploadMs = sw.ElapsedMilliseconds;
            }
            else
            {
                int BufferSize = 1;
                int ArrayStart = 0;
                int startIndex = sorted[0];
                int lastIndex = startIndex;
                for (int i = 1; i < sorted.Count; i++)
                {
                    int index = sorted[i];
                    if (index-lastIndex == 1)
                    {
                        BufferSize++;
                    }
                    else
                    {
                        buffer.SetData(Array, ArrayStart, startIndex, BufferSize);
                        setDataCalls++;
                        startIndex = index;
                        ArrayStart = i;
                        BufferSize = 1;
                    }
                    lastIndex = index;
                }
                if (BufferSize > 0)
                {
                    buffer.SetData(Array, ArrayStart, startIndex, BufferSize);
                    setDataCalls++;
                }
                uploadMs = sw.ElapsedMilliseconds;
            }

            // Remove only the entries we processed from the dirty set.
            if (complete)
            {
                ToUpdate.Clear();
            }
            else
            {
                for (int i = 0; i < sorted.Count; i++)
                {
                    ToUpdate.Remove(sorted[i]);
                }
            }
            UnityEngine.Pool.ListPool<int>.Release(sorted);

            long total = sortMs + fillMs + uploadMs;
            if (total > 8)
            {
                Debug.LogWarning($"[WSM3D][PERF] UpdateBuffer<{typeof(T).Name}> " +
                    (complete ? "DONE" : "PARTIAL") + $": {total}ms " +
                    $"(sort={sortMs}ms fill={fillMs}ms upload={uploadMs}ms " +
                    $"count={toProcess}/{count} setDataCalls={setDataCalls})");
            }
            return complete;
        }
    }
}
