using System;
using System.Runtime.CompilerServices;
using System.Threading;

public sealed class Rasterizer
{
    private readonly int    _width;
    private readonly int    _height;
    private readonly long[] _zColor; // ст z мл цв

    private static readonly long ZColorClear = ((long)BitConverter.SingleToInt32Bits(float.MaxValue) << 32) | 0u;

    public Rasterizer(int width, int height)
    {
        _width  = width;
        _height = height;
        _zColor = new long[width * height];
    }

    public void Clear() => Array.Fill(_zColor, ZColorClear);
   /* public void FlushToPixels(uint[] pixelBuffer)
    {
        int len = _width * _height;
        for (int i = 0; i < len; i++)
        {
            long val = _zColor[i];
            if (val != ZColorClear)
                pixelBuffer[i] = (uint)(val & 0xFFFF_FFFFu);
        }
    }*/
public void FlushToPixels(uint[] pixelBuffer)
{
    int len = _width * _height;
    for (int i = 0; i < len; i++)
    {
        long val = _zColor[i];
        pixelBuffer[i] = (val == ZColorClear) 
            ? 0xFF080818  // фон
            : (uint)(val & 0xFFFF_FFFFu);
    }
}
    /*  // сканир линия
    public void DrawTriangle(PixelSet pixels, Vec3 s0, Vec3 s1, Vec3 s2, uint color)
    {
        // s0 — верхняя вершина, s1 — средняя, s2 — нижняя
        if (s0.Y > s1.Y) (s0, s1) = (s1, s0);
        if (s0.Y > s2.Y) (s0, s2) = (s2, s0);
        if (s1.Y > s2.Y) (s1, s2) = (s2, s1);
        float totalH = s2.Y - s0.Y;
        if (totalH < 1e-3f) return;
        int yMin = Math.Max(0,           (int)MathF.Ceiling(s0.Y));
        int yMax = Math.Min(_height - 1, (int)MathF.Floor  (s2.Y));
        for (int y = yMin; y <= yMax; y++)
        {
            // по дл ребру
            float alpha = (y - s0.Y) / totalH;
            float xL = s0.X + (s2.X - s0.X) * alpha;
            float zL = s0.Z + (s2.Z - s0.Z) * alpha;

            // корот заканч
            bool  upper = y < s1.Y; // s0 s1        s1 s2
            float segH  = upper ? (s1.Y - s0.Y) : (s2.Y - s1.Y);
            float xS, zS;
            if (segH < 1e-3f) { xS = upper ? s1.X : s2.X; zS = upper ? s1.Z : s2.Z; }
            else
            {
                float beta = upper ? (y - s0.Y) / segH : (y - s1.Y) / segH;
                Vec3 vA = upper ? s0 : s1; Vec3 vB = upper ? s1 : s2;
                xS = vA.X + (vB.X - vA.X) * beta;
                zS = vA.Z + (vB.Z - vA.Z) * beta;
            }

            // xL — левая граница, xS — правая
            if (xL > xS) { (xL, xS) = (xS, xL); (zL, zS) = (zS, zL); }
            int   xMin = Math.Max(0,         (int)MathF.Ceiling(xL));
            int   xMax = Math.Min(_width - 1,(int)MathF.Floor  (xS));
            float span = xS - xL;
            for (int x = xMin; x <= xMax; x++)
            {
                float t = span > 1e-3f ? (x - xL) / span : 0f;
                float z = zL + (zS - zL) * t;
                TrySetPixel(y * _width + x, z, color);
            }
        }
    }
    */

    public void DrawTriangle(Vec3 s0, Vec3 s1, Vec3 s2, uint color)
    {
        float area = Edge(s0, s1, s2);
        if (float.Abs(area) < 1e-6f) return;
        if (area < 0f) { (s1, s2) = (s2, s1); area = -area; }
        float invArea = 1f / area;

        int minX = int.Max(0,           (int)float.Floor  (float.Min(s0.X, float.Min(s1.X, s2.X))));
        int maxX = int.Min(_width - 1,  (int)float.Ceiling(float.Max(s0.X, float.Max(s1.X, s2.X))));
        int minY = int.Max(0,           (int)float.Floor  (float.Min(s0.Y, float.Min(s1.Y, s2.Y))));
        int maxY = int.Min(_height - 1, (int)float.Ceiling(float.Max(s0.Y, float.Max(s1.Y, s2.Y))));

        bool tl0 = IsTopLeft(s1, s2), tl1 = IsTopLeft(s2, s0), tl2 = IsTopLeft(s0, s1);

        float dw0_dx = s1.Y - s2.Y, dw0_dy = s2.X - s1.X;
        float dw1_dx = s2.Y - s0.Y, dw1_dy = s0.X - s2.X;
        float dw2_dx = s0.Y - s1.Y, dw2_dy = s1.X - s0.X;

        float px0 = minX + 0.5f, py0 = minY + 0.5f;
        float w0_row = Edge(s1, s2, px0, py0);
        float w1_row = Edge(s2, s0, px0, py0);
        float w2_row = Edge(s0, s1, px0, py0);

        for (int y = minY; y <= maxY; y++)
        {
            float w0 = w0_row, w1 = w1_row, w2 = w2_row;
            for (int x = minX; x <= maxX; x++)
            {
               if ((w0 > 0f || (w0 == 0f && tl0)) && (w1 > 0f || (w1 == 0f && tl1)) && (w2 > 0f || (w2 == 0f && tl2)))
               // if (w0 >= -1e-5f && w1 >= -1e-5f && w2 >= -1e-5f) 
                {
                    float b0 = w0 * invArea, b1 = w1 * invArea, b2 = w2 * invArea;
                    float z  = b0 * s0.Z + b1 * s1.Z + b2 * s2.Z;
                    TrySetPixel(y * _width + x, z, color);
                }
                w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
            }
            w0_row += dw0_dy; w1_row += dw1_dy; w2_row += dw2_dy;
        }
    }

    //  Гуро — интерполяция цвета
    public void DrawTriangleGouraud(Vec3 s0, Vec3 s1, Vec3 s2,
                                    Vec3 c0, Vec3 c1, Vec3 c2)
    {
        float area = Edge(s0, s1, s2);
        if (float.Abs(area) < 1e-6f) return;
        if (area < 0f) { (s1, s2) = (s2, s1); (c1, c2) = (c2, c1); area = -area; }
        float invArea = 1f / area;

        int minX = Math.Max(0,           (int)float.Floor  (float.Min(s0.X, float.Min(s1.X, s2.X))));
        int maxX = Math.Min(_width - 1,  (int)float.Ceiling(float.Max(s0.X, float.Max(s1.X, s2.X))));
        int minY = Math.Max(0,           (int)float.Floor  (float.Min(s0.Y, float.Min(s1.Y, s2.Y))));
        int maxY = Math.Min(_height - 1, (int)float.Ceiling(float.Max(s0.Y, float.Max(s1.Y, s2.Y))));

        bool tl0 = IsTopLeft(s1, s2), tl1 = IsTopLeft(s2, s0), tl2 = IsTopLeft(s0, s1);

        float dw0_dx = s1.Y - s2.Y, dw0_dy = s2.X - s1.X;
        float dw1_dx = s2.Y - s0.Y, dw1_dy = s0.X - s2.X;
        float dw2_dx = s0.Y - s1.Y, dw2_dy = s1.X - s0.X;

        float px0 = minX + 0.5f, py0 = minY + 0.5f;
        float w0_row = Edge(s1, s2, px0, py0);
        float w1_row = Edge(s2, s0, px0, py0);
        float w2_row = Edge(s0, s1, px0, py0);

        for (int y = minY; y <= maxY; y++)
        {
            float w0 = w0_row, w1 = w1_row, w2 = w2_row;
            for (int x = minX; x <= maxX; x++)
            {
                if ((w0 > 0f || (w0 == 0f && tl0))
                 && (w1 > 0f || (w1 == 0f && tl1))
                 && (w2 > 0f || (w2 == 0f && tl2)))
                {
                    float b0 = w0 * invArea, b1 = w1 * invArea, b2 = w2 * invArea;
                    float z  = b0 * s0.Z + b1 * s1.Z + b2 * s2.Z;
                    Vec3 c = new(
                        b0 * c0.X + b1 * c1.X + b2 * c2.X,
                        b0 * c0.Y + b1 * c1.Y + b2 * c2.Y,
                        b0 * c0.Z + b1 * c1.Z + b2 * c2.Z);
                    TrySetPixel(y * _width + x, z, Vec3ToColor(c));
                }
                w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
            }
            w0_row += dw0_dy; w1_row += dw1_dy; w2_row += dw2_dy;
        }
    }

    // Фонг/Блинн-Фонг — интерполяция нормалей
    public void DrawTrianglePhong(
        Vec3 s0,  Vec3 s1,  Vec3 s2,
        Vec3 n0,  Vec3 n1,  Vec3 n2,
        Vec3 w0,  Vec3 w1,  Vec3 w2,
        Vec3 eye, Vec3 lightDir,
        LightSettings light)
    {
        float area = Edge(s0, s1, s2);
        if (float.Abs(area) < 1e-6f) return;
        if (area < 0f)
        {
            (s1, s2) = (s2, s1); (n1, n2) = (n2, n1); (w1, w2) = (w2, w1);
            area = -area;
        }
        float invArea = 1f / area;

        int minX = Math.Max(0,           (int)float.Floor  (float.Min(s0.X, float.Min(s1.X, s2.X))));
        int maxX = Math.Min(_width - 1,  (int)float.Ceiling(float.Max(s0.X, float.Max(s1.X, s2.X))));
        int minY = Math.Max(0,           (int)float.Floor  (float.Min(s0.Y, float.Min(s1.Y, s2.Y))));
        int maxY = Math.Min(_height - 1, (int)float.Ceiling(float.Max(s0.Y, float.Max(s1.Y, s2.Y))));

        bool tl0 = IsTopLeft(s1, s2), tl1 = IsTopLeft(s2, s0), tl2 = IsTopLeft(s0, s1);

        float dw0_dx = s1.Y - s2.Y, dw0_dy = s2.X - s1.X;
        float dw1_dx = s2.Y - s0.Y, dw1_dy = s0.X - s2.X;
        float dw2_dx = s0.Y - s1.Y, dw2_dy = s1.X - s0.X;

        float px0 = minX + 0.5f, py0 = minY + 0.5f;
        float w0_row = Edge(s1, s2, px0, py0);
        float w1_row = Edge(s2, s0, px0, py0);
        float w2_row = Edge(s0, s1, px0, py0);

        Vec3  objColor   = ColorToVec3(light.ObjectColor);
        Vec3  ambient    = objColor * light.AmbientColor;
        float glossiness = light.Glossiness;
        ShadingMode mode = light.Mode;

        for (int y = minY; y <= maxY; y++)
        {
            float ew0 = w0_row, ew1 = w1_row, ew2 = w2_row;
            for (int x = minX; x <= maxX; x++)
            {
                if ((ew0 > 0f || (ew0 == 0f && tl0))
                 && (ew1 > 0f || (ew1 == 0f && tl1))
                 && (ew2 > 0f || (ew2 == 0f && tl2)))
                {
                    float b0 = ew0 * invArea, b1 = ew1 * invArea, b2 = ew2 * invArea;
                    float z  = b0 * s0.Z + b1 * s1.Z + b2 * s2.Z;


                    Vec3 N = new Vec3(
                        b0 * n0.X + b1 * n1.X + b2 * n2.X,
                        b0 * n0.Y + b1 * n1.Y + b2 * n2.Y,
                        b0 * n0.Z + b1 * n1.Z + b2 * n2.Z).Normalized();

                    Vec3 wPos = new Vec3(
                        b0 * w0.X + b1 * w1.X + b2 * w2.X,
                        b0 * w0.Y + b1 * w1.Y + b2 * w2.Y,
                        b0 * w0.Z + b1 * w1.Z + b2 * w2.Z);

                    Vec3 V = (eye - wPos).Normalized();
                    Vec3 H = (lightDir + V).Normalized();

                    Vec3 color;
                    switch (mode)
                    {
                        case ShadingMode.Ambient:
                            color = ambient;
                            break;
                        case ShadingMode.Diffuse:
                        {
                            float diff = float.Max(0f, Vec3.Dot(N, lightDir));
                            color = light.Color * objColor * diff;
                            break;
                        }
                        case ShadingMode.Specular:
                        {
                            float spec = float.Pow(float.Max(0f, Vec3.Dot(H, N)), glossiness);
                            color = light.Color * spec;
                            break;
                        }
                        default: // PhongBlinn
                        {
                            float diff    = float.Max(0f, Vec3.Dot(N, lightDir));
                            Vec3 diffuse  = light.Color * objColor * diff;
                            float spec    = float.Pow(float.Max(0f, Vec3.Dot(H, N)), glossiness);
                            Vec3 specular = light.Color * spec;
                           
                            color = ambient + diffuse + specular;
                            break;
                        }
                    }

                    TrySetPixel(y * _width + x, z, Vec3ToColor(color));
                }
                ew0 += dw0_dx; ew1 += dw1_dx; ew2 += dw2_dx;
            }
            w0_row += dw0_dy; w1_row += dw1_dy; w2_row += dw2_dy;
        }
    }

    private void TrySetPixel(int idx, float zNew, uint color)
    {
       int  newZBits = BitConverter.SingleToInt32Bits(zNew);
        long newVal   = ((long)newZBits << 32) | color;

        while (true)
        {
           // Console.WriteLine($"Z = {zNew:F6}");
            long  oldVal = _zColor[idx];

            int oldZBits = (int)(oldVal >> 32);
            float oldZ = BitConverter.Int32BitsToSingle(oldZBits);

            if (zNew >= oldZ) return;
            if (Interlocked.CompareExchange(ref _zColor[idx], newVal, oldVal) == oldVal)
                return;
        }

    }

    private static float Edge(Vec3 a, Vec3 b, float px, float py)
        => (b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X);

    private static float Edge(Vec3 a, Vec3 b, Vec3 c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool IsTopLeft(Vec3 a, Vec3 b)
    {
        float dy = b.Y - a.Y, dx = b.X - a.X;
        return dy < 0f || (dy == 0f && dx > 0f);
    }


    public static uint Shade(uint objColor, Vec3 lightColor, float lambert)
    {
        float r = ((objColor >> 16) & 0xFF) * lightColor.X * lambert;
        float g = ((objColor >>  8) & 0xFF) * lightColor.Y * lambert;
        float b = ((objColor >>  0) & 0xFF) * lightColor.Z * lambert;
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

    public static uint Vec3ToColor(Vec3 c)
    {
        int r = int.Clamp((int)(c.X * 255f), 0, 255);
        int g = int.Clamp((int)(c.Y * 255f), 0, 255);
        int b = int.Clamp((int)(c.Z * 255f), 0, 255);
        return (0xFFu << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
    }
}