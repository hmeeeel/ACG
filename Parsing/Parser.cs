using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

public class Parser
{
    public ObjModel Parse(string file)
    {
        if (!File.Exists(file))
                throw new FileNotFoundException($"Файл не найден: {file}");

        var model = new ObjModel();  

        foreach (var rawLine in File.ReadLines(file))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

                string[] coord = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (coord.Length == 0) continue;

                switch (coord[0])
                {
                    case "v":  ParseVertical(coord, model); break; 
                    case "vt": ParseTexCoord(coord, model); break; 
                    case "vn": ParseNormal(coord, model);   break; 
                    case "f":  ParseFace(coord, model);     break; 
                }
            }

            return model;
        }

    // v x y z [w] - коорд вершины в пространстве
    private static void ParseVertical(string[] coords, ObjModel model)
    {
        float x = ParseF(coords[1]);
        float y = ParseF(coords[2]);
        float z = ParseF(coords[3]);
        //float w = 1; // для кривых и поверхностей
        model.VertCoords.Add(new Vec3(x, y, z));
    }
    
    private static float ParseF(string s){
        return float.Parse(s, CultureInfo.InvariantCulture);
    }

    // vt u [v] [w] - текст коорд от 0 до 1 - испол в осв
    private static void ParseTexCoord(string[] coords, ObjModel model)
    {
        float u = ParseF(coords[1]);
        float v = coords.Length > 2 ? ParseF(coords[2]) : 0f;
        float w = coords.Length > 3 ? ParseF(coords[3]) : 0f;
        model.TexCoords.Add(new Vec3(u, v, w));
    }

    // vn i j k - нормаль вектора вершины - для плавного освещения - м быть ненорм
    private static void ParseNormal(string[] coords, ObjModel model)
    {
        float i = ParseF(coords[1]);
        float j = ParseF(coords[2]);
        float k = ParseF(coords[3]);
        model.Normals.Add(new Vec3(i, j, k));
    }

        //   f v1 v2 v3                — верш
        //   f v1/vt1 v2/vt2 v3/vt3    — верш + текстура
        //   f v1/vt1/vn1 v2/vt2/vn2 ..— верш + текстура + нормаль
        //   f v1//vn1 v2//vn2 ...     — верш + нормаль
     private static void ParseFace(string[] coords, ObjModel model)
    {
        var face = new Face();
        for (int i = 1; i < coords.Length; i++)
        {
            string[] parts = coords[i].Split('/');

            int v  = ResolveIndex(parts[0], model.VertCoords.Count);
            int vt = parts.Length > 1 && parts[1].Length > 0
                     ? ResolveIndex(parts[1], model.TexCoords.Count)
                     : -1;
            int vn = parts.Length > 2 && parts[2].Length > 0
                     ? ResolveIndex(parts[2], model.Normals.Count)
                     : -1;

            face.Vertices.Add((v, vt, vn));
        }

        model.Faces.Add(face);
    }
    
    // -1-посл доб эл.     с 1
    private static int ResolveIndex(string coord, int count)
    {
        int idx = int.Parse(coord, CultureInfo.InvariantCulture);
        return idx < 0 ? count + idx : idx - 1;
    }
}

