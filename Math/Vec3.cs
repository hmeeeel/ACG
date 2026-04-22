using System;

public struct Vec3
{
    public float X, Y, Z;

    public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

    public static readonly Vec3 Zero  = new(0f, 0f, 0f);
    public static readonly Vec3 UnitX = new(1f, 0f, 0f);
    public static readonly Vec3 UnitY = new(0f, 1f, 0f);
    public static readonly Vec3 UnitZ = new(0f, 0f, 1f);

    public float Length => float.Sqrt(X * X + Y * Y + Z * Z);

    public Vec3 Normalized()
    {
       /* float len = Length;
        if (len < 1e-7f) return Zero;
        return new Vec3(X / len, Y / len, Z / len);*/

        float lenSq = X * X + Y * Y + Z * Z;
        if (lenSq < 1e-14f) return Zero;
        float invLen = 1f / float.Sqrt(lenSq);
        return new Vec3(X * invLen, Y * invLen, Z * invLen);
    }

    public static Vec3 operator +(Vec3 a, Vec3 b)  => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b)  => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 v)          => new(-v.X, -v.Y, -v.Z);
    public static Vec3 operator *(Vec3 v, float s) => new(v.X * s, v.Y * s, v.Z * s);
    public static Vec3 operator *(float s, Vec3 v) => v * s;
    public static Vec3 operator /(Vec3 v, float s) => new(v.X / s, v.Y / s, v.Z / s);
    public static Vec3 operator *(Vec3 a, Vec3 b)  => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

    public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);
}