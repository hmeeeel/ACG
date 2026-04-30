public interface ILightingModel
{
    Vec3 ComputeLighting(
        Vec3 N, Vec3 L, Vec3 V,
        Vec3 lightColor,
        Material material,
        ShadingMode mode,
        Vec3 ambientColor);
}