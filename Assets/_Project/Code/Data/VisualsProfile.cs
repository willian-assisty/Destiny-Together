using System;
using System.Collections.Generic;
using DestinyTogether.Sim;
using UnityEngine;

namespace DestinyTogether.Data
{
    /// <summary>
    /// Uma peça de arte ligada a uma definição de conteúdo.
    ///
    /// Os campos de ajuste existem porque asset de loja nunca vem na escala do seu jogo: um pack
    /// de floresta pode ter árvores de 12 unidades e um pack de cidade casas de 0,4. Normalizar
    /// isso aqui — e não no import settings de cada FBX — mantém o ajuste versionado, reversível
    /// e visível em um lugar só.
    /// </summary>
    [Serializable]
    public struct VisualEntry
    {
        [Tooltip("Nome interno da definicao (Balestra, Enxame, Arvore...). Deve casar com o DefName.")]
        public string DefName;

        public GameObject Prefab;

        [Tooltip("Quantas CELULAS a peca deve ocupar de largura. O auto-fit escala para caber nisso.")]
        public float TargetCells;

        [Tooltip("Escala o resultado do auto-fit. 1 = exatamente TargetCells.")]
        public float ScaleMultiplier;

        [Tooltip("Deslocamento local aplicado depois do encaixe, em unidades Unity.")]
        public Vector3 Offset;

        [Tooltip("Rotacao local em graus. Use quando o modelo nao vem com +Z para a frente.")]
        public Vector3 EulerAngles;

        [Tooltip("Teto de altura em CELULAS. 0 = sem limite. O encaixe normaliza pela LARGURA, " +
                 "entao uma peca alta e estreita (arvore, torre) estoura em altura se ninguem " +
                 "segurar — este campo e quem segura.")]
        public float MaxHeightCells;

        [Tooltip("Desliga o auto-fit e usa a escala original do prefab.")]
        public bool KeepOriginalScale;

        public DefId Id => DefId.FromName(DefName);

        public static VisualEntry Create(string defName, GameObject prefab, float targetCells = 1f,
                                         float maxHeightCells = 0f)
            => new VisualEntry
            {
                DefName = defName,
                Prefab = prefab,
                TargetCells = targetCells,
                MaxHeightCells = maxHeightCells,
                ScaleMultiplier = 1f,
                Offset = Vector3.zero,
                EulerAngles = Vector3.zero
            };
    }

    /// <summary>
    /// O conjunto de arte do jogo inteiro, em um asset.
    ///
    /// Trocar o profile referenciado no Bootstrap troca a aparência de tudo em um clique — e
    /// permite comparar placeholder e arte final lado a lado sem tocar em código. Qualquer
    /// entrada ausente cai automaticamente para a primitiva, então importar meia dúzia de árvores
    /// não obriga ninguém a autorar as outras quarenta peças antes de rodar o jogo.
    ///
    /// Este é o ponto único de contato entre a arte comprada e a simulação. A simulação continua
    /// sem saber que existe um prefab.
    /// </summary>
    [CreateAssetMenu(menuName = "Destiny Together/Perfil Visual", fileName = "Visuals_")]
    public sealed class VisualsProfile : ScriptableObject
    {
        [Header("Identidade")]
        public string DisplayName = "Perfil";
        [TextArea] public string Notes = "";

        [Tooltip("Versao do mapeamento que gerou este asset. Quando o codigo traz um mapeamento " +
                 "mais novo, o setup regenera sozinho em vez de deixar valores velhos em silencio.")]
        public int SetupVersion;

        [Header("Predios e monstros e herois")]
        public List<VisualEntry> Entries = new List<VisualEntry>();

        [Header("Nos de recurso dos Arredores")]
        public VisualEntry Tree;
        public VisualEntry Rock;
        public VisualEntry Chest;

        [Header("Cenario")]
        [Tooltip("Substitui o cubo achatado da Prefeitura.")]
        public VisualEntry TownHall;

        [Tooltip("Material do chao dos Arredores. Vazio = cinza chapado.")]
        public Material GroundMaterial;

        [Tooltip("Material dos tiles do tabuleiro. Vazio = cor chapada por Quadrante.")]
        public Material TileMaterial;
        [Tooltip("Vegetacao: florestas dos cantos e o mundo procedural. Nao afeta a simulacao.")]
        public List<GameObject> ScatterProps = new List<GameObject>();

        [Tooltip("Rocha e penhasco das pedreiras do mundo procedural. Vazio = primitivas.")]
        public List<GameObject> QuarryProps = new List<GameObject>();

        [Header("Mundo procedural")]
        [Tooltip("Raio em CHUNKS (24 celulas) de cenario desenhado em volta do heroi. " +
                 "Cada chunk custa ate ~26 objetos; 5 ja cobre o alcance de visao diurno.")]
        [Range(2, 10)] public int PropRadiusChunks = 5;

        [Tooltip("Largura alvo dos props de pedreira, em celulas.")]
        public float QuarryTargetCells = 2.6f;
        public float QuarryMaxHeightCells = 4f;

        [Tooltip("Total de props, dividido entre as quatro florestas.")]
        [Range(0, 600)] public int ScatterCount = 0;

        [Tooltip("Raio de cada floresta, em celulas.")]
        public float ForestRadius = 13f;

        [Tooltip("Concentracao: 1 = uniforme na area; abaixo de 1 adensa o miolo e rareia a borda.")]
        [Range(0.3f, 1.5f)] public float ForestDensityBias = 0.72f;

        [Tooltip("Nenhum prop nasce a menos que isto do centro. Mantem o campo de batalha limpo.")]
        public float ForestClearing = 4f;

        [Tooltip("Largura alvo de cada prop, em CELULAS. Props de cenario de pack costumam ter " +
                 "dezenas de unidades — sem normalizar, um penhasco cobre a cidade inteira.")]
        public float ScatterTargetCells = 2.2f;

        [Tooltip("Teto de altura dos props, em celulas. Sem isto uma arvore estreita vira torre.")]
        public float ScatterMaxHeightCells = 5f;

        [Tooltip("Variacao aleatoria de tamanho aplicada DEPOIS da normalizacao.")]
        public float ScatterMinScale = 0.7f;
        public float ScatterMaxScale = 1.5f;

        [Tooltip("Correcao de eixo dos props de cenario. O pack Polylised e Z-up e precisa de -90 em X.")]
        public Vector3 ScatterEulerAngles = new Vector3(-90f, 0f, 0f);

        [Header("Tom do chao")]
        [Tooltip("Multiplica a cor do material do chao. Escurecer casa o pack de deserto com a " +
                 "atmosfera noturna sem precisar autorar um material novo.")]
        public Color GroundTint = new Color(0.45f, 0.44f, 0.42f, 1f);

        private Dictionary<int, VisualEntry> _lookup;

        private void OnEnable() => _lookup = null;

        public bool TryGet(DefId id, out VisualEntry entry)
        {
            EnsureLookup();
            return _lookup.TryGetValue(id.Value, out entry) && entry.Prefab != null;
        }

        public bool TryGetNode(HarvestNodeKind kind, out VisualEntry entry)
        {
            entry = kind switch
            {
                HarvestNodeKind.Arvore => Tree,
                HarvestNodeKind.Rocha => Rock,
                _ => Chest
            };
            return entry.Prefab != null;
        }

        public bool TryGetTownHall(out VisualEntry entry)
        {
            entry = TownHall;
            return entry.Prefab != null;
        }

        private void EnsureLookup()
        {
            if (_lookup != null) return;
            _lookup = new Dictionary<int, VisualEntry>(Entries.Count);
            foreach (var e in Entries)
            {
                if (string.IsNullOrEmpty(e.DefName) || e.Prefab == null) continue;
                _lookup[e.Id.Value] = e;
            }
        }

        /// <summary>Chamado pelas ferramentas de editor depois de mexer na lista.</summary>
        public void Invalidate() => _lookup = null;

        /// <summary>Quantas definicoes ja tem arte de verdade. Alimenta o relatorio de cobertura.</summary>
        public int MappedCount()
        {
            int n = 0;
            foreach (var e in Entries) if (e.Prefab != null) n++;
            if (Tree.Prefab != null) n++;
            if (Rock.Prefab != null) n++;
            if (Chest.Prefab != null) n++;
            if (TownHall.Prefab != null) n++;
            return n;
        }
    }
}
