using System;
using System.Runtime.CompilerServices;
using System.Threading;

public sealed class Rasterizer
{
    private readonly int _width;
    private readonly int _height;
    private readonly long[] _zColor;
    
    private static readonly long ZColorClear = ((long)BitConverter.SingleToInt32Bits(float.MaxValue) << 32) | 0u;

    public Rasterizer(int width, int height)
    {
        _width  = width;
        _height = height;
        _zColor = new long[width * height];
    }

    public void Clear()
    {
        Array.Fill(_zColor, ZColorClear);
    }

    public void FlushToPixels(uint[] pixelBuffer)
    {
        int len = _width * _height;
        for (int i = 0; i < len; i++)
        {
            long val = _zColor[i];
            pixelBuffer[i] = (val == ZColorClear) ? 0xFF080818u : (uint)(val & 0xFFFF_FFFFu);
        }
    }


    public void DrawTriangleTextured(
        Vec3 s0, Vec3 s1, Vec3 s2,
         VertexAttributes attr0,
         VertexAttributes attr1,
         VertexAttributes attr2,
        Vec3 eye,
        Vec3 lightDir, 
        LightSettings light,
        TextureMap? diffuseTex,
        TextureMap? normalTex,
        TextureMap? specularTex)
    {
        float area = Edge(s0, s1, s2);
        if (float.Abs(area) < 1e-6f) return;

        if (area < 0f)
        {
            (s1, s2) = (s2, s1);
            (attr1, attr2) = (attr2, attr1);
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

        float lR = light.Color.X, lG = light.Color.Y, lB = light.Color.Z;
        float aR = light.AmbientColor.X, aG = light.AmbientColor.Y, aB = light.AmbientColor.Z;
        float gloss  = light.Glossiness;
        float lDirX  = lightDir.X, lDirY = lightDir.Y, lDirZ = lightDir.Z;

        Vec3 objColor = ColorToVec3(light.ObjectColor);

        bool hasDiffuse  = diffuseTex  != null && light.TexMode != TextureMode.None;
        bool hasNormal   = normalTex   != null &&
                          (light.TexMode == TextureMode.Normal ||
                           light.TexMode == TextureMode.All);
        bool hasSpecular = specularTex != null &&
                          (light.TexMode == TextureMode.Specular ||
                           light.TexMode == TextureMode.All);

        float eyeX = eye.X, eyeY = eye.Y, eyeZ = eye.Z;


        for (int y = minY; y <= maxY; y++)
        {
            float w0 = w0_row, w1 = w1_row, w2 = w2_row;
            int rowBase = y * _width;

            for (int x = minX; x <= maxX; x++)
            {
                if (!InsideTriangle(w0, w1, w2, tl0, tl1, tl2))
                {
                    w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
                    continue;
                }

                float b0 = w0 * invArea;
                float b1 = w1 * invArea;
                float b2 = w2 * invArea;

                float z = b0 * s0.Z + b1 * s1.Z + b2 * s2.Z;

                if (!ZTest(rowBase + x, z))
                {
                    w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
                    continue;
                }

                // ПЕРСПЕКТИВНАЯ КОРРЕКЦИЯ
                VertexAttributes.InterpolatePerspectiveCorrect(
                    b0, b1, b2,
                    in attr0, in attr1, in attr2,
                    out float invW, out float wCorr,
                    out Vec2 uv, out Vec3 normal, out Vec3 worldPos);

                
                float nx, ny, nz;
                
                if (hasNormal)
                {
                    // Object-space normal map: RGB - XYZ
                    // N = normalize(texColor * 2 - 1)
                    Vec3 nt = normalTex!.SampleBilinear(uv.U, uv.V);
                    nx = nt.X * 2f - 1f;
                    ny = nt.Y * 2f - 1f;
                    nz = nt.Z * 2f - 1f;
                }
                else
                {
                    nx = normal.X;
                    ny = normal.Y;
                    nz = normal.Z;
                }

                // Нормализация нормали
                float nLen = float.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nLen > 1e-7f)
                {
                    float invN = 1f / nLen;
                    nx *= invN; ny *= invN; nz *= invN;
                }

                // ВЕКТОР ВЗГЛЯДА (view direction)
                float vx = eyeX - worldPos.X;
                float vy = eyeY - worldPos.Y;
                float vz = eyeZ - worldPos.Z;
                float vLen = float.Sqrt(vx * vx + vy * vy + vz * vz);
                if (vLen > 1e-7f)
                {
                    float invV = 1f / vLen;
                    vx *= invV; vy *= invV; vz *= invV;
                }

                // ДИФФУЗНЫЙ ЦВЕТ: из текстуры или ObjectColor
                float oR, oG, oB;
                
                if (hasDiffuse &&
                    (light.TexMode == TextureMode.Diffuse ||
                     light.TexMode == TextureMode.All     ||
                     light.TexMode == TextureMode.Normal  ||
                     light.TexMode == TextureMode.Specular))
                {
                    Vec3 diffuse = diffuseTex!.SampleBilinear(uv.U, uv.V);
                    oR = diffuse.X;
                    oG = diffuse.Y;
                    oB = diffuse.Z;
                }
                else
                {
                    oR = objColor.X;
                    oG = objColor.Y;
                    oB = objColor.Z;
                }

                // ОСВЕЩЕНИЕ БЛИНН-ФОНГА
                
                // AMBIENT: Ia = ka * ia
                float cR = oR * aR;
                float cG = oG * aG;
                float cB = oB * aB;

                // DIFFUSE: Id = kd * max(0, N*L) * id
                float dotNL = float.Max(0f, nx * lDirX + ny * lDirY + nz * lDirZ);
                cR += lR * oR * dotNL;
                cG += lG * oG * dotNL;
                cB += lB * oB * dotNL;

                // SPECULAR: Is = ks * pow(max(0, H·N), gloss) * is
                // H = normalize(L + V) - Blinn-Phong half-vector
                float hx = lDirX + vx;
                float hy = lDirY + vy;
                float hz = lDirZ + vz;
                float hLen = float.Sqrt(hx * hx + hy * hy + hz * hz);
                
                if (hLen > 1e-7f)
                {
                    float invH = 1f / hLen;
                    hx *= invH; hy *= invH; hz *= invH;

                    float dotHN = float.Max(0f, nx * hx + ny * hy + nz * hz);
                    float spec  = float.Pow(dotHN, gloss);

                    // ks из зеркальной карты или 1.0
                    float ks = hasSpecular
                        ? specularTex!.SampleGrayscale(uv.U, uv.V)
                        : 1f;

                    cR += lR * ks * spec;
                    cG += lG * ks * spec;
                    cB += lB * ks * spec;
                }

                // Запись пикселя с clamping
                SetPixelAfterZTest(rowBase + x, z,
                    Vec3ToColor(
                        float.Min(cR, 1f),
                        float.Min(cG, 1f),
                        float.Min(cB, 1f)));

                w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
            }

            w0_row += dw0_dy; w1_row += dw1_dy; w2_row += dw2_dy;
        }
    }

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
            int rowBase = y * _width;
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

    public void DrawTriangleGouraud(
        Vec3 s0, Vec3 s1, Vec3 s2,
        Vec3 c0, Vec3 c1, Vec3 c2)
    {
        float area = Edge(s0, s1, s2);
        if (float.Abs(area) < 1e-6f) return;
        if (area < 0f) { (s1, s2) = (s2, s1); (c1, c2) = (c2, c1); }

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
            int rowBase = y * _width;
            for (int x = minX; x <= maxX; x++)
            {
                if (!InsideTriangle(w0, w1, w2, tl0, tl1, tl2))
                {
                    w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
                    continue;
                }

                float b0 = w0 * invArea, b1 = w1 * invArea, b2 = w2 * invArea;
                float z  = b0 * s0.Z + b1 * s1.Z + b2 * s2.Z;

                if (!ZTest(rowBase + x, z))
                {
                    w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
                    continue;
                }

                float r = b0 * c0.X + b1 * c1.X + b2 * c2.X;
                float g = b0 * c0.Y + b1 * c1.Y + b2 * c2.Y;
                float b = b0 * c0.Z + b1 * c1.Z + b2 * c2.Z;
                SetPixelAfterZTest(rowBase + x, z, Vec3ToColor(r, g, b));

                w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
            }
            w0_row += dw0_dy; w1_row += dw1_dy; w2_row += dw2_dy;
        }
    }

    public void DrawTrianglePhong(
        Vec3 s0, Vec3 s1, Vec3 s2,
        float invW0, float invW1, float invW2,
        Vec3 n0, Vec3 n1, Vec3 n2,
        Vec3 wp0, Vec3 wp1, Vec3 wp2,
        Vec3 eye, Vec3 lightDir,
        LightSettings light)
    {
        float area = Edge(s0, s1, s2);
        if (float.Abs(area) < 1e-6f) return;

        if (area < 0f)
        {
            (s1, s2) = (s2, s1);
            (invW1, invW2) = (invW2, invW1);
            (n1, n2) = (n2, n1);
            (wp1, wp2) = (wp2, wp1);
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

        Vec3 objColor = ColorToVec3(light.ObjectColor);
        float oR = objColor.X, oG = objColor.Y, oB = objColor.Z;
        float lR = light.Color.X, lG = light.Color.Y, lB = light.Color.Z;
        float aR = light.AmbientColor.X, aG = light.AmbientColor.Y, aB = light.AmbientColor.Z;
        float gloss = light.Glossiness;
        ShadingMode mode = light.Mode;

        float ambR = oR * aR, ambG = oG * aG, ambB = oB * aB;
        float lDirX = lightDir.X, lDirY = lightDir.Y, lDirZ = lightDir.Z;

        for (int y = minY; y <= maxY; y++)
        {
            float w0 = w0_row, w1 = w1_row, w2 = w2_row;
            int rowBase = y * _width;

            for (int x = minX; x <= maxX; x++)
            {
                if (!InsideTriangle(w0, w1, w2, tl0, tl1, tl2))
                {
                    w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
                    continue;
                }

                float b0 = w0 * invArea, b1 = w1 * invArea, b2 = w2 * invArea;
                float z = b0 * s0.Z + b1 * s1.Z + b2 * s2.Z;

                if (!ZTest(rowBase + x, z))
                {
                    w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
                    continue;
                }

                float invW = b0 * invW0 + b1 * invW1 + b2 * invW2;
                float persp = invW > 1e-7f ? 1f / invW : 0f;

                float pc0 = b0 * invW0 * persp;
                float pc1 = b1 * invW1 * persp;
                float pc2 = b2 * invW2 * persp;

                float nx = pc0 * n0.X + pc1 * n1.X + pc2 * n2.X;
                float ny = pc0 * n0.Y + pc1 * n1.Y + pc2 * n2.Y;
                float nz = pc0 * n0.Z + pc1 * n1.Z + pc2 * n2.Z;

                float nLen = float.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nLen > 1e-7f)
                {
                    float invN = 1f / nLen;
                    nx *= invN; ny *= invN; nz *= invN;
                }

                float px = pc0 * wp0.X + pc1 * wp1.X + pc2 * wp2.X;
                float py = pc0 * wp0.Y + pc1 * wp1.Y + pc2 * wp2.Y;
                float pz = pc0 * wp0.Z + pc1 * wp1.Z + pc2 * wp2.Z;

                float vx = eye.X - px, vy = eye.Y - py, vz = eye.Z - pz;
                float vLen = float.Sqrt(vx * vx + vy * vy + vz * vz);
                if (vLen > 1e-7f)
                {
                    float invV = 1f / vLen;
                    vx *= invV; vy *= invV; vz *= invV;
                }

                float cR, cG, cB;

                switch (mode)
                {
                    case ShadingMode.Ambient:
                        cR = ambR; cG = ambG; cB = ambB;
                        break;

                    case ShadingMode.Diffuse: // LightColor * objColor * max(0, dot(N, -L))
                        float diffD = float.Max(0f, nx * lDirX + ny * lDirY + nz * lDirZ);
                        cR = lR * oR * diffD; 
                        cG = lG * oG * diffD; 
                        cB = lB * oB * diffD;
                        break;

                    case ShadingMode.Specular: // LightColor * pow(max(0, dot(H, N)), gloss)
                        float hxS = lDirX + vx, hyS = lDirY + vy, hzS = lDirZ + vz;
                        float hLenS = float.Sqrt(hxS * hxS + hyS * hyS + hzS * hzS);
                        float specS = 0f;
                        if (hLenS > 1e-7f)
                        {
                            float invH = 1f / hLenS;
                            specS = float.Pow(float.Max(0f,
                                nx * hxS * invH + ny * hyS * invH + nz * hzS * invH), gloss);
                        }
                        cR = lR * specS; cG = lG * specS; cB = lB * specS;
                        break;

                    case ShadingMode.PhongBlinn:  //H = normalize(L + V) spec = pow(H*N, gloss)
                        float diffB = float.Max(0f, nx * lDirX + ny * lDirY + nz * lDirZ);
                        float dRB = lR * oR * diffB, dGB = lG * oG * diffB, dBB = lB * oB * diffB;

                        float hxB = lDirX + vx, hyB = lDirY + vy, hzB = lDirZ + vz;
                        float hLenB = float.Sqrt(hxB * hxB + hyB * hyB + hzB * hzB);
                        float specB = 0f;
                        if (hLenB > 1e-7f)
                        {
                            float invH = 1f / hLenB;
                            specB = float.Pow(float.Max(0f,
                                nx * hxB * invH + ny * hyB * invH + nz * hzB * invH), gloss);
                        }
                        float sRB = lR * specB, sGB = lG * specB, sBB = lB * specB;

                        cR = ambR + dRB + sRB; cG = ambG + dGB + sGB; cB = ambB + dBB + sBB;
                        break;

                    case ShadingMode.Phong: // R = 2*(N*L)*N - L spec = pow(R*V, gloss)
                        float dotNL = nx * lDirX + ny * lDirY + nz * lDirZ;
                        float diffP = float.Max(0f, dotNL);
                        float dRP = lR * oR * diffP, dGP = lG * oG * diffP, dBP = lB * oB * diffP;

                        float rx = 2f * dotNL * nx - lDirX;
                        float ry = 2f * dotNL * ny - lDirY;
                        float rz = 2f * dotNL * nz - lDirZ;
                        float dotRV = float.Max(0f, rx * vx + ry * vy + rz * vz);
                        float specP = float.Pow(dotRV, gloss);
                        float sRP = lR * specP, sGP = lG * specP, sBP = lB * specP;

                        cR = ambR + dRP + sRP; cG = ambG + dGP + sGP; cB = ambB + dBP + sBP;
                        break;

                    default:
                        cR = ambR; cG = ambG; cB = ambB;
                        break;
                }

                SetPixelAfterZTest(rowBase + x, z, Vec3ToColor(cR, cG, cB));

                w0 += dw0_dx; w1 += dw1_dx; w2 += dw2_dx;
            }

            w0_row += dw0_dy; w1_row += dw1_dy; w2_row += dw2_dy;
        }
    }

    private bool SetupTriangle(
        ref Vec3 s0, ref Vec3 s1, ref Vec3 s2,
        out int minX, out int maxX, out int minY, out int maxY,
        out float invArea,
        out bool tl0, out bool tl1, out bool tl2,
        out float dw0_dx, out float dw0_dy,
        out float dw1_dx, out float dw1_dy,
        out float dw2_dx, out float dw2_dy,
        out float w0_row, out float w1_row, out float w2_row)
    {
        float area = Edge(s0, s1, s2);
        if (float.Abs(area) < 1e-6f)
        {
            minX = maxX = minY = maxY = 0;
            invArea = 0f;
            tl0 = tl1 = tl2 = false;
            dw0_dx = dw0_dy = dw1_dx = dw1_dy = dw2_dx = dw2_dy = 0f;
            w0_row = w1_row = w2_row = 0f;
            return false;
        }
        if (area < 0f) { (s1, s2) = (s2, s1); area = -area; }

        invArea = 1f / area;

        minX = int.Max(0, (int)float.Floor(float.Min(s0.X, float.Min(s1.X, s2.X))));
        maxX = int.Min(_width - 1, (int)float.Ceiling(float.Max(s0.X, float.Max(s1.X, s2.X))));
        minY = int.Max(0, (int)float.Floor(float.Min(s0.Y, float.Min(s1.Y, s2.Y))));
        maxY = int.Min(_height - 1, (int)float.Ceiling(float.Max(s0.Y, float.Max(s1.Y, s2.Y))));

        tl0 = IsTopLeft(s1, s2);
        tl1 = IsTopLeft(s2, s0);
        tl2 = IsTopLeft(s0, s1);

        dw0_dx = s1.Y - s2.Y; dw0_dy = s2.X - s1.X;
        dw1_dx = s2.Y - s0.Y; dw1_dy = s0.X - s2.X;
        dw2_dx = s0.Y - s1.Y; dw2_dy = s1.X - s0.X;

        float px0 = minX + 0.5f, py0 = minY + 0.5f;
        w0_row = Edge(s1, s2, px0, py0);
        w1_row = Edge(s2, s0, px0, py0);
        w2_row = Edge(s0, s1, px0, py0);

        return true;
    }

    private static bool InsideTriangle(float w0, float w1, float w2,
                                       bool tl0, bool tl1, bool tl2)
        => (w0 > 0f || (w0 == 0f && tl0))
        && (w1 > 0f || (w1 == 0f && tl1))
        && (w2 > 0f || (w2 == 0f && tl2));

    private static float Edge(Vec3 a, Vec3 b, float px, float py)
        => (b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X);

    private static float Edge(Vec3 a, Vec3 b, Vec3 c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool IsTopLeft(Vec3 a, Vec3 b)
    {
        float dy = b.Y - a.Y, dx = b.X - a.X;
        return dy < 0f || (dy == 0f && dx > 0f);
    }

    private bool ZTest(int idx, float zNew)
    {
        long oldVal = _zColor[idx];
        int oldZB = (int)(oldVal >> 32);
        float oldZ = BitConverter.Int32BitsToSingle(oldZB);
        return zNew < oldZ;
    }

    private void SetPixelAfterZTest(int idx, float zNew, uint color)
    {
        int newZBits = BitConverter.SingleToInt32Bits(zNew);
        long newVal = ((long)newZBits << 32) | color;
        while (true)
        {
            long oldVal = _zColor[idx];
            int oldZB = (int)(oldVal >> 32);
            float oldZ = BitConverter.Int32BitsToSingle(oldZB);
            if (zNew >= oldZ) return;
            if (Interlocked.CompareExchange(ref _zColor[idx], newVal, oldVal) == oldVal)
                return;
        }
    }

    private void TrySetPixel(int idx, float zNew, uint color)
    {
        int newZBits = BitConverter.SingleToInt32Bits(zNew);
        long newVal = ((long)newZBits << 32) | color;
        while (true)
        {
            long oldVal = _zColor[idx];
            int oldZB = (int)(oldVal >> 32);
            float oldZ = BitConverter.Int32BitsToSingle(oldZB);
            if (zNew >= oldZ) return;
            if (Interlocked.CompareExchange(ref _zColor[idx], newVal, oldVal) == oldVal)
                return;
        }
    }

    public static uint Shade(uint objColor, Vec3 lightColor, float lambert)
    {
        float r = ((objColor >> 16) & 0xFF) * lightColor.X * lambert;
        float g = ((objColor >>  8) & 0xFF) * lightColor.Y * lambert;
        float b = ( objColor        & 0xFF) * lightColor.Z * lambert;
        uint  a = (objColor >> 24) & 0xFF;

        uint ri = (uint)r; if (ri > 255) ri = 255;
        uint gi = (uint)g; if (gi > 255) gi = 255;
        uint bi = (uint)b; if (bi > 255) bi = 255;

        return (a << 24) | (ri << 16) | (gi << 8) | bi;
    }
    public static Vec3 ColorToVec3(uint c) => new(
        ((c >> 16) & 0xFF) / 255f,
        ((c >> 8) & 0xFF) / 255f,
        (c & 0xFF) / 255f);

    public static uint Vec3ToColor(float r, float g, float b)
    {
        int ri = (int)(r * 255f);
        int gi = (int)(g * 255f);
        int bi = (int)(b * 255f);
        ri = int.Clamp(ri, 0, 255);
        gi = int.Clamp(gi, 0, 255);
        bi = int.Clamp(bi, 0, 255);
        return (0xFFu << 24) | ((uint)ri << 16) | ((uint)gi << 8) | (uint)bi;
    }

    public static uint Vec3ToColor(Vec3 c) => Vec3ToColor(c.X, c.Y, c.Z);
}