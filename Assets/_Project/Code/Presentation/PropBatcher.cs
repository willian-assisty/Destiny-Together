using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using DestinyTogether.Data;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// Desenha o cenario do mundo por INSTANCING, sem um GameObject por peca.
    ///
    /// Existe porque densidade de floresta e um numero que o design quer subir e a arquitetura
    /// anterior nao deixava. Com um GameObject por arvore, uma mata fechada de 360 pecas por chunk
    /// vezes 121 chunks desenhados sao ~26 mil objetos — e cada um custa Transform, hierarquia,
    /// culling individual e uma alocacao no carregamento do chunk. O gargalo nao e a GPU: sao
    /// pixels de sobra e triangulos de sobra numa arvore de 250. E CPU gasta administrando
    /// objetos que ninguem toca.
    ///
    /// Prop de cenario e o caso perfeito para instancing: nunca se move, nunca e clicado, nunca
    /// entra na simulacao. O que sobra dele e uma MATRIZ.
    ///
    /// O molde de cada prefab e medido UMA vez (instancia, encaixa com <see cref="VisualFitter"/>,
    /// le malha/material/matriz, destroi). Dali em diante uma arvore custa 64 bytes numa lista.
    /// </summary>
    public sealed class PropBatcher
    {
        /// <summary>
        /// Teto de instancias por chamada. E limite da API, nao escolha nossa.
        /// </summary>
        private const int BatchLimit = 1023;

        /// <summary>Um pedaco desenhavel: uma malha, um material, e onde ele fica dentro do molde.</summary>
        public readonly struct Part
        {
            public readonly Mesh Mesh;
            public readonly Material Material;
            public readonly int SubMesh;
            public readonly Matrix4x4 Local;

            public Part(Mesh mesh, Material material, int subMesh, Matrix4x4 local)
            {
                Mesh = mesh;
                Material = material;
                SubMesh = subMesh;
                Local = local;
            }
        }

        /// <summary>O molde de um prefab ja encaixado: um ou mais pedacos e a altura que ele ocupa.</summary>
        public sealed class Template
        {
            public readonly List<Part> Parts = new List<Part>(2);
            public float Height = 1f;
        }

        private readonly Dictionary<int, Template> _templates = new Dictionary<int, Template>(32);
        private readonly Transform _stage;

        public PropBatcher(Transform parent)
        {
            var go = new GameObject("MoldesDeProp");
            go.transform.SetParent(parent, false);
            go.SetActive(false);   // o palco nunca e desenhado; ele so serve de regua
            _stage = go.transform;
        }

        /// <summary>
        /// Molde de um prefab para uma medida. Medido uma vez e reaproveitado para sempre.
        ///
        /// A medicao passa pelo MESMO <see cref="VisualFitter"/> que o caminho de GameObject usava.
        /// Reimplementar o encaixe aqui daria duas formulas de normalizacao que divergem no dia em
        /// que alguem corrigir uma so — e o sintoma seria arte de tamanho diferente conforme o
        /// caminho de desenho, que ninguem liga a um refactor de performance.
        /// </summary>
        public Template GetTemplate(GameObject prefab, in VisualEntry entry)
        {
            if (prefab == null) return null;

            // A chave inclui a medida: a mesma malha entra na mata com 2,2 celulas e na pedreira
            // com 6,5, e sao moldes diferentes.
            int key = prefab.GetInstanceID();
            key = key * 397 ^ Mathf.RoundToInt(entry.TargetCells * 100f);
            key = key * 397 ^ Mathf.RoundToInt(entry.MaxHeightCells * 100f);

            if (_templates.TryGetValue(key, out var cached)) return cached;

            var template = Build(prefab, entry);
            _templates[key] = template;
            return template;
        }

        private Template Build(GameObject prefab, in VisualEntry entry)
        {
            var template = new Template();

            var holder = new GameObject("molde");
            holder.transform.SetParent(_stage, false);

            var instance = Object.Instantiate(prefab, holder.transform);

            // Multiplicador 1: a variacao de tamanho de cada arvore entra na matriz da instancia,
            // nao no molde. Escala uniforme em volta da origem mantem a base no chao, entao o
            // encaixe continua valendo depois de multiplicada.
            var measure = entry;
            measure.ScaleMultiplier = 1f;
            VisualFitter.Fit(instance, measure);

            var toHolder = holder.transform.worldToLocalMatrix;

            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;

                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null) continue;

                var materials = renderer.sharedMaterials;
                var local = toHolder * filter.transform.localToWorldMatrix;

                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    var material = i < materials.Length ? materials[i] : null;
                    if (material == null) continue;

                    // Instancing precisa estar ligado no material, senao a chamada cai para uma
                    // instancia por draw e o ganho inteiro evapora em silencio.
                    material.enableInstancing = true;
                    template.Parts.Add(new Part(mesh, material, i, local));
                }
            }

            if (VisualFitter.TryGetBoundsInParent(instance, out var bounds))
                template.Height = Mathf.Max(0.1f, bounds.size.y);

            Object.DestroyImmediate(holder);
            return template;
        }

        /// <summary>Molde de uma primitiva, para quando nao ha arte importada.</summary>
        public Template GetPrimitiveTemplate(PlaceholderFactory factory, in VisualStyle style)
        {
            int key = ((int)style.Shape + 1) * 7919 ^ style.Color.GetHashCode();
            if (_templates.TryGetValue(key, out var cached)) return cached;

            var template = new Template();
            var holder = new GameObject("molde");
            holder.transform.SetParent(_stage, false);

            var instance = factory.CreatePrimitive(style);
            instance.transform.SetParent(holder.transform, false);

            var toHolder = holder.transform.worldToLocalMatrix;
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null) continue;

                var material = renderer.sharedMaterial;
                if (material == null) continue;
                material.enableInstancing = true;

                template.Parts.Add(new Part(filter.sharedMesh, material, 0,
                                            toHolder * filter.transform.localToWorldMatrix));
            }

            if (VisualFitter.TryGetBoundsInParent(instance, out var bounds))
                template.Height = Mathf.Max(0.1f, bounds.size.y);

            Object.DestroyImmediate(holder);
            _templates[key] = template;
            return template;
        }

        // ------------------------------------------------------------------------------------
        // Desenho
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Emite um lote. <paramref name="matrices"/> ja tem as matrizes FINAIS de mundo, com a
        /// posicao do pedaco dentro do molde ja embutida.
        ///
        /// Isso importa: `RenderMeshInstanced` e imediato e roda todo frame. A primeira versao
        /// disto multiplicava `matriz * pedaco` na hora de desenhar — 26 mil multiplicacoes de
        /// matriz 4x4 por frame, para produzir sempre o mesmo resultado. Assar na montagem troca
        /// trabalho por frame por memoria: 64 bytes por peca, uma vez.
        ///
        /// A lista e passada direto para a API, sem copiar para um vetor temporario.
        /// </summary>
        public static void Draw(in Part part, List<Matrix4x4> matrices, Bounds bounds,
                                bool castShadows)
        {
            if (matrices == null || matrices.Count == 0) return;

            var rp = new RenderParams(part.Material)
            {
                worldBounds = bounds,
                // Sombra so perto. Com dezenas de milhares de arvores, mandar todas para o mapa de
                // sombra custa mais que desenha-las — e sombra de arvore a 150 unidades cai fora
                // da cascata mais distante de qualquer jeito.
                shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
            };

            for (int start = 0; start < matrices.Count; start += BatchLimit)
            {
                int count = Mathf.Min(BatchLimit, matrices.Count - start);
                Graphics.RenderMeshInstanced(rp, part.Mesh, part.SubMesh, matrices, count, start);
            }
        }

        public void Dispose()
        {
            if (_stage != null) Object.Destroy(_stage.gameObject);
            _templates.Clear();
        }
    }
}
