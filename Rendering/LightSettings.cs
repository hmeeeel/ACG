public struct LightSettings
{
    public Vec3        Direction;    // нормализованный вектор НА источник 1,к из 2,1
    public Vec3        Color;        // цвет источника: (1,1,1) = белый
    public Vec3        AmbientColor; // цвет фонового освещения
    public uint        ObjectColor;  // базовый цвет модели 0xAARRGGBB  ≈ (0.8, 0.2, 0.6)
    public float       Glossiness;   // коэффициент Фонга/Блинна
    public ShadingMode Mode;

    public static LightSettings Default => new()
    {
        Direction    = new Vec3(-1f, 1f, -1f), // new Vec3(0.577f, 0.816f, 0.577f),
        Color        = new Vec3(1f,  1f,  1f), // new Vec3(1f, 1f, 1f),
        AmbientColor = new Vec3(0.2f, 0.2f, 0.2f),
        ObjectColor  = 0xFF00FF00, // 0xFFCC3399
        Glossiness   = 64f,
        Mode         = ShadingMode.Lambert
    };
}