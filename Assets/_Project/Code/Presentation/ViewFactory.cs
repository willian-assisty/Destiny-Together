using DestinyTogether.Core;
using DestinyTogether.Data;
using DestinyTogether.Sim;
using UnityEngine;

// Desambigua do UnityEngine.EntityId introduzido no Unity 6.
using EntityId = DestinyTogether.Sim.EntityId;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// Cria as views do jogo. Um caminho para arte de verdade, outro para primitiva.
    ///
    /// A hierarquia é a MESMA nos dois casos — é isso que faz a troca ser barata:
    ///
    ///   Raiz  (EntityView, script, sem arte)
    ///     └── Visual        ← primitiva OU prefab comprado, indiferente
    ///
    /// Se o perfil visual não tem entrada para uma definição, ela cai para a primitiva sem drama.
    /// Importar seis árvores não obriga ninguém a autorar as outras quarenta peças antes de rodar
    /// o jogo — e é por isso que a arte pode entrar aos poucos, uma peça por vez.
    /// </summary>
    public sealed class ViewFactory
    {
        private readonly PlaceholderFactory _placeholders;
        private readonly VisualsProfile _profile;
        private readonly Transform _root;

        public ViewFactory(Transform root, VisualsProfile profile)
        {
            _root = root;
            _profile = profile;
            _placeholders = new PlaceholderFactory(root);
        }

        public PlaceholderFactory Placeholders => _placeholders;
        public VisualsProfile Profile => _profile;

        /// <summary>True quando existe arte de verdade para esta definição.</summary>
        public bool HasArt(DefId id) => _profile != null && _profile.TryGet(id, out _);

        /// <summary>
        /// View de uma entidade da simulação. Usa a arte do perfil quando existe; caso contrário,
        /// a primitiva descrita por <paramref name="fallback"/>.
        /// </summary>
        /// <param name="tintOverride">
        /// Cor do assento, quando a peça pertence a um jogador. Vale para a primitiva E para a
        /// arte de verdade: com quatro heróis usando a mesma malha, "de quem é esse" volta a ser
        /// indistinguível se o modelo mandar na cor. Cor diz de que LADO a coisa está — é regra
        /// de leitura, não decoração, e não é a arte que decide.
        /// </param>
        public EntityView CreateEntityView(string name, DefId def, VisualStyle fallback,
                                           Vector3 position, Color? tintOverride = null)
        {
            if (_profile != null && _profile.TryGet(def, out var entry))
                return CreateFromPrefab(name, entry, position, tintOverride);

            if (tintOverride.HasValue) fallback.Color = tintOverride.Value;
            return _placeholders.CreateView(name, fallback, position);
        }

        public EntityView CreateNodeView(string name, HarvestNodeKind kind, VisualStyle fallback, Vector3 position)
        {
            if (_profile != null && _profile.TryGetNode(kind, out var entry))
                return CreateFromPrefab(name, entry, position, fallback.Color);

            return _placeholders.CreateView(name, fallback, position);
        }

        /// <summary>
        /// A Prefeitura é o único objeto que não é uma entidade da simulação mas precisa de view:
        /// ela é o tabuleiro, não uma peça. Por isso tem caminho próprio.
        /// </summary>
        /// <summary>
        /// Aplica a cor do assento na arte importada e, de quebra, cobre o caso do modelo que
        /// chega SEM material.
        ///
        /// Malha exportada por gerador (Meshy e afins) costuma vir só com geometria. Em URP, um
        /// renderer sem material desenha magenta — o personagem apareceria como uma mancha rosa e
        /// pareceria bug de shader. Atribuir o material de placeholder resolve as duas coisas com
        /// uma linha: a peça passa a ter material E a cor certa do jogador.
        /// </summary>
        private void ApplyOwnerTint(GameObject art, Color tint)
        {
            foreach (var r in art.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;

                if (r.sharedMaterial == null)
                {
                    r.sharedMaterial = _placeholders.GetMaterial(tint);
                    continue;
                }

                // Com material próprio (arte texturizada), tinge por property block para não
                // editar o asset do artista.
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                block.SetColor(ShaderIds.BaseColor, tint);
                r.SetPropertyBlock(block);
            }
        }

        public GameObject CreateTownHall(Vector3 position, float sizeInCells)
        {
            if (_profile != null && _profile.TryGetTownHall(out var entry))
            {
                var holder = new GameObject("Prefeitura");
                holder.transform.SetParent(_root, false);
                holder.transform.position = position;

                var art = Object.Instantiate(entry.Prefab, holder.transform);
                var scaled = entry;
                if (scaled.TargetCells <= 0.01f) scaled.TargetCells = sizeInCells;
                VisualFitter.Fit(art, scaled);
                return holder;
            }

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Prefeitura";
            Object.Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(_root, false);
            box.transform.localScale = new Vector3(sizeInCells * 0.92f, 1.6f, sizeInCells * 0.92f);
            box.transform.position = position + Vector3.up * 0.8f;
            box.GetComponent<Renderer>().sharedMaterial =
                _placeholders.GetMaterial(new Color(0.88f, 0.82f, 0.55f));
            return box;
        }

        /// <summary>
        /// As quatro florestas dos cantos. Puramente cosmética: a simulação não sabe que existem,
        /// e por isso podem ser desligadas inteiras sem mudar uma regra.
        ///
        /// Ficam nas DIAGONAIS (NE, SE, SO, NO), em cima dos bolsões de recurso, e isso é decisão
        /// de leitura tanto quanto de cenário: as diagonais viram mata fechada — onde se colhe —
        /// e as Faixas ortogonais (N, S, L, O) ficam abertas, que é por onde a horda vem. O
        /// jogador enxerga o ataque chegando pelo corredor limpo em vez de procurá-lo entre
        /// troncos. O miolo do mapa fica vazio de propósito: é campo de batalha, não paisagem.
        /// </summary>
        /// <param name="cornerDistance">Distância do centro até o coração de cada floresta.</param>
        /// <param name="cityRadius">Meia-largura do tabuleiro: nada de vegetação dentro disso.</param>
        public void ScatterProps(Vec2 center, float cornerDistance, float cityRadius, int seed)
        {
            if (_profile == null || _profile.ScatterCount <= 0 || _profile.ScatterProps.Count == 0) return;

            var container = new GameObject("Florestas").transform;
            container.SetParent(_root, false);

            var rng = new Rng(seed);
            float radius = Mathf.Max(1f, _profile.ForestRadius);
            float bias = Mathf.Clamp(_profile.ForestDensityBias, 0.3f, 1.5f);
            float clearing = Mathf.Max(0f, _profile.ForestClearing);

            for (int i = 0; i < _profile.ScatterCount; i++)
            {
                var prefab = _profile.ScatterProps[rng.Range(0, _profile.ScatterProps.Count)];
                if (prefab == null) continue;

                // Rodízio entre as quatro diagonais: 45, 135, 225, 315 graus.
                float cornerAngle = 45f + 90f * (i % 4);
                var forestCenter = center + Vec2.FromCompassDegrees(cornerAngle) * cornerDistance;

                // Expoente < 0.5 adensa o miolo; 0.5 daria área uniforme. Mata fechada no coração
                // e ralinha na borda lê como floresta de verdade, não como grade de árvores.
                float t = Mathf.Pow(rng.Next01(), bias * 0.5f);
                float angle = rng.Range(0f, 360f);
                var point = forestCenter + Vec2.FromCompassDegrees(angle) * (radius * t);

                // Nada de vegetação em cima da cidade nem no campo de tiro em volta dela.
                if (Vec2.Distance(point, center) < cityRadius + clearing) continue;

                // Um holder recebe posição e rotação; a arte fica dentro dele, normalizada.
                // Sem este passo o prop entra em escala NATIVA — e prop de cenário de pack tem
                // dezenas de unidades, o que põe um penhasco maior que a cidade na frente da
                // câmera. Foi exatamente esse o bug da "pedra enorme".
                var holder = new GameObject("Prop");
                holder.transform.SetParent(container, false);
                holder.transform.position = GridToWorld.ToWorld(point);
                holder.transform.rotation = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);

                var instance = Object.Instantiate(prefab, holder.transform);
                float target = _profile.ScatterTargetCells > 0.01f ? _profile.ScatterTargetCells : 1.6f;
                VisualFitter.Fit(instance, new VisualEntry
                {
                    TargetCells = target,
                    MaxHeightCells = _profile.ScatterMaxHeightCells,
                    ScaleMultiplier = rng.Range(_profile.ScatterMinScale, _profile.ScatterMaxScale),
                    EulerAngles = _profile.ScatterEulerAngles
                });
            }
        }

        private EntityView CreateFromPrefab(string name, in VisualEntry entry, Vector3 position,
                                            Color? tintOverride)
        {
            var root = new GameObject(name);
            root.transform.SetParent(_root, false);
            root.transform.position = position;

            var view = root.AddComponent<EntityView>();

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            var art = Object.Instantiate(entry.Prefab, visual.transform);
            VisualFitter.Fit(art, entry);

            if (tintOverride.HasValue) ApplyOwnerTint(art, tintOverride.Value);

            // O flash de dano precisa VOLTAR para a cor certa: a do dono quando há uma, a do
            // artista quando não há. Ler a cor base do material comprado é o que impede o
            // feedback de "consertar" a peça com a paleta errada na primeira vez que leva dano.
            var renderer = art.GetComponentInChildren<Renderer>();
            var baseColor = tintOverride ?? Color.white;

            if (!tintOverride.HasValue && renderer != null && renderer.sharedMaterial != null &&
                renderer.sharedMaterial.HasProperty(ShaderIds.BaseColor))
                baseColor = renderer.sharedMaterial.GetColor(ShaderIds.BaseColor);

            view.SetVisual(visual.transform, renderer, baseColor);
            return view;
        }

        public void Dispose() => _placeholders.Dispose();
    }
}
