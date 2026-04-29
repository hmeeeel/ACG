using System;

public class SpotLight : LightSource
{
    public Vec3 Position { get; set; }
    public Vec3 Direction { get; set; }
    public float InnerCutoff { get; set; }
    public float OuterCutoff { get; set; }
    
    public float Falloff { get; set; } = 1f;

    public SpotLight(Vec3 position, Vec3 direction, float innerAngle, float outerAngle)
    {
        Type = LightType.Spot;
        Position = position;
        Direction = direction.Normalized();
        
        InnerCutoff = float.Cos(innerAngle);
        OuterCutoff = float.Cos(outerAngle);
        
        if (InnerCutoff < OuterCutoff)
        {
            throw new ArgumentException(
                "innerAngle должен быть МЕНЬШЕ outerAngle " +
                "(innerCutoff должен быть БОЛЬШЕ outerCutoff)");
        }
    }

    public override Vec3 GetLightDirection(Vec3 fragmentPos)
    {
        // Так же, как у Point Light — от фрагмента к источнику
        return (Position - fragmentPos).Normalized();
    }

    public override Vec3 ComputeLighting(
        Vec3 fragmentPos,
        Vec3 normal,
        Vec3 viewDir,
        Material material)
    {
        // Вектор от фрагмента к источнику (не нормализованный)
        Vec3 toLight = Position - fragmentPos;
        float distance = toLight.Length;
        Vec3 L = distance > 1e-6f 
            ? toLight / distance 
            : new Vec3(0f, 1f, 0f);

        // === 1. ЗАТУХАНИЕ С РАССТОЯНИЕМ ===
        float attenuation = 1f / (distance * distance + 0.01f);

        // === 2. ЗАТУХАНИЕ ПО УГЛУ (spot cone) ===
       //  ОТ источника К фрагменту
        float theta = Vec3.Dot(Direction, -L);
        
        float spotIntensity = ComputeSpotIntensity(theta);
        
        if (spotIntensity <= 0f)
            return Vec3.Zero;

        // === 3. DIFFUSE ===
        float NdotL = float.Max(0f, Vec3.Dot(normal, L));
        Vec3 diffuse = material.DiffuseColor * (Color * Intensity * NdotL);

        // === 4. SPECULAR ===
        Vec3 H = (L + viewDir).Normalized();
        float HdotN = float.Max(0f, Vec3.Dot(H, normal));
        float specPower = float.Pow(HdotN, material.Glossiness);
        Vec3 specular = Color * (Intensity * material.Specular * specPower);

        return attenuation * spotIntensity * (diffuse + specular);
    }

    private float ComputeSpotIntensity(float theta)
    {
        // За пределами внешнего конуса — темнота
        if (theta < OuterCutoff)
            return 0f;
        
        // Внутри внутреннего конуса — полная яркость
        if (theta > InnerCutoff)
            return 1f;
        

        // intensity = (theta - outer) / (inner - outer)
        float epsilon = InnerCutoff - OuterCutoff;
        float intensity = (theta - OuterCutoff) / epsilon;
        

        if (Falloff != 1f)
            intensity = float.Pow(intensity, Falloff);
        
        return intensity;
    }

    public static SpotLight FromDegrees(
        Vec3 position, 
        Vec3 direction, 
        float innerAngleDegrees, 
        float outerAngleDegrees)
    {
        const float degToRad = 3.14159265f / 180f;
        return new SpotLight(
            position, 
            direction,
            innerAngleDegrees * degToRad,
            outerAngleDegrees * degToRad);
    }
}