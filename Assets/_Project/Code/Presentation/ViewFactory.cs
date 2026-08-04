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
        public EntityView CreateEntityView(string name, DefId def, VisualStyle fallback,
                                           Vector3 position, Color? tintOverride = null)
        {
            if (_profile != null && _profile.TryGet(def, out var entry))
                return CreateFromPrefab(name, entry, position, fallback.Color);

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
        /// Vegetação decorativa dos Arredores. Puramente cosmética: a simulação não sabe que
        /// existe, e por isso pode ser desligada inteira sem mudar uma regra.
        /// </summary>
        public void ScatterProps(Vec2 center, float innerRadius, float outerRadius, int seed)
        {
            if (_profile == null || _profile.ScatterCount <= 0 || _profile.ScatterProps.Count == 0) return;

            var container = new GameObject("Cenario").transform;
            container.SetParent(_root, false);

            var rng = new Rng(seed);
            for (int i = 0; i < _profile.ScatterCount; i++)
            {
                var prefab = _profile.ScatterProps[rng.Range(0, _profile.ScatterProps.Count)];
                if (prefab == null) continue;

                var point = rng.PointInRing(center, innerRadius, outerRadius);

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
                    ScaleMultiplier = rng.Range(_profile.ScatterMinScale, _profile.ScatterMaxScale),
                    EulerAngles = _profile.ScatterEulerAngles
                });
            }
        }

        private EntityView CreateFromPrefab(string name, in VisualEntry entry, Vector3 position, Color tint)
        {
            var root = new GameObject(name);
            root.transform.SetParent(_root, false);
            root.transform.position = position;

            var view = root.AddComponent<EntityView>();

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            var art = Object.Instantiate(entry.Prefab, visual.transform);
            VisualFitter.Fit(art, entry);

            // O flash de dano precisa VOLTAR para a cor do artista, não para a do placeholder.
            // Ler a cor base do material comprado é o que impede o feedback de "consertar" a peça
            // com a paleta errada na primeira vez que ela leva dano.
            var renderer = art.GetComponentInChildren<Renderer>();
            var baseColor = Color.white;
            if (renderer != null && renderer.sharedMaterial != null &&
                renderer.sharedMaterial.HasProperty(ShaderIds.BaseColor))
                baseColor = renderer.sharedMaterial.GetColor(ShaderIds.BaseColor);

            view.SetVisual(visual.transform, renderer, baseColor);
            return view;
        }

        public void Dispose() => _placeholders.Dispose();
    }
}
