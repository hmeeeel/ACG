using System;
using System.Threading.Tasks;

public class Render
{
    private readonly AvaloniaRender _buffer;
    private Vec4[]  _clipVerts = Array.Empty<Vec4>();
    private float[] _screenX   = Array.Empty<float>();
    private float[] _screenY   = Array.Empty<float>();
    private bool[]  _inFrustum = Array.Empty<bool>();
    private Rasterizer? _rasterizer;
    private Vec3[]      _screenFilled = Array.Empty<Vec3>();
    private Vec3[]      _worldFilled  = Array.Empty<Vec3>();
    private bool[]      _visFilled    = Array.Empty<bool>();

    private Vec3[]    _vertexNormals = Array.Empty<Vec3>();
    private ObjModel? _lastModel;

    public Render(AvaloniaRender buffer) => _buffer = buffer;

    public void DrawWireframe(ObjModel? model, Matrix44 modelMat, Matrix44 viewMat, Matrix44 projMat,
        uint lineColor = 0xFFC4F8FF)
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
            int cap = vertCount + 1000;
            _clipVerts = new Vec4[cap]; _screenX = new float[cap];
            _screenY   = new float[cap]; _inFrustum = new bool[cap];
        }

        var clipVerts = _clipVerts; var screenX = _screenX;
        var screenY   = _screenY;  var inFrustum = _inFrustum;

        //Parallel.For(0, vertCount, i =>
        
        for (int i = 0; i < vertCount; i++){
            Vec4 c = mvp.Multiply(new Vec4(verts[i], 1f));
            clipVerts[i] = c;
            if (c.W > 0f && c.X >= -c.W && c.X <= c.W && c.Y >= -c.W && c.Y <= c.W && c.Z >= 0f && c.Z <= c.W)
            {
                Vec3 ndc = c.PerspectiveDivide();
                Vec4 s   = viewport.Multiply(new Vec4(ndc, 1f));
                screenX[i] = s.X; screenY[i] = s.Y; inFrustum[i] = true;
            }
            else inFrustum[i] = false;
        //});
    }
        var pw    = _buffer.GetPixelSet();
        var faces = model.Faces;

       // Parallel.For(0, faces.Count, fi =>
       for (int fi = 0; fi < faces.Count; fi++)
        {
            var face = faces[fi]; int fv = face.Vertices.Count;
            if (fv < 2) continue;
            for (int i = 0; i < fv; i++)
            {
                int nextI = (i + 1) % fv;
                int idxA  = face.Vertices[i].v, idxB = face.Vertices[nextI].v;
                if (idxA >= vertCount || idxB >= vertCount) continue;
                if (inFrustum[idxA] && inFrustum[idxB])
                {
                    Bresenham(pw,
                        (int)float.Round(screenX[idxA]), (int)float.Round(screenY[idxA]),
                        (int)float.Round(screenX[idxB]), (int)float.Round(screenY[idxB]),
                        lineColor);
                }
                else
                {
                    Vec4 ca = clipVerts[idxA], cb = clipVerts[idxB];
                    if (!ClipLine(ref ca, ref cb)) continue;
                    Vec3 sA = viewport.Multiply(new Vec4(ca.PerspectiveDivide(), 1f)).XYZ;
                    Vec3 sB = viewport.Multiply(new Vec4(cb.PerspectiveDivide(), 1f)).XYZ;
                    Bresenham(pw,
                        (int)float.Round(sA.X), (int)float.Round(sA.Y),
                        (int)float.Round(sB.X), (int)float.Round(sB.Y),
                        lineColor);
                }
            }
       // }); 
        }
    }

    private void Bresenham(PixelSet pw, int x0, int y0, int x1, int y1, uint color)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
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
        float dX = b.X-a.X, dY = b.Y-a.Y, dZ = b.Z-a.Z, dW = b.W-a.W;
        ReadOnlySpan<(float nx, float ny, float nz, float nw)> planes =
        [
            ( 0f,  0f,  1f,  0f),  // near: Z ≥ 0
            ( 0f,  0f, -1f,  1f),  // far:  Z ≤ W
            ( 1f,  0f,  0f,  1f),  // left: X ≥ -W
            (-1f,  0f,  0f,  1f),  // right: X ≤ W
            ( 0f,  1f,  0f,  1f),  // bottom: Y ≥ -W
            ( 0f, -1f,  0f,  1f),  // top: Y ≤ W
        ];
        foreach (var (nx,ny,nz,nw) in planes)
        {
            float f0 = nx*a.X + ny*a.Y + nz*a.Z + nw*a.W;  // расстояние точки A от плоскости
            float fd = nx*dX  + ny*dY  + nz*dZ  + nw*dW;   // скорость изменения вдоль ребра
            float t = -f0 / fd;                            // параметр пересечения
            if (fd > 0f) { if (t > t0) t0 = t; }           // обн нижнюю границу
            else         { if (t < t1) t1 = t; }           // обн верхнюю границу
            if (t0 > t1) return false;                     // диапазон пуст — невидимо
        }
        b = new(a.X+t1*dX, a.Y+t1*dY, a.Z+t1*dZ, a.W+t1*dW);
        a = new(a.X+t0*dX, a.Y+t0*dY, a.Z+t0*dZ, a.W+t0*dW);
        return true;
    }

    public void DrawFilled(ObjModel? model, Matrix44 modelMat, Matrix44 viewMat,
        Matrix44 projMat, Vec3 eye, LightSettings light)
    {
        _buffer.Clear(0xFF00FF00); //0xFF080818
        if (model is null) return;

        _rasterizer ??= new Rasterizer(_buffer.Width, _buffer.Height);
        _rasterizer.Clear();

        if (light.Mode != ShadingMode.Lambert)
            EnsureVertexNormals(model);

        Matrix44 mvp      = projMat * viewMat * modelMat;
        Matrix44 viewport = Matrix44.Viewport(0, 0, _buffer.Width, _buffer.Height);

        var verts     = model.VertCoords;
        int vertCount = verts.Count;

        if (_screenFilled.Length < vertCount)
        {
            int cap = vertCount + 1000;
            _screenFilled = new Vec3[cap]; _worldFilled = new Vec3[cap]; _visFilled = new bool[cap];
        }

        Parallel.For(0, vertCount, i =>
        //for (int i = 0; i < vertCount; i++)
        {
            _worldFilled[i] = modelMat.Multiply(new Vec4(verts[i], 1f)).XYZ;
            Vec4 clip = mvp.Multiply(new Vec4(verts[i], 1f));
            if (clip.W > 1e-5f)
            {
                Vec3 ndc = clip.PerspectiveDivide();
                Vec4 sv  = viewport.Multiply(new Vec4(ndc, 1f));
                _screenFilled[i] = new Vec3(sv.X, sv.Y, ndc.Z);
                _visFilled[i]    = true;
            }
            else _visFilled[i] = false;
        });
       // }

        Vec3 normLight = light.Direction.Normalized();

        //Parallel.For(0, model.Faces.Count, fi =>
        for (int fi = 0; fi < model.Faces.Count; fi++)
        {
            var face = model.Faces[fi]; int faceVerts = face.Vertices.Count;
            if (faceVerts < 3) continue;

            int baseIdx = face.Vertices[0].v;
            int idx1    = face.Vertices[1].v;
            int idx2    = face.Vertices[2].v;

            if ((uint)baseIdx >= (uint)vertCount ||
                (uint)idx1    >= (uint)vertCount ||
                (uint)idx2    >= (uint)vertCount) continue;

            Vec3 wA = _worldFilled[baseIdx];
            Vec3 wB = _worldFilled[idx1];
            Vec3 wC = _worldFilled[idx2];
            Vec3 faceNormal = Vec3.Cross(wB - wA, wC - wA).Normalized();
            Vec3 centroid   = (wA + wB + wC) * (1f / 3f);
            Vec3 viewDir    = (eye - centroid).Normalized();

            if (Vec3.Dot(faceNormal, viewDir) <= 0f) continue;

            for (int i = 1; i < faceVerts - 1; i++)
            {
                int iB = face.Vertices[i    ].v;
                int iC = face.Vertices[i + 1].v;
                if ((uint)iB >= (uint)vertCount || (uint)iC >= (uint)vertCount) continue;
                if (!_visFilled[baseIdx] || !_visFilled[iB] || !_visFilled[iC]) continue;

                Vec3 sA = _screenFilled[baseIdx];
                Vec3 sB = _screenFilled[iB];
                Vec3 sC = _screenFilled[iC];

                switch (light.Mode)
                {
                    case ShadingMode.Lambert:
                    {
                        float lambert = float.Max(0f, Vec3.Dot(faceNormal, normLight));
                        uint  color   = Rasterizer.Shade(light.ObjectColor, light.Color, lambert);
                        
                        
                        _rasterizer.DrawTriangle(sA, sB, sC, color);
                        break;
                    }
                    case ShadingMode.Gouraud:
                    {
                        Vec3 c0 = BlinnPhongVertex(baseIdx, _worldFilled[baseIdx], eye, normLight, light);
                        Vec3 c1 = BlinnPhongVertex(iB,      _worldFilled[iB],      eye, normLight, light);
                        Vec3 c2 = BlinnPhongVertex(iC,      _worldFilled[iC],      eye, normLight, light);
                        _rasterizer.DrawTriangleGouraud(sA, sB, sC, c0, c1, c2);
                        break;
                    }
                    default:
                    {
                        _rasterizer.DrawTrianglePhong(sA, sB, sC,
                            _vertexNormals[baseIdx], _vertexNormals[iB], _vertexNormals[iC],
                            _worldFilled[baseIdx],   _worldFilled[iB],   _worldFilled[iC],
                            eye, normLight, light);
                        break;
                    }
                }
            }
        //});
        }
        _rasterizer.FlushToPixels(_buffer.GetRawBuffer());
    }

    private Vec3 BlinnPhongVertex(int vi, Vec3 worldPos, Vec3 eye, Vec3 lightDir, LightSettings light)
    {
        Vec3 N = _vertexNormals[vi];
        Vec3 V = (eye - worldPos).Normalized();
        Vec3 H = (lightDir + V).Normalized();

        Vec3 obj     = Rasterizer.ColorToVec3(light.ObjectColor);
        Vec3 ambient = obj * light.AmbientColor;
        float diff   = float.Max(0f, Vec3.Dot(N, lightDir));
        Vec3 diffuse = light.Color * obj * diff;
        float spec    = float.Pow(float.Max(0f, Vec3.Dot(H, N)), light.Glossiness);
        Vec3 specular = light.Color * spec;

        return new Vec3(
            Math.Clamp(ambient.X + diffuse.X + specular.X, 0f, 1f),
            Math.Clamp(ambient.Y + diffuse.Y + specular.Y, 0f, 1f),
            Math.Clamp(ambient.Z + diffuse.Z + specular.Z, 0f, 1f));
    }

    private void EnsureVertexNormals(ObjModel model)
    {
        if (ReferenceEquals(model, _lastModel)) return;
        _lastModel = model;

        int count = model.VertCoords.Count;
        _vertexNormals = new Vec3[count];

        foreach (var face in model.Faces)
        {
            var fv = face.Vertices;
            if (fv.Count < 3) continue;
            Vec3 p0 = model.VertCoords[fv[0].v];
            for (int i = 1; i < fv.Count - 1; i++)
            {
                Vec3 p1 = model.VertCoords[fv[i    ].v];
                Vec3 p2 = model.VertCoords[fv[i + 1].v];
                Vec3 fn = Vec3.Cross(p1 - p0, p2 - p0);  
                int vi0 = fv[0    ].v, vi1 = fv[i].v, vi2 = fv[i + 1].v;
                _vertexNormals[vi0] = _vertexNormals[vi0] + fn;
                _vertexNormals[vi1] = _vertexNormals[vi1] + fn;
                _vertexNormals[vi2] = _vertexNormals[vi2] + fn;
            }
        }

        for (int i = 0; i < count; i++)
            _vertexNormals[i] = _vertexNormals[i].Normalized();
    }
}