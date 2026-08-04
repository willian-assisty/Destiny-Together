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
        private const string VisualsPath = ContentFolder + "/Visuals_Cidade.asset";
        private const string AtmospherePath = ContentFolder + "/Atmosfera_Nebuloso.asset";

        private const string City = "Assets/Polylised - Medieval Desert City/Prefabs/";
        private const string Forest = "Assets/Fantasy Forest Environment Free Sample/";

        /// <summary>(definição, caminho do prefab, largura em células, por quê)</summary>
        private static readonly (string def, string path, float cells)[] Buildings =
        {
            // Torres: três silhuetas diferentes para três funções diferentes.
            (DefaultContent.Balestra,         City + "prefab_unique_buildings/castle_tower_round.prefab",       1.0f),
            (DefaultContent.TorreDeGelo,      City + "prefab_unique_buildings/castle_tower_octagon.prefab",     1.0f),
            (DefaultContent.BalistaDeImpacto, City + "prefab_unique_buildings/castle_tower_rectangular.prefab", 1.0f),
            (DefaultContent.PostoDeVigia,     City + "prefab_unique_buildings/citadel_tower_a.prefab",          0.85f),

            // Braseiro é literalmente uma fogueira presa — e ainda emite luz no escuro.
            (DefaultContent.Braseiro,         City + "prefab_props/fire_cage.prefab",                           0.8f),

            // Muralha: peça de parede de verdade, baixa e larga.
            (DefaultContent.Muralha,          City + "prefab_unique_buildings/castle_wall_5m.prefab",           1.0f),

            // Produção: casas civis. Não atiram, então não podem parecer que atiram.
            (DefaultContent.Serraria,         City + "prefab_civilian_buildings/civilian_house_03.prefab",      1.0f),
            (DefaultContent.Pedreira,         City + "prefab_civilian_buildings/civilian_house_11.prefab",      1.0f),
            (DefaultContent.Oficina,          City + "prefab_civilian_buildings/civilian_house_19.prefab",      1.0f),

            // Depósito: pilha de barris lê como armazenamento à primeira vista.
            (DefaultContent.Deposito,         City + "prefab_props/barrel_group.prefab",                        1.0f),
        };

        private const string TownHallPath = City + "prefab_unique_buildings/citadel_main.prefab";
        private const string TreePath     = City + "prefab_trees/dead_tree_a.prefab";
        private const string RockPath     = City + "prefab_terrain/cliff_01.prefab";
        private const string ChestPath    = City + "prefab_props/box.prefab";
        private const string GroundMat    = Forest + "Materials/dirt01.mat";

        /// <summary>Cenário decorativo. Árvores mortas dominam — o clima é de cidade sitiada.</summary>
        private static readonly string[] ScatterPaths =
        {
            City + "prefab_trees/dead_tree_b.prefab", City + "prefab_trees/dead_tree_c.prefab",
            City + "prefab_trees/dead_tree_d.prefab", City + "prefab_trees/dead_tree_e.prefab",
            City + "prefab_trees/dead_tree_f.prefab", City + "prefab_trees/dead_tree_g.prefab",
            City + "prefab_trees/dead_tree_h.prefab", City + "prefab_trees/dead_tree_i.prefab",
            City + "prefab_trees/dead_tree_j.prefab",
            City + "prefab_trees/pine_a.prefab",      City + "prefab_trees/pine_c.prefab",
            City + "prefab_terrain/cliff_02.prefab",  City + "prefab_terrain/cliff_03.prefab",
            City + "prefab_terrain/cliff_04.prefab",
            City + "prefab_props/fence.prefab",       City + "prefab_props/barrel.prefab",
        };

        // ------------------------------------------------------------------------------

        [MenuItem("Destiny Together/Aplicar arte importada (Polylised + Floresta)", false, 5)]
        public static void ApplyMenu() => Apply(verbose: true);

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

        private static void TrySetupOnce()
        {
            if (AssetDatabase.LoadAssetAtPath<VisualsProfile>(VisualsPath) != null) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TownHallPath) == null) return; // packs ausentes
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

        public static void Apply(bool verbose)
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

            visuals.DisplayName = "Cidade sitiada";
            visuals.Notes = "Polylised - Medieval Desert City + Fantasy Forest. " +
                            "Gerado por Destiny Together > Aplicar arte importada.";
            visuals.Entries.Clear();

            foreach (var (def, path, cells) in Buildings)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { missing.Add(path); continue; }
                visuals.Entries.Add(VisualEntry.Create(def, prefab, cells));
                mapped++;
            }

            visuals.TownHall = MakeEntry("Prefeitura", TownHallPath, 3.2f, missing, ref mapped);
            visuals.Tree     = MakeEntry("Arvore",     TreePath,     1.8f, missing, ref mapped);
            visuals.Rock     = MakeEntry("Rocha",      RockPath,     1.3f, missing, ref mapped);
            visuals.Chest    = MakeEntry("Bau",        ChestPath,    0.9f, missing, ref mapped);

            visuals.GroundMaterial = AssetDatabase.LoadAssetAtPath<Material>(GroundMat);
            if (visuals.GroundMaterial == null) missing.Add(GroundMat);

            visuals.ScatterProps = ScatterPaths
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(g => g != null)
                .ToList();
            if (visuals.ScatterCount == 0) visuals.ScatterCount = 110;
            visuals.ScatterMinScale = 0.7f;
            visuals.ScatterMaxScale = 1.5f;

            visuals.Invalidate();
            EditorUtility.SetDirty(visuals);
            EditorUtility.SetDirty(atmosphere);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            BindToOpenScene(visuals, atmosphere, verbose);

            if (!verbose) return;

            Debug.Log($"[Destiny Together] Arte aplicada: {mapped} pecas mapeadas, " +
                      $"{visuals.ScatterProps.Count} props de cenario ({visuals.ScatterCount} instancias). " +
                      (missing.Count == 0
                          ? "Nada faltando."
                          : $"NAO encontrados ({missing.Count}) — seguem em primitiva:\n  " +
                            string.Join("\n  ", missing)));
        }

        private static VisualEntry MakeEntry(string defName, string path, float cells,
                                             List<string> missing, ref int mapped)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { missing.Add(path); return default; }
            mapped++;
            return VisualEntry.Create(defName, prefab, cells);
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
        /// Preenche os campos do Bootstrap se a cena estiver aberta. Não abre nem salva cena por
        /// conta própria — mexer no arquivo de cena de alguém sem pedir é a receita para trabalho
        /// perdido.
        /// </summary>
        private static void BindToOpenScene(VisualsProfile visuals, AtmosphereProfile atmosphere, bool verbose)
        {
            var bootstrap = Object.FindFirstObjectByType<Bootstrap>();
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
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);

            if (verbose)
                Debug.Log("[Destiny Together] Perfis ligados ao Bootstrap. Salve a cena (Ctrl+S) e de Play.");
        }
    }
}
