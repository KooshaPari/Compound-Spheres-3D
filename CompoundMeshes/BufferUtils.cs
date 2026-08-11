using JetBrains.Annotations;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
namespace CompoundMeshes
{
    /// <summary>
    /// An interface meant so the manager can control custom buffers of different types
    /// </summary>
    public interface IBuffer : IDisposable
    {
        /// <summary>
        /// updates a item
        /// </summary>
        public void Update(int I);
        /// <summary>
        /// refreshes the buffer
        /// </summary>
        public void Refresh();
        /// <summary>
        /// sets the size
        /// </summary>
        public void SetSize(int Size);
    }
    public abstract class BufferBase<T> : IDisposable where T : struct
    {
        protected NativeArray<T> Data;
        protected bool[] Dirty;
        public bool IsDirty { get; protected set; } = false;
        public int Size => Data.Length;
        public virtual void Dispose()
        {
            Data.Dispose();
        }
        /// <summary>
        /// if the buffer is a RWStructuredBuffer, use this to update the data with values from GPU. 
        /// </summary>
        public abstract void RefreshFromGPU();
        public void Refresh()
        {
            if (!IsDirty) return;

            int bufferSize = 0;
            int arrayStart = -1;
            int lastIndex = -1;

            for (int i = 0; i < Data.Length; i++)
            {
                if (!Dirty[i]) continue;

                if (arrayStart == -1)
                {
                    arrayStart = i;
                    bufferSize = 1;
                    lastIndex = i;
                }
                else if (i - lastIndex == 1)
                {
                    bufferSize++;
                    lastIndex = i;
                }
                else
                {
                    SetData(arrayStart, bufferSize);
                    arrayStart = i;
                    bufferSize = 1;
                    lastIndex = i;
                }

                Dirty[i] = false;
            }

            if (arrayStart != -1)
            {
                for (int i = arrayStart; i < arrayStart + bufferSize; i++)
                    Dirty[i] = false;
                SetData(arrayStart, bufferSize);
            }

            IsDirty = false;
        }
        protected void MarkDirty(int index)
        {
            if (index >= Size)
            {
                Enlarge(index * 2);
            }
            IsDirty = true;
            Dirty[index] = true;
        }
        public abstract void Enlarge(int NewSize);
        protected abstract void SetData(int Start, int Count);
    }
    public abstract class GraphicsBufferBase<T> : BufferBase<T> where T : struct
    {
        public GraphicsBuffer buffer { get; private set; }
        public readonly Material Material;
        public readonly string Name;
        protected GraphicsBufferBase(GraphicsBuffer Buffer, Material material, string name, int Length)
        {
            buffer = Buffer;
            Material = material;
            Name = name;
            Material?.SetBuffer(Name, buffer);
            Dirty = new bool[Length];
            Data = new NativeArray<T>(Length, Allocator.Persistent);
        }
        public override void RefreshFromGPU()
        { 
           var temp = new T[Data.Length];
           buffer.GetData(temp);
           Data.CopyFrom(temp);
        }
        public override void Dispose()
        {
            buffer.Dispose();
            base.Dispose();
        }
        protected override void SetData(int Start, int Count)
        {
            buffer.SetData(Data, Start, Start, Count);
        }
        public override void Enlarge(int NewSize)
        {
            GraphicsBuffer newBuffer = new GraphicsBuffer(buffer.target, NewSize, Marshal.SizeOf<T>());
            NativeArray<T> temp = new NativeArray<T>(NewSize, Allocator.Persistent);

            bool[] dirty = new bool[NewSize];
            Dirty.CopyTo(dirty, 0);
            Dirty = dirty;
            
            NativeArray<T>.Copy(Data, temp, Data.Length);
            buffer.Dispose();
            buffer = newBuffer;
            buffer.SetData(temp);
            Data.Dispose();
            Data = temp;
            Material.SetBuffer(Name, buffer);
        }
    }
    public class MultiBuffer<T> : GraphicsBufferBase<T> where T : struct
    {
        public MultiBuffer(GraphicsBuffer.Target target, int Length, int ItemSize, Material material, string name) : base(new GraphicsBuffer(target, Length*ItemSize, Marshal.SizeOf<T>()), material, name, Length*ItemSize) { this.ItemSize = ItemSize; }
        public MultiBuffer(GraphicsBuffer Buffer, Material material, string name, int length, int ItemSize) : base(Buffer, material, name, length*ItemSize) { this.ItemSize = ItemSize; }
        public readonly int ItemSize = 1;
        void Check(int index)
        {
            if ((index+1) * ItemSize > Size)
            {
                Enlarge((index+1) * 2);
            }
        }

        public override void Enlarge(int NewSize)
        {
            base.Enlarge(NewSize*ItemSize);
        }
        /// <summary>
        /// writes to the Data Array using a Function. marks it dirty
        /// </summary>
        public void Write(int Index, WriteFunction<T> Function)
        {
            Check(Index);
            Dirty[Index] = true;
            IsDirty = true;
            int Base = Index * ItemSize;
            for (int i = 0; i < ItemSize; i++)
            {
                Data[Base + i] = Function(Index, i);
            }
        }
        /// <summary>
        /// Reads from the Data Array using a Function. doesnt mark it dirty
        /// </summary>
        public void Read(int Index, ReadFunction<T> Function)
        {
            Check(Index);
            int Base = Index * ItemSize;
            for (int i = 0; i < ItemSize; i++)
            {
                Function(Index, i, Data[Base + i]);
            }
        }
        protected override void SetData(int Start, int Count)
        {
            base.SetData(Start*ItemSize, Count*ItemSize);
        }
    }
    public class ComputeBuffer<T> : BufferBase<T> where T : struct
    {
        public ComputeBuffer Buffer { get; private set; }
        public readonly ComputeShader Shader;
        public readonly string Name;
        public readonly int Kernel;
        public ComputeBuffer(ComputeShader material, int Kernel, string name, int Length)
        {
            Buffer = new ComputeBuffer(Length, Marshal.SizeOf<T>());
            Shader = material;
            Name = name;
            this.Kernel = Kernel;
            Shader.SetBuffer(Kernel, Name, Buffer);
            Dirty = new bool[Length];
            Data = new NativeArray<T>(Length, Allocator.Persistent);
        }
        public override void RefreshFromGPU()
        { 
            var temp = new T[Data.Length];
            Buffer.GetData(temp);
            Data.CopyFrom(temp);
        }
        public override void Dispose()
        {
            Buffer.Dispose();
            base.Dispose();
        }
        protected override void SetData(int Start, int Count)
        {
            Buffer.SetData(Data, Start, Start, Count);
        }
        public override void Enlarge(int NewSize)
        {
            ComputeBuffer newBuffer = new ComputeBuffer(NewSize, Marshal.SizeOf<T>());
            NativeArray<T> temp = new NativeArray<T>(NewSize, Allocator.Persistent);

            bool[] dirty = new bool[NewSize];
            Dirty.CopyTo(dirty, 0);
            Dirty = dirty;

            NativeArray<T>.Copy(Data, temp, Data.Length);
            Buffer.Dispose();
            Buffer = newBuffer;
            Buffer.SetData(temp);
            Data.Dispose();
            Data = temp;
            Shader.SetBuffer(Kernel, Name, Buffer);
        }
        public T this[int Index]
        {
            get
            {
                return Data[Index];
            }
            set
            {
                MarkDirty(Index);
                Data[Index] = value;
            }
        }
        /// <summary>
        /// sets a buffer, updating values in the Data Array, NOT efficent for updating buffers, only call this to create buffers
        /// </summary>
        public void Set(Func<int, T> function)
        {
            for (int i = 0; i < Size; i++)
                Data[i] = function(i);
            Buffer.SetData(Data);
        }
    }
    public class Buffer<T> : GraphicsBufferBase<T>, IDisposable where T : struct
    {
        public T this[int Index]
        {
            get
            {
                return Data[Index];
            }
            set
            {
                MarkDirty(Index);
                Data[Index] = value;
            }
        }
        public Buffer(GraphicsBuffer.Target target, int Length, Material material, string name) : base(new GraphicsBuffer(target, Length, Marshal.SizeOf<T>()), material, name, Length) { }
        /// <summary>
        /// sets a buffer, updating values in the Data Array, NOT efficent for updating buffers, only call this to create buffers
        /// </summary>
        public void Set(Func<int, T> function)
        {
            for (int i = 0; i < Size; i++)
                Data[i] = function(i);
            buffer.SetData(Data);
        }
    }
    /// <summary>
    /// a class for managing a buffer efficiently and dynamically
    /// </summary>
    public class WrappedMultiBuffer<T> : IBuffer where T : struct
    {
        /// <summary>
        /// the buffer this is managing
        /// </summary>
        public MultiBuffer<T> Buffer;
        readonly WriteFunction<T> getCustomData;
        /// <summary>
        /// refreshes all of the data
        /// </summary>
        public void Refresh()
        {
           Buffer.Refresh();
        }
        internal WrappedMultiBuffer(GraphicsBuffer Buffer, WriteFunction<T> getdata, int ItemLength, int InitialLength, string name, Material material)
        {
            getCustomData = getdata;
            this.Buffer = new MultiBuffer<T>(Buffer, material, name, InitialLength, ItemLength);
        }
        /// <inheritdoc/>
        public void Update(int I)
        {
            Buffer.Write(I, getCustomData);
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            Buffer.Dispose();
        }

        public void SetSize(int Size)
        {
            if(Size > Buffer.Size*Buffer.ItemSize)
                Buffer.Enlarge(Size);
        }
    }
    /// <summary>
    /// a class for managing a buffer efficiently
    /// </summary>
    public class WrappedBuffer<T> : IBuffer where T : struct
    {
        /// <summary>
        /// the buffer this is managing
        /// </summary>
        public Buffer<T> Buffer;
        readonly GetCustomData<T> getCustomData;
        /// <summary>
        /// refreshes all of the data
        /// </summary>
        public void Refresh()
        {
            if (getCustomData == null)
                return;
            Buffer.Refresh();
        }
        internal WrappedBuffer(Buffer<T> Buffer, GetCustomData<T> getdata)
        {
            getCustomData = getdata;
            this.Buffer = Buffer;
        }
        internal WrappedBuffer(Buffer<T> Buffer, T[] data)
        {
            this.Buffer = Buffer;
            Buffer.Set(i => data[i]);
        }
        /// <inheritdoc/>
        public void Update(int I)
        {
            if (getCustomData == null)
                return;
            Buffer[I] = getCustomData(I);
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            Buffer.Dispose();
        }
        public void SetSize(int Size)
        {
            if (getCustomData != null)
                Buffer.Enlarge(Size);
        }
    }
    public class WrappedComputeBuffer<T> : IBuffer where T : struct
    {
        public ComputeBuffer<T> Buffer;
        readonly GetCustomData<T> getCustomData;
        /// <summary>
        /// refreshes all of the data
        /// </summary>
        public void Refresh()
        {
            if (getCustomData == null)
                return;
            Buffer.Refresh();
        }
        internal WrappedComputeBuffer(ComputeBuffer<T> Buffer, GetCustomData<T> getdata)
        {
            getCustomData = getdata;
            this.Buffer = Buffer;
        }
        internal WrappedComputeBuffer(ComputeBuffer<T> Buffer, T[] data)
        {
            this.Buffer = Buffer;
            Buffer.Set(i => data[i]);
        }
        /// <inheritdoc/>
        public void Update(int I)
        {
            if (getCustomData == null)
                return;
            Buffer[I] = getCustomData(I);
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            Buffer.Dispose();
        }
        public void SetSize(int Size)
        {
            if (getCustomData != null)
                Buffer.Enlarge(Size);
        }
    }
    /// <summary>
    /// a buffer between the compute shader and the graphics shader
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ComputeGraphicsBuffer<T> : IBuffer where T : struct
    {
        public ComputeBuffer Buffer { get; private set; }
        public ComputeBuffer<int> Dirty { get; }
        public Material Material { get; }
        public int Kernel { get; }
        public ComputeShader Shader { get; }
        public string ComputeName { get; }
        public string MaterialName { get; }
        private int ThreadCount;
        private readonly uint Threads;
        public ComputeBufferType Type { get; }
        public bool DirtyEnabled => Dirty != null;
        public ComputeGraphicsBuffer(ComputeShader Shader, Material material, int Kernel, string ComputeName, string MaterialName, int Length, ComputeBufferType type = ComputeBufferType.Structured)
        {
            this.Shader = Shader;
            this.Material = material;
            this.ComputeName = ComputeName;
            this.MaterialName = MaterialName;
            this.Kernel = Kernel;
            Type = type;
            Buffer = new ComputeBuffer(Length, Marshal.SizeOf<T>(), type);
            Shader.SetBuffer(Kernel, ComputeName, Buffer);
            Material.SetBuffer(MaterialName, Buffer);
            Shader.GetKernelThreadGroupSizes(Kernel, out uint x, out _, out _);
            this.Threads = x;
            ThreadCount = Mathf.CeilToInt((float)Length / Threads);
            if (type != ComputeBufferType.Append)
            {
                Dirty = new ComputeBuffer<int>(Shader, Kernel, "Dirty", Length);
                Dirty.Set((int i) => 1);
            }
        }
        public void Dispose()
        {
            Buffer.Dispose();
            Dirty?.Dispose();
        }

        public void Refresh()
        {
            Dirty?.Refresh();
            if(Type == ComputeBufferType.Append)
            {
                Buffer.SetCounterValue(0);
            }
            Shader.Dispatch(Kernel, ThreadCount, 1, 1);
        }

        public void Update(int I)
        {
            if (DirtyEnabled)
            {
                Dirty[I] = 1;
            }
        }

        public void SetSize(int Size)
        {
            Dirty?.Enlarge(Size);

            T[] Temp = new T[Buffer.count]; 
            if(Type != ComputeBufferType.Append)
                Buffer.GetData(Temp);
            
            Buffer.Dispose();             

            Buffer = new ComputeBuffer(Size, Marshal.SizeOf<T>(), Type);
            Shader.SetBuffer(Kernel, ComputeName, Buffer);
            Material.SetBuffer(MaterialName, Buffer);

            if (Type != ComputeBufferType.Append)
                Buffer.SetData(Temp);

            ThreadCount = Mathf.CeilToInt((float)Size / Threads);
        }
    }
}