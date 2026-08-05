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
    /// mata a 120 celulas de distancia sem que uma unica entidade exista la, e sem que um unico
    /// byte trafegue quando o multiplayer entrar. Os quatro clientes derivam a mesma floresta
    /// porque partem da mesma formula, nao porque alguem a enviou.
    ///
    /// **Nao existe GameObject por arvore.** Um chunk vira uma lista de matrizes por molde, e o
    /// desenho sai por instancing (<see cref="PropBatcher"/>). Foi o que permitiu triplicar a
    /// densidade da mata: o custo de uma arvore caiu de um Transform com hierarquia e culling
    /// proprio para 64 bytes numa lista.
    ///
    /// Os chunks materializados aqui e os do <see cref="WorldStreamer"/> tem raios diferentes de
    /// proposito: a simulacao so precisa do que da para tocar, a tela precisa do que da para ver.
    /// </summary>
    public sealed class WorldPropStreamer
    {
        /// <summary>Chunks a mais que o raio antes de apagar. Evita piscar na fronteira.</summary>
        private const int Hysteresis = 1;

        /// <summary>Teto de chunks montados. Rede de seguranca contra um raio mal configurado.</summary>
        private const int MaxChunks = 400;

        /// <summary>
        /// Props montados por frame.
        ///
        /// Montar um chunk agora e so preencher listas de matrizes, mas atravessar uma fronteira
        /// ainda pede uma FILEIRA inteira de chunks de uma vez — em mata fechada isso passa de
        /// quatro mil pecas no mesmo frame. Espalhar o trabalho troca o engasgo por algumas
        /// arvores aparecendo na borda da tela, que e o lado certo da troca.
        /// </summary>
        private const int PropsPerFrame = 2500;

        /// <summary>Ate onde as pecas ainda projetam sombra, em unidades.</summary>
        private const float ShadowDistance = 70f;

        /// <summary>
        /// Multiplicador de alcance da nevoa. Fator de nevoa exponencial-quadratica cai a ~2% em
        /// 1,98/densidade; um pouco de folga evita que um chunk suma na borda do quadro.
        /// </summary>
        private const float FogReach = 2.4f;

        private readonly VisualsProfile _profile;
        private readonly PlaceholderFactory _placeholders;
        private readonly PropBatcher _batcher;
        private readonly ArenaSpec _arena;
        private readonly Vec2 _cityCenter;
        private readonly int _seed;

        /// <summary>
        /// Um chunk montado: matrizes FINAIS agrupadas por pedaco desenhavel.
        ///
        /// Agrupado por PEDACO e nao por molde porque a matriz do pedaco dentro do molde ja entra
        /// assada aqui — assim desenhar nao faz conta nenhuma. Um prefab de uma malha so (que e o
        /// caso de toda a nossa arte) da exatamente um grupo.
        /// </summary>
        private sealed class Chunk
        {
            public readonly List<PropBatcher.Part> Parts = new List<PropBatcher.Part>(8);
            public readonly List<List<Matrix4x4>> Matrices = new List<List<Matrix4x4>>(8);
            public Bounds Bounds;
            public Vector3 Center;
            public int PropCount;

            public List<Matrix4x4> BucketFor(in PropBatcher.Part part)
            {
                for (int i = 0; i < Parts.Count; i++)
                {
                    var p = Parts[i];
                    if (ReferenceEquals(p.Mesh, part.Mesh) && ReferenceEquals(p.Material, part.Material) &&
                        p.SubMesh == part.SubMesh && p.Local == part.Local)
                        return Matrices[i];
                }

                Parts.Add(part);
                var list = new List<Matrix4x4>(64);
                Matrices.Add(list);
                return list;
            }
        }

        private readonly Dictionary<long, Chunk> _chunks = new Dictionary<long, Chunk>(256);
        private readonly ChunkContent _content = new ChunkContent();
        private readonly List<long> _toRemove = new List<long>(32);
        private readonly List<long> _queue = new List<long>(128);

        private int _lastChunkX = int.MinValue;
        private int _lastChunkZ = int.MinValue;

        public int LoadedChunkCount => _chunks.Count;
        public int PendingChunkCount => _queue.Count;

        /// <summary>Quantas pecas estao desenhadas agora. E o numero que se olha ao mexer na densidade.</summary>
        public int DrawnPropCount { get; private set; }

        public WorldPropStreamer(Transform parent, VisualsProfile profile, PlaceholderFactory placeholders,
                                 ArenaSpec arena, Vec2 cityCenter, int seed)
        {
            _profile = profile;
            _placeholders = placeholders;
            _arena = arena;
            _cityCenter = cityCenter;
            _seed = seed;
            _batcher = new PropBatcher(parent);
        }

        /// <summary>
        /// Reavalia o que precisa existir e desenha. Chamado por frame.
        ///
        /// A parte de MONTAR so faz trabalho quando o foco troca de chunk; a de DESENHAR roda
        /// sempre, porque instancing e imediato — nao existe objeto persistente para o motor
        /// desenhar sozinho.
        /// </summary>
        public void Tick(Vec2 focus)
        {
            int radius = _profile != null ? Mathf.Clamp(_profile.PropRadiusChunks, 2, 10) : 5;

            WorldGen.ChunkOf(focus, out int cx, out int cz);

            if (cx != _lastChunkX || cz != _lastChunkZ)
            {
                _lastChunkX = cx;
                _lastChunkZ = cz;
                Reschedule(cx, cz, radius);
                Unload(cx, cz, radius + Hysteresis);
            }

            DrainQueue();
            Draw(focus);
        }

        // ------------------------------------------------------------------------------------
        // Montagem
        // ------------------------------------------------------------------------------------

        private void Reschedule(int cx, int cz, int radius)
        {
            _queue.Clear();

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    long key = WorldGen.KeyOf(cx + dx, cz + dz);
                    if (!_chunks.ContainsKey(key)) _queue.Add(key);
                }
            }

            // Mais LONGE primeiro na lista, porque o consumo tira do fim: assim o proximo a ser
            // montado e sempre o mais perto, sem custar uma copia do vetor por chunk.
            _queue.Sort((a, b) =>
            {
                Decode(a, out int ax, out int az);
                Decode(b, out int bx, out int bz);
                int da = (ax - cx) * (ax - cx) + (az - cz) * (az - cz);
                int db = (bx - cx) * (bx - cx) + (bz - cz) * (bz - cz);
                return db.CompareTo(da);
            });
        }

        private void Unload(int cx, int cz, int keep)
        {
            _toRemove.Clear();
            foreach (var pair in _chunks)
            {
                Decode(pair.Key, out int px, out int pz);
                if (Mathf.Abs(px - cx) > keep || Mathf.Abs(pz - cz) > keep) _toRemove.Add(pair.Key);
            }

            // Descarregar agora e so soltar listas — nao ha GameObject para destruir, e portanto
            // nao ha o pico de Destroy que atravessar uma fronteira costumava custar.
            for (int i = 0; i < _toRemove.Count; i++) _chunks.Remove(_toRemove[i]);
        }

        private void DrainQueue()
        {
            int budget = PropsPerFrame;

            while (_queue.Count > 0 && budget > 0 && _chunks.Count < MaxChunks)
            {
                long key = _queue[_queue.Count - 1];
                _queue.RemoveAt(_queue.Count - 1);

                Decode(key, out int cx, out int cz);
                budget -= Build(cx, cz, key);
            }
        }

        private int Build(int cx, int cz, long key)
        {
            WorldGen.Generate(_seed, cx, cz, _arena, _cityCenter, _content);

            var chunk = new Chunk();
            _chunks[key] = chunk;

            float x0 = cx * WorldGen.ChunkSize, z0 = cz * WorldGen.ChunkSize;
            float tallest = 1f;

            for (int i = 0; i < _content.Props.Count; i++)
            {
                var prop = _content.Props[i];
                var template = TemplateFor(prop);
                if (template == null || template.Parts.Count == 0) continue;

                // Escala uniforme em volta da origem da peca: a base fica no chao porque o molde ja
                // colocou a base em y=0, e escalar em volta da origem mantem y=0 em y=0.
                var world = Matrix4x4.TRS(GridToWorld.ToWorld(prop.Position),
                                          Quaternion.Euler(0f, prop.Yaw, 0f),
                                          Vector3.one * prop.Scale);

                for (int k = 0; k < template.Parts.Count; k++)
                {
                    var part = template.Parts[k];
                    chunk.BucketFor(part).Add(world * part.Local);
                }

                if (template.Height * prop.Scale > tallest) tallest = template.Height * prop.Scale;
                chunk.PropCount++;
            }

            // Caixa do chunk, usada para o motor descartar por frustum sem olhar peca por peca.
            float half = WorldGen.ChunkSize * 0.5f;
            chunk.Center = new Vector3(x0 + half, 0f, z0 + half);
            chunk.Bounds = new Bounds(chunk.Center + Vector3.up * tallest * 0.5f,
                                      new Vector3(WorldGen.ChunkSize, tallest + BoardRenderer.Relief * 2f,
                                                  WorldGen.ChunkSize));

            return chunk.PropCount;
        }

        // ------------------------------------------------------------------------------------
        // Desenho
        // ------------------------------------------------------------------------------------

        private void Draw(Vec2 focus)
        {
            var eye = GridToWorld.ToFlatWorld(focus);
            float reach = VisibleReach();
            float reachSq = reach * reach;
            float shadowSq = ShadowDistance * ShadowDistance;

            int drawn = 0;

            foreach (var pair in _chunks)
            {
                var chunk = pair.Value;
                if (chunk.PropCount == 0) continue;

                // Nao desenhar o que a NEVOA ja esconde. E o descarte mais barato que existe aqui,
                // e ele rende mais justamente quando mais importa: de noite a nevoa fecha para ~44
                // unidades, e a noite e quando ha 140 monstros vivos disputando o mesmo frame.
                float distanceSq = (chunk.Center - eye).sqrMagnitude;
                if (distanceSq > reachSq) continue;

                bool shadows = distanceSq < shadowSq;

                for (int i = 0; i < chunk.Parts.Count; i++)
                    PropBatcher.Draw(chunk.Parts[i], chunk.Matrices[i], chunk.Bounds, shadows);

                drawn += chunk.PropCount;
            }

            DrawnPropCount = drawn;
        }

        /// <summary>
        /// Ate onde da para ver, derivado da nevoa que esta no ar AGORA.
        ///
        /// Ler a nevoa em vez de fixar um raio e o que faz o descarte acompanhar o ciclo de dia e
        /// noite sozinho: o mesmo codigo desenha 200 unidades de mata ao meio-dia e 44 a meia-noite,
        /// sem ninguem configurar nada.
        /// </summary>
        private static float VisibleReach()
        {
            if (!RenderSettings.fog) return 400f;

            float density = Mathf.Max(0.0005f, RenderSettings.fogDensity);
            return Mathf.Clamp(FogReach / density, 60f, 400f);
        }

        // ------------------------------------------------------------------------------------

        private PropBatcher.Template TemplateFor(in WorldProp prop)
        {
            var prefab = PickPrefab(prop.Kind, prop.Variant);

            if (prefab == null)
            {
                // Sem pack de arte o mundo continua existindo — em primitivas, com a mesma
                // gramatica de silhueta do resto do jogo. Nunca existe um estado em que a mata
                // simplesmente nao aparece.
                return _batcher.GetPrimitiveTemplate(_placeholders, FallbackStyle(prop.Kind, 1f));
            }

            float target, ceiling;
            switch (prop.Kind)
            {
                case WorldPropKind.Penhasco:
                    target = _profile.CliffTargetCells;
                    ceiling = _profile.CliffMaxHeightCells;
                    break;
                case WorldPropKind.Pedra:
                    target = _profile.QuarryTargetCells;
                    ceiling = _profile.QuarryMaxHeightCells;
                    break;
                default:
                    target = _profile.ScatterTargetCells;
                    ceiling = _profile.ScatterMaxHeightCells;
                    break;
            }

            return _batcher.GetTemplate(prefab, new VisualEntry
            {
                TargetCells = target,
                MaxHeightCells = ceiling,
                ScaleMultiplier = 1f,
                EulerAngles = _profile.ScatterEulerAngles
            });
        }

        /// <summary>
        /// A malha de um prop. A escolha vem da VARIANTE que o gerador sorteou, nao de um hash do
        /// tipo — antes, todo prop do mesmo tipo desenhava a mesma malha, entao importar cinco
        /// arvores diferentes teria produzido uma floresta com duas.
        /// </summary>
        private GameObject PickPrefab(WorldPropKind kind, int variant)
        {
            if (_profile == null) return null;

            var list = kind switch
            {
                WorldPropKind.Penhasco => Available(_profile.CliffProps) ?? _profile.QuarryProps,
                WorldPropKind.Pedra => _profile.QuarryProps,
                _ => _profile.ScatterProps
            };

            if (list == null || list.Count == 0) return null;
            return list[(variant & 0x7FFFFFFF) % list.Count];
        }

        private static List<GameObject> Available(List<GameObject> list)
            => list != null && list.Count > 0 ? list : null;

        private static VisualStyle FallbackStyle(WorldPropKind kind, float scale) => kind switch
        {
            // As alturas base seguem o token de escala e valem para scale = 1: arvore e pinheiro
            // sao "prop medio" (3,0), pedra e "prop pequeno" (0,8). O rochedo fica FORA da tabela
            // de proposito — num mundo sem fim e sem minimapa, marco de terreno e o que permite
            // voltar, e por isso ele e a unica peca maior que a Prefeitura.
            //
            // As cores sobem para a faixa de ambiente (35-55% de luminancia linear, dessaturado):
            // o caminho sem arte tem de ler igual ao com arte, senao "nunca ha um estado meio
            // migrado" vira so uma frase.
            WorldPropKind.Pinheiro => new VisualStyle
            {
                Shape = PrimitiveShape.Cone,
                Color = new Color(0.510f, 0.671f, 0.541f),
                Scale = 2.0f * scale,
                Height = 3.0f * scale
            },
            WorldPropKind.Pedra => new VisualStyle
            {
                Shape = PrimitiveShape.Octaedro,
                Color = new Color(0.697f, 0.670f, 0.655f),
                Scale = 1.2f * scale,
                Height = 0.8f * scale
            },
            WorldPropKind.Penhasco => new VisualStyle
            {
                Shape = PrimitiveShape.Cube,
                Color = new Color(0.694f, 0.671f, 0.635f),
                Scale = 5.0f * scale,
                Height = 6.5f * scale
            },
            _ => new VisualStyle
            {
                Shape = PrimitiveShape.Cylinder,
                Color = new Color(0.561f, 0.678f, 0.529f),
                Scale = 1.4f * scale,
                Height = 3.0f * scale
            }
        };

        private static void Decode(long key, out int cx, out int cz)
        {
            cx = (int)(key >> 32);
            cz = (int)(uint)(key & 0xFFFFFFFFL);
        }

        public void Dispose()
        {
            _batcher.Dispose();
            _chunks.Clear();
        }
    }
}
