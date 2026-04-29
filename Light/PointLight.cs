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
        Material material)
    {
        // Вектор от фрагмента к источнику (НЕ нормализованный!)
        Vec3 toLight = Position - fragmentPos;
        
        // ВАЖНО: Вычисляем расстояние ДО нормализации
        // distance = length(toLight) = sqrt(dx² + dy² + dz²)
        float distance = toLight.Length;
        
        // Нормализуем 
        Vec3 L = distance > 1e-6f 
            ? toLight / distance 
            : new Vec3(0f, 1f, 0f);

        float attenuation = 1f / (distance * distance + 0.01f);


        float NdotL = float.Max(0f, Vec3.Dot(normal, L));
        Vec3 diffuse = material.DiffuseColor * (Color * Intensity * NdotL);

        Vec3 H = (L + viewDir).Normalized();
        float HdotN = float.Max(0f, Vec3.Dot(H, normal));
        float specPower = float.Pow(HdotN, material.Glossiness);
        Vec3 specular = Color * (Intensity * material.Specular * specPower);

        return attenuation * (diffuse + specular);
    }
}