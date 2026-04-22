using System.Runtime.CompilerServices;

/// Атрибуты вершины, требующие перспективной коррекции.
/// 
/// КРИТИЧЕСКИ ВАЖНО (из методички):
/// "Интерполирование в экранных координатах не соответствует интерполированию в трехмерных.
/// Необходимо выполнять интерполяцию с коррекцией глубины для:
/// - мировых координат
/// - нормалей
/// - текстурных координат
/// - цвета
/// 
/// НЕ нужно интерполировать с коррекцией:
/// - экранные координаты
/// - z для z-буфера"
public readonly struct VertexAttributes
{
    // 1/w — сохранённое значение clip.W перед перспективным делением
    public readonly float InvW;
    
    // UV-координаты (требуют перспективной коррекции)
    public readonly Vec2 UV;
    
    // Нормаль в object/world space (требует перспективной коррекции)
    public readonly Vec3 Normal;
    
    // Мировая позиция вершины (требует перспективной коррекции)
    public readonly Vec3 WorldPos;

    public VertexAttributes(float invW, Vec2 uv, Vec3 normal, Vec3 worldPos)
    {
        InvW     = invW;
        UV       = uv;
        Normal   = normal;
        WorldPos = worldPos;
    }

    // Перспективно-корректная интерполяция атрибутов.
    public static void InterpolatePerspectiveCorrect(
        float b0, float b1, float b2,
        in VertexAttributes v0,
        in VertexAttributes v1,
        in VertexAttributes v2,
        out float invW,
        out float wCorr,
        out Vec2 uv,
        out Vec3 normal,
        out Vec3 worldPos)
    {
        // 1. Интерполируем 1/w линейно
        invW = b0 * v0.InvW + b1 * v1.InvW + b2 * v2.InvW;
        
        // 2. Находим корректирующий множитель w
        wCorr = invW > 1e-7f ? 1f / invW : 0f;
        
        // 3. Перспективно-корректные барицентрические координаты
        float pc0 = b0 * v0.InvW * wCorr;
        float pc1 = b1 * v1.InvW * wCorr;
        float pc2 = b2 * v2.InvW * wCorr;
        
        // 4. Интерполируем атрибуты с коррекцией
        uv = new Vec2(
            pc0 * v0.UV.U + pc1 * v1.UV.U + pc2 * v2.UV.U,
            pc0 * v0.UV.V + pc1 * v1.UV.V + pc2 * v2.UV.V
        );
        
        normal = new Vec3(
            pc0 * v0.Normal.X + pc1 * v1.Normal.X + pc2 * v2.Normal.X,
            pc0 * v0.Normal.Y + pc1 * v1.Normal.Y + pc2 * v2.Normal.Y,
            pc0 * v0.Normal.Z + pc1 * v1.Normal.Z + pc2 * v2.Normal.Z
        );
        
        worldPos = new Vec3(
            pc0 * v0.WorldPos.X + pc1 * v1.WorldPos.X + pc2 * v2.WorldPos.X,
            pc0 * v0.WorldPos.Y + pc1 * v1.WorldPos.Y + pc2 * v2.WorldPos.Y,
            pc0 * v0.WorldPos.Z + pc1 * v1.WorldPos.Z + pc2 * v2.WorldPos.Z
        );
    }
}