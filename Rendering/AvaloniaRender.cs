using System;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

public sealed class AvaloniaRender
{
    public int Width  { get; }
    public int Height { get; }

    private readonly WriteableBitmap _bitmap;
    private readonly uint[] _backBuffer;

    public WriteableBitmap FrontBitmap => _bitmap;

    public uint[] GetRawBuffer() => _backBuffer;

    public AvaloniaRender(int width, int height)
    {
        Width  = width;
        Height = height;

        _bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        _backBuffer = new uint[width * height];
    }

    public void Clear(uint color = 0xFF0E035F) => Array.Fill(_backBuffer, color);

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