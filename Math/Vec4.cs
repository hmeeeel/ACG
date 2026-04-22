using System;

public struct Vec4
{
    public float X, Y, Z, W;

    public Vec4(float x, float y, float z, float w = 1f)
    {
        X = x; Y = y; Z = z; W = w;
    }

    public Vec4(Vec3 v, float w = 1f)
    {
        X = v.X; Y = v.Y; Z = v.Z; W = w;
    }

    public readonly Vec3 PerspectiveDivide()
    {
        if (float.Abs(W) < 1e-7f) return new Vec3(X, Y, Z);
        return new Vec3(X / W, Y / W, Z / W);
    }

    public readonly Vec3 XYZ => new(X, Y, Z);
}
