using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace CompoundMeshes
{
    public class DynamicHandler : MeshHandler
    {
        ComputeBuffer<int> VisibileIndices;
        ComputeGraphicsBuffer<int> Indices;
        string IndicesKernel;
        public int Count { get; private set; }
        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        ComputeBuffer argsBuffer;
        public Bounds Bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        public DynamicHandler(string IndicesKernel, int InitialCount)
        {
            this.IndicesKernel = IndicesKernel;
            this.Count = InitialCount;
        }
        /// <summary>
        /// weather each index is enabled or not
        /// </summary>
        public bool this[int Index]
        {
            get
            {
                return VisibileIndices[Index] == 1;
            }
            set
            {
                VisibileIndices[Index] = value ? 1 : 0;
            }
        }
        public void Enlarge(int New)
        {
            Count += New;
            VisibileIndices.Enlarge(Count);
            Indices.SetSize(Count);
            Manager.SetSize(Count);
        }
        public void Dispose()
        {
            VisibileIndices.Dispose();
            Indices.Dispose();
            argsBuffer.Dispose();
        }
        public void RefreshIndices()
        {
            VisibileIndices.Refresh();
            Indices.Refresh();
            ComputeBuffer.CopyCount(Indices.Buffer, argsBuffer, sizeof(uint));
        }
        public void DrawMeshes()
        {
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, Bounds, argsBuffer);
        }
        public MeshManager Manager { get; private set; }
        public Mesh mesh => Manager.Mesh;
        public Material material => Manager.Material;
        public int Prepare(MeshManager Manager)
        {
            this.Manager = Manager;
           int kernel = Manager.ComputeShader.FindKernel(IndicesKernel);
           VisibileIndices = new ComputeBuffer<int>(Manager.ComputeShader, kernel, "VisibleIndices", Count);
           Indices = new ComputeGraphicsBuffer<int>(Manager.ComputeShader, Manager.Material, kernel, "OutputIndices", "Indices", Count, ComputeBufferType.Append);

            argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint),
            ComputeBufferType.IndirectArguments);
            args[0] = mesh.GetIndexCount(0);
            args[1] = 0; // overwritten by CopyCount
            args[2] = mesh.GetIndexStart(0);
            args[3] = (uint)mesh.GetBaseVertex(0);
            argsBuffer.SetData(args);
            return Count;
        }
    }
}
