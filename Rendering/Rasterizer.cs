using System;
using System.Runtime.CompilerServices;
using System.Threading;

public sealed class Rasterizer
{
    private readonly int   _width;
    private readonly int   _height;
    private readonly int[] _zBits;

    private static readonly int ZMaxBits = BitConverter.SingleToInt32Bits(float.MaxValue);

    public Rasterizer(int width, int height)
    {
        _width  = width;
        _height = height;
        _zBits  = new int[width * height];
    }

    public void Clear() => Array.Fill(_zBits, ZMaxBits);

    // сканир линия
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
            if (segH < 1e-3f)
            {
                xS = upper ? s1.X : s2.X;
                zS = upper ? s1.Z : s2.Z;
            }
            else
            {
                float beta = upper ? (y - s0.Y) / segH : (y - s1.Y) / segH;
                Vec3  vA   = upper ? s0 : s1;
                Vec3  vB   = upper ? s1 : s2;
                xS = vA.X + (vB.X - vA.X) * beta;
                zS = vA.Z + (vB.Z - vA.Z) * beta;
            }

            // xL — левая граница, xS — правая
            if (xL > xS) { (xL, xS) = (xS, xL); (zL, zS) = (zS, zL); }

            int   xMin  = Math.Max(0,         (int)MathF.Ceiling(xL));
            int   xMax  = Math.Min(_width - 1,(int)MathF.Floor  (xS));
            float span  = xS - xL; //190

            for (int x = xMin; x <= xMax; x++)
            {
                float t = span > 1e-3f ? (x - xL) / span : 0f; // 0 - xL 1 - xS
                float z = zL + (zS - zL) * t;   // ближе к камере = меньше z

                if (TryUpdateZ(y * _width + x, z))
                    pixels.SetPixelUnchecked(x, y, color);
            }
        }
    }

    // z_new < z_old = фрагмент ближе = обновляем и разрешаем рисовать.
    private bool TryUpdateZ(int idx, float zNew)
    {
        int newBits = BitConverter.SingleToInt32Bits(zNew);
        while (true)
        {
            int   oldBits = Volatile.Read(ref _zBits[idx]);
            float oldZ    = BitConverter.Int32BitsToSingle(oldBits);
            // zNew >= _zBuffer[idx]
            if (zNew >= oldZ) return false;
            if (Interlocked.CompareExchange(ref _zBits[idx], newBits, oldBits) == oldBits)
                return true;
        }
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
}