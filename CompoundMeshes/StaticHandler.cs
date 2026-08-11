using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CompoundMeshes
{
    /// <summary>
    /// A Static Manager. this one's rows and cols do not move. it uses a texture array as its textures, rather then an atlas
    /// </summary>
    public class StaticHandler : MeshHandler
    {
        StaticTile[] Tiles;
        public StaticTile GetTile(int I)
        {
            return Tiles[I];
        }
        public MeshManager Manager { get; private set; }
        public Material Material => Manager.Material;
        public Mesh Mesh => Manager.Mesh;
        /// <summary>
        /// a spheretile at x and y coordinates
        /// </summary>
        public StaticTile this[int x, int y] => Tiles[(x*Cols)+y];
        /// <summary>
        /// a Row at an X position
        /// </summary>
        public StaticRow this[int x] => StaticRows[x];
        
        internal StaticRow[] StaticRows;
        /// <summary>
        /// The X Axis
        /// </summary>
        public int Rows { private set; get; }
        /// <summary>
        /// The Y Axis
        /// </summary>
        public int Cols { private set; get; }

        internal GraphicsBuffer commandBuf;
        GetCameraRange GetCameraRange;

        readonly GraphicsBuffer.IndirectDrawIndexedArgs[] commandData = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        void SetRenderAmount(uint Amount)
        {
            commandData[0].instanceCount = Amount;
            commandBuf.SetData(commandData);
        }
        private StaticHandler() { }
        /// <summary>
        /// clamps a position + change to the X Axis
        /// </summary>
        public float Clamp(float Pos, float Change)
        {
            Pos += Change;
            if (Pos < 0)
            {
                return Rows + Pos;
            }
            return Pos % Rows;
        }
        /// <summary>
        /// the sphere manager acts as a list of sphere rows
        /// </summary>
        public IEnumerator GetEnumerator()
        {
            return StaticRows.GetEnumerator();
        }
        public int Prepare(MeshManager Manager)
        {
            this.Manager = Manager;
            commandBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            commandData[0].indexCountPerInstance = Mesh.GetIndexCount(0);
            commandData[0].instanceCount = (uint)Cols;
            commandBuf.SetData(commandData);

            for (int i = 0; i < Rows; i++)
            {
                StaticRow row = StaticRows[i] = new StaticRow(this, i);
                for (int j = 0; j < Cols; j++)
                {
                    StaticTile Tile = Tiles[(i * Cols) + j] = new StaticTile(i, j, row);
                }
            }

            return Cols * Rows;
        }
        public StaticHandler(int Rows, int Cols, GetCameraRange GetRange)
        {
            this.Rows = Rows;
            StaticRows = new StaticRow[Rows];
            Tiles = new StaticTile[Rows * Cols];
            this.Cols = Cols;
            this.GetCameraRange = GetRange;
        }
        Range Range;
        int CameraX;
        public void RefreshRanges(int CameraX, int CameraY)
        {
            this.CameraX = CameraX;
            GetCameraRange(this, out Range, out Range Col);
            uint MaxCol = (uint)Mathf.Clamp(Col.Max + CameraY, 0, Cols);
            int MinCol = Mathf.Clamp(CameraY + Col.Min, 0, Cols);
            SetRenderAmount(MaxCol - (uint)MinCol);
            Material.SetInteger("Col", MinCol);
        }
        public void DrawMeshes()
        {
            for (int i = Range.Min; i < Range.Max; i++)
            {
                int I = (int)Clamp(CameraX, i);
                StaticRows[I].DrawTiles();
            }
        }

        public void Dispose()
        {
            commandBuf.Dispose();
        }
    }
    public class StaticTile
    {
        /// <summary>
        /// the Row this tile is Im
        /// </summary>
        public StaticRow Row { get; private set; }
        /// <summary>
        /// the Manager of this tile
        /// </summary>
        public StaticHandler Manager => Row.Manager;
        public int Index { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        internal StaticTile(int X, int Y, StaticRow row)
        {
            Row = row;
            this.Index = (X * row.Cols) + Y;
            this.X = X;
            this.Y = Y;
        }
    }
    public class StaticRow : IEnumerable
    {
        /// <summary>
        /// the manager of this row
        /// </summary>
        public readonly StaticHandler Manager;
        /// <summary>
        /// get a sphere tile at this row and column i
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public StaticTile this[int i] => Manager[Row, i];
        /// <summary>
        /// the number of tiles in this row
        /// </summary>
        public int Cols => Manager.Cols;
        /// <summary>
        /// the X coordinate of this row
        /// </summary>
        public readonly int Row;
        /// <summary>
        /// the material properties for this specific row
        /// </summary>
        /// <remarks>dont add custom buffers directly to this, instead use Manager.addcustombuffer, since the manager will manage the buffer for you </remarks>
        public MaterialPropertyBlock Properties => _rp.matProps;

        internal StaticRow(StaticHandler manager, int Row)
        {
            Manager = manager;
            this.Row = Row;
            _rp = new RenderParams(manager.Manager.Material)
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
            Graphics.RenderMeshIndirect(_rp, Manager.Manager.Mesh, Manager.commandBuf, 1);
        }
        private RenderParams _rp;
    }
}