using System.Collections.Generic;
using UnityEngine;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// Efeitos de curta duracao (tracer de tiro, anel de explosao) num pool simples.
    ///
    /// Existem porque, com primitivas, o tiro precisa ser VISIVEL sem animacao: uma linha que
    /// aparece por 80ms comunica "aquela torre acertou aquele monstro" melhor que qualquer
    /// mudanca de cor. Quando a arte final entrar, isto vira o ponto de plug de VFX.
    /// </summary>
    public sealed class EffectPool
    {
        private struct ActiveEffect
        {
            public Transform Transform;
            public LineRenderer Line;
            public float Remaining;
            public float Duration;
            public Vector3 BaseScale;
            public bool IsRing;
        }

        private readonly Transform _root;
        private readonly PlaceholderFactory _factory;
        private readonly Stack<LineRenderer> _freeLines = new Stack<LineRenderer>();
        private readonly Stack<Transform> _freeRings = new Stack<Transform>();
        private readonly List<ActiveEffect> _active = new List<ActiveEffect>(64);
        private Material _lineMaterial;

        public EffectPool(Transform root, PlaceholderFactory factory)
        {
            _root = root;
            _factory = factory;
        }

        public void Tracer(Vector3 from, Vector3 to, Color color, float duration = 0.08f)
        {
            var line = _freeLines.Count > 0 ? _freeLines.Pop() : CreateLine();
            line.gameObject.SetActive(true);
            line.SetPosition(0, from + Vector3.up * 0.6f);
            line.SetPosition(1, to + Vector3.up * 0.4f);
            line.startColor = color;
            line.endColor = new Color(color.r, color.g, color.b, 0.15f);

            _active.Add(new ActiveEffect
            {
                Transform = line.transform,
                Line = line,
                Remaining = duration,
                Duration = duration,
                IsRing = false
            });
        }

        public void Ring(Vector3 center, float radius, Color color, float duration = 0.35f)
        {
            var ring = _freeRings.Count > 0 ? _freeRings.Pop() : CreateRing();
            ring.gameObject.SetActive(true);
            ring.position = center + Vector3.up * 0.15f;
            var scale = new Vector3(radius * 2f, 0.08f, radius * 2f);
            ring.localScale = scale;

            var renderer = ring.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = _factory.GetMaterial(color);

            _active.Add(new ActiveEffect
            {
                Transform = ring,
                Remaining = duration,
                Duration = duration,
                BaseScale = scale,
                IsRing = true
            });
        }

        public void Tick(float dt)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var e = _active[i];
                e.Remaining -= dt;

                if (e.Remaining <= 0f)
                {
                    e.Transform.gameObject.SetActive(false);
                    if (e.IsRing) _freeRings.Push(e.Transform);
                    else _freeLines.Push(e.Line);
                    _active.RemoveAt(i);
                    continue;
                }

                if (e.IsRing)
                {
                    float t = 1f - e.Remaining / e.Duration;
                    e.Transform.localScale = Vector3.Lerp(e.BaseScale * 0.3f, e.BaseScale, t);
                }

                _active[i] = e;
            }
        }

        private LineRenderer CreateLine()
        {
            var go = new GameObject("Tracer");
            go.transform.SetParent(_root, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = 0.10f;
            line.endWidth = 0.02f;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = LineMaterial();
            return line;
        }

        private Material LineMaterial()
        {
            if (_lineMaterial != null) return _lineMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            _lineMaterial = new Material(shader) { name = "PH_Tracer" };
            return _lineMaterial;
        }

        private Transform CreateRing()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Ring";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root, false);
            return go.transform;
        }

        public void Dispose()
        {
            if (_lineMaterial != null) Object.Destroy(_lineMaterial);
        }
    }
}
