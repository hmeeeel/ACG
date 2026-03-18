using System;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

public sealed class AvaloniaRender
{
    public int Width  { get; }
    public int Height { get; }

    // WriteableBitmap — изменяемый массив, наследуемый от BitmapSource (only read)
    private readonly WriteableBitmap _bitmap;
    private readonly uint[] _backBuffer;

    public WriteableBitmap FrontBitmap => _bitmap;

    public AvaloniaRender(int width, int height) // пиксель
    {
        Width  = width;
        Height = height;

    
        //WriteableBitmap(PixelSize size, Vector dpi, PixelFormat? format = null, AlphaFormat? alphaFormat = null)
        _bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        _backBuffer = new uint[width * height];
    }

    public void Clear(uint color = 0xFF0E035F)
    {
        Array.Fill(_backBuffer, color);
    }

    public PixelSet GetPixelSet() => new PixelSet(_backBuffer, Width, Height);

    public unsafe void SwapBuffers()
    {
        using var locked = _bitmap.Lock();
        fixed (uint* src = _backBuffer)
        {
            Buffer.MemoryCopy(
                src,
                locked.Address.ToPointer(),
                (long)Width * Height * sizeof(uint),
                (long)Width * Height * sizeof(uint));
        }
    }
}