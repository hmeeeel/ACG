using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

public class Render
{
    private readonly AvaloniaRender _buffer;

    private Vec4[] _clipVerts = Array.Empty<Vec4>();
    private float[] _screenX = Array.Empty<float>();
    private float[] _screenY = Array.Empty<float>();
    private bool[] _inFrustum = Array.Empty<bool>();

    private Vec3[] _screenFilled = Array.Empty<Vec3>();
    private Vec3[] _worldFilled = Array.Empty<Vec3>();
    private float[] _invWFilled = Array.Empty<float>();
    private bool[] _visFilled = Array.Empty<bool>();
    private Vec3[] _vertexNormals = Array.Empty<Vec3>();

    private Rasterizer? _rasterizer;
    private ObjModel? _lastModel;

    public Render(AvaloniaRender buffer) => _buffer = buffer;

    public void DrawWireframe(ObjModel? model, Matrix44 modelMat,
        Matrix44 viewMat, Matrix44 projMat, uint lineColor = 0xFFC4F8FF)
    {
        _buffer.Clear();
        if (model is null) return;

        Matrix44 mvp = projMat * viewMat * modelMat;
        Matrix44 viewport = Matrix44.Viewport(0, 0, _buffer.Width, _buffer.Height);

        var verts = model.VertCoords;
        int vertCount = verts.Count;
        EnsureVertexBuffers(vertCount);

        for (int i = 0; i < vertCount; i++)
        {
            Vec4 c = mvp.Multiply(new Vec4(verts[i], 1f));
            _clipVerts[i] = c;
            if (c.W > 0f
                && c.X >= -c.W && c.X <= c.W
                && c.Y >= -c.W && c.Y <= c.W
                && c.Z >= 0f && c.Z <= c.W)
            {
                Vec3 ndc = c.PerspectiveDivide();
                Vec4 s = viewport.Multiply(new Vec4(ndc, 1f));
                _screenX[i] = s.X;
                _screenY[i] = s.Y;
                _inFrustum[i] = true;
            }
            else _inFrustum[i] = false;
        }

        var pw = _buffer.GetPixelSet();
        var faces = model.Faces;

        for (int fi = 0; fi < faces.Count; fi++)
        {
            var face = faces[fi];
            int fv = face.Vertices.Count;
            if (fv < 2) continue;

            for (int i = 0; i < fv; i++)
            {
                int nextI = (i + 1) % fv;
                int idxA = face.Vertices[i].v;
                int idxB = face.Vertices[nextI].v;
                if (idxA >= vertCount || idxB >= vertCount) continue;

                if (_inFrustum[idxA] && _inFrustum[idxB])
                {
                    Bresenham(pw,
                        (int)float.Round(_screenX[idxA]),
                        (int)float.Round(_screenY[idxA]),
                        (int)float.Round(_screenX[idxB]),
                        (int)float.Round(_screenY[idxB]),
                        lineColor);
                }
                else
                {
                    Vec4 ca = _clipVerts[idxA], cb = _clipVerts[idxB];
                    if (!ClipLine(ref ca, ref cb)) continue;
                    Vec3 sA = viewport.Multiply(new Vec4(ca.PerspectiveDivide(), 1f)).XYZ;
                    Vec3 sB = viewport.Multiply(new Vec4(cb.PerspectiveDivide(), 1f)).XYZ;
                    Bresenham(pw,
                        (int)float.Round(sA.X), (int)float.Round(sA.Y),
                        (int)float.Round(sB.X), (int)float.Round(sB.Y),
                        lineColor);
                }
            }
        }
    }

    public void DrawFilled(
        ObjModel? model,
        Matrix44 modelMat, Matrix44 viewMat, Matrix44 projMat,
        Vec3 eye, LightSettings light,
        TextureMap? diffuseTex = null,
        TextureMap? normalTex = null,
        TextureMap? specularTex = null)
    {
        _buffer.Clear(0xFF080818);
        if (model is null) return;

        _rasterizer ??= new Rasterizer(_buffer.Width, _buffer.Height);
        _rasterizer.Clear();

        EnsureVertexNormals(model);

        Matrix44 mvp = projMat * viewMat * modelMat;
        Matrix44 viewport = Matrix44.Viewport(0, 0, _buffer.Width, _buffer.Height);

        var verts = model.VertCoords;
        int vertCount = verts.Count;
        EnsureFilledBuffers(vertCount);

        
        // ВЕРШИНЫ
        Parallel.For(0, vertCount, i =>
        {
            Vec4 world = modelMat.Multiply(new Vec4(verts[i], 1f));
            _worldFilled[i] = world.XYZ;

            Vec4 clip = mvp.Multiply(new Vec4(verts[i], 1f));
            if (clip.W > 1e-5f)
            {

                _invWFilled[i] = 1f / clip.W;

                Vec3 ndc = clip.PerspectiveDivide();
                Vec4 sv = viewport.Multiply(new Vec4(ndc, 1f));
                _screenFilled[i] = new Vec3(sv.X, sv.Y, ndc.Z);
                _visFilled[i] = true;
            }
            else
            {
                _invWFilled[i] = 0f;
                _visFilled[i] = false;
            }
        });

        Vec3 normLight = light.Direction.Normalized();
        bool useTextures = light.TexMode != TextureMode.None
                           && (diffuseTex != null || normalTex != null || specularTex != null);


        // РАСТЕРИЗАЦИЯ ГРАНЕЙ
        Parallel.For(0, model.Faces.Count, fi => 
        //for (int fi = 0; fi < model.Faces.Count; fi++)
        {
            var face = model.Faces[fi];
            int faceVerts = face.Vertices.Count;
           // if (faceVerts < 3) continue;
            if (faceVerts < 3) return;

            int baseIdx = face.Vertices[0].v;
           // if ((uint)baseIdx >= (uint)vertCount) continue;
            if ((uint)baseIdx >= (uint)vertCount) return;

            // Back-face culling по геометрической нормали
            Vec3 wA = _worldFilled[baseIdx];
            Vec3 wB = _worldFilled[face.Vertices[1].v];
            Vec3 wC = _worldFilled[face.Vertices[2].v];
            Vec3 faceNormal = Vec3.Cross(wB - wA, wC - wA).Normalized();
            Vec3 centroid = (wA + wB + wC) * (1f / 3f);
            Vec3 viewDir = (eye - centroid).Normalized();
           // if (Vec3.Dot(faceNormal, viewDir) <= 0f) continue;

            if (Vec3.Dot(faceNormal, viewDir) <= 0f) return;

            // Fan-триангуляция полигона
            for (int i = 1; i < faceVerts - 1; i++)
            {
                int i0 = 0, i1 = i, i2 = i + 1;

                int vi0 = face.Vertices[i0].v;
                int vi1 = face.Vertices[i1].v;
                int vi2 = face.Vertices[i2].v;

                if ((uint)vi1 >= (uint)vertCount ||
                    (uint)vi2 >= (uint)vertCount) continue;
                if (!_visFilled[vi0] ||
                    !_visFilled[vi1] ||
                    !_visFilled[vi2]) continue;

                Vec3 sA = _screenFilled[vi0];
                Vec3 sB = _screenFilled[vi1];
                Vec3 sC = _screenFilled[vi2];

                if (useTextures)
                {
                    
                    // Собираем атрибуты вершин для перспективной коррекции
                    VertexAttributes attr0 = BuildVertexAttributes(
                        model, face.Vertices[i0], vi0);
                    VertexAttributes attr1 = BuildVertexAttributes(
                        model, face.Vertices[i1], vi1);
                    VertexAttributes attr2 = BuildVertexAttributes(
                        model, face.Vertices[i2], vi2);

                    _rasterizer.DrawTriangleTextured(
                        sA, sB, sC,
                         attr0,  attr1,  attr2,
                        eye, normLight, light,
                        diffuseTex, normalTex, specularTex);
                }
                else
                {
                    float iw0 = _invWFilled[vi0];
                    float iw1 = _invWFilled[vi1];
                    float iw2 = _invWFilled[vi2];

                    switch (light.Mode)
                    {
                        case ShadingMode.Lambert:
                        {
                            float lambert = float.Max(0f,
                                Vec3.Dot(faceNormal, normLight));
                            uint color = Rasterizer.Shade(
                                light.ObjectColor, light.Color, lambert);
                            _rasterizer.DrawTriangle(sA, sB, sC, color);
                            break;
                        }
                        case ShadingMode.Gouraud:
                        {
                            Vec3 c0 = BlinnPhongVertex(vi0, eye, normLight, light);
                            Vec3 c1 = BlinnPhongVertex(vi1, eye, normLight, light);
                            Vec3 c2 = BlinnPhongVertex(vi2, eye, normLight, light);
                            _rasterizer.DrawTriangleGouraud(sA, sB, sC, c0, c1, c2);
                            break;
                        }
                        default:
                        {
                            Vec3 n0 = GetNormal(model, face.Vertices[i0].vn, vi0);
                            Vec3 n1 = GetNormal(model, face.Vertices[i1].vn, vi1);
                            Vec3 n2 = GetNormal(model, face.Vertices[i2].vn, vi2);
                            _rasterizer.DrawTrianglePhong(
                                sA, sB, sC,
                                iw0, iw1, iw2,
                                n0, n1, n2,
                                _worldFilled[vi0], _worldFilled[vi1], _worldFilled[vi2],
                                eye, normLight, light);
                            break;
                        }
                    }
                }
            }
        //}
});
        _rasterizer.FlushToPixels(_buffer.GetRawBuffer());
    }


    // Собирает все атрибуты вершины для перспективной коррекции
    private VertexAttributes BuildVertexAttributes(
        ObjModel model,
        (int v, int vt, int vn) faceVertex,
        int vertIdx)
    {
        return new VertexAttributes(
            invW:     _invWFilled[vertIdx],
            uv:       GetUV(model, faceVertex.vt),
            normal:   GetNormal(model, faceVertex.vn, vertIdx),
            worldPos: _worldFilled[vertIdx]
        );
    }

    // Получить UV по индексу vt (-1 = нет)
    private static Vec2 GetUV(ObjModel model, int vtIdx)
    {
        if (vtIdx < 0 || vtIdx >= model.TexCoords.Count)
            return Vec2.Zero;
        return model.TexCoords[vtIdx];
    }

    private Vec3 GetNormal(ObjModel model, int vnIdx, int vertIdx)
    {
        if (vnIdx >= 0 && vnIdx < model.Normals.Count)
            return model.Normals[vnIdx].Normalized();
        if (vertIdx < _vertexNormals.Length)
            return _vertexNormals[vertIdx];
        return new Vec3(0f, 1f, 0f);
    }

    private Vec3 BlinnPhongVertex(int vi, Vec3 eye, Vec3 lightDir, LightSettings light)
    {
        Vec3 N = _vertexNormals[vi];
        Vec3 worldPos = _worldFilled[vi];

        float vx = eye.X - worldPos.X;
        float vy = eye.Y - worldPos.Y;
        float vz = eye.Z - worldPos.Z;
        float vLen = float.Sqrt(vx * vx + vy * vy + vz * vz);
        if (vLen > 1e-7f) { float inv = 1f / vLen; vx *= inv; vy *= inv; vz *= inv; }

        float hx = lightDir.X + vx;
        float hy = lightDir.Y + vy;
        float hz = lightDir.Z + vz;
        float hLen = float.Sqrt(hx * hx + hy * hy + hz * hz);
        if (hLen > 1e-7f) { float inv = 1f / hLen; hx *= inv; hy *= inv; hz *= inv; }

        Vec3 obj = Rasterizer.ColorToVec3(light.ObjectColor);
        float diff = float.Max(0f, Vec3.Dot(N, lightDir));
        float spec = float.Pow(float.Max(0f,
            N.X * hx + N.Y * hy + N.Z * hz), light.Glossiness);

        return new Vec3(
            float.Min(obj.X * light.AmbientColor.X + light.Color.X * obj.X * diff + light.Color.X * spec, 1f),
            float.Min(obj.Y * light.AmbientColor.Y + light.Color.Y * obj.Y * diff + light.Color.Y * spec, 1f),
            float.Min(obj.Z * light.AmbientColor.Z + light.Color.Z * obj.Z * diff + light.Color.Z * spec, 1f));
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
                Vec3 p1 = model.VertCoords[fv[i].v];
                Vec3 p2 = model.VertCoords[fv[i + 1].v];
                Vec3 fn = Vec3.Cross(p1 - p0, p2 - p0);
                _vertexNormals[fv[0].v] = _vertexNormals[fv[0].v] + fn;
                _vertexNormals[fv[i].v] = _vertexNormals[fv[i].v] + fn;
                _vertexNormals[fv[i + 1].v] = _vertexNormals[fv[i + 1].v] + fn;
            }
        }

        for (int i = 0; i < count; i++)
            _vertexNormals[i] = _vertexNormals[i].Normalized();
    }

    private void EnsureVertexBuffers(int vertCount)
    {
        if (_clipVerts.Length >= vertCount) return;
        int cap = vertCount + 1000;
        _clipVerts = new Vec4[cap];
        _screenX = new float[cap];
        _screenY = new float[cap];
        _inFrustum = new bool[cap];
    }

    private void EnsureFilledBuffers(int vertCount)
    {
        if (_screenFilled.Length >= vertCount) return;
        int cap = vertCount + 1000;
        _screenFilled = new Vec3[cap];
        _worldFilled = new Vec3[cap];
        _invWFilled = new float[cap];
        _visFilled = new bool[cap];
    }

    private void Bresenham(PixelSet pw, int x0, int y0, int x1, int y1, uint color)
    {
        int dx = int.Abs(x1 - x0), dy = int.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
        while (true)
        {
            pw.SetPixel(x0, y0, color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = err * 2;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    private static bool ClipLine(ref Vec4 a, ref Vec4 b)
    {
        float t0 = 0f, t1 = 1f;
        float dX = b.X - a.X, dY = b.Y - a.Y, dZ = b.Z - a.Z, dW = b.W - a.W;
        ReadOnlySpan<(float nx, float ny, float nz, float nw)> planes =
        [
            ( 0f,  0f,  1f,  0f),
            ( 0f,  0f, -1f,  1f),
            ( 1f,  0f,  0f,  1f),
            (-1f,  0f,  0f,  1f),
            ( 0f,  1f,  0f,  1f),
            ( 0f, -1f,  0f,  1f),
        ];
        foreach (var (nx, ny, nz, nw) in planes)
        {
            float f0 = nx * a.X + ny * a.Y + nz * a.Z + nw * a.W;
            float fd = nx * dX + ny * dY + nz * dZ + nw * dW;
            float t = -f0 / fd;
            if (fd > 0f) { if (t > t0) t0 = t; }
            else { if (t < t1) t1 = t; }
            if (t0 > t1) return false;
        }
        b = new(a.X + t1 * dX, a.Y + t1 * dY, a.Z + t1 * dZ, a.W + t1 * dW);
        a = new(a.X + t0 * dX, a.Y + t0 * dY, a.Z + t0 * dZ, a.W + t0 * dW);
        return true;
    }
}