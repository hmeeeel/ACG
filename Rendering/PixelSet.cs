
public sealed class PixelSet
{
    private readonly uint[] _buffer;
    private readonly int    _width;
    private readonly int    _height;

    internal PixelSet(uint[] buffer, int width, int height)
    {
        _buffer = buffer;
        _width  = width;
        _height = height;
    }

    public void SetPixelUnchecked(int x, int y, uint color)
    {
        _buffer[y * _width + x] = color; 
    }

    //800на600 - x: от 0 до 799 y: от 0 до 599
    public void SetPixel(int x, int y, uint color)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height) return;
        _buffer[y * _width + x] = color;
    }
}

