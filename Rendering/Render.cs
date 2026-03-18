using System;
using System.Threading.Tasks;

public class Render
{
    private readonly AvaloniaRender _buffer;

    private Vec4[]  _clipVerts = Array.Empty<Vec4>();
    private float[] _screenX   = Array.Empty<float>();
    private float[] _screenY   = Array.Empty<float>();
    private bool[]  _inFrustum = Array.Empty<bool>();

    public Render(AvaloniaRender buffer)
    {
        _buffer = buffer;
    }

    public void DrawWireframe(ObjModel? model, Matrix44  modelMat, Matrix44  viewMat, Matrix44  projMat,
        uint      lineColor = 0xFFC4F8FF)
    {
        _buffer.Clear();
        if (model is null) return;

        //[𝑉𝑖𝑒𝑤𝑝𝑜𝑟𝑡] × [𝑃𝑟𝑜𝑗𝑒𝑐𝑡𝑖𝑜𝑛] × [𝑉𝑖𝑒𝑤] × [𝑀𝑜𝑑𝑒𝑙]
        // из лок коорд в мировые затем что видит камнра и в плоский мир 
        Matrix44 mvp      = projMat * viewMat * modelMat; // чт сп-нал 
        Matrix44 viewport = Matrix44.Viewport(0, 0, _buffer.Width, _buffer.Height);

        var verts     = model.VertCoords;
        int vertCount = verts.Count;

        if (_clipVerts.Length < vertCount)
        {
            int capacity = vertCount + 1000;
            _clipVerts  = new Vec4[capacity];
            _screenX    = new float[capacity];
            _screenY    = new float[capacity];
            _inFrustum  = new bool[capacity];
        }

        var clipVerts  = _clipVerts;
        var screenX    = _screenX;
        var screenY    = _screenY;
        var inFrustum  = _inFrustum;

        Parallel.For(0, vertCount, i =>
        {
            Vec4 c = mvp.Multiply(new Vec4(verts[i], 1f));
            clipVerts[i] = c;

            if (c.W > 0f && c.X >= -c.W && c.X <= c.W &&  c.Y >= -c.W && c.Y <= c.W && c.Z >= 0f   && c.Z <= c.W)
            {
                Vec3 ndc = c.PerspectiveDivide(); // дел на w Normalized Device Coordinates все коорд от -1 до 1
                Vec4 s   = viewport.Multiply(new Vec4(ndc, 1f)); //перевод NDC в пиксели экрана
                screenX[i]   = s.X;
                screenY[i]   = s.Y;
                inFrustum[i] = true;
            }
            else
            {
                inFrustum[i] = false;
            }
        });

        var pw    = _buffer.GetPixelSet();
        var faces = model.Faces;

        Parallel.For(0, faces.Count, fi =>
        {
            var face      = faces[fi];
            int faceVerts = face.Vertices.Count;
            if (faceVerts < 2) return;

            for (int i = 0; i < faceVerts; i++)
            {
                int nextI = (i + 1) % faceVerts;
                int idxA  = face.Vertices[i].v;
                int idxB  = face.Vertices[nextI].v;

                if (idxA >= vertCount || idxB >= vertCount) continue;

                if (inFrustum[idxA] && inFrustum[idxB]) //обе вершины в кадре
                {
                    Bresenham(pw,
                        (int)MathF.Round(screenX[idxA]), (int)MathF.Round(screenY[idxA]),
                        (int)MathF.Round(screenX[idxB]), (int)MathF.Round(screenY[idxB]),
                        lineColor);
                }
                else//одна или обе вершины вне кадра
                { 
                    Vec4 clipA = clipVerts[idxA];
                    Vec4 clipB = clipVerts[idxB];

                    if (!ClipLine(ref clipA, ref clipB)) continue;

                    Vec3 sA = viewport.Multiply(new Vec4(clipA.PerspectiveDivide(), 1f)).XYZ;
                    Vec3 sB = viewport.Multiply(new Vec4(clipB.PerspectiveDivide(), 1f)).XYZ;

                    Bresenham(pw,
                        (int)MathF.Round(sA.X), (int)MathF.Round(sA.Y),
                        (int)MathF.Round(sB.X), (int)MathF.Round(sB.Y),
                        lineColor);
                }
            }
        });
    }

    private void Bresenham(PixelSet pw, int x0, int y0, int x1, int y1, uint color)
    {

        int dx  = Math.Abs(x1 - x0);
        int dy  = Math.Abs(y1 - y0);
        int sx  = x0 < x1 ? 1 : -1;
        int sy  = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            pw.SetPixel(x0, y0, color); 
            if (x0 == x1 && y0 == y1) break;
            int e2 = err * 2;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 <  dx) { err += dx; y0 += sy; }
        }
    }

    private static bool ClipLine(ref Vec4 a, ref Vec4 b)
    {
        float t0 = 0f, t1 = 1f;
        float dX = b.X - a.X, dY = b.Y - a.Y, dZ = b.Z - a.Z, dW = b.W - a.W;

        ReadOnlySpan<(float nx, float ny, float nz, float nw)> planes =
        [
            ( 0f,  0f,  1f,  0f),  // near: Z ≥ 0
            ( 0f,  0f, -1f,  1f),  // far:  Z ≤ W
            ( 1f,  0f,  0f,  1f),  // left: X ≥ -W
            (-1f,  0f,  0f,  1f),  // right: X ≤ W
            ( 0f,  1f,  0f,  1f),  // bottom: Y ≥ -W
            ( 0f, -1f,  0f,  1f),  // top: Y ≤ W
        ];

        foreach (var (nx, ny, nz, nw) in planes)
        {
            float f0 = nx*a.X + ny*a.Y + nz*a.Z + nw*a.W;  // расстояние точки A от плоскости
            float fd = nx*dX  + ny*dY  + nz*dZ  + nw*dW;   // скорость изменения вдоль ребра
            float t = -f0 / fd;                            // параметр пересечения
            if (fd > 0f) { if (t > t0) t0 = t; }           // обн нижнюю границу
            else         { if (t < t1) t1 = t; }           // обн верхнюю границу
            if (t0 > t1) return false;                     // диапазон пуст — невидимо
        }

        b = new(a.X + t1*dX, a.Y + t1*dY, a.Z + t1*dZ, a.W + t1*dW);
        a = new(a.X + t0*dX, a.Y + t0*dY, a.Z + t0*dZ, a.W + t0*dW);
        return true;
    }
}