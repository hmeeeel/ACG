using System;
using System.Runtime.CompilerServices;
using System.Threading;

public sealed class Rasterizer
{
    private readonly int _width;
    private readonly int _height;
    private readonly long[] _zColor;

    private static readonly long ZColorClear =
        ((long)BitConverter.SingleToInt32Bits(float.MaxValue) << 32) | 0u;

    public Rasterizer(int width, int height)
    {
        _width  = width;
        _height = height;
        _zColor = new long[width * height];
    }

    public void Clear() => Array.Fill(_zColor, ZColorClear);

    public void FlushToPixels(uint[] pixelBuffer)
    {
        int len = _width * _height;
        for (int i = 0; i < len; i++)
        {
            long val = _zColor[i];
            pixelBuffer[i] = (val == ZColorClear)
                ? 0xFF080818u
                : (uint)(val & 0xFFFF_FFFFu);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Общая структура Setup — вычисляет AABB, edge-инкременты, 
    //  стартовые веса. Возвращает false если треугольник вырожден.
    // ─────────────────────────────────────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool SetupTriangle(
        ref Vec3 s0, ref Vec3 s1, ref Vec3 s2,
        out int minX, out int maxX, out int minY, out int maxY,
        out float invArea,
        out bool tl0, out bool tl1, out bool tl2,
        out float dw0_dx, out float dw0_dy,
        out float dw1_dx, out float dw1_dy,
        out float dw2_dx, out float dw2_dy,
        out float w0_row,  out float w1_row,  out float w2_row)
    {
        float area = Edge(s0, s1, s2);
        if (float.Abs(area) < 1e-6f)
        {
            // out-параметры должны быть инициализированы
            minX = maxX = minY = maxY = 0;
            invArea = 0f;
            tl0 = tl1 = tl2 = false;
            dw0_dx = dw0_dy = dw1_dx = dw1_dy = dw2_dx = dw2_dy = 0f;
            w0_row = w1_row = w2_row = 0f;
            return false;
        }
        if (area < 0f) { (s1, s2) = (s2, s1); area = -area; }

        invArea = 1f / area;

        minX = int.Max(0,          (int)float.Floor  (float.Min(s0.X, float.Min(s1.X, s2.X))));
        maxX = int.Min(_width - 1, (int)float.Ceiling(float.Max(s0.X, float.Max(s1.X, s2.X))));
        minY = int.Max(0,          (int)float.Floor  (float.Min(s0.Y, float.Min(s1.Y, s2.Y))));
        maxY = int.Min(_height - 1,(int)float.Ceiling(float.Max(s0.Y, float.Max(s1.Y, s2.Y))));

        tl0 = IsTopLeft(s1, s2);
        tl1 = IsTopLeft(s2, s0);
        tl2 = IsTopLeft(s0, s1);

        dw0_dx = s1.Y - s2.Y;  dw0_dy = s2.X - s1.X;
        dw1_dx = s2.Y - s0.Y;  dw1_dy = s0.X - s2.X;
        dw2_dx = s0.Y - s1.Y;  dw2_dy = s1.X - s0.X;

        float px0 = minX + 0.5f, py0 = minY + 0.5f;
        w0_row = Edge(s1, s2, px0, py0);
        w1_row = Edge(s2, s0, px0, py0);
        w2_row = Edge(s0, s1, px0, py0);

        return true;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Flat shading — цвет на весь треугольник
    // ─────────────────────────────────────────────────────────────────
    public void DrawTriangle(Vec3 s0, Vec3 s1, Vec3 s2, uint color)
    {
        if (!SetupTriangle(
                ref s0, ref s1, ref s2,
                out int minX, out int maxX, out int minY, out int maxY,
                out float invArea,
                out bool tl0, out bool tl1, out bool tl2,
                out float dw0_dx, out float dw0_dy,
                out float dw1_dx, out float dw1_dy,
                out float dw2_dx, out float dw2_dy,
                out float w0_row, out float w1_row, out float w2_row))
            return;

        for (int y = minY; y <= maxY; y++)
        {
            float w0 = w0_row, w1 = w1_row, w2 = w2_row;
            int   rowBase = y * _width;
            for (int x = minX; x <= maxX; x++)
            {
                if (InsideTriangle(w0, w1, w2, tl0, tl1, tl2))
                {
                    float b0 = w0 * invArea, b1 = w1 * invArea, b2 = w2 * invArea;
                    float z  = b0 * s0.Z + b1 * s1.Z + b2 * s2.Z;
                    TrySetPixel(rowBase + x, z, color);
                }
                w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
            }
            w0_row += dw0_dy; w1_row += dw1_dy; w2_row += dw2_dy;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Gouraud — интерполяция цвета по вершинам
    // ─────────────────────────────────────────────────────────────────
    public void DrawTriangleGouraud(
        Vec3 s0, Vec3 s1, Vec3 s2,
        Vec3 c0, Vec3 c1, Vec3 c2)
    {
        // При смене winding нужно поменять и атрибуты
        float area = Edge(s0, s1, s2);
        if (float.Abs(area) < 1e-6f) return;
        if (area < 0f) { (s1, s2) = (s2, s1); (c1, c2) = (c2, c1); area = -area; }

        // Переиспользуем setup без ref-swap атрибутов
        if (!SetupTriangle(
                ref s0, ref s1, ref s2,
                out int minX, out int maxX, out int minY, out int maxY,
                out float invArea,
                out bool tl0, out bool tl1, out bool tl2,
                out float dw0_dx, out float dw0_dy,
                out float dw1_dx, out float dw1_dy,
                out float dw2_dx, out float dw2_dy,
                out float w0_row, out float w1_row, out float w2_row))
            return;

        for (int y = minY; y <= maxY; y++)
        {
            float w0 = w0_row, w1 = w1_row, w2 = w2_row;
            int   rowBase = y * _width;
            for (int x = minX; x <= maxX; x++)
            {
                if (InsideTriangle(w0, w1, w2, tl0, tl1, tl2))
                {
                    float b0 = w0 * invArea, b1 = w1 * invArea, b2 = w2 * invArea;
                    float z  = b0 * s0.Z + b1 * s1.Z + b2 * s2.Z;

                    // Z-тест ДО вычисления цвета
                    if (!ZTest(rowBase + x, z)) goto Next;

                    float r = b0 * c0.X + b1 * c1.X + b2 * c2.X;
                    float g = b0 * c0.Y + b1 * c1.Y + b2 * c2.Y;
                    float b = b0 * c0.Z + b1 * c1.Z + b2 * c2.Z;
                    SetPixelAfterZTest(rowBase + x, z, Vec3ToColor(r, g, b));
                }
                Next:
                w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
            }
            w0_row += dw0_dy; w1_row += dw1_dy; w2_row += dw2_dy;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Phong / Blinn-Phong — интерполяция нормалей по пикселям
    //  Blinn-Phong: specular = pow(dot(H, N), gloss),  H = norm(V - L)
    //  (L здесь — вектор ОТ фрагмента К источнику, т.е. -lightDir)
    // ─────────────────────────────────────────────────────────────────
    public void DrawTrianglePhong(
        Vec3 s0,  Vec3 s1,  Vec3 s2,   // screen-space позиции
        Vec3 n0,  Vec3 n1,  Vec3 n2,   // vertex normals (world)
        Vec3 wp0, Vec3 wp1, Vec3 wp2,  // vertex positions (world)
        Vec3 eye, Vec3 lightDir,       // lightDir = нормализованный вектор К источнику
        LightSettings light)
    {
        float area = Edge(s0, s1, s2);
        if (float.Abs(area) < 1e-6f) return;
        if (area < 0f)
        {
            (s1, s2)   = (s2, s1);
            (n1, n2)   = (n2, n1);
            (wp1, wp2) = (wp2, wp1);
            area = -area;
        }

        if (!SetupTriangle(
                ref s0, ref s1, ref s2,
                out int minX, out int maxX, out int minY, out int maxY,
                out float invArea,
                out bool tl0, out bool tl1, out bool tl2,
                out float dw0_dx, out float dw0_dy,
                out float dw1_dx, out float dw1_dy,
                out float dw2_dx, out float dw2_dy,
                out float w0_row, out float w1_row, out float w2_row))
            return;

        // Константы вне цикла
        Vec3  objColor   = ColorToVec3(light.ObjectColor);
        float oR = objColor.X, oG = objColor.Y, oB = objColor.Z;
        float lR = light.Color.X, lG = light.Color.Y, lB = light.Color.Z;
        float aR = light.AmbientColor.X, aG = light.AmbientColor.Y, aB = light.AmbientColor.Z;
        float gloss = light.Glossiness;
        ShadingMode mode = light.Mode;

        // ambient = objColor * ambientColor  (компонентно)
        float ambR = oR * aR, ambG = oG * aG, ambB = oB * aB;

        // lightDir = вектор К источнику (нормализован)
        float lDirX = lightDir.X, lDirY = lightDir.Y, lDirZ = lightDir.Z;

        for (int y = minY; y <= maxY; y++)
        {
            float w0 = w0_row, w1 = w1_row, w2 = w2_row;
            int   rowBase = y * _width;
            for (int x = minX; x <= maxX; x++)
            {
                if (InsideTriangle(w0, w1, w2, tl0, tl1, tl2))
                {
                    float b0 = w0 * invArea, b1 = w1 * invArea, b2 = w2 * invArea;
                    float z  = b0 * s0.Z + b1 * s1.Z + b2 * s2.Z;

                    // ── Z-ТЕСТ ДО освещения ──────────────────────────
                    if (!ZTest(rowBase + x, z)) goto Next;

                    // ── Интерполяция нормали (без new Vec3) ──────────
                    float nx = b0 * n0.X + b1 * n1.X + b2 * n2.X;
                    float ny = b0 * n0.Y + b1 * n1.Y + b2 * n2.Y;
                    float nz = b0 * n0.Z + b1 * n1.Z + b2 * n2.Z;
                    // Нормализация
                    float nLen = float.Sqrt(nx*nx + ny*ny + nz*nz);
                    if (nLen > 1e-7f) { float inv = 1f/nLen; nx *= inv; ny *= inv; nz *= inv; }

                    // ── Интерполяция мировой позиции ─────────────────
                    float px = b0 * wp0.X + b1 * wp1.X + b2 * wp2.X;
                    float py = b0 * wp0.Y + b1 * wp1.Y + b2 * wp2.Y;
                    float pz = b0 * wp0.Z + b1 * wp1.Z + b2 * wp2.Z;

                    // ── V = normalize(eye - fragPos) ─────────────────
                    float vx = eye.X - px, vy = eye.Y - py, vz = eye.Z - pz;
                    float vLen = float.Sqrt(vx*vx + vy*vy + vz*vz);
                    if (vLen > 1e-7f) { float inv = 1f/vLen; vx *= inv; vy *= inv; vz *= inv; }

                    float cR, cG, cB;

                    switch (mode)
                    {
                        case ShadingMode.Ambient:
                            cR = ambR; cG = ambG; cB = ambB;
                            break;

                        case ShadingMode.Diffuse:
                        {
                            // dot(N, L)  — L = lightDir (к источнику)
                            float diff = float.Max(0f, nx*lDirX + ny*lDirY + nz*lDirZ);
                            cR = lR * oR * diff;
                            cG = lG * oG * diff;
                            cB = lB * oB * diff;
                            break;
                        }

                        case ShadingMode.Specular:
                        {
                            // Blinn-Phong: H = normalize(L + V)
                            float hx = lDirX + vx, hy = lDirY + vy, hz = lDirZ + vz;
                            float hLen = float.Sqrt(hx*hx + hy*hy + hz*hz);
                            if (hLen > 1e-7f) { float inv = 1f/hLen; hx *= inv; hy *= inv; hz *= inv; }
                            float spec = float.Pow(float.Max(0f, nx*hx + ny*hy + nz*hz), gloss);
                            cR = lR * spec; cG = lG * spec; cB = lB * spec;
                            break;
                        }

                        default: // PhongBlinn (ambient + diffuse + specular)
                        {
                            float diff = float.Max(0f, nx*lDirX + ny*lDirY + nz*lDirZ);
                            float dR   = lR * oR * diff;
                            float dG   = lG * oG * diff;
                            float dB   = lB * oB * diff;

                            // H = normalize(L + V)
                            float hx = lDirX + vx, hy = lDirY + vy, hz = lDirZ + vz;
                            float hLen = float.Sqrt(hx*hx + hy*hy + hz*hz);
                            if (hLen > 1e-7f) { float inv = 1f/hLen; hx *= inv; hy *= inv; hz *= inv; }
                            float spec = float.Pow(float.Max(0f, nx*hx + ny*hy + nz*hz), gloss);
                            float sR   = lR * spec, sG = lG * spec, sB = lB * spec;

                            cR = ambR + dR + sR;
                            cG = ambG + dG + sG;
                            cB = ambB + dB + sB;
                            break;
                        }
                    }

                    SetPixelAfterZTest(rowBase + x, z, Vec3ToColor(cR, cG, cB));
                }
                Next:
                w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
            }
            w0_row += dw0_dy; w1_row += dw1_dy; w2_row += dw2_dy;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Z-тест отдельно от записи (для early-out)
    // ─────────────────────────────────────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ZTest(int idx, float zNew)
    {
        long  oldVal  = Volatile.Read(ref _zColor[idx]);
        int   oldZB   = (int)(oldVal >> 32);
        float oldZ    = BitConverter.Int32BitsToSingle(oldZB);
        return zNew < oldZ;   // true = пиксель прошёл тест
    }

    // Запись после того, как ZTest вернул true
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetPixelAfterZTest(int idx, float zNew, uint color)
    {
        int  newZBits = BitConverter.SingleToInt32Bits(zNew);
        long newVal   = ((long)newZBits << 32) | color;
        while (true)
        {
            long oldVal  = _zColor[idx];
            int  oldZB   = (int)(oldVal >> 32);
            float oldZ   = BitConverter.Int32BitsToSingle(oldZB);
            if (zNew >= oldZ) return;
            if (Interlocked.CompareExchange(ref _zColor[idx], newVal, oldVal) == oldVal)
                return;
        }
    }

    // Оригинальный TrySetPixel — используется в DrawTriangle (flat)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TrySetPixel(int idx, float zNew, uint color)
    {
        int  newZBits = BitConverter.SingleToInt32Bits(zNew);
        long newVal   = ((long)newZBits << 32) | color;
        while (true)
        {
            long  oldVal  = _zColor[idx];
            int   oldZB   = (int)(oldVal >> 32);
            float oldZ    = BitConverter.Int32BitsToSingle(oldZB);
            if (zNew >= oldZ) return;
            if (Interlocked.CompareExchange(ref _zColor[idx], newVal, oldVal) == oldVal)
                return;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Вспомогательные
    // ─────────────────────────────────────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool InsideTriangle(float w0, float w1, float w2,
                                       bool tl0, bool tl1, bool tl2)
        => (w0 > 0f || (w0 == 0f && tl0))
        && (w1 > 0f || (w1 == 0f && tl1))
        && (w2 > 0f || (w2 == 0f && tl2));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Edge(Vec3 a, Vec3 b, float px, float py)
        => (b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Edge(Vec3 a, Vec3 b, Vec3 c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTopLeft(Vec3 a, Vec3 b)
    {
        float dy = b.Y - a.Y, dx = b.X - a.X;
        return dy < 0f || (dy == 0f && dx > 0f);
    }

    public static uint Shade(uint objColor, Vec3 lightColor, float lambert)
    {
        float r = ((objColor >> 16) & 0xFF) * lightColor.X * lambert;
        float g = ((objColor >>  8) & 0xFF) * lightColor.Y * lambert;
        float b = ( objColor        & 0xFF) * lightColor.Z * lambert;
        uint  a = (objColor >> 24) & 0xFF;
        return (a << 24)
            | (Math.Clamp((uint)r, 0, 255) << 16)
            | (Math.Clamp((uint)g, 0, 255) <<  8)
            |  Math.Clamp((uint)b, 0, 255);
    }

    public static Vec3 ColorToVec3(uint c) => new(
        ((c >> 16) & 0xFF) / 255f,
        ((c >>  8) & 0xFF) / 255f,
        ( c        & 0xFF) / 255f);

    // Без new Vec3 — сразу в uint
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Vec3ToColor(float r, float g, float b)
    {
        int ri = int.Clamp((int)(r * 255f), 0, 255);
        int gi = int.Clamp((int)(g * 255f), 0, 255);
        int bi = int.Clamp((int)(b * 255f), 0, 255);
        return (0xFFu << 24) | ((uint)ri << 16) | ((uint)gi << 8) | (uint)bi;
    }

    public static uint Vec3ToColor(Vec3 c) => Vec3ToColor(c.X, c.Y, c.Z);
}