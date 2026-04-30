
public enum LightType
{
    Directional,
    Point,
    Spot
}


public abstract class LightSource
{
    public LightType Type { get; protected set; }
    public Vec3 Color { get; set; } = new Vec3(1f, 1f, 1f);
    public float Intensity { get; set; } = 1f;

    public abstract Vec3 ComputeLighting(
        Vec3 fragmentPos,
        Vec3 normal,
        Vec3 viewDir,
        Material material,
        ShadingMode mode,
        Vec3 ambientColor);
    
    public abstract Vec3 GetLightDirection(Vec3 fragmentPos);
}

public struct Material
{
    public Vec3 DiffuseColor;
    public float Specular;
    public float Glossiness;
    
    public Material(Vec3 diffuse, float specular = 1f, float glossiness = 64f)
    {
        DiffuseColor = diffuse;
        Specular = specular;
        Glossiness = glossiness;
    }
}