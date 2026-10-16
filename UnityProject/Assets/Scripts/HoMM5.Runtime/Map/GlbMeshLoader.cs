using System.Collections.Generic;
using HoMM5.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace HoMM5.Map
{
    /// <summary>
    /// Reader for the binary glTF the importer writes.
    ///
    /// Done in-house rather than through a glTF package for two reasons: the
    /// files are a known, closed subset (one buffer, POSITION/NORMAL/TEXCOORD_0
    /// plus u16 indices), and the coordinate fix-up has to happen on the way in.
    ///
    /// HoMM5 geometry is Z-up and wound counter-clockwise against a right-handed
    /// cross product.  Unity is Y-up and culls counter-clockwise faces.  Swapping
    /// y and z reverses orientation, so the winding has to be reversed too --
    /// measured over 53224 shipped triangles, the swap alone leaves 99.6% of
    /// faces pointing away and reversed winding restores them.
    /// </summary>
    public static class GlbMeshLoader
    {
        const uint GlbMagic = 0x46546C67;
        const uint ChunkJson = 0x4E4F534A;
        const uint ChunkBin = 0x004E4942;

        const int ComponentByte = 5120;
        const int ComponentUByte = 5121;
        const int ComponentShort = 5122;
        const int ComponentUShort = 5123;
        const int ComponentFloat = 5126;

        /// <summary>Load every mesh node of a <c>.glb</c>.  Null if the file is absent.</summary>
        public static List<Mesh> Load(string path, string namePrefix = null)
        {
            var bytes = H5Paths.ReadAllBytes(path);
            if (bytes == null) return null;
            return LoadFromBytes(bytes, namePrefix ?? System.IO.Path.GetFileNameWithoutExtension(path));
        }

        public static List<Mesh> LoadFromBytes(byte[] bytes, string namePrefix)
        {
            if (bytes.Length < 20) return null;
            if (System.BitConverter.ToUInt32(bytes, 0) != GlbMagic) return null;

            var offset = 12;
            string json = null;
            byte[] bin = null;

            while (offset + 8 <= bytes.Length)
            {
                var len = System.BitConverter.ToUInt32(bytes, offset);
                var type = System.BitConverter.ToUInt32(bytes, offset + 4);
                var start = offset + 8;
                if (start + len > bytes.Length) break;
                if (type == ChunkJson)
                    json = System.Text.Encoding.UTF8.GetString(bytes, start, (int)len);
                else if (type == ChunkBin)
                {
                    bin = new byte[len];
                    System.Buffer.BlockCopy(bytes, start, bin, 0, (int)len);
                }
                offset = start + (int)len;
            }

            if (json == null || bin == null) return null;

            JObject doc;
            try { doc = JObject.Parse(json); }
            catch { return null; }

            var bufferViews = doc["bufferViews"] as JArray;
            var accessors = doc["accessors"] as JArray;
            var meshes = doc["meshes"] as JArray;
            if (bufferViews == null || accessors == null || meshes == null) return null;

            var result = new List<Mesh>(meshes.Count);
            for (var mi = 0; mi < meshes.Count; mi++)
            {
                var prims = meshes[mi]["primitives"] as JArray;
                if (prims == null || prims.Count == 0) continue;
                var p = prims[0];
                var attrs = p["attributes"];
                if (attrs == null) continue;

                var positions = ReadVec3(accessors, bufferViews, bin, attrs["POSITION"]);
                if (positions == null || positions.Count == 0) continue;

                var normals = ReadVec3(accessors, bufferViews, bin, attrs["NORMAL"]);
                var uvs = ReadVec2(accessors, bufferViews, bin, attrs["TEXCOORD_0"]);
                var indices = ReadIndices(accessors, bufferViews, bin, p["indices"]);

                var name = meshes[mi]["name"] != null ? meshes[mi]["name"].ToString() : namePrefix + "_" + mi;
                var mesh = BuildMesh(name, positions, normals, uvs, indices);
                if (mesh != null) result.Add(mesh);
            }
            return result;
        }

        static Mesh BuildMesh(string name, List<Vector3> positions, List<Vector3> normals,
                              List<Vector2> uvs, List<int> indices)
        {
            // Flip winding: the y/z swap above reverses orientation.
            for (var i = 0; i + 2 < indices.Count; i += 3)
            {
                var t = indices[i + 1];
                indices[i + 1] = indices[i + 2];
                indices[i + 2] = t;
            }

            var mesh = new Mesh { name = name };
            mesh.indexFormat = positions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            if (normals != null && normals.Count == positions.Count) mesh.SetNormals(normals);
            if (uvs != null && uvs.Count == positions.Count) mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            if (normals == null || normals.Count != positions.Count) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // -- accessor decoding ---------------------------------------------

        static (int offset, int length) Resolve(JArray accessors, JArray bufferViews, byte[] bin, JToken token)
        {
            if (token == null) return (-1, 0);
            var acc = accessors[(int)token] as JObject;
            if (acc == null) return (-1, 0);
            var bvIndex = (int)(acc["bufferView"] ?? 0);
            if (bvIndex < 0 || bvIndex >= bufferViews.Count) return (-1, 0);
            var bv = bufferViews[bvIndex] as JObject;
            if (bv == null) return (-1, 0);
            var bvOffset = (int)(bv["byteOffset"] ?? 0);
            var bvLength = (int)(bv["byteLength"] ?? 0);
            var accOffset = (int)(acc["byteOffset"] ?? 0);
            var count = (int)(acc["count"] ?? 0);
            var compType = (int)(acc["componentType"] ?? ComponentFloat);
            var comps = ComponentCount(acc["type"] != null ? acc["type"].ToString() : "SCALAR");
            var elemSize = ComponentSize(compType) * comps;
            var start = bvOffset + accOffset;
            var total = count * elemSize;
            if (start < 0 || total <= 0 || start + total > bin.Length) return (-1, 0);
            return (start, total);
        }

        static int ComponentCount(string type)
        {
            switch (type)
            {
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                case "MAT4": return 16;
                default: return 1;
            }
        }

        static int ComponentSize(int componentType)
        {
            switch (componentType)
            {
                case ComponentByte:
                case ComponentUByte: return 1;
                case ComponentShort:
                case ComponentUShort: return 2;
                case ComponentFloat: return 4;
                default: return 4;
            }
        }

        /// <summary>
        /// Read a vec3 accessor and convert Z-up to Y-up: <c>(x, y, z) -> (x, z, y)</c>.
        /// </summary>
        static List<Vector3> ReadVec3(JArray accessors, JArray bufferViews, byte[] bin, JToken token)
        {
            var (off, len) = Resolve(accessors, bufferViews, bin, token);
            if (off < 0) return null;
            var count = len / 12;
            var list = new List<Vector3>(count);
            for (var i = 0; i < count; i++)
            {
                var o = off + i * 12;
                var x = System.BitConverter.ToSingle(bin, o);
                var y = System.BitConverter.ToSingle(bin, o + 4);
                var z = System.BitConverter.ToSingle(bin, o + 8);
                list.Add(new Vector3(x, z, y));
            }
            return list;
        }

        static List<Vector2> ReadVec2(JArray accessors, JArray bufferViews, byte[] bin, JToken token)
        {
            var (off, len) = Resolve(accessors, bufferViews, bin, token);
            if (off < 0) return null;
            var count = len / 8;
            var list = new List<Vector2>(count);
            for (var i = 0; i < count; i++)
            {
                var o = off + i * 8;
                list.Add(new Vector2(System.BitConverter.ToSingle(bin, o),
                                     System.BitConverter.ToSingle(bin, o + 4)));
            }
            return list;
        }

        static List<int> ReadIndices(JArray accessors, JArray bufferViews, byte[] bin, JToken token)
        {
            var (off, len) = Resolve(accessors, bufferViews, bin, token);
            if (off < 0) return new List<int>();
            var acc = accessors[(int)token] as JObject;
            var compType = (int)(acc["componentType"] ?? ComponentUShort);
            var list = new List<int>(len / ComponentSize(compType));
            if (compType == ComponentUShort)
            {
                for (var o = off; o + 2 <= off + len; o += 2)
                    list.Add(System.BitConverter.ToUInt16(bin, o));
            }
            else if (compType == ComponentShort)
            {
                for (var o = off; o + 2 <= off + len; o += 2)
                    list.Add(System.BitConverter.ToInt16(bin, o));
            }
            else if (compType == ComponentUByte)
            {
                for (var o = off; o < off + len; o++) list.Add(bin[o]);
            }
            else
            {
                for (var o = off; o + 4 <= off + len; o += 4)
                    list.Add(System.BitConverter.ToInt32(bin, o));
            }
            return list;
        }
    }
}
