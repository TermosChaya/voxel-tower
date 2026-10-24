using System.Runtime.InteropServices;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Hands the core's mesh buffers to Godot. The array of arrays is let go at once, here on the main thread: left to the
/// collector it is destroyed on its own thread while the main one is in the engine, which can bring the game down.
/// </summary>
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
