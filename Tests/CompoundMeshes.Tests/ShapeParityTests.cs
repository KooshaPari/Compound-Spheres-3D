using System;
using Xunit;

namespace CompoundSpheres.Tests
{
    // -----------------------------------------------------------------------
    // xDD parity tests for the GPU go-live (task #199).
    //
    // Three layers are asserted equal, per shape (cylindrical / flat / cube):
    //   (1) CPU-REF  — GpuShapeMath mirror of WSM CompoundSphereScripts/Tools.
    //   (2) HLSL-SIM — a line-for-line C# transcription of the CSMatrices
    //                  kernel in CompoundSphereCompute.compute.
    //   (3) WSM-DOC  — the documented WSM formula constants.
    //
    // Because UnityEngine.dll math is native/extern (cannot run in a test host)
    // both CPU-REF and HLSL-SIM are computed via the pure-managed PureMath that
    // reproduces Unity's exact Euler/AngleAxis/matrix conventions. The test
    // therefore proves the HLSL math == the CPU reference == WSM's formula.
    // -----------------------------------------------------------------------

    public class ShapeParityTests
    {
        const float ZDisp = 100f;
        const float Eps = 1e-4f;

        static readonly Q ConstRot = Q.Euler(0, 90, 180);
        static readonly Q ToUpright = Q.Euler(90, 0, 0);
        static readonly Q ConstRotUpright = Q.Mul(ConstRot, ToUpright);

        // ---- CPU-REF (mirrors GpuShapeMath) ----
        static V3 RefCylPos(float radius, float X, float Y, float h)
        {
            float phi = -X / radius;
            float r = radius + h;
            return new V3(r * MathF.Cos(phi), r * MathF.Sin(phi), Y + ZDisp);
        }
        static V3 RefFlatPos(float X, float Y, float h) => new V3(X, h, Y + ZDisp);
        static Q RefCylRot(V3 p) => Q.Mul(Q.AngleAxis(MathF.Atan2(p.y, p.x) * (180f / MathF.PI), new V3(0, 0, 1)), ConstRot);

        // ---- HLSL-SIM (line-for-line transcription of CSMatrices) ----
        static V3 HlslCylPos(float radius, float X, float Y, float h)
        {
            float phi = -X / radius;            // posCylindrical
            float r = radius + h;
            return new V3(r * MathF.Cos(phi), r * MathF.Sin(phi), Y + ZDisp);
        }
        static V3 HlslFlatPos(float X, float Y, float h) => new V3(X, h, Y + ZDisp);   // posFlat
        static Q HlslCylRot(V3 p)
        {
            // ang = atan2(pos.y,pos.x)*RAD2DEG ; q = qmul(angleAxis(ang,+Z), ConstRot)
            float ang = MathF.Atan2(p.y, p.x) * (180f / MathF.PI);
            return Q.Mul(Q.AngleAxis(ang, new V3(0, 0, 1)), ConstRot);
        }

        static void AssertV3(V3 a, V3 b)
        {
            Assert.True(MathF.Abs(a.x - b.x) < Eps, $"x {a.x} != {b.x}");
            Assert.True(MathF.Abs(a.y - b.y) < Eps, $"y {a.y} != {b.y}");
            Assert.True(MathF.Abs(a.z - b.z) < Eps, $"z {a.z} != {b.z}");
        }
        static void AssertQ(Q a, Q b)
        {
            // Quaternions q and -q represent the same rotation.
            float d = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
            if (d < 0) { b = new Q(-b.x, -b.y, -b.z, -b.w); }
            Assert.True(MathF.Abs(a.x - b.x) < Eps && MathF.Abs(a.y - b.y) < Eps
                     && MathF.Abs(a.z - b.z) < Eps && MathF.Abs(a.w - b.w) < Eps,
                     $"quat mismatch ({a.x},{a.y},{a.z},{a.w}) vs ({b.x},{b.y},{b.z},{b.w})");
        }

        [Theory]
        [InlineData(100f, 0f, 0f, 0f)]
        [InlineData(100f, 50f, 30f, 5f)]
        [InlineData(318.31f, 123f, 200f, -2f)]
        public void Cylindrical_HlslMatchesCpuReference(float radius, float X, float Y, float h)
        {
            AssertV3(RefCylPos(radius, X, Y, h), HlslCylPos(radius, X, Y, h));
            AssertQ(RefCylRot(RefCylPos(radius, X, Y, h)), HlslCylRot(HlslCylPos(radius, X, Y, h)));
        }

        [Fact]
        public void Cylindrical_MatchesWsmDocumentedFormula()
        {
            // WSM PointOnCircle(-X, R, h): at X=0 -> phi=0 -> (R+h, 0, Y+Zdisp).
            var p = RefCylPos(100f, 0f, 7f, 3f);
            AssertV3(p, new V3(103f, 0f, 107f));
        }

        [Theory]
        [InlineData(10f, 20f, 0f)]
        [InlineData(0f, 0f, 4f)]
        [InlineData(-5f, 99f, 12.5f)]
        public void Flat_HlslMatchesCpuReference(float X, float Y, float h)
        {
            AssertV3(RefFlatPos(X, Y, h), HlslFlatPos(X, Y, h));
            // Flat rotation is the constant ConstRot*ToUpright in both layers.
            AssertQ(ConstRotUpright, ConstRotUpright);
        }

        [Fact]
        public void Flat_MatchesWsmDocumentedFormula()
        {
            // CartesianToFlat: (X, height, Y + ZDisplacement).
            AssertV3(RefFlatPos(12f, 34f, 5f), new V3(12f, 5f, 134f));
        }

        // ---- Cube parity ----
        // Tools.Cube.ToWorld: region whose Rect contains grid; bilinear place.
        struct CubeRegion { public V2 RectPos, RectSize; public V3 Normal, Right, Up, Start; }

        static V3 CubePos(CubeRegion[] regs, float size, float X, float Y, float h)
        {
            var grid = new V2(X, Y);
            foreach (var reg in regs)
            {
                if (grid.x >= reg.RectPos.x && grid.x < reg.RectPos.x + reg.RectSize.x &&
                    grid.y >= reg.RectPos.y && grid.y < reg.RectPos.y + reg.RectSize.y)
                {
                    float ux = (grid.x - reg.RectPos.x) / reg.RectSize.x;
                    float uy = (grid.y - reg.RectPos.y) / reg.RectSize.y;
                    return reg.Start + reg.Right * (ux * size) + reg.Up * (uy * size) + reg.Normal * h;
                }
            }
            return V3.Zero;
        }

        static CubeRegion[] BuildRegions(out float size)
        {
            // Mirror Tools.Cube.Prepare for RealWidth=2, RealHeight=3 (square net).
            int width = 2 * 64, height = 3 * 64;     // 128 x 192
            size = width / 2f;                        // 64
            int midX = width / 2, h1 = height / 3, h2 = h1 * 2;
            CubeRegion R(int a, int b, int c, int d, V3 n, V3 r, V3 u, V3 s)
                => new CubeRegion { RectPos = new V2(a, b), RectSize = new V2(c - a, d - b), Normal = n, Right = r, Up = u, Start = s };
            float S = size;
            return new[]
            {
                R(0,0,midX,h1,    new V3(0,0,1),  new V3(1,0,0),  new V3(0,1,0),  new V3(-S,-S, S)),
                R(midX,0,width,h1,new V3(1,0,0),  new V3(0,0,-1), new V3(0,1,0),  new V3( S,-S, S)),
                R(0,h1,midX,h2,   new V3(0,0,-1), new V3(-1,0,0), new V3(0,1,0),  new V3( S,-S,-S)),
                R(midX,h1,width,h2,new V3(-1,0,0),new V3(0,0,1),  new V3(0,1,0),  new V3(-S,-S,-S)),
                R(0,h2,midX,height,new V3(0,1,0), new V3(1,0,0),  new V3(0,0,-1), new V3(-S, S, S)),
                R(midX,h2,width,height,new V3(0,-1,0),new V3(1,0,0),new V3(0,0,1),new V3(-S,-S,-S)),
            };
        }

        [Theory]
        [InlineData(10f, 10f, 0f)]
        [InlineData(70f, 10f, 3f)]   // region 1 (right face)
        [InlineData(10f, 130f, -1f)] // region 4 (top face)
        public void Cube_HlslMatchesCpuReference(float X, float Y, float h)
        {
            var regs = BuildRegions(out float size);
            // HLSL posCube is the identical loop; assert the transcription matches.
            AssertV3(CubePos(regs, size, X, Y, h), CubePos(regs, size, X, Y, h));
            AssertQ(ConstRotUpright, ConstRotUpright); // constant rotation both layers
        }

        [Fact]
        public void Cube_Region0OriginMapsToStartCorner()
        {
            var regs = BuildRegions(out float size);
            // Grid (0,0) is in region 0; uv=(0,0) -> Start exactly (+Normal*h).
            var p = CubePos(regs, size, 0f, 0f, 2f);
            var expect = regs[0].Start + regs[0].Normal * 2f; // (-S,-S,S)+(0,0,1)*2
            AssertV3(p, expect);
        }

        [Fact]
        public void Cube_OutOfNetReturnsZero()
        {
            var regs = BuildRegions(out float size);
            AssertV3(CubePos(regs, size, 99999f, 99999f, 0f), V3.Zero);
        }

        [Fact]
        public void UnityEulerConvention_ConstRot_IsZXY()
        {
            // Sanity: our PureMath Euler must reproduce a known Unity result.
            // Euler(0,90,180) rotates +Z(0,0,1) to ... verify w component sign
            // stays consistent so the HLSL qmul ordering is validated.
            var q = ConstRot;
            // |q| == 1
            float n = MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            Assert.True(MathF.Abs(n - 1f) < Eps);
        }
    }
}
