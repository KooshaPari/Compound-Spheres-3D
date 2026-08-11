using UnityEngine;

namespace CompoundSpheres.Gpu
{
    /// <summary>
    /// Canonical tile-shape identifiers shared by the C# CPU-reference math and
    /// the <c>Shape</c> uniform consumed by CompoundSphereCompute.compute's
    /// CSMatrices kernel. The integer values are part of the GPU contract — do
    /// not renumber without updating the .compute.
    /// </summary>
    public enum TileShape
    {
        /// <summary>PointOnCircle cylinder (WSM CartesianToCylindrical).</summary>
        Cylindrical = 0,
        /// <summary>Planar grid (WSM CartesianToFlat).</summary>
        Flat = 1,
        /// <summary>6-face cube unwrap (WSM Tools.Cube.ToWorld).</summary>
        Cube = 2
    }

    /// <summary>
    /// A single cube face, mirroring WorldSphereMod Tools.Cube.Region. Laid out
    /// to match the HLSL <c>CubeRegion</c> struct byte-for-byte so the array can
    /// be uploaded straight into the <c>CubeRegions</c> StructuredBuffer.
    /// Layout (floats): RectPos(2) RectSize(2) Normal(3) Right(3) Up(3) Start(3).
    /// </summary>
    [System.Serializable]
    public struct GpuCubeRegion
    {
        public Vector2 RectPos;
        public Vector2 RectSize;
        public Vector3 Normal;
        public Vector3 Right;
        public Vector3 Up;
        public Vector3 Start;
        /// <summary>Stride in bytes — must equal the HLSL struct size.</summary>
        public const int Stride = (2 + 2 + 3 + 3 + 3 + 3) * sizeof(float); // 64
    }

    /// <summary>
    /// CPU reference implementation of the three tile-shape transforms that the
    /// CSMatrices compute kernel mirrors. This is the single source of truth the
    /// parity tests assert against; the HLSL is documented to mirror it exactly.
    ///
    /// All math is copied verbatim from WorldSphereMod
    /// (Code/CompoundSphereScripts.cs, Code/Tools.cs, Code/Constants.cs) so a
    /// tile placed by the GPU path lands pixel-identically to the legacy CPU path.
    /// </summary>
    public static class GpuShapeMath
    {
        /// <summary>WSM Constants.ZDisplacement.</summary>
        public const float ZDisplacement = 100f;

        /// <summary>WSM Constants.ConstRot = Euler(0,90,180).</summary>
        public static readonly Quaternion ConstRot = Quaternion.Euler(0, 90, 180);
        /// <summary>WSM Constants.ToUpright = Euler(90,0,0).</summary>
        public static readonly Quaternion ToUpright = Quaternion.Euler(90, 0, 0);
        /// <summary>Constant rotation used by Flat + Cube tiles (ConstRot * ToUpright).</summary>
        public static readonly Quaternion ConstRotUpright = ConstRot * ToUpright;

        // ---- Positions (mirror WSM CartesianTo* exactly) ----

        /// <summary>WSM CartesianToCylindrical: PointOnCircle(-X, R, h); z = Y + ZDisplacement.</summary>
        public static Vector3 PosCylindrical(float radius, float X, float Y, float height = 0f)
        {
            float phi = -X / radius;
            float r = radius + height;
            return new Vector3(r * Mathf.Cos(phi), r * Mathf.Sin(phi), Y + ZDisplacement);
        }

        /// <summary>WSM CartesianToFlat: (X, height, Y + ZDisplacement).</summary>
        public static Vector3 PosFlat(float X, float Y, float height = 0f)
            => new Vector3(X, height, Y + ZDisplacement);

        /// <summary>WSM Tools.Cube.ToWorld(grid, height) — region/face based.</summary>
        public static Vector3 PosCube(GpuCubeRegion[] regions, float cubeSize, float X, float Y, float height = 0f)
        {
            var grid = new Vector2(X, Y);
            for (int i = 0; i < regions.Length; i++)
            {
                var reg = regions[i];
                Vector2 lo = reg.RectPos;
                Vector2 hi = reg.RectPos + reg.RectSize;
                if (grid.x >= lo.x && grid.x < hi.x && grid.y >= lo.y && grid.y < hi.y)
                {
                    Vector2 uv = new Vector2(
                        (grid.x - reg.RectPos.x) / reg.RectSize.x,
                        (grid.y - reg.RectPos.y) / reg.RectSize.y);
                    return reg.Start
                         + reg.Right * (uv.x * cubeSize)
                         + reg.Up * (uv.y * cubeSize)
                         + reg.Normal * height;
                }
            }
            return Vector3.zero;
        }

        // ---- Rotations (mirror WSM *Rotation exactly) ----

        /// <summary>WSM CylindricalRotation(pos): AngleAxis(atan2(y,x)deg, +Z) * ConstRot.</summary>
        public static Quaternion RotCylindrical(Vector3 pos)
            => Quaternion.AngleAxis(Mathf.Atan2(pos.y, pos.x) * Mathf.Rad2Deg, Vector3.forward) * ConstRot;

        /// <summary>WSM FlatRotation / CubeRotation: constant ConstRot * ToUpright.</summary>
        public static Quaternion RotFlatCube() => ConstRotUpright;

        /// <summary>
        /// Full per-tile model matrix for a shape — the CPU reference the GPU
        /// CSMatrices kernel reproduces. Scale is applied later in the vertex
        /// shader, so this is translation * rotation only (identity scale).
        /// </summary>
        public static Matrix4x4 TileMatrix(TileShape shape, float radius, float X, float Y,
            float height, GpuCubeRegion[] cubeRegions = null, float cubeSize = 0f)
        {
            Vector3 pos;
            Quaternion rot;
            switch (shape)
            {
                case TileShape.Flat:
                    pos = PosFlat(X, Y, height);
                    rot = RotFlatCube();
                    break;
                case TileShape.Cube:
                    pos = PosCube(cubeRegions, cubeSize, X, Y, height);
                    rot = RotFlatCube();
                    break;
                default: // Cylindrical
                    pos = PosCylindrical(radius, X, Y, height);
                    rot = RotCylindrical(pos);
                    break;
            }
            return Matrix4x4.Translate(pos) * Matrix4x4.Rotate(rot);
        }
    }
}
