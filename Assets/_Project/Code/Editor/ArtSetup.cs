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

        /// <summary>Arte própria do projeto, ao contrário dos packs de loja que ficam na raiz.</summary>
        public const string CharacterFolder = "Assets/_Project/Art/Final/Characters/";

        /// <summary>Cenario proprio do projeto. Mesma estrutura de um personagem, sem esqueleto.</summary>
        public const string NatureFolder = "Assets/_Project/Art/Final/Nature/";

        /// <summary>
        /// As arvores do mundo procedural, todas do mesmo tronco estilistico.
        ///
        /// Uma lista e nao um par: quando <see cref="WorldPropStreamer"/> escolhia a malha por um
        /// hash do TIPO, cinco arvores diferentes teriam desenhado uma floresta com duas. Agora a
        /// escolha vem da variante sorteada pelo gerador, entao acrescentar uma linha aqui aumenta
        /// de verdade a variedade da mata.
        /// </summary>
        private static readonly string[] Trees =
        {
            "ArvoreBaixoPoli",
            "ArvoreGeometrica",
            "ArvoreMonolito",
            "PinheiroGeometrico",
            "PinheiroNevado",
        };

        /// <summary>Arte propria do projeto — vale para personagem e cenario.</summary>
        private static bool IsOwnArt(string path)
            => path.StartsWith(CharacterFolder, System.StringComparison.Ordinal) ||
               path.StartsWith(NatureFolder, System.StringComparison.Ordinal);

        /// <summary>
        /// Prefabs das arvores proprias: malha + material URP, sem Animator.
        ///
        /// Reusa o montador de personagem porque a operacao e a MESMA — FBX texturizado vira peca
        /// jogavel — e a lista de clipes vazia e o que separa uma coisa da outra. Manter dois
        /// montadores quase iguais custaria a proxima correcao ser feita so em um deles.
        /// </summary>
        private static List<GameObject> BuildTrees(List<string> missing, ref int mapped)
        {
            var built = new List<GameObject>(Trees.Length);

            foreach (var name in Trees)
            {
                string folder = $"{NatureFolder}{name}/";
                var prefab = CharacterSetup.Build(folder, name);
                if (prefab == null) { missing.Add($"{folder}{name}.fbx"); continue; }

                built.Add(prefab);
                mapped++;
            }

            return built;
        }

        /// <summary>
        /// (classe de herói, malha, largura em células, teto de altura em células)
        ///
        /// Herói é o único caso em que o teto de altura é o valor que MANDA, e a razão é a pose:
        /// medido, o Azure Sentinel tem 1,90 de envergadura por 1,40 de altura — braços abertos.
        /// Normalizar pela largura faria um personagem de braços abertos sair baixinho e, no dia
        /// em que ele for riggado com os braços ao lado do corpo, crescer sozinho. Com a altura
        /// mandando (largura folgada de propósito), a estatura fica constante em qualquer pose,
        /// que é o que um personagem precisa e um prédio não.
        ///
        /// 1,7 célula casa com as cápsulas de placeholder (1,4-1,6) e ocupa ~12% da altura da
        /// tela na câmera atual (FOV 35 a 22 unidades).
        /// </summary>
        private static readonly (string def, string name, float cells, float maxHeight)[] Heroes =
        {
            // "Azure Sentinel" -> Guarda: sentinela é quem segura a Linha, e azul já é a cor do
            // assento 0 na gramática de placeholder. O nome do arquivo casou com o design sozinho.
            (DefaultContent.Guarda, "AzureSentinel", 3.0f, 1.7f),

            // Arqueiro -> Arauto, que é a classe de alcance do jogo: o mais rápido (8,8), o mais
            // frágil (85 HP) e o de MAIOR raio de ataque. Arqueiro é exatamente esse kit.
            //
            // Medido no GLB, ele já vem 1,70 de altura com pivô nos pés — a única peça até agora
            // que chegou na escala e na orientação certas sem precisar de nada.
            (DefaultContent.Arauto, "Arqueiro", 3.0f, 1.7f),
        };

        /// <summary>Sufixos dos FBX de animação, na ordem em que entram no controlador.</summary>
        private static readonly string[] HeroClips = { "Run" };

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
        /// v7: primeiro personagem proprio (Azure Sentinel -> Guarda).
        /// v8: correcao de eixo do personagem — ele tambem e Z-up, como o pack.
        /// v9: personagem riggado com clipe de corrida; orientacao passa a ser medida (AutoUpright).
        /// v10: Arqueiro (previa em OBJ, sem rig) -> Arauto; malha sem esqueleto nao ganha Animator.
        /// v11: cinco arvores proprias na mata; mata fechada; prefab por variante em vez de por tipo.
        /// </summary>
        private const int CurrentSetupVersion = 11;

        private static void TrySetupOnce()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TownHallPath) == null) return; // packs ausentes

            var existing = AssetDatabase.LoadAssetAtPath<VisualsProfile>(VisualsPath);
            if (existing != null && existing.SetupVersion >= CurrentSetupVersion && !RigIsBroken()) return;

            Apply(verbose: true);
        }

        /// <summary>
        /// Um personagem tem FBX de animação no disco mas o prefab saiu sem Animator?
        ///
        /// O gate de versão sozinho é bom para "o setup mudou" e péssimo para "o setup falhou": um
        /// import fora de ordem podia gravar um prefab sem Animator, satisfazer o gate e deixar o
        /// personagem sem animação PARA SEMPRE, porque a única forma de reconstruir era um clique
        /// de menu que ninguém sabe que precisa dar. Foi exatamente assim que a corrida do primeiro
        /// personagem sumiu. Verificar o resultado em vez de confiar no número fecha esse buraco.
        /// </summary>
        private static bool RigIsBroken()
        {
            foreach (var (_, name, _, _) in Heroes)
            {
                string folder = $"{CharacterFolder}{name}/";

                bool hasClip = false;
                foreach (var suffix in HeroClips)
                    hasClip |= System.IO.File.Exists($"{folder}{name}_{suffix}.fbx");
                if (!hasClip) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}{name}.prefab");
                if (prefab == null || prefab.GetComponentInChildren<Animator>(true) == null)
                {
                    Debug.LogWarning($"[ArtSetup] '{name}' tem clipe no disco mas o prefab está sem " +
                                     $"Animator. Reconstruindo.");
                    return true;
                }
            }
            return false;
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
                        path.StartsWith(Forest, System.StringComparison.Ordinal) ||
                        path.StartsWith(CharacterFolder, System.StringComparison.Ordinal))
                    {
                        packArrived = true;
                        break;
                    }
                }

                // TrySetupOnce sai cedo se o perfil já existe, então os assets que o próprio
                // Apply cria não realimentam este callback.
                if (packArrived) EditorApplication.delayCall += TrySetupOnce;
            }

            /// <summary>
            /// Ajustes de import dos personagens, aplicados na primeira vez que o FBX entra.
            ///
            /// Existem aqui e não no .meta porque .meta é gerado pelo editor: um arquivo largado
            /// na pasta chega sem ele, e "esqueci de conferir o inspector" é o modo de falha mais
            /// comum de pipeline de arte. Escrever a regra em código a torna válida para o
            /// próximo personagem também.
            /// </summary>
            private void OnPreprocessModel()
            {
                if (!IsOwnArt(assetPath)) return;
                if (assetImporter is not ModelImporter importer) return;

                // O material vem do CharacterSetup, não do FBX: o Meshy referencia a textura por
                // um caminho da máquina dele (`/tmp/.../Character_output.fbm/texture_0.png`), que
                // aqui nunca existe. Importar materiais criaria um .mat com textura faltando.
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importCameras = false;
                importer.importLights = false;
                importer.importVisibility = false;
                importer.isReadable = false;
                importer.importBlendShapes = false;

                // A malha do personagem é `Personagens/Nome/Nome.fbx`; qualquer outro FBX naquela
                // pasta é clipe. Detectar por "tem underscore no nome" quebraria no primeiro
                // personagem chamado "Azure_Sentinel".
                string dir = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                bool isClip = !string.IsNullOrEmpty(dir) &&
                              System.IO.Path.GetFileNameWithoutExtension(assetPath) !=
                              System.IO.Path.GetFileName(dir);

                // Generic, não Humanoid. O clipe e a malha vêm do MESMO esqueleto, então a
                // ligação por caminho de transform casa exatamente e não há retarget para dar
                // errado. Humanoid destravaria a biblioteca do Mixamo, ao custo de um mapeamento
                // de avatar que pode falhar — troca que vale a pena depois de o jogo rodar, não
                // antes.
                // Malha SEM esqueleto (prévia em OBJ, por exemplo) não ganha rig. Pedir avatar de
                // uma malha estática produz um avatar inválido, e Animator com avatar inválido
                // congela o corpo em vez de deixá-lo em paz.
                if (!HasSkeleton(importer))
                {
                    importer.animationType = ModelImporterAnimationType.None;
                    importer.importAnimation = false;
                    return;
                }

                importer.animationType = ModelImporterAnimationType.Generic;
                importer.importAnimation = isClip;

                if (isClip)
                {
                    // O clipe não traz malha nova; ele empresta o esqueleto do personagem.
                    //
                    // Mas se a malha ainda não foi importada o avatar não existe, e `CopyFromOther`
                    // apontando para o nada devolve um clipe VAZIO. Nesse caso o clipe cria o
                    // próprio avatar a partir do esqueleto que ele mesmo carrega — medido, isso
                    // liga por caminho de transform sem uma única divergência.
                    var avatar = FindCharacterAvatar(assetPath);
                    importer.avatarSetup = avatar != null
                        ? ModelImporterAvatarSetup.CopyFromOther
                        : ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.sourceAvatar = avatar;
                }
                else
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

                    // Soldar e otimizar só a malha do personagem. Num FBX de animação isso é
                    // trabalho jogado fora — ele existe pelas curvas, não pelos vértices.
                    importer.weldVertices = true;
                    importer.optimizeMeshPolygons = true;
                    importer.optimizeMeshVertices = true;
                }
            }

            /// <summary>
            /// True quando o arquivo traz ossos. OBJ nunca traz; FBX de personagem riggado sim.
            /// Ler a lista de transforms do importador é a maneira barata de perguntar.
            /// </summary>
            private static bool HasSkeleton(ModelImporter importer)
            {
                // Cenário nunca é riggado, e OBJ não carrega osso em formato nenhum.
                if (importer.assetPath.StartsWith(NatureFolder, System.StringComparison.Ordinal)) return false;
                if (importer.assetPath.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase)) return false;

                var bones = importer.transformPaths;

                // `transformPaths` só existe DEPOIS de um import: na primeira passada por um
                // arquivo novo ele vem VAZIO, e é justamente essa a passada que decide se o
                // personagem ganha rig. Ler vazio como "malha estática" importava o herói sem
                // esqueleto e o clipe sem curva — em silêncio, e de forma pegajosa, porque o
                // .meta gravado passa a dizer "estático" para sempre.
                //
                // Vazio significa "ainda não sei". Para um FBX de personagem a resposta segura é
                // "tem esqueleto": no pior caso sobra um avatar sem uso; no melhor, o rig entra.
                if (bones == null || bones.Length == 0) return true;

                // Já sabemos: transformPaths inclui a raiz (""), então um único item é só a malha.
                return bones.Length > 2;
            }

            /// <summary>
            /// Avatar do personagem que este clipe acompanha: `Pasta/Nome/Nome_Run.fbx` empresta
            /// o esqueleto de `Pasta/Nome/Nome.fbx`. Null na primeira importação, se o clipe
            /// chegar antes da malha — o Apply reimporta depois e resolve.
            /// </summary>
            private static Avatar FindCharacterAvatar(string clipPath)
            {
                string folder = System.IO.Path.GetDirectoryName(clipPath)?.Replace('\\', '/');
                if (string.IsNullOrEmpty(folder)) return null;

                string name = System.IO.Path.GetFileName(folder);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath($"{folder}/{name}.fbx"))
                    if (asset is Avatar avatar) return avatar;

                return null;
            }

            /// <summary>Todo clipe de locomoção é cíclico. Sem loop, o herói corre uma vez e congela.</summary>
            private void OnPreprocessAnimation()
            {
                if (!IsOwnArt(assetPath)) return;
                if (assetImporter is not ModelImporter importer) return;

                var clips = importer.defaultClipAnimations;
                if (clips == null || clips.Length == 0) return;

                for (int i = 0; i < clips.Length; i++) clips[i].loopTime = true;
                importer.clipAnimations = clips;
            }

            /// <summary>
            /// Mapa de normal precisa ser marcado como normal, e os mapas de dados (metallic,
            /// roughness) precisam sair do espaço sRGB. Errar isso não quebra nada — só deixa a
            /// iluminação sutilmente errada de um jeito que ninguém liga à caixinha do inspector.
            /// </summary>
            private void OnPreprocessTexture()
            {
                if (!IsOwnArt(assetPath)) return;
                if (assetImporter is not TextureImporter importer) return;

                string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);

                if (file.EndsWith("_Normal", System.StringComparison.OrdinalIgnoreCase))
                    importer.textureType = TextureImporterType.NormalMap;
                else if (file.EndsWith("_Metallic", System.StringComparison.OrdinalIgnoreCase) ||
                         file.EndsWith("_Roughness", System.StringComparison.OrdinalIgnoreCase))
                    importer.sRGBTexture = false;

                // 26 MB de albedo em 4K num personagem que ocupa ~12% da tela é desperdício puro.
                //
                // Árvore leva metade disso: ela ocupa 2 células numa tela de ~28, e existem
                // MILHARES delas em memória ao mesmo tempo. Num personagem a textura é o rosto do
                // jogo; numa árvore de mata fechada ela é uma mancha de cor a 40 unidades de
                // distância, quase sempre dentro da névoa.
                bool nature = assetPath.StartsWith(NatureFolder, System.StringComparison.Ordinal);
                importer.maxTextureSize = nature ? 1024 : 2048;
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

            // Heróis: prefab montado (malha + material URP + Animator), não o FBX cru.
            //
            // Sem rotação fixa, e isso é uma correção de rumo. A rotação certa não é propriedade
            // do projeto, é propriedade de cada arquivo — e muda até dentro do mesmo personagem:
            // medido, a malha estática do Azure Sentinel veio Z-up (pedia -90 em X) e a versão
            // riggada veio Y-up (em que o mesmo -90 a deitaria). As duas declaram `UpAxis=Y` e as
            // duas trazem `Lcl Rotation (-90,0,0)` no nó. Não dá para saber lendo o cabeçalho,
            // então `AutoUpright` mede e decide em runtime.
            foreach (var (def, name, cells, maxHeight) in Heroes)
            {
                string folder = $"{CharacterFolder}{name}/";
                var prefab = CharacterSetup.Build(folder, name, HeroClips);
                if (prefab == null) { missing.Add($"{folder}{name}.fbx"); continue; }

                var entry = VisualEntry.Create(def, prefab, cells, maxHeight);
                entry.AutoUpright = true;
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

            // As arvores proprias primeiro, as do pack depois. A ordem importa porque a variante
            // sorteada indexa a lista: com as nossas na frente, elas dominam a mata mesmo se um
            // pack for removido — e se as nossas sumirem, o pack cobre o buraco sem deixar o mundo
            // pelado.
            visuals.ScatterProps = BuildTrees(missing, ref mapped)
                .Concat(ScatterPaths
                    .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                    .Where(g => g != null))
                .ToList();
            // 440 arvores divididas por quatro florestas = 110 cada, num circulo de raio 13: uma
            // arvore a cada ~4,8 celulas. E a mesma densidade do nucleo do mundo procedural, para
            // que o bosque da vila e o cinturao que vem logo depois leiam como a MESMA floresta.
            // O miolo do mapa e as Faixas ortogonais continuam limpos.
            visuals.ScatterCount = 440;
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
