using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
namespace CompoundSpheres
{
    /// <summary>
    /// sphere rows control the displaying of tiles
    /// </summary>
    public class SphereRow : IEnumerable
    {
        /// <summary>
        /// the manager of this row
        /// </summary>
        public readonly SphereManager SphereManager;
        /// <summary>
        /// get a sphere tile at this row and column i
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public SphereTile this[int i] => SphereManager[Row, i];
        /// <summary>
        /// the number of tiles in this row
        /// </summary>
        public int Cols => SphereManager.Cols;
        /// <summary>
        /// the X coordinate of this row
        /// </summary>
        public readonly int Row;
        /// <summary>
        /// the material properties for this specific row
        /// </summary>
        /// <remarks>dont add custom buffers directly to this, instead use Manager.addcustombuffer, since the manager will manage the buffer for you </remarks>
        public MaterialPropertyBlock Properties => _rp.matProps;
        internal SphereRow(SphereManager manager, int Row)
        {
            SphereManager = manager;
            this.Row = Row;
            _rp = new RenderParams(manager.Material)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 10000),
                matProps = new MaterialPropertyBlock()
            };
            Properties.SetInteger("Row", Row * Cols);
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            for (int i = 0; i < Cols; i++)
            {
                yield return this[i];
            }
        }
        /// <summary>
        /// draw the spheretiles
        /// </summary>
        public void DrawTiles()
        {
            Graphics.RenderMeshIndirect(_rp, SphereManager.SphereTileMesh, SphereManager.commandBuf, 1);
        }
        /// <summary>
        /// draw a contiguous sub-range of columns in this row.
        /// colStart is the first column index, colCount is how many to draw.
        /// The shader indexes as (Row + instance_id) so we shift Row by colStart
        /// and set instanceCount to colCount.
        /// </summary>
        public void DrawTiles(int colStart, int colCount)
        {
            if (colCount <= 0) return;
            if (colStart == 0 && colCount == Cols)
            {
                DrawTiles();
                return;
            }
            Properties.SetInteger("Row", Row * Cols + colStart);
            _rangeCommandData[0].indexCountPerInstance = SphereManager.SphereTileMesh.GetIndexCount(0);
            _rangeCommandData[0].instanceCount = (uint)colCount;
            _rangeCommandBuf.SetData(_rangeCommandData);
            Graphics.RenderMeshIndirect(_rp, SphereManager.SphereTileMesh, _rangeCommandBuf, 1);
            Properties.SetInteger("Row", Row * Cols);
        }
        internal void InitRangeBuffer()
        {
            _rangeCommandBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
            _rangeCommandData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        }
        internal void ReleaseRangeBuffer()
        {
            _rangeCommandBuf?.Release();
            _rangeCommandBuf = null;
        }
        private GraphicsBuffer _rangeCommandBuf;
        private GraphicsBuffer.IndirectDrawIndexedArgs[] _rangeCommandData;
        private RenderParams _rp;
    }
}