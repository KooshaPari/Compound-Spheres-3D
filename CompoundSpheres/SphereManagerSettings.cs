using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CompoundSpheres
{
    /// <summary>
    /// a interface so the manager can import custom buffers of different types
    /// </summary>
    public interface IBufferData {
        /// <summary>
        /// adds a custom buffer to a manager and returns it
        /// </summary>
        IBuffer GetBuffer(SphereManager sphereManager);
        /// <summary>
        /// the name of the custom buffer
        /// </summary>
        string GetName();
    }
    /// <summary>
    /// a custom buffer configuration, which tells the sphere manager to add a new compute buffer
    /// </summary>
    public readonly struct CustomBufferData<T> : IBufferData where T : struct
    {
        /// <summary>
        /// a function that returns your custom data for each sphere tile
        /// </summary>
        public readonly GetCustomData<T> getCustomData;
        /// <summary>
        /// the name of this buffer, in your custom shader
        /// </summary>
        public readonly string Name;
        /// <summary>
        /// the size of the data being stored, in bytes
        /// </summary>
        public readonly int Size;
        /// <summary>
        /// a custom buffer configuration, which tells the sphere manager to add a new compute buffer
        /// </summary>
        /// <param name="Name">the name of this buffer, in your custom shader</param>
        /// <param name="getCustomData">a function that returns your custom data for each sphere tile</param>
        public CustomBufferData(string Name, GetCustomData<T> getCustomData)
        {
            this.Name = Name;
            Size = Marshal.SizeOf<T>();
            this.getCustomData = getCustomData;
        }
        /// <summary>
        /// Legacy constructor accepting an explicit byte size (ignored — size
        /// is computed via Marshal.SizeOf&lt;T&gt;()).
        /// </summary>
        /// <param name="Name">the name of this buffer in your custom shader</param>
        /// <param name="BufferSize">ignored, kept for backward compatibility</param>
        /// <param name="getCustomData">a function that returns your custom data for each sphere tile</param>
        public CustomBufferData(string Name, int BufferSize, GetCustomData<T> getCustomData)
        {
            this.Name = Name;
            Size = Marshal.SizeOf<T>();
            this.getCustomData = getCustomData;
        }
        /// <inheritdoc/>
        public IBuffer GetBuffer(SphereManager sphereManager)
        {
            GraphicsBuffer Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, sphereManager.TotalTiles, Size);
            sphereManager.Material.SetBuffer(Name, Buffer);
            return new CustomBuffer<T>(sphereManager, Buffer, getCustomData);
        }
        /// <inheritdoc/>
        public string GetName()
        {
            return Name;
        }
    }
    /// <summary>
    /// the mode which indicates how tiles are displayed
    /// </summary>
    public enum DisplayMode
    {
        /// <summary>
        /// only the colors are displayed on each tile
        /// </summary>
        ColorOnly = 0,
        /// <summary>
        /// only the textures are displayed on each tile
        /// </summary>
        TextureOnly = 1,
        /// <summary>
        /// the textures are displayed, and the color is applied over them
        /// </summary>
        ColoredTexture = 2,
        /// <summary>
        /// the color is added to the texture
        /// </summary>
        ColorAndTexture = 3
    }
    /// <summary>
    /// a function that returns a custom data
    /// </summary>
    public delegate T GetCustomData<T>(SphereTile Tile) where T : struct;
    /// <summary>
    /// converts a X,Y position and height to a position on the Sphere
    /// </summary>
    public delegate Vector3 GetSphereTilePosition(SphereManager Manager, float x, float y, float height);
    /// <summary>
    /// the rotation of any spheretile, position is made then the rotation is made
    /// </summary>
    public delegate Quaternion GetSphereTileRotation(SphereTile SphereTile);
    /// <summary>
    /// the scale of a spheretile, called everytime its Row updates it
    /// </summary>
    public delegate Vector3 GetSphereTileScale(SphereTile SphereTile);
    /// <summary>
    /// the Index of a spheretiles texture in the sphere managers texture array
    /// </summary>
    public delegate int GetSphereTileTexture(SphereTile SphereTile);
    /// <summary>
    /// the color of a spheretile
    /// </summary>
    public delegate Color32 GetSphereTileColor(SphereTile sphereTile);
    /// <summary>
    /// if true, textures will be displayed on all tiles, if false only colors will be displayed
    /// </summary>
    public delegate DisplayMode GetDisplayMode(SphereManager SphereManager);
    /// <summary>
    /// the Range of Rows around the camera that draw their tiles
    /// </summary>
    public delegate void GetCameraRange(SphereManager SphereManager, out int Min, out int Max);
    /// <summary>
    /// called once the manager is created
    /// </summary>
    public delegate void Initiation(SphereManager Manager);
    /// <summary>
    /// The Settings of a sphere manager, only used when it is created, you cannot update it after
    /// </summary>
    /// <remarks>every single setting must be set! default settings are stored in <see cref="DefaultSettings"/></remarks>
    public struct SphereManagerSettings
    {
        /// <summary>
        /// converts a X,Y position and height to a position on the Sphere
        /// </summary>
        public GetSphereTilePosition getspheretileposition;
        /// <summary>
        /// the rotation of any spheretile, position is made then the rotation is made
        /// </summary>
        public GetSphereTileRotation GetSphereTileRotation;
        /// <summary>
        /// the scale of a spheretile, called everytime its Row updates it
        /// </summary>
        public GetSphereTileScale GetSphereTileScale;
        /// <summary>
        /// the Index of a spheretiles texture in the sphere managers texture array
        /// </summary>
        public GetSphereTileTexture GetSphereTileTexture;
        /// <summary>
        /// the color of a spheretile
        /// </summary>
        public GetSphereTileColor GetSphereTileColor;
        /// <summary>
        /// the mesh used to display tiles
        /// </summary>
        public Mesh SphereTileMesh;
        /// <summary>
        /// the Material of the meshes, must have the compound spheres shader to work!
        /// </summary>
        public Material SphereTileMaterial;
        /// <summary>
        /// how the textures and colors are rendered on each tile, called everytime they are drawn
        /// </summary>
        public GetDisplayMode GetDisplayMode;
        /// <summary>
        /// the Range of Rows around the camera that draw their tiles
        /// </summary>
        public GetCameraRange GetCameraRange;
        /// <summary>
        /// the array of textures the manager can display, a spheretile displays a texture at index getspheretiletexture
        /// </summary>
        public Texture2DArray TextureArray;
        /// <summary>
        /// a list of custom buffers this manager's shader has (Optional)
        /// </summary>
        public List<IBufferData> CustomBuffers;
        /// <summary>
        /// called once the manager is created
        /// </summary>
        public Initiation Initiation;
        /// <summary>
        /// Settings for a sphere manager
        /// </summary>
        /// <param name="Initiation">the function called when the sphere manager is created</param>
        /// <param name="getSphereTilePosition">converts a x,y coordinate on a grid and a Height variable to a coordinate on the sphere</param>
        /// <param name="getSphereTileRotation">gets the rotation of a sphere tile, called once</param>
        /// <param name="getSphereTileScale">gets the scale of the tile, called everytime it is refreshed</param>
        /// <param name="getSphereTileColor">gets the color of the tile, called everytime it is refreshed</param>
        /// <param name="getSphereTileTexture">how the textures and colors are rendered on each tile, called everytime they are drawn</param>
        /// <param name="getdisplaymode">if true, tiles will display their textures, otherwise they only display their color, called everytime tiles are rendered</param>
        /// <param name="Textures">an array of textures that can be displayed, every texture must have same format provided and size</param>
        /// <param name="Format">The Texture format</param>
        /// <param name="mesh">the spheretile mesh, default quad</param>
        /// <param name="BufferSize">the max distance between two tiles on the sphere to be included in the same batch to be sent to the gpu, if there are tiles in between, they will also be sent to the gpu and their data is recalculated!</param>
        /// <param name="material">The Material of the Mesh</param>
        /// <param name="getCameraRange">gets the range of rows that are displayede around the cameraparam</param>
        /// <param name="custombuffers">a list of custom buffers this manager's shader has (Optional)</param>
        /// <remarks>the Material MUST have the compound sphere shader or another shader with same functionality</remarks>
        public SphereManagerSettings(Initiation Initiation, GetSphereTilePosition getSphereTilePosition, GetSphereTileRotation getSphereTileRotation, GetSphereTileScale getSphereTileScale, GetSphereTileColor getSphereTileColor, GetSphereTileTexture getSphereTileTexture, GetDisplayMode getdisplaymode, Texture2D[] Textures, TextureFormat Format, Mesh mesh, Material material, GetCameraRange getCameraRange, List<IBufferData> custombuffers = null)
        {
            getspheretileposition = getSphereTilePosition;
            GetSphereTileRotation = getSphereTileRotation;
            GetSphereTileScale = getSphereTileScale;
            GetSphereTileTexture = getSphereTileTexture;
            GetSphereTileColor = getSphereTileColor;
            SphereTileMesh = mesh;
            SphereTileMaterial = material;
            GetDisplayMode = getdisplaymode;
            GetCameraRange = getCameraRange;
            this.Initiation = Initiation;
            TextureArray = new Texture2DArray(Textures[0].width, Textures[0].height, Textures.Length, Format, true, false);
            TextureArray.filterMode = FilterMode.Bilinear;
            TextureArray.wrapMode = TextureWrapMode.Repeat;
            for (int i = 0; i < Textures.Length; i++)
            {
                TextureArray.SetPixels32(Textures[i].GetPixels32(), i);
            }
            TextureArray.Apply();
            CustomBuffers = custombuffers;
        }
        /// <summary>
        /// Settings for a sphere manager
        /// </summary>
        /// <param name="Initiation">the function called when the sphere manager is created</param>
        /// <param name="getSphereTilePosition">converts a x,y coordinate on a grid and a Height variable to a coordinate on the sphere</param>
        /// <param name="getSphereTileRotation">gets the rotation of a sphere tile, called once</param>
        /// <param name="getSphereTileScale">gets the scale of the tile, called everytime it is refreshed</param>
        /// <param name="getSphereTileColor">gets the color of the tile, called everytime it is refreshed</param>
        /// <param name="getSphereTileTexture">gets the index of a texture in the Textures Array, of the tile, called everytime it is refreshed</param>
        /// <param name="BufferSize">the max distance between two tiles on the sphere to be included in the same batch to be sent to the gpu, if there are tiles in between, they will also be sent to the gpu and their data is recalculated!</param>
        /// <param name="getdisplaymode">how the textures and colors are rendered on each tile, called everytime they are drawn</param>
        /// <param name="Textures">an array of textures that can be displayed, you must create this and apply it first</param>
        /// <param name="mesh">the spheretile mesh, default quad</param>
        /// <param name="material">The Material of the Mesh</param>
        /// <param name="custombuffers">a list of custom buffers this manager's shader has (Optional)</param>
        /// <param name="getCameraRange">gets the range of rows that are displayede around the cameraparam</param>
        /// <remarks>the Material MUST have the compound sphere shader or another shader with same functionality</remarks>
        public SphereManagerSettings(Initiation Initiation, GetSphereTilePosition getSphereTilePosition, GetSphereTileRotation getSphereTileRotation, GetSphereTileScale getSphereTileScale, GetSphereTileColor getSphereTileColor, GetSphereTileTexture getSphereTileTexture, GetDisplayMode getdisplaymode, Texture2DArray Textures, Mesh mesh, Material material, GetCameraRange getCameraRange, List<IBufferData> custombuffers = null)
        {
            getspheretileposition = getSphereTilePosition;
            GetSphereTileRotation = getSphereTileRotation;
            GetSphereTileScale = getSphereTileScale;
            GetSphereTileTexture = getSphereTileTexture;
            GetSphereTileColor = getSphereTileColor;
            SphereTileMesh = mesh;
            SphereTileMaterial = material;
            GetDisplayMode = getdisplaymode;
            GetCameraRange = getCameraRange;
            this.Initiation = Initiation;
            TextureArray = Textures;
            CustomBuffers = custombuffers;
        }
        /// <summary>
        /// copies settings from another setting, allows you to also add a new mesh since that is the most commonly modified setting
        /// </summary>
        public SphereManagerSettings(SphereManagerSettings Original, Mesh OverrideMesh = null)
        {
            getspheretileposition = Original.getspheretileposition;
            GetSphereTileRotation = Original.GetSphereTileRotation;
            GetSphereTileScale = Original.GetSphereTileScale;
            GetSphereTileTexture = Original.GetSphereTileTexture;
            GetSphereTileColor = Original.GetSphereTileColor;
            SphereTileMesh = OverrideMesh != null ? OverrideMesh : Original.SphereTileMesh;
            SphereTileMaterial = Original.SphereTileMaterial;
            GetDisplayMode = Original.GetDisplayMode;
            GetCameraRange = Original.GetCameraRange;
            Initiation = Original.Initiation;
            CustomBuffers = Original.CustomBuffers;
            TextureArray = Original.TextureArray;
        }
    }
    /// <summary>
    /// Thrown when the GPU does not meet minimum requirements
    /// (instancing, compute shaders, indirect args).
    /// </summary>
    public class IncompatibleHardwareException : System.Exception
    {
        public IncompatibleHardwareException() : base("GPU does not support required features (instancing / compute / indirect args).") { }
        public IncompatibleHardwareException(string message) : base(message) { }
    }
}