using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HoMM5.Anim
{
    /// <summary>
    /// Runtime rig for an imported Granny model.
    ///
    /// HoMM5 clips are sampled into ordinary, non-legacy Unity AnimationClips.
    /// They are played through an Animator and a PlayableGraph rather than the
    /// deprecated Animation component. This is important in Unity 6: assigning
    /// a name to a legacy clip can change the selected clip while leaving every
    /// bone still, whereas a graph owns and evaluates the actual clip playables.
    /// </summary>
    public sealed class H5AnimationPlayer : MonoBehaviour
    {
        sealed class Entry
        {
            public string key;
            public AnimationClip clip;
            public AnimationClipPlayable playable;
            public int input;
        }

        readonly List<Entry> _entries = new List<Entry>();
        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        Animator _animator;
        int _active = -1;
        int _target = -1;
        float _fadeDuration;
        float _fadeTime;

        public bool IsPlaying => _graph.IsValid() && _active >= 0;
        public string CurrentKey => _active >= 0 && _active < _entries.Count ? _entries[_active].key : null;

        public void Initialize(Animator animator, AnimationClip firstClip, string firstKey)
        {
            _animator = animator;
            _graph = PlayableGraph.Create("HoMM5 animation graph");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            // Unity 6.6 removed the effect of normalizeWeights; use the
            // non-obsolete overload so the graph has no compiler warning.
            _mixer = AnimationMixerPlayable.Create(_graph, 0);
            var output = AnimationPlayableOutput.Create(_graph, "HoMM5 animation", _animator);
            output.SetSourcePlayable(_mixer);
            if (firstClip != null) AddClip(firstClip, firstKey, true);
            _graph.Play();
            if (_entries.Count > 0) Play(_entries[0].key, 0f);
        }

        public bool AddClip(AnimationClip clip, string key, bool loop)
        {
            if (!_graph.IsValid() || clip == null || string.IsNullOrEmpty(key)) return false;
            // Runtime clips produced by H5Anim.Bake are intentionally Mecanim
            // clips. Setting this explicitly protects against an imported asset
            // carrying the legacy bit from an earlier Unity version.
            clip.legacy = false;
            clip.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            var playable = AnimationClipPlayable.Create(_graph, clip);
            playable.SetApplyFootIK(false);
            var input = _entries.Count;
            _mixer.SetInputCount(input + 1);
            _graph.Connect(playable, 0, _mixer, input);
            _mixer.SetInputWeight(input, 0f);
            _entries.Add(new Entry { key = key, clip = clip, playable = playable, input = input });
            return true;
        }

        public bool Play(string key, float fadeSeconds)
        {
            var index = -1;
            for (var i = 0; i < _entries.Count; i++)
                if (_entries[i].key == key) { index = i; break; }
            if (index < 0) return false;

            if (_active < 0 || fadeSeconds <= 0f)
            {
                for (var i = 0; i < _entries.Count; i++) _mixer.SetInputWeight(i, i == index ? 1f : 0f);
                _active = _target = index;
                _fadeDuration = _fadeTime = 0f;
                return true;
            }

            _target = index;
            _fadeDuration = Mathf.Max(0.001f, fadeSeconds);
            _fadeTime = 0f;
            return true;
        }

        void Update()
        {
            if (!_graph.IsValid() || _target < 0 || _target == _active) return;
            _fadeTime += Time.deltaTime;
            var t = Mathf.Clamp01(_fadeTime / _fadeDuration);
            var from = _active;
            var to = _target;
            _mixer.SetInputWeight(from, 1f - t);
            _mixer.SetInputWeight(to, t);
            if (t >= 1f)
            {
                for (var i = 0; i < _entries.Count; i++)
                    _mixer.SetInputWeight(i, i == to ? 1f : 0f);
                _active = to;
                _fadeDuration = _fadeTime = 0f;
            }
        }

        void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }

    /// <summary>
    /// Creates the source skeleton, binds imported meshes to it and installs the
    /// non-legacy PlayableGraph player used by adventure-map objects.
    /// </summary>
    public static class H5Rig
    {
        public static H5AnimationPlayer Build(GameObject host, List<Mesh> meshes,
                                               List<Material> materials, H5Clip clip)
        {
            if (host == null || clip == null || clip.bones.Count == 0) return null;
            if (meshes == null || meshes.Count == 0) return null;

            var rigRoot = new GameObject("Rig");
            rigRoot.transform.SetParent(host.transform, false);
            var bones = H5Anim.BuildRig(clip, rigRoot.transform);
            if (bones.Count == 0) return null;

            // Bind pose: mesh space -> bone space, taken at rest. The rig root
            // is identity under the host, so these are the source mesh-space
            // bone transforms converted to inverse bind matrices.
            var hostToWorld = host.transform.localToWorldMatrix;
            var bind = new Matrix4x4[bones.Count];
            var authoredBind = true;
            for (var i = 0; i < clip.bones.Count; i++)
            {
                if (!H5Anim.TryUnityInverseBind(clip.bones[i], out _))
                {
                    authoredBind = false;
                    break;
                }
            }
            for (var i = 0; i < bones.Count; i++)
            {
                // Prefer Granny's exact inverse bind (including shear). The
                // rest-pose reconstruction is retained only for old imports
                // produced before inverseBind was exported.
                if (!authoredBind || !H5Anim.TryUnityInverseBind(clip.bones[i], out bind[i]))
                    bind[i] = bones[i].worldToLocalMatrix * hostToWorld;
            }

            var skinnedCount = 0;
            for (var m = 0; m < meshes.Count; m++)
            {
                var mesh = meshes[m];
                if (mesh != null && mesh.boneWeights != null && mesh.boneWeights.Length > 0)
                    skinnedCount++;
            }
            if (skinnedCount == 0) return null;
            var baked = H5Anim.Bake(clip, "Rig");
            if (baked == null) return null;

            // A source model can mix skinned body meshes with unskinned weapon,
            // saddle or effect meshes. Keep those pieces in the animated build;
            // dropping them when the first skinned submesh succeeds makes a
            // creature look incomplete even though its clip is playing.
            var playable = 0;
            for (var m = 0; m < meshes.Count; m++)
            {
                var mesh = meshes[m];
                if (mesh == null) continue;
                var go = new GameObject(mesh.name);
                go.transform.SetParent(host.transform, false);
                var material = materials != null && m < materials.Count ? materials[m] : null;
                if (mesh.boneWeights == null || mesh.boneWeights.Length == 0)
                {
                    var filter = go.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    continue;
                }
                mesh.bindposes = bind;
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = bones.ToArray();
                smr.rootBone = bones[0];
                smr.quality = SkinQuality.Bone4;
                smr.updateWhenOffscreen = false;
                smr.sharedMaterial = material;
                playable++;
            }
            if (playable == 0) return null;
            var animator = host.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var player = host.AddComponent<H5AnimationPlayer>();
            player.Initialize(animator, baked, "idle");
            return player;
        }
    }
}
