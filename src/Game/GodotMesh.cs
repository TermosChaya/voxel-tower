using System;
using System.Runtime.InteropServices;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Hands the core's mesh buffers to Godot. Compressed, a vertex is a third smaller on the graphics card: its position in
/// sixteen bits a side within the mesh's bounds, its normal folded into four bytes, its uvs in sixteen bits each (what
/// the valley's meshes hold — corner shades, glow, sway and the skin's number — loses nothing to it). A mesh can be made
/// on a worker thread: Godot queues what it sends the card for the main thread. The Godot array and the values put
/// in it are let go at once, on the thread that made them, never left for the garbage collector to release from a
/// thread of its own (which has brought the game down, and keeps the vertices in memory till it comes round).
/// </summary>
public static class GodotMesh
{
    public static ArrayMesh From(MeshBuffers b, bool compress = false)
    {
        if (b == null || b.Quads == 0) return null;
        var mesh = new ArrayMesh();
        AddTo(mesh, b, compress);
        return mesh;
    }

    /// <summary>Adds the buffers to a mesh as a surface of their own.</summary>
    public static void AddTo(ArrayMesh mesh, MeshBuffers b, bool compress = false)
    {
        // straight from the core's arrays into Godot's, only as much of them as the mesh holds (borrowed ones are longer)
        int v = b.Vertices;
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        Put(arrays, Mesh.ArrayType.Vertex, MemoryMarshal.Cast<float, Vector3>(b.Pos.AsSpan(0, v * 3)));
        Put(arrays, Mesh.ArrayType.Normal, MemoryMarshal.Cast<float, Vector3>(b.Nrm.AsSpan(0, v * 3)));
        if (b.Col != null) Put(arrays, Mesh.ArrayType.Color, MemoryMarshal.Cast<float, Color>(b.Col.AsSpan(0, v * 4)));
        if (b.Uv != null) Put(arrays, Mesh.ArrayType.TexUV, MemoryMarshal.Cast<float, Vector2>(b.Uv.AsSpan(0, v * 2)));
        if (b.Uv2 != null) Put(arrays, Mesh.ArrayType.TexUV2, MemoryMarshal.Cast<float, Vector2>(b.Uv2.AsSpan(0, v * 2)));
        Put(arrays, Mesh.ArrayType.Index, b.Idx.AsSpan(0, b.Quads * 6));
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: compress ? Mesh.ArrayFormat.FlagCompressAttributes : 0);
    }

    /// <summary>One of a surface's arrays put in its place, the value that carried it let go at once.</summary>
    static void Put(Godot.Collections.Array arrays, Mesh.ArrayType at, Variant value)
    {
        arrays[(int)at] = value;
        value.Dispose();
    }
}
