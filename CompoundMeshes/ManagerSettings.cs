using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Unity.Collections;
using UnityEngine;

namespace CompoundMeshes
{
    public class ComputeGraphicsBufferData<T> : IBufferData where T : struct
    {
        public string Name { get; set; }
        public string Kernel;
        public string ComputeName;
        public IBuffer GetBuffer(MeshManager ManagerData)
        {
           var buffer = new ComputeGraphicsBuffer<T>(ManagerData.ComputeShader, ManagerData.Material, ManagerData.ComputeShader.FindKernel(Kernel), ComputeName, Name, ManagerData.MeshCount);
           return buffer;
        }
        public ComputeGraphicsBufferData(string Name, string ComputeName, string Kernel)
        {
            this.Name = Name;
            this.Kernel = Kernel;
            this.ComputeName = ComputeName;
        }
    }
    public class ComputeBufferData<T> : IBufferData where T : struct
    {
        public string Name { get; private set; }
        public string Kernel { get; private set; }
        public GetCustomData<T> getCustomData;
        public IBuffer GetBuffer(MeshManager ManagerData)
        {
            return new WrappedComputeBuffer<T>(new ComputeBuffer<T>(ManagerData.ComputeShader, ManagerData.ComputeShader.FindKernel(Kernel), Name, ManagerData.MeshCount), getCustomData);
        }
        public ComputeBufferData(string Name, string Kernel, GetCustomData<T> getCustomData)
        {
           this.Name = Name;
           this.Kernel = Kernel;
           this.getCustomData = getCustomData;
        }
    }
    /// <summary>
    /// a buffer, whose items can have multiple values inside
    /// </summary>
    public class MultiBufferData<T> : IBufferData where T : struct
    {
        /// <summary>
        /// a function that returns your custom data for each sphere tile
        /// </summary>
        public readonly WriteFunction<T> getCustomData;
        /// <summary>
        /// the name of this buffer, in your custom shader
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// the size of each item in the buffer. 
        /// </summary>
        public readonly int ItemLength;
        /// <summary>
        /// if set, the buffer can have a different size then the manager
        /// </summary>
        public readonly int OverrideLength;

        /// <summary>
        /// a custom buffer configuration, which tells the sphere manager to add a new compute buffer
        /// </summary>
        /// <param name="Name">the name of this buffer, in your custom shader</param>
        /// <param name="ItemLength">the size of each item in the buffer. </param>
        /// <param name="getCustomData">a function that returns your custom data for each sphere tile</param>
        /// <param name="OverrideLength">if set, the buffer can have a different size then the manager initially</param>
        public MultiBufferData(string Name, int ItemLength, WriteFunction<T> getCustomData, int OverrideLength = -1)
        {
            this.Name = Name;
            this.ItemLength = ItemLength;
            this.getCustomData = getCustomData;
            this.OverrideLength = OverrideLength;
        }
        /// <inheritdoc/>
        public IBuffer GetBuffer(MeshManager Manager)
        {
            int Count = OverrideLength != -1 ? OverrideLength : Manager.MeshCount;
            GraphicsBuffer Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Count * ItemLength, Marshal.SizeOf<T>());
            Manager.Material.SetBuffer(Name, Buffer);
            return new WrappedMultiBuffer<T>(Buffer, getCustomData, ItemLength, Count, Name, Manager.Material);
        }
    }
    /// <summary>
    /// a interface so the manager can import custom buffers of different types
    /// </summary>
    public interface IBufferData {
        /// <summary>
        /// adds a custom buffer to a manager and returns it
        /// </summary>
        IBuffer GetBuffer(MeshManager ManagerData);
        /// <summary>
        /// the name of the custom buffer
        /// </summary>
        string Name { get; }
    }
    /// <summary>
    /// a static buffer. its values never change, it can never resize
    /// </summary>
    public class StaticBufferData<T> : IBufferData where T : struct
    {
        /// <summary>
        /// the name of this buffer, in your custom shader
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// the static Data. modifying it after the buffer has been imported does nothing.
        /// </summary>
        public T[] Data;
        /// <summary>
        /// the compute shader kernel, if not defined this is a graphics buffer
        /// </summary>
        public string kernel;
        /// <summary>
        /// a custom buffer configuration, which tells the sphere manager to add a new buffer
        /// </summary>
        /// <param name="Name">the name of this buffer, in your custom shader</param>
        /// <param name="Data">the static data</param>
        public StaticBufferData(string Name, T[] Data)
        {
            this.Name = Name;
            this.Data = Data;
        }
        public StaticBufferData(string Name, uint Length)
        {
            this.Name = Name;
            this.Data = new T[Length];
        }
        public StaticBufferData(string Kernel, string Name, T[] Data)
        {
            this.Name = Name;
            kernel = Kernel;
            this.Data = Data;
        }
        public StaticBufferData(string Kernel, string Name, uint Length)
        {
            this.Name = Name;
            kernel = Kernel;
            this.Data = new T[Length];
        }
        /// <inheritdoc/>
        public IBuffer GetBuffer(MeshManager manager)
        {
            return kernel == null ?
                new WrappedBuffer<T>(new Buffer<T>(GraphicsBuffer.Target.Structured, Data.Length, manager.Material, Name), Data)
                :
                new WrappedComputeBuffer<T>(new ComputeBuffer<T>(manager.ComputeShader, manager.ComputeShader.FindKernel(kernel), Name, Data.Length), Data);
        }
    }
    /// <summary>
    /// a custom buffer configuration, which tells the sphere manager to add a new graphics buffer
    /// </summary>
    public class GraphicsBufferData<T> : IBufferData where T : struct
    {
        /// <summary>
        /// a function that returns your custom data for each mesh
        /// </summary>
        public readonly GetCustomData<T> getCustomData;
        /// <summary>
        /// the name of this buffer, in your custom shader
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// a custom buffer configuration, which tells the sphere manager to add a new compute buffer
        /// </summary>
        /// <param name="Name">the name of this buffer, in your custom shader</param>
        /// <param name="getCustomData">a function that returns your custom data for each sphere tile</param>
        public GraphicsBufferData(string Name, GetCustomData<T> getCustomData)
        {
            this.Name = Name;
            this.getCustomData = getCustomData;
        }
        /// <inheritdoc/>
        public IBuffer GetBuffer(MeshManager manager)
        {
            return new WrappedBuffer<T>(new Buffer<T>(GraphicsBuffer.Target.Structured, manager.MeshCount, manager.Material, Name), getCustomData);
        }
    }
    public struct Range
    {
        public int Min;
        public int Max;
        public Range(int Min, int Max)
        {
            this.Min = Min;
            this.Max = Max;
        }
    }
    /// <summary>
    /// a function that reads a item at Index and SubIndex
    /// </summary>
    /// <remarks>Index, the index of the item in the buffer</remarks>
    public delegate void ReadFunction<in T>(int Index, int SubIndex, T item) where T : struct;
    /// <summary>
    /// a function that returns a item at Index and SubIndex
    /// </summary>
    /// <remarks>Index, the index of the item in the buffer</remarks>
    public delegate T WriteFunction<out T>(int Index, int SubIndex) where T : struct;
    /// <summary>
    /// a function that returns a custom data
    /// </summary>
    /// <remarks>Index, the index of the tile in the manager</remarks>
    public delegate T GetCustomData<out T>(int Index) where T : struct;
    /// <summary>
    /// the Range of Rows and Cols around the camera that draw their tiles
    /// </summary>
    public delegate void GetCameraRange(StaticHandler SphereManager, out Range Rows, out Range Cols);
    public class ManagerSettings
    {
        public ManagerSettings(Mesh Mesh, Material Material, ComputeShader Shader, List<IBufferData> Buffers, MeshHandler Renderer)
        {
            this.Mesh = Mesh;
            this.Material = Material;
            this.ComputeShader = Shader;
            this.Buffers = Buffers;
            this.Handler = Renderer;
        }
        public MeshHandler Handler;
        /// <summary>
        /// the mesh used to display tiles
        /// </summary>
        public Mesh Mesh;
        /// <summary>
        /// the Material of the meshes, must have the compound spheres shader to work!
        /// </summary>
        public Material Material;
        /// <summary>
        /// a list of buffers this manager has
        /// </summary>
        public List<IBufferData> Buffers;

        public ComputeShader ComputeShader;
    }
}