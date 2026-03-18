using System;

public sealed class OrbitCamera
{
    public const float MinRadius = 0.1f;
    public const float MaxRadius = 20f;

    // 0 xAxis становится нулевым вектором = модель исчезает
    public const float MinTheta  = 0.01f;
    public const float MaxTheta  = MathF.PI - 0.01f;
    public float Radius { get; private set; } = 3f;

    // полярный — горизонтальное вращение (0 .. 2π)
    public float Phi    { get; private set; } = 0.5f;

    // зенитный — вертикальное вращение 
    public float Theta  { get; private set; } = MathF.PI / 3f; // над Target свреху

    public Vec3 Target { get; set; } = new Vec3(0f, 0f, 0f);

    public void Rotate(float dX, float dY, float sensitivity = 0.008f)
    {
        Phi   += dX * sensitivity;                                  
        Theta  = Math.Clamp(Theta + dY * sensitivity, MinTheta, MaxTheta);
    }

    public void Zoom(float delta, float factor = 0.15f)
    {
        Radius = Math.Clamp(Radius * (1f - delta * factor), MinRadius, MaxRadius);
    }

    public void Reset()
    {
        Radius = 3f;
        Phi    = 0.5f;
        Theta  = MathF.PI / 3f;
    }

    public Vec3 EyePosition()
    {
        float sinT = MathF.Sin(Theta);
        float cosT = MathF.Cos(Theta);
        float sinP = MathF.Sin(Phi);
        float cosP = MathF.Cos(Phi);

        return new Vec3(
            Target.X + Radius * sinT * sinP,
            Target.Y + Radius * cosT,
            Target.Z + Radius * sinT * cosP);
    }

    public Matrix44 ViewMatrix()
    {
        return Matrix44.LookAt(EyePosition(), Target, new Vec3(0f, 1f, 0f));
    }
}