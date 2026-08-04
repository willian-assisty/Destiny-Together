using System.Collections.Generic;
using DestinyTogether.Core;
using DestinyTogether.Data;
using DestinyTogether.Sim;
using UnityEngine;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// Desenha o mundo procedural em volta de quem esta olhando, e apaga o que ficou para tras.
    ///
    /// Nao conversa com a simulacao: chama <see cref="WorldGen"/> direto, com a mesma semente. E o
    /// ganho central de gerar por funcao pura em vez de por dados — a apresentacao pode desenhar
    /// mata a 120 celulas de distancia sem que uma unica entidade exista lá, e sem que um unico
    /// byte trafegue quando o multiplayer entrar. Os quatro clientes derivam a mesma floresta
    /// porque partem da mesma formula, nao porque alguem a enviou.
    ///
    /// Os chunks materializados aqui e os do <see cref="WorldStreamer"/> tem raios diferentes de
    /// proposito: a simulacao so precisa do que da para tocar, a tela precisa do que da para ver.
    /// </summary>
    public sealed class WorldPropStreamer
    {
        /// <summary>Chunks a mais que o raio antes de apagar. Evita piscar na fronteira.</summary>
        private const int Hysteresis = 1;

        /// <summary>Teto de chunks desenhados. Rede de seguranca contra um raio mal configurado.</summary>
        private const int MaxChunks = 400;

        private readonly Transform _root;
        private readonly VisualsProfile _profile;
        private readonly PlaceholderFactory _placeholders;
        private readonly ArenaSpec _arena;
        private readonly Vec2 _cityCenter;
        private readonly int _seed;

        private readonly Dictionary<long, Transform> _chunks = new Dictionary<long, Transform>(256);
        private readonly ChunkContent _content = new ChunkContent();
        private readonly List<long> _toRemove = new List<long>(32);

        private int _lastChunkX = int.MinValue;
        private int _lastChunkZ = int.MinValue;

        public int LoadedChunkCount => _chunks.Count;

        public WorldPropStreamer(Transform parent, VisualsProfile profile, PlaceholderFactory placeholders,
                                 ArenaSpec arena, Vec2 cityCenter, int seed)
        {
            _profile = profile;
            _placeholders = placeholders;
            _arena = arena;
            _cityCenter = cityCenter;
            _seed = seed;

            var go = new GameObject("Mundo");
            go.transform.SetParent(parent, false);
            _root = go.transform;
        }

        /// <summary>
        /// Reavalia o que precisa existir. So faz trabalho quando o foco TROCA de chunk — andar
        /// dentro do mesmo chunk nao mexe em nada, o que mantem o custo em zero na maior parte
        /// dos frames.
        /// </summary>
        public void Tick(Vec2 focus)
        {
            int radius = _profile != null ? Mathf.Clamp(_profile.PropRadiusChunks, 2, 10) : 5;

            WorldGen.ChunkOf(focus, out int cx, out int cz);
            if (cx == _lastChunkX && cz == _lastChunkZ) return;
            _lastChunkX = cx;
            _lastChunkZ = cz;

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (_chunks.Count >= MaxChunks) break;
                    long key = WorldGen.KeyOf(cx + dx, cz + dz);
                    if (_chunks.ContainsKey(key)) continue;
                    Build(cx + dx, cz + dz, key);
                }
            }

            int keep = radius + Hysteresis;
            _toRemove.Clear();
            foreach (var pair in _chunks)
            {
                Decode(pair.Key, out int px, out int pz);
                if (Mathf.Abs(px - cx) > keep || Mathf.Abs(pz - cz) > keep) _toRemove.Add(pair.Key);
            }

            for (int i = 0; i < _toRemove.Count; i++)
            {
                if (_chunks.TryGetValue(_toRemove[i], out var t) && t != null) Object.Destroy(t.gameObject);
                _chunks.Remove(_toRemove[i]);
            }
        }

        private void Build(int cx, int cz, long key)
        {
            WorldGen.Generate(_seed, cx, cz, _arena, _cityCenter, _content);

            var container = new GameObject($"Chunk_{cx}_{cz}").transform;
            container.SetParent(_root, false);
            _chunks[key] = container;

            if (_content.Props.Count == 0) return;

            for (int i = 0; i < _content.Props.Count; i++)
                Spawn(_content.Props[i], container);
        }

        private void Spawn(in WorldProp prop, Transform parent)
        {
            var prefab = PickPrefab(prop.Kind);

            var holder = new GameObject("P");
            holder.transform.SetParent(parent, false);
            holder.transform.position = GridToWorld.ToWorld(prop.Position);
            holder.transform.rotation = Quaternion.Euler(0f, prop.Yaw, 0f);

            if (prefab == null)
            {
                // Sem pack de arte o mundo continua existindo — em primitivas, com a mesma
                // gramatica de silhueta do resto do jogo. Nunca existe um estado em que a mata
                // simplesmente nao aparece.
                var fallback = _placeholders.CreatePrimitive(FallbackStyle(prop.Kind, prop.Scale));
                fallback.transform.SetParent(holder.transform, false);
                return;
            }

            bool quarry = prop.Kind is WorldPropKind.Pedra or WorldPropKind.Penhasco;
            var instance = Object.Instantiate(prefab, holder.transform);

            VisualFitter.Fit(instance, new VisualEntry
            {
                TargetCells = quarry ? _profile.QuarryTargetCells : _profile.ScatterTargetCells,
                MaxHeightCells = quarry ? _profile.QuarryMaxHeightCells : _profile.ScatterMaxHeightCells,
                ScaleMultiplier = prop.Scale,
                EulerAngles = _profile.ScatterEulerAngles
            });
        }

        private GameObject PickPrefab(WorldPropKind kind)
        {
            if (_profile == null) return null;

            var list = kind is WorldPropKind.Pedra or WorldPropKind.Penhasco
                ? _profile.QuarryProps
                : _profile.ScatterProps;

            if (list == null || list.Count == 0) return null;

            // Escolha estavel por tipo e nao por sorteio: reconstruir o mesmo chunk tem de dar a
            // mesma floresta, senao voltar sobre os proprios passos mostra outra mata.
            int index = ((int)kind * 2654435761u).GetHashCode();
            index = Mathf.Abs(index) % list.Count;
            return list[index];
        }

        private static VisualStyle FallbackStyle(WorldPropKind kind, float scale) => kind switch
        {
            WorldPropKind.Pinheiro => new VisualStyle
            {
                Shape = PrimitiveShape.Cone,
                Color = new Color(0.16f, 0.26f, 0.18f),
                Scale = 1.6f * scale,
                Height = 4.4f * scale
            },
            WorldPropKind.Pedra => new VisualStyle
            {
                Shape = PrimitiveShape.Octaedro,
                Color = new Color(0.38f, 0.37f, 0.36f),
                Scale = 1.8f * scale,
                Height = 1.4f * scale
            },
            WorldPropKind.Penhasco => new VisualStyle
            {
                Shape = PrimitiveShape.Cube,
                Color = new Color(0.30f, 0.29f, 0.28f),
                Scale = 3.2f * scale,
                Height = 3.0f * scale
            },
            _ => new VisualStyle
            {
                Shape = PrimitiveShape.Cylinder,
                Color = new Color(0.22f, 0.30f, 0.20f),
                Scale = 1.1f * scale,
                Height = 3.6f * scale
            }
        };

        private static void Decode(long key, out int cx, out int cz)
        {
            cx = (int)(key >> 32);
            cz = (int)(uint)(key & 0xFFFFFFFFL);
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            _chunks.Clear();
        }
    }
}
