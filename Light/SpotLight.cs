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
        Vec3 toLight = Position - fragmentPos;
        float distance = toLight.Length;
        Vec3 L = distance > 1e-6f 
            ? toLight / distance 
            : new Vec3(0f, 1f, 0f);

        float attenuation = 1f / (distance * distance + 0.01f);
        
        float theta = Vec3.Dot(Direction, -L);
        float spotIntensity = ComputeSpotIntensity(theta);
        
        if (spotIntensity <= 0f)
        {
           return material.DiffuseColor * ambientColor;  
        }
        

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
        return ambient + (litPart * attenuation * spotIntensity);
    }

    private float ComputeSpotIntensity(float theta)
    {
        if (theta < OuterCutoff) return 0f;
        if (theta > InnerCutoff) return 1f;
        
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