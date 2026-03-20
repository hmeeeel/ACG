public struct LightSettings
{
    public Vec3 Direction;    // нормализованный вектор НА источник 1,к из 2,1
    public Vec3 Color;        // цвет источника: (1,1,1) = белый
    public uint ObjectColor;  // базовый цвет модели 0xAARRGGBB

    public static LightSettings Default => new()
    {
        Direction   = new Vec3(0.577f, 0.816f, 0.577f),
        Color       = new Vec3(1f, 1f, 1f),
        ObjectColor = 0xFFB4A090
    };
}