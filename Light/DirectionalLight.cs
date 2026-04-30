using System;

public class DirectionalLight : LightSource
{
    public Vec3 Direction { get; set; }

    public DirectionalLight(Vec3 direction)
    {
        Type = LightType.Directional;
        Direction = direction.Normalized();
    }

    public DirectionalLight(float x, float y, float z)
        : this(new Vec3(x, y, z))
    {
    }

    public override Vec3 GetLightDirection(Vec3 fragmentPos)
    {
        return -Direction;
    }

    public override Vec3 ComputeLighting(
        Vec3 fragmentPos,
        Vec3 normal,
        Vec3 viewDir,
        Material material,
        ShadingMode mode,
        Vec3 ambientColor)
    {
        Vec3 L = -Direction;
        Vec3 effectiveLight = Color * Intensity;

        return LightingHelper.ComputePhongLighting(
            normal.X, normal.Y, normal.Z,
            L.X, L.Y, L.Z,
            viewDir.X, viewDir.Y, viewDir.Z,
            effectiveLight.X, effectiveLight.Y, effectiveLight.Z,
            material.DiffuseColor.X, material.DiffuseColor.Y, material.DiffuseColor.Z,
            ambientColor.X, ambientColor.Y, ambientColor.Z,
            material.Specular,
            material.Glossiness,
            mode);
    }
}