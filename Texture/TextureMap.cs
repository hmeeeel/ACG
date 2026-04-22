using System;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

public sealed class TextureMap
{
    private readonly uint[] _pixels;
    private readonly int    _width;
    private readonly int    _height;
    private readonly int    _widthMask;
    private readonly int    _heightMask;
    private readonly float  _widthFloat;
    private readonly float  _heightFloat;

    public int Width  => _width;
    public int Height => _height;

    private TextureMap(uint[] pixels, int w, int h)
    {
        _pixels      = pixels;
        _width       = w;
        _height      = h;
        _widthFloat  = w;
        _heightFloat = h;
        
        _widthMask  = IsPowerOfTwo(w) ? w - 1 : -1;
        _heightMask = IsPowerOfTwo(h) ? h - 1 : -1;
    }

    public static TextureMap? Load(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            using var bmp = new Bitmap(path);
            int w = bmp.PixelSize.Width;
            int h = bmp.PixelSize.Height;
            var pixels = new uint[w * h];

            var wb = new WriteableBitmap(
                bmp.PixelSize,
                bmp.Dpi,
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            using (var src = wb.Lock())
            {
                bmp.CopyPixels(
                    new Avalonia.PixelRect(0, 0, w, h),
                    src.Address,
                    src.RowBytes * h,
                    src.RowBytes);
            }

            unsafe
            {
                using var lk = wb.Lock();
                uint* ptr = (uint*)lk.Address;
                for (int i = 0; i < w * h; i++)
                    pixels[i] = ptr[i];
            }

            return new TextureMap(pixels, w, h);
        }
        catch { return null; }
    }

    /// Билинейная интерполяция текстур — смешивает цвета 4 соседних текселей.
    /// Магнификация — близко приблизили, пиксель накрывает меньше одного текселя.
    /// Минификация — далеко, один пиксель накрывает много текселей (муаровые узоры без фильтрации)."
    public Vec3 SampleBilinear(float u, float v)
    {
        // V-flip: в OBJ v=0 — низ текстуры, в растре строка 0 — верх
        float vFlipped = 1f - v;

        // Тексельные координаты (центр текселя = +0.5)
        float fx = u        * _widthFloat  - 0.5f;
        float fy = vFlipped * _heightFloat - 0.5f;

        int x0 = (int)MathF.Floor(fx);
        int y0 = (int)MathF.Floor(fy);
        
        // Дробные части для интерполяции
        float tx = fx - x0;
        float ty = fy - y0;

        int x1 = x0 + 1;
        int y1 = y0 + 1;

        // Wrap (repeat) — оптимизированный для POT текстур
        x0 = WrapCoord(x0, _width,  _widthMask);
        x1 = WrapCoord(x1, _width,  _widthMask);
        y0 = WrapCoord(y0, _height, _heightMask);
        y1 = WrapCoord(y1, _height, _heightMask);

        // Выборка 4 текселей
        uint c00 = _pixels[y0 * _width + x0];
        uint c10 = _pixels[y0 * _width + x1];
        uint c01 = _pixels[y1 * _width + x0];
        uint c11 = _pixels[y1 * _width + x1];

        // Билинейное смешение: сначала по X, потом по Y
        return BilinearBlend(c00, c10, c01, c11, tx, ty);
    }

    /// Выборка grayscale (для specular map).
    /// Используем стандартную формулу luminance: 0.299R + 0.587G + 0.114B
    public float SampleGrayscale(float u, float v)
    {
        Vec3 c = SampleBilinear(u, v);
        return 0.299f * c.X + 0.587f * c.Y + 0.114f * c.Z;
    }


    private static int WrapCoord(int x, int size, int mask)
    {
        if (mask >= 0) // power-of-2: битовая маска
            return x & mask;
        
        // non-POT: модуль
        x %= size;
        return x < 0 ? x + size : x;
    }

    private static bool IsPowerOfTwo(int x) => x > 0 && (x & (x - 1)) == 0;

    /// Распаковка BGRA и билинейное смешение за один проход.
    /// ОПТИМИЗАЦИЯ: избегаем промежуточных Vec3, работаем с float напрямую.
    private static Vec3 BilinearBlend(uint c00, uint c10, uint c01, uint c11, float tx, float ty)
    {
        const float inv255 = 1f / 255f;
        
        float r00 = ((c00 >> 16) & 0xFF) * inv255;
        float g00 = ((c00 >>  8) & 0xFF) * inv255;
        float b00 = ( c00        & 0xFF) * inv255;

        float r10 = ((c10 >> 16) & 0xFF) * inv255;
        float g10 = ((c10 >>  8) & 0xFF) * inv255;
        float b10 = ( c10        & 0xFF) * inv255;

        float r01 = ((c01 >> 16) & 0xFF) * inv255;
        float g01 = ((c01 >>  8) & 0xFF) * inv255;
        float b01 = ( c01        & 0xFF) * inv255;

        float r11 = ((c11 >> 16) & 0xFF) * inv255;
        float g11 = ((c11 >>  8) & 0xFF) * inv255;
        float b11 = ( c11        & 0xFF) * inv255;

        // Билинейное смешение: lerp(lerp(c00, c10, tx), lerp(c01, c11, tx), ty)
        float r = Lerp(Lerp(r00, r10, tx), Lerp(r01, r11, tx), ty);
        float g = Lerp(Lerp(g00, g10, tx), Lerp(g01, g11, tx), ty);
        float b = Lerp(Lerp(b00, b10, tx), Lerp(b01, b11, tx), ty);

        return new Vec3(r, g, b);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}