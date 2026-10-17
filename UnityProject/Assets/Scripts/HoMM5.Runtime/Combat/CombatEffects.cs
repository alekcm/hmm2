using System;
using System.Collections.Generic;
using HoMM5.Audio;
using HoMM5.Core;
using HoMM5.Data;
using UnityEngine;

namespace HoMM5.Combat
{
    /// <summary>
    /// What a battle looks and sounds like beyond the unit animation: each event on
    /// <see cref="CombatGameState.Presented"/> becomes sounds from the creature's
    /// animation set, a projectile for shots and ranged spells, impact bursts, death
    /// smoke, buff auras, and the particle textures each SpellVisual names.
    ///
    /// No prefabs: emitters and projectiles are built in code and remove themselves
    /// when they finish. Spell art comes from the game's own textures where the
    /// manifest has them; anything missing falls back to a soft glow tinted by the
    /// spell's element.
    /// </summary>
    public class CombatEffects : MonoBehaviour
    {
        const float ProjectileSpeedCells = 7f;   // cells per second
        const float MeleeImpactDelay = 0.12f;    // the lunge has finished by then
        const float MinFlightSeconds = 0.22f;
        const float MaxFlightSeconds = 1.2f;
        const int MaxSpellTextures = 6;

        static readonly List<Texture> NoTextures = new List<Texture>();
        static Gradient _fadeOut;

        CombatGameState _state;
        CombatSceneBuilder _scene;
        H5TextureCache _textures;
        Camera _camera;
        float _cell = 1f;

        Shader _additive;
        Shader _alpha;
        bool _shaderWarned;
        Texture2D _glow;
        readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();

        readonly List<Projectile> _projectiles = new List<Projectile>();
        readonly List<Scheduled> _scheduled = new List<Scheduled>();

        /// <summary>Wire the effects to a battlefield. Call before <see cref="Bind"/>.</summary>
        public void Setup(CombatSceneBuilder scene, H5TextureCache textures, Camera camera)
        {
            _scene = scene;
            _textures = textures;
            _camera = camera;
            if (scene != null)
            {
                var step = Vector3.Distance(scene.CellToWorld(0, 0), scene.CellToWorld(1, 0));
                if (step > 0.01f) _cell = step;
            }
        }

        /// <summary>Start listening to a fight. Replaces any earlier subscription.</summary>
        public void Bind(CombatGameState state)
        {
            Unbind();
            _state = state;
            if (_state != null) _state.Presented += OnPresented;
        }

        void Unbind()
        {
            if (_state != null) _state.Presented -= OnPresented;
            _state = null;
        }

        void OnDestroy()
        {
            Unbind();
            foreach (var p in _projectiles)
            {
                if (p.Head != null) Destroy(p.Head.gameObject);
                if (p.Trail != null) Destroy(p.Trail.gameObject);
            }
            _projectiles.Clear();
            _scheduled.Clear();

            // Emitters still on screen keep their materials for a moment, so the
            // materials and the glow are destroyed late rather than now.
            foreach (var m in _materials.Values)
                if (m != null) Destroy(m, 3f);
            _materials.Clear();
            if (_glow != null) Destroy(_glow, 3f);
            _glow = null;
        }

        void Update()
        {
            UpdateProjectiles();
            RunScheduled();
        }

        // -- events --------------------------------------------------------

        void OnPresented(CombatVisual visual)
        {
            if (visual == null) return;
            switch (visual.Kind)
            {
                case CombatVisualKind.Strike:
                    Strike(visual);
                    break;
                case CombatVisualKind.Death:
                    Death(visual.Target);
                    break;
                case CombatVisualKind.Spell:
                    CastSpell(visual);
                    break;
                case CombatVisualKind.Summon:
                    Summon(visual.Target);
                    break;
                case CombatVisualKind.Aid:
                    Aid(visual.Target);
                    break;
            }
        }

        void Strike(CombatVisual v)
        {
            var attacker = v.Source;
            var target = v.Target;
            if (attacker == null || target == null) return;

            PlayUnitSound(attacker, v.Ranged ? "rangeattack" : "attack", "attack");
            var to = UnitPoint(target);
            var missed = v.Missed;

            if (v.Ranged)
            {
                var from = UnitPoint(attacker);
                Fly(from, to, null, new Color(1f, 0.95f, 0.8f), 0.3f * _cell, 0.12f,
                    () => Landed(target, to, missed, NoTextures, new Color(1f, 0.85f, 0.5f), 0.5f),
                    new Color(1f, 0.92f, 0.7f));
            }
            else
            {
                Schedule(MeleeImpactDelay, () =>
                    Landed(target, to, missed, NoTextures, new Color(1f, 0.85f, 0.5f), 0.5f));
            }
        }

        /// <summary>A blow arrived: a hit reaction, or a ghostly puff when it passed through.</summary>
        void Landed(CombatUnit target, Vector3 point, bool missed, List<Texture> textures, Color tint, float scale)
        {
            if (missed)
            {
                Burst(point, GlowMaterial(true), new Emit
                {
                    Color = new Color(0.85f, 0.9f, 1f, 0.6f), Size = 0.6f * _cell, Lifetime = 0.8f,
                    Speed = 0.6f * _cell, Radius = 0.3f * _cell, Burst = 10, Gravity = -0.2f,
                });
                return;
            }
            Impact(target, point, textures, tint, scale);
        }

        void Death(CombatUnit unit)
        {
            if (unit == null) return;
            PlayUnitSound(unit, "death");
            var at = UnitPoint(unit) - Vector3.up * _cell * 0.4f;
            Burst(at, ParticleMaterial(null, false), new Emit
            {
                Color = new Color(0.8f, 0.78f, 0.74f, 0.85f), Size = 0.9f * _cell, Lifetime = 1.4f,
                Speed = 0.8f * _cell, Radius = 0.4f * _cell, Burst = 18, Gravity = -0.1f,
            });
        }

        void Summon(CombatUnit unit)
        {
            if (unit == null) return;
            var at = UnitPoint(unit);
            var green = new Color(0.6f, 1f, 0.7f);
            Burst(at, GlowMaterial(true), new Emit
            {
                Color = green, Size = 1.5f * _cell, Lifetime = 0.8f, Speed = 0f, Radius = 0.02f, Burst = 1,
            });
            Burst(at, GlowMaterial(true), new Emit
            {
                Color = green, Size = 0.3f * _cell, Lifetime = 0.9f, Speed = 2f * _cell,
                Radius = 0.8f * _cell, Burst = 30,
            });
        }

        void Aid(CombatUnit unit)
        {
            if (unit == null) return;
            Burst(UnitPoint(unit), GlowMaterial(true), new Emit
            {
                Color = new Color(0.5f, 1f, 0.5f), Size = 0.15f * _cell, Lifetime = 1f,
                Speed = 1.2f * _cell, Radius = 0.5f * _cell, Burst = 20, Gravity = -0.3f,
            });
        }

        void CastSpell(CombatVisual v)
        {
            var spell = v.Spell;
            if (spell == null) return;

            var info = FirstVisual(spell);
            if (info != null && !string.IsNullOrEmpty(info.sound)) H5Audio.Play(info.sound);

            var textures = VisualTextures(info);
            // Textured spells keep their own colours; untextured ones take the element.
            var color = textures.Count > 0 ? Color.white : SpellTint(spell);
            var target = v.Target;
            var landing = target != null ? UnitPoint(target)
                : v.HasCell ? CellPoint(v.CellX, v.CellY) + Vector3.up * _cell * 0.5f
                : Center();

            if (IsOffensive(spell))
            {
                var from = CasterPoint(v.Side);
                var scale = spell.isAreaAttack ? 1.6f : 1.1f;
                Fly(from, landing, PickTexture(textures, null), color, 0.9f * _cell, 0.12f,
                    () => Impact(target, landing, textures, color, scale), color);
            }
            else
            {
                var delay = info != null ? Mathf.Clamp(info.hitDelay, 0f, 1.5f) : 0f;
                var seconds = info != null && info.particleDuration > 0f
                    ? Mathf.Clamp(info.particleDuration, 0.8f, 3f) : 1.2f;
                Schedule(delay, () => Aura(landing, textures, color, seconds));
            }
        }

        // -- building blocks -------------------------------------------------

        /// <summary>A flash and a spray of sparks where something lands.</summary>
        void Impact(CombatUnit target, Vector3 point, List<Texture> textures, Color tint, float scale)
        {
            if (target != null && target.IsAlive) PlayUnitSound(target, "hit");
            var color = textures.Count > 0 ? Color.white : tint;
            Burst(point, ParticleMaterial(PickTexture(textures, null), true), new Emit
            {
                Color = color, Size = 0.35f * _cell * scale, Lifetime = 0.7f, Speed = 2.5f * _cell * scale,
                Radius = 0.25f * _cell * scale, Burst = 28, Gravity = 0.5f,
            });
            Burst(point, ParticleMaterial(PickTexture(textures, null), true), new Emit
            {
                Color = color, Size = 1.4f * _cell * scale, Lifetime = 0.3f, Speed = 0f,
                Radius = 0.02f, Burst = 1,
            });
        }

        /// <summary>A soft cloud around a unit or cell that a buff or debuff covers.</summary>
        void Aura(Vector3 point, List<Texture> textures, Color tint, float seconds)
        {
            var color = textures.Count > 0 ? Color.white : tint;
            Spawn("fx_aura", point, ParticleMaterial(PickTexture(textures, null), true), new Emit
            {
                Color = color, Size = 0.7f * _cell, Lifetime = seconds, Speed = 0.6f * _cell,
                Radius = 0.8f * _cell, Gravity = -0.25f, Rate = 18f,
            });
        }

        /// <summary>A projectile that flies from one point to another and calls back on arrival.</summary>
        void Fly(Vector3 from, Vector3 to, Texture texture, Color tint, float size, float arc,
                 Action onArrive, Color trailColor)
        {
            var head = GameObject.CreatePrimitive(PrimitiveType.Quad);
            head.name = "fx_head";
            var collider = head.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            head.transform.position = from;
            head.transform.localScale = Vector3.one * size;
            var headRenderer = head.GetComponent<MeshRenderer>();
            if (headRenderer != null)
            {
                headRenderer.sharedMaterial = SpriteMaterial(texture, tint);
                headRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                headRenderer.receiveShadows = false;
            }

            var distance = Vector3.Distance(from, to);
            var seconds = Mathf.Clamp(distance / (ProjectileSpeedCells * _cell), MinFlightSeconds, MaxFlightSeconds);
            var trail = Spawn("fx_trail", from, ParticleMaterial(null, true), new Emit
            {
                Color = trailColor, Size = size * 0.5f, Lifetime = 0.4f, Speed = 0.1f,
                Radius = 0.03f, Rate = 70f, Loop = true,
            });

            _projectiles.Add(new Projectile
            {
                Head = head.transform,
                Trail = trail,
                From = from,
                To = to,
                Start = Time.time,
                Duration = seconds,
                Arc = arc * distance,
                OnArrive = onArrive,
            });
        }

        void UpdateProjectiles()
        {
            for (var i = _projectiles.Count - 1; i >= 0; i--)
            {
                var p = _projectiles[i];
                if (p.Head == null)
                {
                    _projectiles.RemoveAt(i);
                    continue;
                }

                var t = Mathf.Clamp01((Time.time - p.Start) / p.Duration);
                var position = Vector3.Lerp(p.From, p.To, t) + Vector3.up * (p.Arc * 4f * t * (1f - t));
                p.Head.position = position;
                if (p.Trail != null) p.Trail.transform.position = position;
                if (_camera != null) p.Head.rotation = _camera.transform.rotation;
                if (t < 1f) continue;

                _projectiles.RemoveAt(i);
                Destroy(p.Head.gameObject);
                if (p.Trail != null)
                {
                    p.Trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    Destroy(p.Trail.gameObject, 2f);
                }
                if (p.OnArrive != null) p.OnArrive();
            }
        }

        void Burst(Vector3 at, Material material, Emit emit) => Spawn("fx_burst", at, material, emit);

        /// <summary>
        /// One particle system, configured from <paramref name="e"/>. A one-off emits
        /// its burst and destroys itself; a looping emitter lives until its owner stops it.
        /// </summary>
        ParticleSystem Spawn(string name, Vector3 at, Material material, Emit e)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = e.Loop;
            main.duration = Mathf.Max(0.1f, e.Lifetime);
            main.startLifetime = e.Lifetime;
            main.startSpeed = e.Speed;
            main.startSize = e.Size;
            main.startColor = e.Color;
            main.gravityModifier = e.Gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Clamp(e.Burst * 2 + Mathf.CeilToInt(e.Rate * e.Lifetime) * 2 + 8, 16, 400);
            main.stopAction = e.Loop ? ParticleSystemStopAction.None : ParticleSystemStopAction.Destroy;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.01f, e.Radius);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = e.Rate;
            if (e.Burst > 0)
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)e.Burst) });

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            colorOverLife.color = new ParticleSystem.MinMaxGradient(FadeOut());

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            ps.Play();
            if (!e.Loop) Destroy(go, e.Lifetime + 2f);
            return ps;
        }

        static Gradient FadeOut()
        {
            if (_fadeOut != null) return _fadeOut;
            _fadeOut = new Gradient();
            _fadeOut.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f),
                });
            return _fadeOut;
        }

        sealed class Emit
        {
            public Color Color = Color.white;
            public float Size = 0.3f;
            public float Lifetime = 0.6f;
            public float Speed = 1f;
            public float Radius = 0.2f;
            public float Gravity;
            public int Burst;
            public float Rate;
            public bool Loop;
        }

        sealed class Projectile
        {
            public Transform Head;
            public ParticleSystem Trail;
            public Vector3 From;
            public Vector3 To;
            public float Start;
            public float Duration;
            public float Arc;
            public Action OnArrive;
        }

        struct Scheduled
        {
            public float At;
            public Action Run;
        }

        void Schedule(float delay, Action run)
        {
            if (delay <= 0f)
            {
                run();
                return;
            }
            _scheduled.Add(new Scheduled { At = Time.time + delay, Run = run });
        }

        void RunScheduled()
        {
            var i = 0;
            while (i < _scheduled.Count)
            {
                if (Time.time < _scheduled[i].At)
                {
                    i++;
                    continue;
                }
                var run = _scheduled[i].Run;
                _scheduled.RemoveAt(i);
                run();
            }
        }

        // -- materials and textures ---------------------------------------------

        void EnsureShaders()
        {
            if (_additive != null && _alpha != null) return;
            _additive = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default");
            _alpha = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
            if ((_additive == null || _alpha == null) && !_shaderWarned)
            {
                _shaderWarned = true;
                Debug.LogWarning("[HoMM5] no particle shader found; battle effects will not draw.");
            }
        }

        /// <summary>A particle material, shared per texture and blend mode.</summary>
        Material ParticleMaterial(Texture texture, bool additive)
        {
            EnsureShaders();
            var shader = additive ? _additive : _alpha;
            if (shader == null) return null;

            var key = (additive ? "add|" : "alpha|") + (texture != null ? texture.GetEntityId().ToString() : "none");
            Material m;
            if (_materials.TryGetValue(key, out m) && m != null) return m;

            m = new Material(shader) { name = "H5_fx_" + (additive ? "add" : "alpha") };
            m.mainTexture = texture != null ? texture : GlowTexture();
            m.SetColor("_TintColor", Color.white);
            m.SetColor("_Color", Color.white);
            _materials[key] = m;
            return m;
        }

        Material GlowMaterial(bool additive) => ParticleMaterial(null, additive);

        /// <summary>A quad's material, tinted, for the projectile head.</summary>
        Material SpriteMaterial(Texture texture, Color tint)
        {
            EnsureShaders();
            if (_additive == null) return null;

            var key = "head|" + (texture != null ? texture.GetEntityId().ToString() : "none") + "|" + tint.ToString();
            Material m;
            if (_materials.TryGetValue(key, out m) && m != null) return m;

            m = new Material(_additive) { name = "H5_fx_head" };
            m.mainTexture = texture != null ? texture : GlowTexture();
            m.SetColor("_TintColor", tint);
            m.SetColor("_Color", tint);
            _materials[key] = m;
            return m;
        }

        /// <summary>A white radial falloff: the fallback for every effect with no art of its own.</summary>
        Texture2D GlowTexture()
        {
            if (_glow != null) return _glow;
            const int size = 64;
            _glow = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "H5_glow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5f) / size * 2f - 1f;
                var dy = (y + 0.5f) / size * 2f - 1f;
                var falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = new Color(1f, 1f, 1f, falloff * falloff);
            }
            _glow.SetPixels(pixels);
            _glow.Apply(false, false);
            return _glow;
        }

        /// <summary>The spell's textures that the manifest has on disk, up to a handful.</summary>
        List<Texture> VisualTextures(SpellVisualDef info)
        {
            var list = new List<Texture>();
            if (info == null || info.textures == null || _textures == null) return list;
            foreach (var path in info.textures)
            {
                if (list.Count >= MaxSpellTextures) break;
                if (!_textures.Has(path)) continue;
                var texture = _textures.Get(path);
                if (texture != null) list.Add(texture);
            }
            return list;
        }

        static Texture PickTexture(List<Texture> list, Texture fallback)
        {
            if (list == null || list.Count == 0) return fallback;
            return list[UnityEngine.Random.Range(0, list.Count)];
        }

        // -- positions -----------------------------------------------------------

        Vector3 CellPoint(int x, int y) => _scene != null ? _scene.CellToWorld(x, y) : Vector3.zero;

        Vector3 Center() => CellPoint(CombatGameState.GridWidth / 2, CombatGameState.GridHeight / 2) +
                            Vector3.up * _cell * 0.5f;

        /// <summary>Chest height of the stack, where hits and smoke land.</summary>
        Vector3 UnitPoint(CombatUnit unit)
        {
            if (unit == null) return Center();
            if (_scene != null && _scene.Views.TryGetValue(unit.Id, out var view) && view != null)
                return view.transform.position + Vector3.up * _cell * 0.9f;
            return CellPoint(unit.X, unit.Y) + Vector3.up * _cell * 0.9f;
        }

        /// <summary>Where the hero's spell leaves from: the edge of the caster's side.</summary>
        Vector3 CasterPoint(CombatSide side)
        {
            var x = side == CombatSide.Attacker ? 0 : CombatGameState.GridWidth - 1;
            return CellPoint(x, CombatGameState.GridHeight / 2) + Vector3.up * _cell;
        }

        // -- sounds and spell data -----------------------------------------------

        /// <summary>Play the first of the named events the creature's animation set has a sound for.</summary>
        static void PlayUnitSound(CombatUnit unit, params string[] events)
        {
            if (unit == null || unit.Def == null || unit.Def.sounds == null) return;
            foreach (var name in events)
            {
                string key;
                if (unit.Def.sounds.TryGetValue(name, out key) && !string.IsNullOrEmpty(key))
                {
                    H5Audio.Play(key);
                    return;
                }
            }
        }

        static SpellVisualDef FirstVisual(SpellDef spell)
        {
            if (spell == null || spell.visualInfo == null) return null;
            foreach (var info in spell.visualInfo)
                if (info != null && !string.IsNullOrEmpty(info.sound)) return info;
            return spell.visualInfo.Count > 0 ? spell.visualInfo[0] : null;
        }

        static bool IsOffensive(SpellDef spell) => spell != null && spell.damage != null && spell.damage.Count > 0;

        /// <summary>The element's colour, or the school's when the element is neutral.</summary>
        static Color SpellTint(SpellDef spell)
        {
            if (spell == null) return new Color(0.9f, 0.9f, 1f);
            switch (spell.element ?? "")
            {
                case "ELEMENT_FIRE": return new Color(1f, 0.55f, 0.15f);
                case "ELEMENT_WATER": return new Color(0.45f, 0.75f, 1f);
                case "ELEMENT_EARTH": return new Color(0.75f, 0.6f, 0.35f);
                case "ELEMENT_AIR": return new Color(0.85f, 0.95f, 1f);
            }
            switch (spell.school ?? "")
            {
                case "MAGIC_SCHOOL_DARK": return new Color(0.7f, 0.35f, 1f);
                case "MAGIC_SCHOOL_LIGHT": return new Color(1f, 0.95f, 0.6f);
                case "MAGIC_SCHOOL_SUMMONING": return new Color(0.5f, 1f, 0.6f);
                case "MAGIC_SCHOOL_WARCRIES": return new Color(1f, 0.85f, 0.4f);
            }
            return new Color(0.9f, 0.9f, 1f);
        }
    }
}
