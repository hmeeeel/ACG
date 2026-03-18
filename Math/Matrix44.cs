using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public struct Matrix44
{
    public float M00, M01, M02, M03;
    public float M10, M11, M12, M13;
    public float M20, M21, M22, M23;
    public float M30, M31, M32, M33;

    public float this[int row, int col]
    {
        get => (row * 4 + col) switch
        {
            0  => M00, 1  => M01, 2  => M02, 3  => M03,
            4  => M10, 5  => M11, 6  => M12, 7  => M13,
            8  => M20, 9  => M21, 10 => M22, 11 => M23,
            12 => M30, 13 => M31, 14 => M32, 15 => M33,
            _  => throw new IndexOutOfRangeException()
        };
        set
        {
            switch (row * 4 + col)
            {
                case 0:  M00 = value; break; case 1:  M01 = value; break;
                case 2:  M02 = value; break; case 3:  M03 = value; break;
                case 4:  M10 = value; break; case 5:  M11 = value; break;
                case 6:  M12 = value; break; case 7:  M13 = value; break;
                case 8:  M20 = value; break; case 9:  M21 = value; break;
                case 10: M22 = value; break; case 11: M23 = value; break;
                case 12: M30 = value; break; case 13: M31 = value; break;
                case 14: M32 = value; break; case 15: M33 = value; break;
            }
        }
    }   

    // матричн преобр коорд из модели в мировое пр-вл
    public static Matrix44 Identity()
    {
        var m = default(Matrix44);
        m.M00 = m.M11 = m.M22 = m.M33 = 1f;
        return m;
    }

    public static Matrix44 Translation(float tx, float ty, float tz)
    {
        var m = new Matrix44();
        m.M03 = tx; m.M13 = ty; m.M23 = tz; m.M33 = 1f;
        return m;
    }

    public static Matrix44 Scale(float sx, float sy, float sz)
    {
        var m = default(Matrix44);
        m.M00 = sx; m.M11 = sy; m.M22 = sz; m.M33 = 1f;
        return m;
    }

    public static Matrix44 Scale(float s) => Scale(s, s, s);

    public static Matrix44 RotationX(float angle)
    {
        var m = Identity();
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        m.M11 =  c; m.M12 = -s;
        m.M21 =  s; m.M22 =  c;
        return m;
    }
     
    public static Matrix44 RotationY(float angle)
    {
        var m = Identity();
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        m.M00 =  c; m.M02 =  s;
        m.M20 = -s; m.M22 =  c;
        return m;
    }

    public static Matrix44 RotationZ(float angle)
    {
        var m = Identity();
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        m.M00 =  c; m.M01 = -s;
        m.M10 =  s; m.M11 =  c;
        return m;
    }

    // матричные преобразования коорд из морового пр-ва в пр-во набл
    public static Matrix44 LookAt(Vec3 eye, Vec3 target, Vec3 up)
    {

        /* var e = new Vector3(eye.X, eye.Y, eye.Z);
         var t = new Vector3(target.X, target.Y, target.Z);
        var u = new Vector3(up.X, up.Y, up.Z);

        Vector3 zAxis = Vector3.Normalize(e - t);
        Vector3 xAxis = Vector3.Normalize(Vector3.Cross(u, zAxis));
        Vector3 yAxis = Vector3.Cross(zAxis, xAxis);
    */
        Vec3 zAxis = (eye - target).Normalized();
        Vec3 xAxis = Vec3.Cross(up, zAxis).Normalized();
        Vec3 yAxis = Vec3.Cross(zAxis, xAxis);

        var m = default(Matrix44);
        m.M00 = xAxis.X; m.M01 = xAxis.Y; m.M02 = xAxis.Z; m.M03 = -Vec3.Dot(xAxis, eye);
        m.M10 = yAxis.X; m.M11 = yAxis.Y; m.M12 = yAxis.Z; m.M13 = -Vec3.Dot(yAxis, eye);
        m.M20 = zAxis.X; m.M21 = zAxis.Y; m.M22 = zAxis.Z; m.M23 = -Vec3.Dot(zAxis, eye);
        m.M33 = 1f;
        return m;
    }


    // матр преобразования коорд из пр-ва набл в пр-во проекции (кубоид размеры от -1 до 1) - масшатбирование
    // правост система
    public static Matrix44 Orthographic(float width, float height, float zNear, float zFar)
    {
        var m = default(Matrix44);
        m.M00 = 2f / width;
        m.M11 = 2f / height;
        m.M22 = 1f / (zNear - zFar);
        m.M23 = zNear / (zNear - zFar);
        m.M33 = 1f;
        return m;
    }

    // просмотр - усечен пирамида - чем дальше тем меньше
    public static Matrix44 Perspective(float fovY, float aspect, float zNear, float zFar)
    {
        float tanHalf = MathF.Tan(fovY * 0.5f);
        var m = default(Matrix44);
        m.M00 = 1f / (aspect * tanHalf);
        m.M11 = 1f / tanHalf;
        m.M22 = zFar / (zNear - zFar);
        m.M23 = (zNear * zFar) / (zNear - zFar);
        m.M32 = -1f;
        return m;
    }

    // матр преобразование коорд из пр-ва проекции в пр-во окна просмотра
    public static Matrix44 Viewport(float xMin, float yMin, float width, float height)
    {
        var m = default(Matrix44);
        m.M00 =  width  / 2f; m.M03 = xMin + width  / 2f;
        m.M11 = -height / 2f; m.M13 = yMin + height / 2f;
        m.M22 = 1f;
        m.M33 = 1f;
        return m;
    }

    public readonly Vec4 Multiply(Vec4 v) => new(
        M00 * v.X + M01 * v.Y + M02 * v.Z + M03 * v.W,
        M10 * v.X + M11 * v.Y + M12 * v.Z + M13 * v.W,
        M20 * v.X + M21 * v.Y + M22 * v.Z + M23 * v.W,
        M30 * v.X + M31 * v.Y + M32 * v.Z + M33 * v.W);

    public static Matrix44 operator *(Matrix44 a, Matrix44 b)
    {
        Matrix44 r = default;
        r.M00 = a.M00*b.M00 + a.M01*b.M10 + a.M02*b.M20 + a.M03*b.M30;
        r.M01 = a.M00*b.M01 + a.M01*b.M11 + a.M02*b.M21 + a.M03*b.M31;
        r.M02 = a.M00*b.M02 + a.M01*b.M12 + a.M02*b.M22 + a.M03*b.M32;
        r.M03 = a.M00*b.M03 + a.M01*b.M13 + a.M02*b.M23 + a.M03*b.M33;

        r.M10 = a.M10*b.M00 + a.M11*b.M10 + a.M12*b.M20 + a.M13*b.M30;
        r.M11 = a.M10*b.M01 + a.M11*b.M11 + a.M12*b.M21 + a.M13*b.M31;
        r.M12 = a.M10*b.M02 + a.M11*b.M12 + a.M12*b.M22 + a.M13*b.M32;
        r.M13 = a.M10*b.M03 + a.M11*b.M13 + a.M12*b.M23 + a.M13*b.M33;

        r.M20 = a.M20*b.M00 + a.M21*b.M10 + a.M22*b.M20 + a.M23*b.M30;
        r.M21 = a.M20*b.M01 + a.M21*b.M11 + a.M22*b.M21 + a.M23*b.M31;
        r.M22 = a.M20*b.M02 + a.M21*b.M12 + a.M22*b.M22 + a.M23*b.M32;
        r.M23 = a.M20*b.M03 + a.M21*b.M13 + a.M22*b.M23 + a.M23*b.M33;

        r.M30 = a.M30*b.M00 + a.M31*b.M10 + a.M32*b.M20 + a.M33*b.M30;
        r.M31 = a.M30*b.M01 + a.M31*b.M11 + a.M32*b.M21 + a.M33*b.M31;
        r.M32 = a.M30*b.M02 + a.M31*b.M12 + a.M32*b.M22 + a.M33*b.M32;
        r.M33 = a.M30*b.M03 + a.M31*b.M13 + a.M32*b.M23 + a.M33*b.M33;
        return r;
    }
}