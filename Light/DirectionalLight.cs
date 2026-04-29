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
        Material material)
    {
        Vec3 L = -Direction;

        // === DIFFUSE (Lambertian) ===
        // Id = kd * max(0, N·L) * lightColor
        float NdotL = float.Max(0f, Vec3.Dot(normal, L));
        Vec3 diffuse = material.DiffuseColor * (Color * Intensity * NdotL);

        // === SPECULAR (Blinn-Phong) ===
        // H = normalize(L + V)
        // Is = ks * pow(max(0, H·N), glossiness) * lightColor
        Vec3 H = (L + viewDir).Normalized();
        float HdotN = float.Max(0f, Vec3.Dot(H, normal));
        float specPower = float.Pow(HdotN, material.Glossiness);
        Vec3 specular = Color * (Intensity * material.Specular * specPower);

        return diffuse + specular;
    }
}