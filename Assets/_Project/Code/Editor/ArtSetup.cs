using System.Collections.Generic;
using System.IO;
using System.Linq;
using DestinyTogether.App;
using DestinyTogether.Data;
using DestinyTogether.Sim;
using UnityEditor;
using UnityEngine;

namespace DestinyTogether.EditorTools
{
    /// <summary>
    /// Liga os packs "Polylised — Medieval Desert City" e "Fantasy Forest Environment" ao jogo.
    ///
    /// O mapeamento abaixo foi escolhido peça por peça, não por casamento de nome. O critério não
    /// é "qual prefab tem nome parecido", é <b>qual silhueta comunica a função</b> — a mesma
    /// gramática dos placeholders. Torre redonda atira longe, octógono congela, retangular
    /// empurra: três silhuetas distintas para três comportamentos distintos, legíveis de cima e
    /// no escuro. Uma casa civil nunca vira torre, porque casa não deve parecer que atira.
    ///
    /// Nada aqui é obrigatório: o que não for encontrado continua em primitiva, e a janela
    /// "Mapear arte importada" existe para trocar qualquer escolha destas.
    /// </summary>
    public static class ArtSetup
    {
        private const string ContentFolder = "Assets/_Project/Content/Visuals";
        private const string ScenePath = "Assets/_Project/Scenes/Arena.unity";
        private const string VisualsPath = ContentFolder + "/Visuals_Cidade.asset";
        private const string AtmospherePath = ContentFolder + "/Atmosfera_Nebuloso.asset";

        private const string City = "Assets/Polylised - Medieval Desert City/Prefabs/";
        private const string Forest = "Assets/Fantasy Forest Environment Free Sample/";

        /// <summary>
        /// (definição, prefab, largura em células, teto de altura em células)
        ///
        /// O teto de altura importa tanto quanto a largura: tudo aqui ocupa 1 tile de 1×1, e sem
        /// limite vertical uma torre de castelo real (dezenas de unidades de alto) vira um poste
        /// que tapa metade do tabuleiro visto de cima. Torres podem ser altas — são o marco da
        /// defesa; casas e muralhas ficam baixas para não competir.
        /// </summary>
        private static readonly (string def, string path, float cells, float maxHeight)[] Buildings =
        {
            // Torres: três silhuetas diferentes para três funções diferentes.
            (DefaultContent.Balestra,         City + "prefab_unique_buildings/castle_tower_round.prefab",       1.0f, 2.6f),
            (DefaultContent.TorreDeGelo,      City + "prefab_unique_buildings/castle_tower_octagon.prefab",     1.0f, 2.6f),
            (DefaultContent.BalistaDeImpacto, City + "prefab_unique_buildings/castle_tower_rectangular.prefab", 1.0f, 2.2f),
            (DefaultContent.PostoDeVigia,     City + "prefab_unique_buildings/citadel_tower_a.prefab",          0.85f, 3.2f),

            // Braseiro é literalmente uma fogueira presa — e ainda emite luz no escuro.
            (DefaultContent.Braseiro,         City + "prefab_props/fire_cage.prefab",                           0.8f, 1.4f),

            // Muralha: peça de parede de verdade, baixa e larga.
            (DefaultContent.Muralha,          City + "prefab_unique_buildings/castle_wall_5m.prefab",           1.0f, 1.2f),

            // Produção: casas civis. Não atiram, então não podem parecer que atiram.
            (DefaultContent.Serraria,         City + "prefab_civilian_buildings/civilian_house_03.prefab",      1.0f, 1.6f),
            (DefaultContent.Pedreira,         City + "prefab_civilian_buildings/civilian_house_11.prefab",      1.0f, 1.6f),
            (DefaultContent.Oficina,          City + "prefab_civilian_buildings/civilian_house_19.prefab",      1.0f, 1.6f),

            // Depósito: pilha de barris lê como armazenamento à primeira vista.
            (DefaultContent.Deposito,         City + "prefab_props/barrel_group.prefab",                        1.0f, 1.0f),
        };

        // Medido no FBX com o eixo correto (Z para cima): 6047 x 3651 de base por 6911 de ALTURA.
        // A citadela sempre foi a peça mais vertical e monumental do pack — ela só entrava
        // tombada. Trocá-la por uma igreja tratava o sintoma; corrigir o eixo resolve a causa.
        private const string TownHallPath = City + "prefab_unique_buildings/citadel_main.prefab";
        private const string TreePath     = City + "prefab_trees/dead_tree_a.prefab";
        private const string RockPath     = City + "prefab_terrain/cliff_01.prefab";
        private const string ChestPath    = City + "prefab_props/box.prefab";

        // Grama (não terra) + tint de grama morta: a textura dá a quebra visual que areia lisa
        // não dava, e o tint faz o verde virar palha seca sem precisar autorar material novo.
        private const string GroundMat    = Forest + "Materials/grass01.mat";
        private static readonly Color DeadGrassTint = new Color(0.40f, 0.35f, 0.21f, 1f);

        /// <summary>
        /// A mata das quatro florestas. SÓ árvores — penhascos e barris entravam aqui antes e
        /// poluíam a leitura: floresta tem que se ler como floresta à primeira olhada, senão
        /// deixa de marcar "aqui se colhe" e vira ruído no campo de visão.
        ///
        /// Mortas dominam (o clima é de cidade sitiada), com pinheiros para quebrar a silhueta.
        /// </summary>
        private static readonly string[] ScatterPaths =
        {
            City + "prefab_trees/dead_tree_b.prefab", City + "prefab_trees/dead_tree_c.prefab",
            City + "prefab_trees/dead_tree_d.prefab", City + "prefab_trees/dead_tree_e.prefab",
            City + "prefab_trees/dead_tree_f.prefab", City + "prefab_trees/dead_tree_g.prefab",
            City + "prefab_trees/dead_tree_h.prefab", City + "prefab_trees/dead_tree_i.prefab",
            City + "prefab_trees/dead_tree_j.prefab",
            City + "prefab_trees/pine_a.prefab",      City + "prefab_trees/pine_b.prefab",
            City + "prefab_trees/pine_c.prefab",      City + "prefab_trees/pine_d.prefab",
        };

        /// <summary>
        /// As pedreiras do mundo procedural. Penhascos e afloramentos voltam AQUI — eles poluíam
        /// a floresta quando estavam misturados na mesma lista, mas como bioma próprio fazem o
        /// oposto: dão ao jogador um segundo tipo de lugar, reconhecível de longe, para onde ir.
        /// </summary>
        private static readonly string[] QuarryPaths =
        {
            City + "prefab_terrain/cliff_01.prefab", City + "prefab_terrain/cliff_02.prefab",
            City + "prefab_terrain/cliff_03.prefab", City + "prefab_terrain/cliff_04.prefab",
            City + "prefab_terrain/rock_01.prefab",  City + "prefab_terrain/rock_02.prefab",
            City + "prefab_terrain/rock_03.prefab",
        };

        // ------------------------------------------------------------------------------

        [MenuItem("Destiny Together/Aplicar arte importada (Polylised + Floresta)", false, 5)]
        public static void ApplyMenu() => Apply(verbose: true, allowOpenScene: true);

        /// <summary>
        /// Roda uma vez após a compilação: se os packs estão presentes e os assets ainda não
        /// existem, monta tudo. Evita que o projeto fique num estado em que a arte está no disco
        /// mas o jogo continua rodando em cubos porque ninguém clicou num menu.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void AutoSetup()
        {
            EditorApplication.delayCall += TrySetupOnce;
        }

        /// <summary>
        /// Sobe quando o mapeamento muda de forma que exige regenerar o asset.
        /// v2: tetos de altura por peça, normalização de escala do cenário, tint do chão.
        /// v3: mapa dobrado, Prefeitura vertical (igreja), chão de grama morta.
        /// v4: correção de eixo do pack (Z-up) — o pack inteiro entrava deitado.
        /// v5: florestas concentradas nas quatro diagonais, miolo do mapa limpo.
        /// v6: mundo procedural — lista de pedreiras e raio de streaming de cenario.
        /// </summary>
        private const int CurrentSetupVersion = 6;

        private static void TrySetupOnce()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TownHallPath) == null) return; // packs ausentes

            var existing = AssetDatabase.LoadAssetAtPath<VisualsProfile>(VisualsPath);
            if (existing != null && existing.SetupVersion >= CurrentSetupVersion) return;

            Apply(verbose: true);
        }

        /// <summary>
        /// Importar arte não recompila nada, então <see cref="AutoSetup"/> sozinho nunca veria os
        /// packs chegando — o setup ficaria esperando um clique de menu que ninguém sabe que
        /// precisa dar. Este observador fecha essa lacuna: assim que os prefabs entram, a arte é
        /// aplicada.
        /// </summary>
        private sealed class ImportWatcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                                                       string[] movedTo, string[] movedFrom)
            {
                if (imported == null || imported.Length == 0) return;

                bool packArrived = false;
                foreach (var path in imported)
                {
                    if (path.StartsWith(City, System.StringComparison.Ordinal) ||
                        path.StartsWith(Forest, System.StringComparison.Ordinal))
                    {
                        packArrived = true;
                        break;
                    }
                }

                // TrySetupOnce sai cedo se o perfil já existe, então os assets que o próprio
                // Apply cria não realimentam este callback.
                if (packArrived) EditorApplication.delayCall += TrySetupOnce;
            }
        }

        public static void Apply(bool verbose, bool allowOpenScene = false)
        {
            if (!AssetDatabase.IsValidFolder(ContentFolder))
                Directory.CreateDirectory(ContentFolder);

            // Sem isto a cidade inteira aparece magenta: os packs vêm com shader built-in e o
            // projeto é URP. Converter primeiro evita o susto de "importei e quebrou tudo".
            UrpMaterialUpgrader.Convert(out _);

            var visuals = LoadOrCreate<VisualsProfile>(VisualsPath);
            var atmosphere = LoadOrCreate<AtmosphereProfile>(AtmospherePath);

            var missing = new List<string>();
            int mapped = 0;

            visuals.SetupVersion = CurrentSetupVersion;
            visuals.DisplayName = "Cidade sitiada";
            visuals.Notes = "Polylised - Medieval Desert City + Fantasy Forest. " +
                            "Gerado por Destiny Together > Aplicar arte importada.";
            visuals.Entries.Clear();

            foreach (var (def, path, cells, maxHeight) in Buildings)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { missing.Add(path); continue; }
                var entry = VisualEntry.Create(def, prefab, cells, maxHeight);
                entry.EulerAngles = PolylisedUpright;
                visuals.Entries.Add(entry);
                mapped++;
            }

            // 4.5 células de largura dentro dos 5x5 da Prefeitura, com folga para a altura ir a 7:
            // o centro da vila deve ser a coisa mais alta do tabuleiro.
            visuals.TownHall = MakeEntry("Prefeitura", TownHallPath, 4.5f, 7f, missing, ref mapped);
            visuals.Tree     = MakeEntry("Arvore",     TreePath,     1.6f, 3.2f, missing, ref mapped);
            visuals.Rock     = MakeEntry("Rocha",      RockPath,     1.3f, 1.1f, missing, ref mapped);
            visuals.Chest    = MakeEntry("Bau",        ChestPath,    0.9f, 0.9f, missing, ref mapped);

            visuals.GroundMaterial = AssetDatabase.LoadAssetAtPath<Material>(GroundMat);
            if (visuals.GroundMaterial == null) missing.Add(GroundMat);
            visuals.GroundTint = DeadGrassTint;

            visuals.ScatterProps = ScatterPaths
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(g => g != null)
                .ToList();
            // 320 arvores divididas por quatro florestas = 80 cada. Densidade suficiente para
            // ler como mata fechada; o miolo do mapa e as Faixas ortogonais continuam limpos.
            visuals.ScatterCount = 320;
            visuals.ForestRadius = 13f;
            visuals.ForestDensityBias = 0.72f;
            visuals.ForestClearing = 4f;
            visuals.ScatterTargetCells = 2.2f;
            visuals.ScatterMaxHeightCells = 5f;
            // Pedreiras do mundo procedural. Ausente na pasta = cai para primitiva sozinho, e o
            // mundo continua existindo — nunca ha um estado "meio migrado" em que a mata some.
            visuals.QuarryProps = QuarryPaths
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(g => g != null)
                .ToList();
            visuals.QuarryTargetCells = 2.6f;
            visuals.QuarryMaxHeightCells = 4f;
            visuals.PropRadiusChunks = 5;

            visuals.ScatterMinScale = 0.65f;
            visuals.ScatterMaxScale = 1.6f;

            visuals.Invalidate();
            EditorUtility.SetDirty(visuals);
            EditorUtility.SetDirty(atmosphere);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            BindToScene(visuals, atmosphere, verbose, allowOpenScene);

            if (!verbose) return;

            // Relatório de tamanhos junto com o setup: descobrir que uma peça saiu gigante ao dar
            // Play é caro; descobrir lendo o Console é grátis.
            Debug.Log(ArtValidator.Validate(visuals));

            Debug.Log($"[Destiny Together] Arte aplicada: {mapped} pecas mapeadas, " +
                      $"{visuals.ScatterProps.Count} props de mata + {visuals.QuarryProps.Count} de pedreira " +
                      $"({visuals.ScatterCount} instancias nas florestas da vila, mundo procedural sob demanda). " +
                      (missing.Count == 0
                          ? "Nada faltando."
                          : $"NAO encontrados ({missing.Count}) — seguem em primitiva:\n  " +
                            string.Join("\n  ", missing)));
        }

        /// <summary>
        /// O pack Polylised é modelado em Z-up mas exportado declarando UpAxis=Y, então o Unity
        /// importa tudo DEITADO. Confirmado medindo peças com simetria de revolução: o barril tem
        /// X=177,9 e Y=176,0 (o círculo está em XY) com Z=294,7 de comprimento; a fonte tem
        /// X=Y=462,17 exatos. Ambos só fazem sentido com Z para cima.
        ///
        /// Girar -90° em X põe o pack inteiro de pé. Vale para todas as peças dele — torres,
        /// muralhas, casas, props e árvores.
        /// </summary>
        private static readonly Vector3 PolylisedUpright = new Vector3(-90f, 0f, 0f);

        private static VisualEntry MakeEntry(string defName, string path, float cells, float maxHeight,
                                             List<string> missing, ref int mapped)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { missing.Add(path); return default; }
            mapped++;
            var entry = VisualEntry.Create(defName, prefab, cells, maxHeight);
            if (path.StartsWith(City, System.StringComparison.Ordinal))
                entry.EulerAngles = PolylisedUpright;
            return entry;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        /// <summary>
        /// Liga os perfis ao Bootstrap e SALVA a cena.
        ///
        /// Salvar é o ponto crítico: sem isso o campo fica preenchido só em memória e o Play
        /// seguinte roda com o valor do disco — que é vazio. Foi exatamente esse o modo de falha
        /// que fez a arte "não aparecer" mesmo com tudo importado e mapeado.
        ///
        /// Quando a cena não está aberta, só abre se <paramref name="allowOpenScene"/> permitir,
        /// e ainda assim passando pelo diálogo padrão de salvar o trabalho em andamento.
        /// </summary>
        private static void BindToScene(VisualsProfile visuals, AtmosphereProfile atmosphere,
                                        bool verbose, bool allowOpenScene)
        {
            var bootstrap = Object.FindFirstObjectByType<Bootstrap>();

            if (bootstrap == null && allowOpenScene)
            {
                if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
                bootstrap = Object.FindFirstObjectByType<Bootstrap>();
            }

            if (bootstrap == null)
            {
                if (verbose)
                    Debug.Log("[Destiny Together] Abra a cena Arena e rode " +
                              "'Destiny Together > Aplicar arte importada' para ligar os perfis.");
                return;
            }

            Undo.RecordObject(bootstrap, "Aplicar arte");
            bootstrap.Visuals = visuals;
            bootstrap.Atmosfera = atmosphere;
            EditorUtility.SetDirty(bootstrap);

            var scene = bootstrap.gameObject.scene;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            if (verbose)
                Debug.Log("[Destiny Together] Perfis ligados ao Bootstrap e cena salva. Pode dar Play.");
        }
    }
}
