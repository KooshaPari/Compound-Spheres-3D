using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CompoundSpheres.Gpu
{
    // -----------------------------------------------------------------------
    // P2: upstream MelvinShwuaner/Compound-Spheres BufferUtils.cs imported
    // ADDITIVELY into the CompoundSpheres.Gpu namespace so it coexists with the
    // legacy CompoundSpheres.BufferUtils types (which define same-named
    // ComputeBuffer<T>/Buffer<T>/IBuffer with incompatible signatures). The
    // legacy CPU path keeps compiling unchanged; the GPU-compute manager
    // (GpuManagerBase) and the LegacyManagerShim drive THESE types.
    // Source: git show upstream/main:CompoundSpheres/BufferUtils.cs
    // -----------------------------------------------------------------------

    /// <summary>An interface meant so the manager can control custom buffers of different types.</summary>
    public interface IGpuBuffer : IDisposable
    {
        void Update(int I);
        void Refresh();
    }

    public abstract class BufferBase<T> : IDisposable where T : struct
    {
        protected T[] Data;
        protected bool[] Dirty;
        public bool IsDirty { get; protected set; } = false;
        public int Size => Data.Length;
        public virtual void Dispose() { }
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
                SetData(arrayStart, bufferSize);

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
        public readonly MaterialPropertyBlock Property;
        public readonly string Name;
        public GraphicsBufferBase(GraphicsBuffer Buffer, Material material, string name, int Length)
        {
            buffer = Buffer;
            Material = material;
            Name = name;
            Material?.SetBuffer(Name, buffer);
            Dirty = new bool[Length];
            Data = new T[Length];
        }
        public GraphicsBufferBase(GraphicsBuffer Buffer, MaterialPropertyBlock material, string name, int Length)
        {
            buffer = Buffer;
            Property = material;
            Name = name;
            Property.SetBuffer(Name, buffer);
            Dirty = new bool[Length];
            Data = new T[Length];
        }
        public override void Dispose()
        {
            buffer?.Dispose();
            base.Dispose();
        }
        protected override void SetData(int Start, int Count)
        {
            buffer.SetData(Data, Start, Start, Count);
        }
        public override void Enlarge(int NewSize)
        {
            GraphicsBuffer newBuffer = new GraphicsBuffer(buffer.target, NewSize, Marshal.SizeOf<T>());
            T[] temp = new T[NewSize];

            bool[] dirty = new bool[NewSize];
            Dirty.CopyTo(dirty, 0);
            Dirty = dirty;

            Array.Copy(Data, temp, Data.Length);
            buffer.Dispose();
            buffer = newBuffer;
            buffer.SetData(temp);
            Data = temp;
            if (Material != null)
            {
                Material.SetBuffer(Name, buffer);
            }
            else
            {
                Property.SetBuffer(Name, buffer);
            }
        }
    }

    public class MultiBuffer<T> : GraphicsBufferBase<T> where T : struct
    {
        public MultiBuffer(GraphicsBuffer.Target target, int Length, int ItemSize, Material material, string name) : base(new GraphicsBuffer(target, Length * ItemSize, Marshal.SizeOf<T>()), material, name, Length * ItemSize) { this.ItemSize = ItemSize; }
        public MultiBuffer(GraphicsBuffer.Target target, int Length, int ItemSize, MaterialPropertyBlock material, string name) : base(new GraphicsBuffer(target, Length * ItemSize, Marshal.SizeOf<T>()), material, name, Length * ItemSize) { this.ItemSize = ItemSize; }
        public MultiBuffer(GraphicsBuffer Buffer, Material material, string name, int length, int ItemSize) : base(Buffer, material, name, length * ItemSize) { this.ItemSize = ItemSize; }
        public readonly int ItemSize = 1;
        void Check(int index)
        {
            if ((index + 1) * ItemSize > Size)
            {
                Enlarge((index + 1) * 2 * ItemSize);
            }
        }
        public void Write(int Index, BufferFunction<T> Function)
        {
            Check(Index);
            Dirty[Index] = true;
            IsDirty = true;
            Function(Index, Data, ItemSize);
        }
        public void Read(int Index, BufferFunction<T> Function)
        {
            Check(Index);
            Function(Index, Data, ItemSize);
        }
        protected override void SetData(int Start, int Count)
        {
            base.SetData(Start * ItemSize, Count * ItemSize);
        }
    }

    public class GpuComputeBuffer<T> : BufferBase<T> where T : struct
    {
        public ComputeBuffer Buffer { get; private set; }
        public readonly ComputeShader Shader;
        public readonly string Name;
        public readonly int Kernel;
        public GpuComputeBuffer(ComputeShader material, int Kernel, string name, int Length)
        {
            Buffer = new ComputeBuffer(Length, Marshal.SizeOf<T>());
            Shader = material;
            Name = name;
            this.Kernel = Kernel;
            Shader.SetBuffer(Kernel, Name, Buffer);
            Dirty = new bool[Length];
            Data = new T[Length];
        }
        public override void Dispose()
        {
            Buffer?.Dispose();
            base.Dispose();
        }
        protected override void SetData(int Start, int Count)
        {
            Buffer.SetData(Data, Start, Start, Count);
        }
        public override void Enlarge(int NewSize)
        {
            ComputeBuffer newBuffer = new ComputeBuffer(NewSize, Marshal.SizeOf<T>());
            T[] temp = new T[NewSize];

            bool[] dirty = new bool[NewSize];
            Dirty.CopyTo(dirty, 0);
            Dirty = dirty;

            Array.Copy(Data, temp, Data.Length);
            Buffer.Dispose();
            Buffer = newBuffer;
            Buffer.SetData(temp);
            Data = temp;
            Shader.SetBuffer(Kernel, Name, Buffer);
        }
        public T this[int Index]
        {
            get => Data[Index];
            set
            {
                MarkDirty(Index);
                Data[Index] = value;
            }
        }
        /// <summary>sets a buffer, NOT efficient for updates, only call to create buffers.</summary>
        public void Set(Func<int, T> function)
        {
            for (int i = 0; i < Size; i++) Data[i] = function(i);
            Buffer.SetData(Data);
        }
    }

    public class Buffer<T> : GraphicsBufferBase<T>, IDisposable where T : struct
    {
        public T this[int Index]
        {
            get => Data[Index];
            set
            {
                MarkDirty(Index);
                Data[Index] = value;
            }
        }
        public Buffer(GraphicsBuffer.Target target, int Length, Material material, string name) : base(new GraphicsBuffer(target, Length, Marshal.SizeOf<T>()), material, name, Length) { }
        public Buffer(GraphicsBuffer.Target target, int Length, MaterialPropertyBlock material, string name) : base(new GraphicsBuffer(target, Length, Marshal.SizeOf<T>()), material, name, Length) { }
        public void Set(Func<int, T> function)
        {
            for (int i = 0; i < Size; i++) Data[i] = function(i);
            buffer.SetData(Data);
        }
    }

    /// <summary>a buffer between the compute shader and a graphics buffer.</summary>
    public class ComputeGraphicsBuffer<T> : IGpuBuffer where T : struct
    {
        public GraphicsBuffer Buffer { get; private set; }
        public GpuComputeBuffer<int> Dirty { get; private set; }
        public Material Material { get; private set; }
        public int Kernel { get; private set; }
        public ComputeShader Shader { get; private set; }
        public string ComputeName { get; private set; }
        public string MaterialName { get; private set; }
        private int ThreadCount;
        public ComputeGraphicsBuffer(ComputeShader Shader, Material material, int Kernel, string ComputeName, string MaterialName, int Length, int Threads)
        {
            this.Shader = Shader;
            this.Material = material;
            this.ComputeName = ComputeName;
            this.MaterialName = MaterialName;
            this.Kernel = Kernel;
            Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Length, Marshal.SizeOf<T>());
            Shader.SetBuffer(Kernel, ComputeName, Buffer);
            Material.SetBuffer(MaterialName, Buffer);
            ThreadCount = Mathf.CeilToInt(Length / (float)Threads);
            Dirty = new GpuComputeBuffer<int>(Shader, Kernel, "Dirty", Length);
            Dirty.Set((int i) => 1);
        }
        public void Dispose()
        {
            Buffer?.Dispose();
            Dirty?.Dispose();
        }
        public void Refresh()
        {
            Dirty.Refresh();
            Shader.Dispatch(Kernel, ThreadCount, 1, 1);
        }
        public void Update(int I)
        {
            Dirty[I] = 1;
        }
    }

    public class WrappedBuffer<T> : IGpuBuffer where T : struct
    {
        public Buffer<T> Buffer;
        readonly GetCustomData<T> getCustomData;
        public void Refresh() => Buffer.Refresh();
        internal WrappedBuffer(Buffer<T> Buffer, GetCustomData<T> getdata)
        {
            getCustomData = getdata;
            this.Buffer = Buffer;
        }
        public void Update(int I) => Buffer[I] = getCustomData(I);
        public void Dispose() => Buffer.Dispose();
    }

    public class WrappedComputeBuffer<T> : IGpuBuffer where T : struct
    {
        public GpuComputeBuffer<T> Buffer;
        readonly GetCustomData<T> getCustomData;
        public void Refresh() => Buffer.Refresh();
        internal WrappedComputeBuffer(GpuComputeBuffer<T> Buffer, GetCustomData<T> getdata)
        {
            getCustomData = getdata;
            this.Buffer = Buffer;
        }
        public void Update(int I) => Buffer[I] = getCustomData(I);
        public void Dispose() => Buffer.Dispose();
    }

    public class WrappedMultiBuffer<T> : IGpuBuffer where T : struct
    {
        public MultiBuffer<T> Buffer;
        readonly BufferFunction<T> getCustomData;
        public void Refresh() => Buffer.Refresh();
        internal WrappedMultiBuffer(GraphicsBuffer Buffer, BufferFunction<T> getdata, int ItemLength, int InitialLength, string name, Material material)
        {
            getCustomData = getdata;
            this.Buffer = new MultiBuffer<T>(Buffer, material, name, InitialLength, ItemLength);
        }
        public void Update(int I) => Buffer.Write(I, getCustomData);
        public void Dispose() => Buffer.Dispose();
    }

    // ---- delegates / data types used by the GPU manager (namespaced) ----

    public delegate void BufferFunction<T>(int Index, T[] Buffer, int ItemSize) where T : struct;
    public delegate T GetCustomData<T>(int Index) where T : struct;
    public delegate Vector3 GetSphereTileScale<T>(T SphereTile) where T : TileBase;
    public delegate DisplayMode GetDisplayMode();

    public struct Range
    {
        public int Min;
        public int Max;
        public Range(int Min, int Max) { this.Min = Min; this.Max = Max; }
    }

    public enum DisplayMode
    {
        ColorOnly = 0,
        TextureOnly = 1,
        ColoredTexture = 2,
        ColorAndTexture = 3
    }

    /// <summary>a interface so the manager can import custom buffers of different types.</summary>
    public interface IBufferData
    {
        IGpuBuffer GetBuffer(ManagerRoot ManagerData);
        string Name { get; }
    }

    public class CustomBufferData<T> : IBufferData where T : struct
    {
        public readonly GetCustomData<T> getCustomData;
        public string Name { get; set; }
        public readonly int Size;
        public CustomBufferData(string Name, GetCustomData<T> getCustomData)
        {
            this.Name = Name;
            Size = Marshal.SizeOf<T>();
            this.getCustomData = getCustomData;
        }
        public IGpuBuffer GetBuffer(ManagerRoot manager)
        {
            return new WrappedBuffer<T>(new Buffer<T>(GraphicsBuffer.Target.Structured, manager.TotalTiles, manager.Material, Name), getCustomData);
        }
    }

    public class ComputeGraphicsBufferData<T> : IBufferData where T : struct
    {
        public string Name { get; set; }
        public int Kernel;
        public string ComputeName;
        public int Threads;
        public IGpuBuffer GetBuffer(ManagerRoot ManagerData)
        {
            return new ComputeGraphicsBuffer<T>(ManagerData.ComputeShader, ManagerData.Material, Kernel, ComputeName, Name, ManagerData.TotalTiles, Threads);
        }
        public ComputeGraphicsBufferData(string Name, string ComputeName, int Kernel, int Threads)
        {
            this.Name = Name;
            this.Kernel = Kernel;
            this.ComputeName = ComputeName;
            this.Threads = Threads;
        }
    }
}
