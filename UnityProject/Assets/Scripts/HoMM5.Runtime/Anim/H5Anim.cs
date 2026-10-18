using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HoMM5.Anim
{
    /// <summary>One channel of one bone. ``controls`` holds ``width`` values per
    /// knot: 3 for a position, 4 for a quaternion, 9 for scale-shear.</summary>
    public class H5Curve
    {
        public int degree;
        public int width;
        public float[] knots;
        public float[] controls;

        public int KeyCount => knots != null ? knots.Length : 0;
    }

    public class H5Track
    {
        public string name;
        public H5Curve position;
        public H5Curve orientation;
        public H5Curve scaleShear;
    }

    public class H5Bone
    {
        public string name;
        public int parent;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale = Vector3.one;
        // Granny writes InverseWorldTransform as a row-major, source-axis
        // matrix. Keeping the authored bind matrix avoids reconstructing it
        // from a Transform (which would discard source shear).
        public float[] inverseBind;
    }

    public class H5Clip
    {
        public string name;
        public string source;
        public float duration;
        public float timeStep;
        public List<H5Track> tracks = new List<H5Track>();
        public List<H5Bone> bones = new List<H5Bone>();
    }

    /// <summary>
    /// Reads the animation files the importer wrote from Granny GR2 and bakes
    /// them into Unity clips.
    ///
    /// An animation file carries its own copy of the skeleton it animates, so
    /// one read gives bones, rest pose and curves together. The files under
    /// ``bin/Skeletons`` are the pose the mesh was skinned in and are the ones
    /// inverse bind matrices should come from, but every one of them is packed
    /// with Oodle1, which the importer does not decode -- so the clip's own copy
    /// stands in. That is correct while a clip starts near the bind pose, which
    /// adventure-map idles do, and wrong when it does not.
    /// </summary>
    public static class H5Anim
    {
        public static H5Clip Load(string uid)
        {
            var path = Path.Combine(Core.H5Paths.Root, "Animations", uid + ".json.gz");
            if (!File.Exists(path)) return null;

            string json;
            try
            {
                using (var fs = File.OpenRead(path))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var sr = new StreamReader(gz))
                    json = sr.ReadToEnd();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[HoMM5] cannot read animation {uid}: {e.Message}");
                return null;
            }

            try { return Parse(JObject.Parse(json), uid); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[HoMM5] cannot parse animation {uid}: {e.Message}");
                return null;
            }
        }

        static H5Clip Parse(JObject root, string uid)
        {
            var clip = new H5Clip { name = uid, source = (string)root["source"] ?? "" };

            var skels = root["skeletons"] as JArray;
            if (skels != null && skels.Count > 0)
            {
                var bones = skels[0]["bones"] as JArray;
                if (bones != null)
                {
                    foreach (var b in bones)
                    {
                        var pos = b["position"] as JArray;
                        var q = b["quaternion"] as JArray;
                        var ss = b["scaleShear"] as JArray;
                        clip.bones.Add(new H5Bone
                        {
                            name = (string)b["name"] ?? "",
                            parent = (int?)b["parent"] ?? -1,
                            position = ToUnity(pos),
                            rotation = ToUnityQuat(q),
                            scale = ToUnityScale(ss),
                            inverseBind = ToFloatArray(b["inverseBind"] as JArray),
                        });
                    }
                }
            }

            var anims = root["animations"] as JArray;
            if (anims != null && anims.Count > 0)
            {
                var a = anims[0];
                clip.name = (string)a["name"] ?? uid;
                clip.duration = (float?)a["duration"] ?? 0f;
                clip.timeStep = (float?)a["timeStep"] ?? 0f;

                var tracks = a["tracks"] as JArray;
                if (tracks != null)
                {
                    foreach (var t in tracks)
                    {
                        clip.tracks.Add(new H5Track
                        {
                            name = (string)t["name"] ?? "",
                            position = ReadCurve(t["position"], 3),
                            orientation = ReadCurve(t["orientation"], 4),
                            scaleShear = ReadCurve(t["scaleShear"], 9),
                        });
                    }
                }
            }
            return clip;
        }

        static H5Curve ReadCurve(JToken token, int width)
        {
            var o = token as JObject;
            if (o == null) return null;
            var knots = o["knots"] as JArray;
            var controls = o["controls"] as JArray;
            if (knots == null || controls == null || knots.Count == 0) return null;

            var c = new H5Curve
            {
                degree = (int?)o["degree"] ?? 0,
                width = width,
                knots = new float[knots.Count],
                controls = new float[controls.Count],
            };
            for (var i = 0; i < knots.Count; i++) c.knots[i] = (float)knots[i];
            for (var i = 0; i < controls.Count; i++) c.controls[i] = (float)controls[i];
            return c;
        }

        /// <summary>HoMM5 is Z-up; Unity is Y-up. Swap the two axes, the same
        /// transform the mesh loader applies to positions.</summary>
        static Vector3 ToUnity(JArray a)
        {
            if (a == null || a.Count < 3) return Vector3.zero;
            return new Vector3((float)a[0], (float)a[2], (float)a[1]);
        }

        /// <summary>
        /// The axis swap is a reflection, which no quaternion can express, so the
        /// rotation has to be mirrored as well as reordered: (x,y,z,w) becomes
        /// (x,z,y,-w). NOT verified against the game -- there is no renderer here
        /// to check it against.
        /// </summary>
        static Quaternion ToUnityQuat(JArray a)
        {
            if (a == null || a.Count < 4) return Quaternion.identity;
            return new Quaternion((float)a[0], (float)a[2], (float)a[1], -(float)a[3]);
        }

        static Vector3 ToUnityScale(JArray a)
        {
            if (a == null || a.Count < 9) return Vector3.one;
            // Granny stores the 3x3 scale/shear matrix row-major in source
            // Z-up order. Unity cannot represent shear on a Transform, but the
            // diagonal scale is part of the authored transform and is preserved.
            return new Vector3((float)a[0], (float)a[8], (float)a[4]);
        }

        static float[] ToFloatArray(JArray a)
        {
            if (a == null || a.Count == 0) return null;
            var values = new float[a.Count];
            for (var i = 0; i < a.Count; i++) values[i] = (float)a[i];
            return values;
        }

        /// <summary>
        /// Convert Granny's authored inverse bind matrix to Unity space. The GR2
        /// matrix is row-major and uses the source row-vector convention; Unity
        /// uses column vectors. Transpose first, then apply the same y/z basis
        /// change as the glTF mesh importer.
        /// </summary>
        public static bool TryUnityInverseBind(H5Bone bone, out Matrix4x4 matrix)
        {
            matrix = Matrix4x4.identity;
            var a = bone != null ? bone.inverseBind : null;
            if (a == null || a.Length != 16) return false;
            var axis = new[] { 0, 2, 1, 3 };
            for (var row = 0; row < 4; row++)
            for (var col = 0; col < 4; col++)
                matrix[row, col] = a[axis[col] * 4 + axis[row]];
            return true;
        }

        /// <summary>
        /// One component of one Granny curve at time t.
        ///
        /// The decompiled Granny data exposes the curve degree, knots and one
        /// control tuple per knot. Granny's own converter evaluates these with
        /// De Boor after padding the knot vector with the first and last knot;
        /// this is that algorithm, not a visual approximation between key names.
        /// Degree zero is the constant channel and degree one is the linear
        /// special case of the same padded evaluator.
        /// </summary>
        public static float Sample(H5Curve c, float t, int slot)
        {
            if (c == null || c.knots == null || c.knots.Length == 0 ||
                c.controls == null || c.controls.Length == 0) return 0f;
            var n = c.knots.Length;
            var degree = Mathf.Clamp(c.degree, 0, n - 1);
            if (degree == 0 || n == 1) return At(c, 0, slot);
            if (t <= c.knots[0]) return At(c, 0, slot);
            if (t >= c.knots[n - 1]) return At(c, n - 1, slot);

            var paddedCount = n + degree + 1;
            var padded = new float[paddedCount];
            // Equivalent to Granny's padded_knots(): degree + 1 copies of
            // the first knot, the interior source knots, and degree + 1 copies
            // of the last knot (the source last knot is in the interior slice).
            for (var i = 0; i <= degree; i++) padded[i] = c.knots[0];
            for (var i = degree; i < n; i++) padded[i + 1] = c.knots[i];
            for (var i = 0; i < degree; i++) padded[n + 1 + i] = c.knots[n - 1];

            var span = degree;
            while (span < n && padded[span] <= t) span++;
            span--;
            span = Mathf.Clamp(span, degree, n - 1);

            var d = new float[degree + 1];
            for (var j = 0; j <= degree; j++)
                d[j] = At(c, span - degree + j, slot);
            for (var r = 1; r <= degree; r++)
            {
                for (var j = degree; j >= r; j--)
                {
                    var left = padded[j + span - degree];
                    var right = padded[j + 1 + span - r];
                    var alpha = Mathf.Abs(right - left) > 1e-9f ? (t - left) / (right - left) : 0f;
                    d[j] = Mathf.LerpUnclamped(d[j - 1], d[j], alpha);
                }
            }
            return d[degree];
        }

        static float At(H5Curve c, int key, int slot)
        {
            var i = key * c.width + slot;
            return i >= 0 && i < c.controls.Length ? c.controls[i] : 0f;
        }

        /// <summary>
        /// Sample a quaternion with Granny's spherical De Boor interpolation.
        /// Interpolating quaternion components and normalizing afterwards is not
        /// equivalent for the degree-2 orientation curves used by the creatures.
        /// </summary>
        public static Quaternion SampleQuat(H5Curve c, float t)
        {
            if (c == null || c.knots == null || c.knots.Length == 0 ||
                c.controls == null || c.controls.Length < 4) return Quaternion.identity;
            var n = c.knots.Length;
            var degree = Mathf.Clamp(c.degree, 0, n - 1);
            if (degree == 0 || n == 1) return Normalize(AtQuat(c, 0));

            if (t <= c.knots[0]) return Normalize(AtQuat(c, 0));
            if (t >= c.knots[n - 1]) return Normalize(AtQuat(c, n - 1));

            var padded = new float[n + degree + 1];
            for (var i = 0; i <= degree; i++) padded[i] = c.knots[0];
            for (var i = degree; i < n; i++) padded[i + 1] = c.knots[i];
            for (var i = 0; i < degree; i++) padded[n + 1 + i] = c.knots[n - 1];

            var span = degree;
            while (span < n && padded[span] <= t) span++;
            span = Mathf.Clamp(span - 1, degree, n - 1);
            var d = new Quaternion[degree + 1];
            for (var j = 0; j <= degree; j++) d[j] = AtQuat(c, span - degree + j);
            for (var r = 1; r <= degree; r++)
            {
                for (var j = degree; j >= r; j--)
                {
                    var left = padded[j + span - degree];
                    var right = padded[j + 1 + span - r];
                    var alpha = Mathf.Abs(right - left) > 1e-9f ? (t - left) / (right - left) : 0f;
                    d[j] = Quaternion.SlerpUnclamped(d[j - 1], d[j], alpha);
                }
            }
            return Normalize(d[degree]);
        }

        static Quaternion AtQuat(H5Curve c, int key)
        {
            var i = key * c.width;
            if (i < 0 || i + 3 >= c.controls.Length) return Quaternion.identity;
            return new Quaternion(c.controls[i], c.controls[i + 1], c.controls[i + 2], c.controls[i + 3]);
        }

        static Quaternion Normalize(Quaternion q)
        {
            var m = Mathf.Sqrt(Quaternion.Dot(q, q));
            return m > 1e-6f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m)
                             : Quaternion.identity;
        }

        public static Vector3 SampleVec3(H5Curve c, float t)
        {
            if (c == null) return Vector3.zero;
            return new Vector3(Sample(c, t, 0), Sample(c, t, 1), Sample(c, t, 2));
        }

        public static Vector3 SampleScale(H5Curve c, float t)
        {
            if (c == null) return Vector3.one;
            // Scale/shear is a row-major 3x3 source matrix. The off-diagonal
            // shear cannot be represented by a Unity Transform; retain the
            // diagonal scale in the same axis conversion as the bind pose.
            return new Vector3(Sample(c, t, 0), Sample(c, t, 8), Sample(c, t, 4));
        }

        /// <summary>
        /// Bake a clip into an AnimationClip. Curves are written against the bone
        /// transforms' paths, so the rig must be built with those transforms
        /// parented under <paramref name="rootPath"/> and named by bone name.
        ///
        /// Samples at the authoring frame interval. A bone that turns fast needs a
        /// finer grid or slerp takes the short way between keys -- the shipped
        /// clips run to 171 degrees between 15 fps samples.
        /// </summary>
        public static AnimationClip Bake(H5Clip clip, string rootPath, float maxFps = 60f)
        {
            if (clip == null || clip.duration <= 0f) return null;

            var step = clip.timeStep > 1e-4f ? clip.timeStep : 1f / 30f;
            // Never sample coarser than the authoring rate, never finer than maxFps.
            step = Mathf.Max(step, 1f / maxFps);
            var frames = Mathf.Max(2, Mathf.CeilToInt(clip.duration / step) + 1);

            // H5Rig drives this clip through an Animator PlayableGraph. Keep the
            // generated clip Mecanim-compatible; the deprecated Animation
            // component is deliberately not used.
            var anim = new AnimationClip { name = clip.name, frameRate = 1f / step };
            anim.legacy = false;
            anim.wrapMode = WrapMode.Loop;

            foreach (var t in clip.tracks)
            {
                if (string.IsNullOrEmpty(t.name)) continue;
                var path = string.IsNullOrEmpty(rootPath) ? t.name : rootPath + "/" + t.name;

                if (t.orientation != null)
                {
                    var rx = new AnimationCurve();
                    var ry = new AnimationCurve();
                    var rz = new AnimationCurve();
                    var rw = new AnimationCurve();
                    for (var f = 0; f < frames; f++)
                    {
                        var time = Mathf.Min(f * step, clip.duration);
                        var q = ToUnityQuatRaw(SampleQuat(t.orientation, time));
                        rx.AddKey(time, q.x); ry.AddKey(time, q.y);
                        rz.AddKey(time, q.z); rw.AddKey(time, q.w);
                    }
                    anim.SetCurve(path, typeof(Transform), "localRotation.x", rx);
                    anim.SetCurve(path, typeof(Transform), "localRotation.y", ry);
                    anim.SetCurve(path, typeof(Transform), "localRotation.z", rz);
                    anim.SetCurve(path, typeof(Transform), "localRotation.w", rw);
                }

                if (t.position != null)
                {
                    var px = new AnimationCurve();
                    var py = new AnimationCurve();
                    var pz = new AnimationCurve();
                    for (var f = 0; f < frames; f++)
                    {
                        var time = Mathf.Min(f * step, clip.duration);
                        var v = ToUnity(SampleVec3(t.position, time));
                        px.AddKey(time, v.x); py.AddKey(time, v.y); pz.AddKey(time, v.z);
                    }
                    anim.SetCurve(path, typeof(Transform), "localPosition.x", px);
                    anim.SetCurve(path, typeof(Transform), "localPosition.y", py);
                    anim.SetCurve(path, typeof(Transform), "localPosition.z", pz);
                }

                if (t.scaleShear != null)
                {
                    var sx = new AnimationCurve();
                    var sy = new AnimationCurve();
                    var sz = new AnimationCurve();
                    for (var f = 0; f < frames; f++)
                    {
                        var time = Mathf.Min(f * step, clip.duration);
                        var v = SampleScale(t.scaleShear, time);
                        sx.AddKey(time, v.x); sy.AddKey(time, v.y); sz.AddKey(time, v.z);
                    }
                    anim.SetCurve(path, typeof(Transform), "localScale.x", sx);
                    anim.SetCurve(path, typeof(Transform), "localScale.y", sy);
                    anim.SetCurve(path, typeof(Transform), "localScale.z", sz);
                }
            }
            return anim;
        }

        /// <summary>Apply the Z-up to Y-up mirror to an already-sampled
        /// quaternion, which arrives in the source's own axes.</summary>
        static Quaternion ToUnityQuatRaw(Quaternion q)
        {
            return new Quaternion(q.x, q.z, q.y, -q.w);
        }

        static Vector3 ToUnity(Vector3 v)
        {
            return new Vector3(v.x, v.z, v.y);
        }

        /// <summary>Build the bone hierarchy. Returns the transforms in the order
        /// of <see cref="H5Clip.bones"/>, which is the order the skin's bone
        /// indices address.</summary>
        public static List<Transform> BuildRig(H5Clip clip, Transform parent)
        {
            var bones = new List<Transform>();
            if (clip == null || clip.bones.Count == 0) return bones;

            for (var i = 0; i < clip.bones.Count; i++)
            {
                var b = clip.bones[i];
                var go = new GameObject(string.IsNullOrEmpty(b.name) ? "bone" + i : b.name);
                var tr = go.transform;
                var pi = b.parent;
                tr.SetParent(pi >= 0 && pi < bones.Count ? bones[pi] : parent, false);
                tr.localPosition = b.position;
                tr.localRotation = b.rotation;
                tr.localScale = b.scale;
                bones.Add(tr);
            }
            return bones;
        }
    }
}
