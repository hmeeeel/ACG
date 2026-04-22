public struct Vec2
{
    public float U, V;

    public Vec2(float u, float v) { U = u; V = v; }

    public static readonly Vec2 Zero = new(0f, 0f);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.U + b.U, a.V + b.V);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.U - b.U, a.V - b.V);
    public static Vec2 operator *(Vec2 v, float s) => new(v.U * s, v.V * s);
    public static Vec2 operator *(float s, Vec2 v) => v * s;
}