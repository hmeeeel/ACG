public static class LightingHelper
{
    public static Vec3 ComputePhongLighting(
        float nx, float ny, float nz,
        float lDirX, float lDirY, float lDirZ,
        float vx, float vy, float vz,
        float lR, float lG, float lB,
        float oR, float oG, float oB,
        float aR, float aG, float aB,
        float specular,
        float gloss,
        ShadingMode mode)
    {
        float ambR = oR * aR;
        float ambG = oG * aG;
        float ambB = oB * aB;

        switch (mode)
        {
            case ShadingMode.Ambient:
                return new Vec3(ambR, ambG, ambB);

            case ShadingMode.Diffuse:
            {
                float diffD = float.Max(0f, nx * lDirX + ny * lDirY + nz * lDirZ);
                return new Vec3(lR * oR * diffD, lG * oG * diffD, lB * oB * diffD);
            }

            case ShadingMode.Specular:
            {
                float hx = lDirX + vx, hy = lDirY + vy, hz = lDirZ + vz;
                float hLen = float.Sqrt(hx * hx + hy * hy + hz * hz);
                float spec = 0f;
                if (hLen > 1e-7f)
                {
                    float invH = 1f / hLen;
                    float dotHN = float.Max(0f, nx * hx * invH + ny * hy * invH + nz * hz * invH);
                    spec = float.Pow(dotHN, gloss);
                }
                return new Vec3(lR * specular * spec, lG * specular * spec, lB * specular * spec);
            }

            case ShadingMode.PhongBlinn:
            {
                float diff = float.Max(0f, nx * lDirX + ny * lDirY + nz * lDirZ);
                
                float hx = lDirX + vx, hy = lDirY + vy, hz = lDirZ + vz;
                float hLen = float.Sqrt(hx * hx + hy * hy + hz * hz);
                float spec = 0f;
                if (hLen > 1e-7f)
                {
                    float invH = 1f / hLen;
                    float dotHN = float.Max(0f, nx * hx * invH + ny * hy * invH + nz * hz * invH);
                    spec = float.Pow(dotHN, gloss);
                }
                
                return new Vec3(
                    ambR + lR * oR * diff + lR * specular * spec,
                    ambG + lG * oG * diff + lG * specular * spec,
                    ambB + lB * oB * diff + lB * specular * spec);
            }

            case ShadingMode.Phong:
            {
                float dotNL = nx * lDirX + ny * lDirY + nz * lDirZ;
                float diff = float.Max(0f, dotNL);
                
                float rx = 2f * dotNL * nx - lDirX;
                float ry = 2f * dotNL * ny - lDirY;
                float rz = 2f * dotNL * nz - lDirZ;
                
                float dotRV = float.Max(0f, rx * vx + ry * vy + rz * vz);
                float spec = float.Pow(dotRV, gloss);
                
                return new Vec3(
                    ambR + lR * oR * diff + lR * specular * spec,
                    ambG + lG * oG * diff + lG * specular * spec,
                    ambB + lB * oB * diff + lB * specular * spec);
            }

            default:
                return new Vec3(ambR, ambG, ambB);
        }
    }
}