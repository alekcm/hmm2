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
                        clip.bones.Add(new H5Bone
                        {
                            name = (string)b["name"] ?? "",
                            parent = (int?)b["parent"] ?? -1,
                            position = ToUnity(pos),
                            rotation = ToUnityQuat(q),
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

        /// <summary>
        /// One component of one curve at time t.
        ///
        /// Degree 0 is a channel that never moves and degree 1 is linear; both
        /// are exact. Degree 2 and 3 are B-splines whose controls are NOT points
        /// the curve passes through, and the shipped library is mostly degree 2.
        /// Evaluating those properly needs a clamped knot vector; until that is
        /// checked against the game this blends linearly between the per-knot
        /// control sets, which moves in the right direction but under-rotates
        /// where the curve is sharply bent.
        /// </summary>
        public static float Sample(H5Curve c, float t, int slot)
        {
            if (c == null || c.knots == null || c.knots.Length == 0) return 0f;
            var w = c.width;
            var n = c.knots.Length;

            if (c.degree == 0 || n == 1)
                return slot < c.controls.Length ? c.controls[slot] : 0f;

            if (t <= c.knots[0]) return At(c, 0, slot);
            if (t >= c.knots[n - 1]) return At(c, n - 1, slot);

            for (var i = 1; i < n; i++)
            {
                if (t < c.knots[i])
                {
                    var span = c.knots[i] - c.knots[i - 1];
                    var f = span > 1e-9f ? (t - c.knots[i - 1]) / span : 0f;
                    return Mathf.Lerp(At(c, i - 1, slot), At(c, i, slot), f);
                }
            }
            return At(c, n - 1, slot);
        }

        static float At(H5Curve c, int key, int slot)
        {
            var i = key * c.width + slot;
            return i < c.controls.Length ? c.controls[i] : 0f;
        }

        /// <summary>Sample a quaternion track and renormalize; blending four
        /// independent components drifts off the unit sphere.</summary>
        public static Quaternion SampleQuat(H5Curve c, float t)
        {
            if (c == null) return Quaternion.identity;
            var q = new Quaternion(Sample(c, t, 0), Sample(c, t, 1),
                                   Sample(c, t, 2), Sample(c, t, 3));
            var m = Mathf.Sqrt(Quaternion.Dot(q, q));
            return m > 1e-6f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m)
                             : Quaternion.identity;
        }

        public static Vector3 SampleVec3(H5Curve c, float t)
        {
            if (c == null) return Vector3.zero;
            return new Vector3(Sample(c, t, 0), Sample(c, t, 1), Sample(c, t, 2));
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

            var anim = new AnimationClip { name = clip.name, frameRate = 1f / step };
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
                bones.Add(tr);
            }
            return bones;
        }
    }
}
