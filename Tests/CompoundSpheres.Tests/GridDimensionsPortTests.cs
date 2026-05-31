using System;
using System.IO;
using Xunit;

namespace CompoundSpheres.Tests
{
    // -----------------------------------------------------------------------
    // Issue #199 Phase 0 contract: IGridDimensions extraction.
    //
    // The concrete managers depend on UnityEngine.dll (native/extern) and so
    // cannot be instantiated in the net8 test host. We therefore assert the
    // extraction at the source level (matching the superproject's
    // source-invariant test idiom): the port exists, declares exactly the four
    // shared members, both managers implement it, and HeightFieldRenderer
    // depends on the abstraction (not the concrete CPU manager).
    // -----------------------------------------------------------------------
    public class GridDimensionsPortTests
    {
        static string SrcRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CompoundSpheres.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "CompoundSpheres");
        }

        static string Read(string rel) => File.ReadAllText(Path.Combine(SrcRoot(), rel));

        [Fact]
        public void Port_interface_exists_with_shared_members()
        {
            var src = Read("IGridDimensions.cs");
            Assert.Contains("interface IGridDimensions", src);
            Assert.Contains("int Rows { get; }", src);
            Assert.Contains("int Cols { get; }", src);
            Assert.Contains("Material Material { get; }", src);
            Assert.Contains("Vector3 SphereTilePosition(float X, float Y, float Height)", src);
        }

        [Fact]
        public void CpuSphereManager_implements_port()
        {
            var src = Read("SphereManager.cs");
            Assert.Contains("class SphereManager : MonoBehaviour, IEnumerable, IGridDimensions", src);
        }

        [Fact]
        public void GpuSphereManager_implements_port()
        {
            var src = Read("Gpu/GpuSphereManager.cs");
            Assert.Contains("class GpuSphereManager : ManagerBase<GpuSphereTile>, IEnumerable, IGridDimensions", src);
        }

        [Fact]
        public void HeightFieldRenderer_depends_on_abstraction()
        {
            var src = Read("HeightFieldRenderer.cs");
            Assert.Contains("readonly IGridDimensions _manager;", src);
            Assert.Contains("public HeightFieldRenderer(IGridDimensions manager)", src);
            Assert.DoesNotContain("readonly SphereManager _manager;", src);
        }

        // ---- Phase 1: GPU async creator ----
        [Fact]
        public void GpuCreator_exposes_async_wrapper()
        {
            var src = Read("Gpu/GpuSphereManager.cs");
            Assert.Contains("public static IEnumerator CreateSphereManagerAsync(int rows, int cols, GpuSphereManagerSettings settings, Action<GpuSphereManager> onCreated", src);
            // Heavy tile loop must yield across frames (matches CPU coroutine).
            Assert.Contains("if (++count % chunkSize == 0)", src);
            Assert.Contains("yield return null;", src);
            // Manager handed back before the synchronous Begin().
            Assert.Contains("onCreated?.Invoke(manager);", src);
        }
    }
}
