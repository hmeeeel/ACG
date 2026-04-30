using System;

public class PointLight : LightSource
{
    public Vec3 Position { get; set; }

    public PointLight(Vec3 position)
    {
        Type = LightType.Point;
        Position = position;
    }

    public PointLight(float x, float y, float z)
        : this(new Vec3(x, y, z))
    {
    }

    public override Vec3 GetLightDirection(Vec3 fragmentPos)
    {
        // Вектор L = направление ОТ фрагмента К источнику
        return (Position - fragmentPos).Normalized();
    }

    public override Vec3 ComputeLighting(
        Vec3 fragmentPos,
        Vec3 normal,
        Vec3 viewDir,
        Material material,
        ShadingMode mode,
        Vec3 ambientColor)
    {
        // Вектор от фрагмента к источнику (НЕ нормализованный!)
        Vec3 toLight = Position - fragmentPos;
        // ВАЖНО: Вычисляем расстояние ДО нормализации
        // distance = length(toLight) = sqrt(dx² + dy² + dz²)
        float distance = toLight.Length;
        //норм
        Vec3 L = distance > 1e-6f 
            ? toLight / distance 
            : new Vec3(0f, 1f, 0f);

        float attenuation = 1f / (distance * distance + 0.01f);
        Vec3 effectiveLight = Color * Intensity;

        Vec3 lighting = LightingHelper.ComputePhongLighting(
            normal.X, normal.Y, normal.Z,
            L.X, L.Y, L.Z,
            viewDir.X, viewDir.Y, viewDir.Z,
            effectiveLight.X, effectiveLight.Y, effectiveLight.Z,
            material.DiffuseColor.X, material.DiffuseColor.Y, material.DiffuseColor.Z,
            ambientColor.X, ambientColor.Y, ambientColor.Z,
            material.Specular,
            material.Glossiness,
            mode);

        if (mode == ShadingMode.Ambient)
            return lighting;

        Vec3 ambient = material.DiffuseColor * ambientColor;
        Vec3 litPart = lighting - ambient;
        return ambient + (litPart * attenuation);
    }
}