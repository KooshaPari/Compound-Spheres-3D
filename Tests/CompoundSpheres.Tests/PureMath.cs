using System;

namespace CompoundSpheres.Tests
{
    // Pure-managed reimplementation of the exact UnityEngine math conventions
    // used by WSM + the GpuShapeMath C# reference + the CompoundSphereCompute
    // HLSL kernel. UnityEngine.dll math is native/extern and cannot run in a
    // test host, so this file reproduces Unity's documented formulas so parity
    // can be asserted end-to-end (WSM formula == C# ref == HLSL math).

    public struct V3
    {
        public float x, y, z;
        public V3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static V3 operator *(V3 a, float s) => new V3(a.x * s, a.y * s, a.z * s);
        public static V3 Zero => new V3(0, 0, 0);
    }

    public struct V2
    {
        public float x, y;
        public V2(float x, float y) { this.x = x; this.y = y; }
    }

    // Quaternion (x,y,z,w).
    public struct Q
    {
        public float x, y, z, w;
        public Q(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        // Unity Quaternion.AngleAxis(angleDeg, axis) — axis normalized.
        public static Q AngleAxis(float angleDeg, V3 axis)
        {
            float half = angleDeg * (MathF.PI / 180f) * 0.5f;
            float s = MathF.Sin(half);
            float len = MathF.Sqrt(axis.x * axis.x + axis.y * axis.y + axis.z * axis.z);
            if (len > 0) { axis.x /= len; axis.y /= len; axis.z /= len; }
            return new Q(axis.x * s, axis.y * s, axis.z * s, MathF.Cos(half));
        }

        // Unity Quaternion.Euler(x,y,z) in degrees. Unity applies rotations in
        // Z, X, Y order (intrinsic) -> q = qy * qx * qz.
        public static Q Euler(float xDeg, float yDeg, float zDeg)
        {
            Q qx = AngleAxis(xDeg, new V3(1, 0, 0));
            Q qy = AngleAxis(yDeg, new V3(0, 1, 0));
            Q qz = AngleAxis(zDeg, new V3(0, 0, 1));
            return Mul(qy, Mul(qx, qz));
        }

        // Hamilton product matching Unity operator* (a * b).
        public static Q Mul(Q a, Q b)
        {
            return new Q(
                a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
                a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
                a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        }
    }

    // 4x4 matrix, row-major, mul(M, v) convention (Unity Matrix4x4 / HLSL mul).
    public struct M4
    {
        public float[,] m;
        public M4(float[,] m) { this.m = m; }

        public static M4 Rotate(Q q)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            float xx = x * x, yy = y * y, zz = z * z;
            float xy = x * y, xz = x * z, yz = y * z;
            float wx = w * x, wy = w * y, wz = w * z;
            var r = new float[4, 4];
            r[0, 0] = 1 - 2 * (yy + zz); r[0, 1] = 2 * (xy - wz); r[0, 2] = 2 * (xz + wy); r[0, 3] = 0;
            r[1, 0] = 2 * (xy + wz); r[1, 1] = 1 - 2 * (xx + zz); r[1, 2] = 2 * (yz - wx); r[1, 3] = 0;
            r[2, 0] = 2 * (xz - wy); r[2, 1] = 2 * (yz + wx); r[2, 2] = 1 - 2 * (xx + yy); r[2, 3] = 0;
            r[3, 0] = 0; r[3, 1] = 0; r[3, 2] = 0; r[3, 3] = 1;
            return new M4(r);
        }

        public static M4 Translate(V3 t)
        {
            var r = new float[4, 4];
            for (int i = 0; i < 4; i++) r[i, i] = 1;
            r[0, 3] = t.x; r[1, 3] = t.y; r[2, 3] = t.z;
            return new M4(r);
        }

        public static M4 operator *(M4 a, M4 b)
        {
            var r = new float[4, 4];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    float s = 0;
                    for (int k = 0; k < 4; k++) s += a.m[i, k] * b.m[k, j];
                    r[i, j] = s;
                }
            return new M4(r);
        }
    }
}
