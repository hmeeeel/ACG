using System.Collections.Generic;

public class ObjModel
    {
        public List<Vec3> VertCoords { get; } = new();
        public List<Vec3> TexCoords { get; } = new();
        public List<Vec3> Normals { get; } = new();
        public List<Face> Faces { get; } = new();
    }