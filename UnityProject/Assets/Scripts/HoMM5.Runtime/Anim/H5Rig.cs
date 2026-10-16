using System.Collections.Generic;
using UnityEngine;

namespace HoMM5.Anim
{
    /// <summary>
    /// Turns a skinned mesh plus a clip into something that deforms: a bone
    /// hierarchy, bind poses, a <see cref="SkinnedMeshRenderer"/> and a playing
    /// <see cref="Animation"/>.
    ///
    /// The bind poses are computed from the clip's own skeleton rather than read
    /// from the model's. The two are different poses -- the standalone files
    /// under ``bin/Skeletons`` hold the pose the mesh was skinned in, and the
    /// copy inside a clip holds the pose that clip starts from -- and the
    /// standalone ones are all packed with Oodle1, which is not decoded. This is
    /// correct while the clip starts near the bind pose, which adventure-map
    /// idles do, and visibly wrong when it does not: a combat stance 167 degrees
    /// from bind will shred the mesh.
    /// </summary>
    public static class H5Rig
    {
        /// <summary>
        /// Attach the rig to <paramref name="host"/>. Returns the playing
        /// Animation, or null when the clip has no bones to bind to.
        /// </summary>
        public static Animation Build(GameObject host, List<Mesh> meshes,
                                      List<Material> materials, H5Clip clip)
        {
            if (host == null || clip == null || clip.bones.Count == 0) return null;
            if (meshes == null || meshes.Count == 0) return null;

            var rigRoot = new GameObject("Rig");
            rigRoot.transform.SetParent(host.transform, false);
            var bones = H5Anim.BuildRig(clip, rigRoot.transform);
            if (bones.Count == 0) return null;

            // Bind pose: mesh space -> bone space, taken at rest. rigRoot sits at
            // the host's origin with an identity transform, so the bones' local
            // transforms are already in the mesh's own space.
            var hostToWorld = host.transform.localToWorldMatrix;
            var bind = new Matrix4x4[bones.Count];
            for (var i = 0; i < bones.Count; i++)
                bind[i] = bones[i].worldToLocalMatrix * hostToWorld;

            var playable = 0;
            for (var m = 0; m < meshes.Count; m++)
            {
                var mesh = meshes[m];
                if (mesh == null) continue;
                if (mesh.boneWeights == null || mesh.boneWeights.Length == 0) continue;

                mesh.bindposes = bind;

                var go = new GameObject(mesh.name);
                go.transform.SetParent(host.transform, false);
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = bones.ToArray();
                smr.rootBone = bones[0];
                smr.quality = SkinQuality.Bone4;
                smr.updateWhenOffscreen = false;
                if (materials != null && m < materials.Count && materials[m] != null)
                    smr.sharedMaterial = materials[m];
                playable++;
            }
            if (playable == 0) return null;

            var baked = H5Anim.Bake(clip, "Rig");
            if (baked == null) return null;

            var anim = host.AddComponent<Animation>();
            anim.AddClip(baked, baked.name);
            anim.clip = baked;
            anim.playAutomatically = true;
            return anim;
        }
    }
}
