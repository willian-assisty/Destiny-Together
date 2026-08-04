using System;
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
    /// Liga a arte comprada às definições do jogo.
    ///
    /// Packs de Asset Store trazem centenas de prefabs com nomes de artista ("SM_Tree_04",
    /// "Building_House_A"). Casar isso na mão com as 20 definições do jogo é o tipo de trabalho
    /// que ninguém refaz quando troca de pack — então a janela propõe o mapeamento por
    /// palavra-chave, mostra o que achou, e deixa qualquer linha ser corrigida antes de aplicar.
    ///
    /// O que ela NÃO faz: decidir por você. Toda sugestão aparece com o caminho do prefab e pode
    /// ser trocada ou zerada. Cobertura parcial é um estado válido — o que não for mapeado
    /// continua em primitiva.
    /// </summary>
    public sealed class ArtMapperWindow : EditorWindow
    {
        private const string ProfileFolder = "Assets/_Project/Content/Visuals";

        private VisualsProfile _profile;
        private DefaultBinding[] _bindings;
        private Vector2 _scroll;
        private string _searchFolder = "Assets";
        private string _status = "";
        private bool _scanned;

        [MenuItem("Destiny Together/Mapear arte importada", false, 10)]
        public static void Open()
        {
            var window = GetWindow<ArtMapperWindow>(false, "Mapear arte", true);
            window.minSize = new Vector2(720, 480);
            window.Show();
        }

        // ------------------------------------------------------------------------------
        // Dicionario de palavras-chave
        //
        // Ordem importa: a primeira palavra e a mais especifica e vale mais pontos.
        // ------------------------------------------------------------------------------
        private static readonly (string def, string label, float cells, string[] keys)[] Recipes =
        {
            (DefaultContent.Balestra,         "Balestra",           0.9f, new[]{ "ballista","crossbow","archer","arrow","turret","tower" }),
            (DefaultContent.Braseiro,         "Braseiro",           0.9f, new[]{ "brazier","campfire","firepit","torch","fire","lamp" }),
            (DefaultContent.TorreDeGelo,      "Torre de Gelo",      0.9f, new[]{ "ice","frost","crystal","obelisk","spire" }),
            (DefaultContent.BalistaDeImpacto, "Balista de Impacto", 0.9f, new[]{ "catapult","trebuchet","siege","ram","ballista" }),
            (DefaultContent.Serraria,         "Serraria",           1.0f, new[]{ "sawmill","lumber","timber","woodcut","logs","workshop" }),
            (DefaultContent.Pedreira,         "Pedreira",           1.0f, new[]{ "quarry","mine","stonecut","rockpile","stone" }),
            (DefaultContent.Oficina,          "Oficina",            1.0f, new[]{ "forge","blacksmith","anvil","smith","workshop" }),
            (DefaultContent.Muralha,          "Muralha",            1.0f, new[]{ "wall","rampart","palisade","barrier","fence","gate" }),
            (DefaultContent.PostoDeVigia,     "Posto de Vigia",     0.9f, new[]{ "watchtower","lookout","guard","watch","tower" }),
            (DefaultContent.Deposito,         "Deposito",           1.0f, new[]{ "warehouse","storage","granary","barn","silo","depot","house" }),
        };

        private static readonly string[] TreeKeys  = { "tree","pine","oak","birch","palm","spruce","willow","trunk","bush" };
        private static readonly string[] RockKeys  = { "rock","boulder","stone","cliff","pebble" };
        private static readonly string[] ChestKeys = { "chest","crate","barrel","box","sack","treasure" };
        private static readonly string[] HallKeys  = { "townhall","castle","keep","palace","temple","cathedral","citadel","mansion","tower" };
        private static readonly string[] ScatterKeys = { "grass","fern","plant","flower","mushroom","bush","shrub","rock","tree" };

        private sealed class DefaultBinding
        {
            public string DefName;
            public string Label;
            public float Cells;
            public GameObject Prefab;
            public string Suggestion;
            public bool IsNode;
            public HarvestNodeKind NodeKind;
            public bool IsTownHall;
        }

        // ------------------------------------------------------------------------------

        private void OnEnable()
        {
            if (_profile == null)
                _profile = AssetDatabase.FindAssets("t:VisualsProfile")
                    .Select(g => AssetDatabase.LoadAssetAtPath<VisualsProfile>(AssetDatabase.GUIDToAssetPath(g)))
                    .FirstOrDefault();
            BuildBindings();
        }

        private void BuildBindings()
        {
            var list = new List<DefaultBinding>();

            foreach (var r in Recipes)
                list.Add(new DefaultBinding { DefName = r.def, Label = r.label, Cells = r.cells });

            list.Add(new DefaultBinding { DefName = "Arvore", Label = "Arvore (recurso)", Cells = 1.6f, IsNode = true, NodeKind = HarvestNodeKind.Arvore });
            list.Add(new DefaultBinding { DefName = "Rocha", Label = "Rocha (recurso)", Cells = 1.1f, IsNode = true, NodeKind = HarvestNodeKind.Rocha });
            list.Add(new DefaultBinding { DefName = "Bau", Label = "Bau (recurso)", Cells = 0.9f, IsNode = true, NodeKind = HarvestNodeKind.Bau });
            list.Add(new DefaultBinding { DefName = "Prefeitura", Label = "PREFEITURA", Cells = 3f, IsTownHall = true });

            _bindings = list.ToArray();
            LoadFromProfile();
        }

        private void LoadFromProfile()
        {
            if (_profile == null || _bindings == null) return;

            foreach (var b in _bindings)
            {
                if (b.IsTownHall) { b.Prefab = _profile.TownHall.Prefab; continue; }
                if (b.IsNode)
                {
                    b.Prefab = b.NodeKind switch
                    {
                        HarvestNodeKind.Arvore => _profile.Tree.Prefab,
                        HarvestNodeKind.Rocha => _profile.Rock.Prefab,
                        _ => _profile.Chest.Prefab
                    };
                    continue;
                }
                var entry = _profile.Entries.FirstOrDefault(e => e.DefName == b.DefName);
                b.Prefab = entry.Prefab;
            }
        }

        // ------------------------------------------------------------------------------

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Mapear arte importada nas definicoes do jogo", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "1) Importe os packs pela Asset Store (Window > Package Manager > My Assets).\n" +
                "2) Aponte a pasta e clique em Varrer.\n" +
                "3) Ajuste o que quiser e clique em Aplicar.\n" +
                "O que ficar vazio continua em primitiva — cobertura parcial e um estado valido.",
                MessageType.Info);

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                _profile = (VisualsProfile)EditorGUILayout.ObjectField("Perfil visual", _profile, typeof(VisualsProfile), false);
                if (GUILayout.Button("Criar novo", GUILayout.Width(90))) CreateProfile();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _searchFolder = EditorGUILayout.TextField("Pasta de busca", _searchFolder);
                if (GUILayout.Button("Varrer", GUILayout.Width(90))) Scan();
            }

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, MessageType.None);

            EditorGUILayout.Space(6);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var b in _bindings)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var style = new GUIStyle(EditorStyles.boldLabel);
                        if (b.Prefab == null) style.normal.textColor = new Color(0.75f, 0.55f, 0.2f);
                        EditorGUILayout.LabelField(b.Label, style, GUILayout.Width(170));

                        b.Prefab = (GameObject)EditorGUILayout.ObjectField(b.Prefab, typeof(GameObject), false);

                        EditorGUILayout.LabelField($"{b.Cells:0.#} cel", GUILayout.Width(52));
                        b.Cells = EditorGUILayout.Slider(b.Cells, 0.3f, 6f, GUILayout.Width(120));
                    }

                    if (_scanned && !string.IsNullOrEmpty(b.Suggestion))
                        EditorGUILayout.LabelField($"    sugerido: {b.Suggestion}", EditorStyles.miniLabel);
                    else if (_scanned && b.Prefab == null)
                        EditorGUILayout.LabelField("    nada encontrado — fica em primitiva", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_profile == null))
                {
                    if (GUILayout.Button("APLICAR ao perfil", GUILayout.Height(32))) Apply();
                    if (GUILayout.Button("Preencher cenario com o que sobrou", GUILayout.Height(32), GUILayout.Width(260)))
                        FillScatter();
                    if (GUILayout.Button("Ligar perfil ao Bootstrap da cena", GUILayout.Height(32), GUILayout.Width(240)))
                        BindToScene();
                }
            }

            if (_profile != null)
                EditorGUILayout.LabelField($"Cobertura atual do perfil: {_profile.MappedCount()} de {_bindings.Length} pecas.",
                                           EditorStyles.miniLabel);
        }

        // ------------------------------------------------------------------------------

        private void CreateProfile()
        {
            Directory.CreateDirectory(ProfileFolder);
            var profile = CreateInstance<VisualsProfile>();
            profile.DisplayName = "Arte importada";
            profile.Notes = "Gerado pela janela 'Mapear arte importada'.";

            string path = AssetDatabase.GenerateUniqueAssetPath($"{ProfileFolder}/Visuals_Importado.asset");
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();

            _profile = profile;
            _status = $"Perfil criado em {path}.";
            LoadFromProfile();
        }

        private void Scan()
        {
            string folder = string.IsNullOrWhiteSpace(_searchFolder) ? "Assets" : _searchFolder.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(folder))
            {
                _status = $"Pasta '{folder}' nao existe.";
                return;
            }

            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                // O proprio projeto nao entra na busca: so interessa arte importada.
                .Where(p => !p.StartsWith("Assets/_Project/", StringComparison.Ordinal))
                .ToArray();

            if (paths.Length == 0)
            {
                _status = $"Nenhum prefab em '{folder}'. Os packs ja foram importados?";
                _scanned = true;
                return;
            }

            int matched = 0;
            foreach (var b in _bindings)
            {
                var keys = KeysFor(b);
                string best = null;
                float bestScore = 0f;

                foreach (var path in paths)
                {
                    float score = Score(path, keys);
                    if (score > bestScore) { bestScore = score; best = path; }
                }

                if (best == null || bestScore <= 0f) { b.Suggestion = null; continue; }

                b.Suggestion = $"{Path.GetFileNameWithoutExtension(best)}  ({bestScore:0.0} pts)  {Path.GetDirectoryName(best)}";
                if (b.Prefab == null)
                {
                    b.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(best);
                    matched++;
                }
            }

            _scanned = true;
            _status = $"{paths.Length} prefabs varridos em '{folder}'. {matched} sugestoes preenchidas. " +
                      "Confira antes de aplicar — casamento por nome erra.";
        }

        private static string[] KeysFor(DefaultBinding b)
        {
            if (b.IsTownHall) return HallKeys;
            if (b.IsNode)
                return b.NodeKind switch
                {
                    HarvestNodeKind.Arvore => TreeKeys,
                    HarvestNodeKind.Rocha => RockKeys,
                    _ => ChestKeys
                };

            foreach (var r in Recipes)
                if (r.def == b.DefName) return r.keys;
            return Array.Empty<string>();
        }

        /// <summary>
        /// Pontua um caminho de prefab contra as palavras-chave. A primeira palavra da receita e a
        /// mais especifica, entao vale mais — sem isso "tower" casaria com tudo antes de
        /// "watchtower" ter chance.
        /// </summary>
        private static float Score(string path, string[] keys)
        {
            if (keys.Length == 0) return 0f;

            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            string full = path.ToLowerInvariant();
            float score = 0f;

            for (int i = 0; i < keys.Length; i++)
            {
                float weight = 1f + (keys.Length - i) * 0.35f;
                if (name.Contains(keys[i])) score += weight * 2f;
                else if (full.Contains(keys[i])) score += weight * 0.5f;
            }

            // Variantes de LOD e colisao nunca sao a peca principal.
            if (name.Contains("lod") || name.Contains("collision") || name.Contains("_col")) score *= 0.2f;
            return score;
        }

        private void Apply()
        {
            if (_profile == null) return;
            Undo.RecordObject(_profile, "Mapear arte");

            _profile.Entries.Clear();

            foreach (var b in _bindings)
            {
                if (b.IsTownHall)
                {
                    _profile.TownHall = MakeEntry(b);
                    continue;
                }

                if (b.IsNode)
                {
                    switch (b.NodeKind)
                    {
                        case HarvestNodeKind.Arvore: _profile.Tree = MakeEntry(b); break;
                        case HarvestNodeKind.Rocha: _profile.Rock = MakeEntry(b); break;
                        default: _profile.Chest = MakeEntry(b); break;
                    }
                    continue;
                }

                if (b.Prefab != null) _profile.Entries.Add(MakeEntry(b));
            }

            _profile.Invalidate();
            EditorUtility.SetDirty(_profile);
            AssetDatabase.SaveAssets();

            _status = $"Aplicado. {_profile.MappedCount()} pecas com arte; o resto continua em primitiva.";
        }

        private static VisualEntry MakeEntry(DefaultBinding b)
        {
            var entry = VisualEntry.Create(b.DefName, b.Prefab, b.Cells);
            return entry;
        }

        /// <summary>
        /// Vegetação decorativa: pega o que sobrou do pack de floresta e espalha pelos Arredores.
        /// É puro cenário — a simulação não sabe que existe.
        /// </summary>
        private void FillScatter()
        {
            if (_profile == null) return;

            string folder = string.IsNullOrWhiteSpace(_searchFolder) ? "Assets" : _searchFolder.TrimEnd('/');
            var used = new HashSet<GameObject>(_bindings.Where(b => b.Prefab != null).Select(b => b.Prefab));

            var props = AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.StartsWith("Assets/_Project/", StringComparison.Ordinal))
                .Where(p => ScatterKeys.Any(k => Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Contains(k)))
                .Where(p => !Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Contains("lod"))
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(g => g != null && !used.Contains(g))
                .Take(24)
                .ToList();

            Undo.RecordObject(_profile, "Preencher cenario");
            _profile.ScatterProps = props;
            if (_profile.ScatterCount == 0) _profile.ScatterCount = 90;
            EditorUtility.SetDirty(_profile);
            AssetDatabase.SaveAssets();

            _status = $"{props.Count} props de cenario, {_profile.ScatterCount} instancias por partida. " +
                      "Ajuste ScatterCount no asset se pesar.";
        }

        private void BindToScene()
        {
            var bootstrap = FindFirstObjectByType<Bootstrap>();
            if (bootstrap == null)
            {
                _status = "Nenhum Bootstrap na cena aberta. Abra a cena Arena primeiro.";
                return;
            }

            Undo.RecordObject(bootstrap, "Ligar perfil visual");
            bootstrap.Visuals = _profile;
            EditorUtility.SetDirty(bootstrap);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);

            _status = "Perfil ligado ao Bootstrap. Salve a cena (Ctrl+S) e de Play.";
        }
    }
}
