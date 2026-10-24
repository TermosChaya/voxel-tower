using System.Runtime.InteropServices;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>Hands the core's mesh buffers to Godot.</summary>
public static class GodotMesh
{
    public static ArrayMesh From(MeshBuffers b)
    {
        if (b == null || b.Quads == 0) return null;
        var mesh = new ArrayMesh();
        AddTo(mesh, b);
        return mesh;
    }

    /// <summary>Adds the buffers to a mesh as a surface of their own.</summary>
    public static void AddTo(ArrayMesh mesh, MeshBuffers b)
    {
        // let go at once: left to the garbage collector, a Godot array is released from its own thread, which has
        // brought the game down
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = MemoryMarshal.Cast<float, Vector3>(b.Pos).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = MemoryMarshal.Cast<float, Vector3>(b.Nrm).ToArray();
        if (b.Col != null) arrays[(int)Mesh.ArrayType.Color] = MemoryMarshal.Cast<float, Color>(b.Col).ToArray();
        if (b.Uv != null) arrays[(int)Mesh.ArrayType.TexUV] = MemoryMarshal.Cast<float, Vector2>(b.Uv).ToArray();
        if (b.Uv2 != null) arrays[(int)Mesh.ArrayType.TexUV2] = MemoryMarshal.Cast<float, Vector2>(b.Uv2).ToArray();
        arrays[(int)Mesh.ArrayType.Index] = b.Idx;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
}
