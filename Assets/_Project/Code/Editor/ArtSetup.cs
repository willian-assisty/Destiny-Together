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

        /// <summary>Pedras pequenas — o que se vê de perto, dentro da pedreira.</summary>
        private static readonly string[] Rocks =
        {
            "PedraGeometrica",
            "PedraCume",
            "PedraMontanha",
        };

        /// <summary>
        /// Rochedos grandes — o que se vê de LONGE.
        ///
        /// Lista à parte porque `WorldPropKind` sempre separou Pedra de Penhasco e a apresentação
        /// ignorava a separação, sorteando as duas da mesma lista com a mesma medida. O resultado
        /// era um "penhasco" do tamanho de um seixo: o tipo existia no código e não existia na
        /// tela. Num mundo sem fim e sem minimapa, marco de terreno é o que permite voltar.
        /// </summary>
        private static readonly string[] Boulders =
        {
            "RochedoMonolitico",
            "RochedoCume",
            "RochedoFacetado",
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
        private static List<GameObject> BuildNature(string[] names, List<string> missing, ref int mapped)
        {
            var built = new List<GameObject>(names.Length);

            foreach (var name in names)
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
        /// 1,8 célula é o token de escala do player, e agora as cápsulas de placeholder valem o
        /// mesmo — os quatro heróis têm a MESMA estatura e se distinguem pela largura. Ocupa ~13%
        /// da altura da tela na câmera atual (FOV 35 a 22 unidades).
        /// </summary>
        private static readonly (string def, string name, float cells, float maxHeight)[] Heroes =
        {
            // "Azure Sentinel" -> Guarda: sentinela é quem segura a Linha, e azul já é a cor do
            // assento 0 na gramática de placeholder. O nome do arquivo casou com o design sozinho.
            (DefaultContent.Guarda, "AzureSentinel", 3.0f, 1.8f),

            // Arqueiro -> Arauto, que é a classe de alcance do jogo: o mais rápido (8,8), o mais
            // frágil (85 HP) e o de MAIOR raio de ataque. Arqueiro é exatamente esse kit.
            //
            // Medido no GLB, ele já vem 1,70 de altura com pivô nos pés — a única peça até agora
            // que chegou na escala e na orientação certas sem precisar de nada.
            (DefaultContent.Arauto, "Arqueiro", 3.0f, 1.8f),
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
            // A ESCADA de altura é a mesma do placeholder (PlaceholderVisuals), em múltiplos de
            // 0,5: 1,0 base · 1,5 utilitário · 2,0 torre · 3,0 vigia · 5,5 Prefeitura. Com arte, o
            // teto é `maxHeight`; a largura (`cells`) fica folgada para a altura ser quem amarra.

            // Torres: três silhuetas diferentes para três funções diferentes, MESMA altura — qual
            // torre é qual já é dito pela forma, e a gramática reserva forma para função.
            (DefaultContent.Balestra,         City + "prefab_unique_buildings/castle_tower_round.prefab",       1.0f, 2.0f),
            (DefaultContent.TorreDeGelo,      City + "prefab_unique_buildings/castle_tower_octagon.prefab",     1.1f, 2.0f),
            (DefaultContent.BalistaDeImpacto, City + "prefab_unique_buildings/castle_tower_rectangular.prefab", 1.0f, 2.0f),
            (DefaultContent.PostoDeVigia,     City + "prefab_unique_buildings/citadel_tower_a.prefab",          0.85f, 3.0f),

            // Braseiro é literalmente uma fogueira presa — e ainda emite luz no escuro.
            (DefaultContent.Braseiro,         City + "prefab_props/fire_cage.prefab",                           1.0f, 1.5f),

            // Muralha: peça de parede de verdade, baixa e larga.
            (DefaultContent.Muralha,          City + "prefab_unique_buildings/castle_wall_5m.prefab",           1.0f, 1.0f),

            // Produção: casas civis. Não atiram, então não podem parecer que atiram.
            (DefaultContent.Serraria,         City + "prefab_civilian_buildings/civilian_house_03.prefab",      1.0f, 1.0f),
            (DefaultContent.Pedreira,         City + "prefab_civilian_buildings/civilian_house_11.prefab",      1.05f, 1.0f),
            (DefaultContent.Oficina,          City + "prefab_civilian_buildings/civilian_house_19.prefab",      1.0f, 1.5f),

            // Depósito: pilha de barris lê como armazenamento à primeira vista. O token de "prop
            // pequeno" (barril, caixa) é 0,8, e a malha chega a 0,70 dentro de 1 célula de largura.
            (DefaultContent.Deposito,         City + "prefab_props/barrel_group.prefab",                        1.0f, 0.8f),
        };

        // Medido no FBX com o eixo correto (Z para cima): 6047 x 3651 de base por 6911 de ALTURA.
        // A citadela sempre foi a peça mais vertical e monumental do pack — ela só entrava
        // tombada. Trocá-la por uma igreja tratava o sintoma; corrigir o eixo resolve a causa.
        private const string TownHallPath = City + "prefab_unique_buildings/citadel_main.prefab";
        private const string ChestPath    = City + "prefab_props/box.prefab";

        /// <summary>
        /// Verde dessaturado #97B48C. Cor CHAPADA, sem textura.
        ///
        /// O chão passou a ser uma malha facetada, e ali o volume vem das NORMAIS: cada triângulo
        /// pega a luz de um jeito. Textura por cima disso brigaria com a faceta em vez de somar —
        /// é o mesmo motivo pelo qual a referência low-poly não tem textura nenhuma.
        ///
        /// Tem de ser IGUAL a <c>BoardRenderer.GrassColor</c> e a <c>RegionGroundTints[0]</c>: as
        /// três são a mesma superfície por caminhos diferentes, e divergir faz o chão mudar de cor
        /// conforme o perfil visual esteja ou não carregado.
        /// </summary>
        private static readonly Color GrassTint = new Color(0.592f, 0.706f, 0.549f, 1f);

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
        /// v12: mata e pedreira 100% proprias — o pack sai das duas; rochedo ganha lista e medida
        ///      propria, e Penhasco volta a significar alguma coisa na tela.
        /// v13: chao facetado low-poly em verde chapado (#4F6B45); sem textura de chao.
        /// v14: tokens de escala e de luminancia — heroi 1,8; horda 1,0/1,6/2,4; Kaiju 4,0; escada
        ///      de predios em multiplos de 0,5; mata 3,0; pedra 0,8; Prefeitura 5,5. Chao e tints
        ///      de regiao sobem para a faixa de ambiente 35-55%.
        /// </summary>
        private const int CurrentSetupVersion = 14;

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

            // 5,0 células de largura dentro dos 5x5 da Prefeitura, altura 5,5: o centro da vila
            // continua a coisa mais alta do TABULEIRO, agora por margem verificável — acima do
            // Kaiju (4,0) e do teto de torre fundida (4,0, ver PresentationDirector).
            visuals.TownHall = MakeEntry("Prefeitura", TownHallPath, 5.0f, 5.5f, missing, ref mapped);
            // Baú é "prop pequeno" no token de escala: 0,8.
            visuals.Chest    = MakeEntry("Bau",        ChestPath,    1.2f, 0.8f, missing, ref mapped);

            // Sem material de textura: o chão é malha facetada de cor chapada. O campo continua no
            // perfil para quem quiser voltar atrás, mas o setup não o preenche mais.
            visuals.GroundMaterial = null;
            visuals.GroundTint = GrassTint;

            // As quatro cores de chao NUNCA eram escritas aqui: o asset ficava com o que quer que
            // tivesse sido serializado, e como e ele que o BoardRenderer le, mudar a cor em codigo
            // nao mudava nada na tela. Agora o setup e a fonte, e as quatro vivem na faixa de
            // ambiente do token (35-55% de luminancia linear, dessaturadas).
            visuals.RegionGroundTints = new[]
            {
                GrassTint,                                  // Mata — #97B48C · L 41,1%
                new Color(0.690f, 0.675f, 0.639f, 1f),      // Pedreira — #B0ACA3 · L 41,4%
                new Color(0.702f, 0.682f, 0.533f, 1f),      // Pasto — #B3AE88 · L 41,6%
                new Color(0.561f, 0.686f, 0.584f, 1f),      // Pantano — #8FAF95 · L 38,7%
            };

            // SÓ a mata própria. As árvores mortas e os pinheiros do pack saíram da lista: eram do
            // deserto, e conviviam mal com uma floresta viva — misturados, a mata lia como duas
            // florestas sobrepostas em vez de uma. Um bioma tem de parecer um bioma.
            var trees = BuildNature(Trees, missing, ref mapped);
            var rocks = BuildNature(Rocks, missing, ref mapped);
            var boulders = BuildNature(Boulders, missing, ref mapped);

            visuals.ScatterProps = trees;

            // O nó COLHÍVEL usa a mesma arte do cenário, e por isso entra MENOR: 1,6 célula contra
            // 2,2 da mata, 1,3 contra 2,6 da pedreira. É o que sobrou para dizer "esta dá para
            // colher" agora que a arte do pack saiu — a diferença de tamanho é real, mas é fraca, e
            // um anel no chão sob o nó resolveria isso muito melhor.
            visuals.Tree = NatureEntry("Arvore", trees.FirstOrDefault(), 1.8f, 3.0f, missing, ref mapped);
            visuals.Rock = NatureEntry("Rocha", rocks.FirstOrDefault(), 1.6f, 0.8f, missing, ref mapped);
            // 440 arvores divididas por quatro florestas = 110 cada, num circulo de raio 13: uma
            // arvore a cada ~4,8 celulas. E a mesma densidade do nucleo do mundo procedural, para
            // que o bosque da vila e o cinturao que vem logo depois leiam como a MESMA floresta.
            // O miolo do mapa e as Faixas ortogonais continuam limpos.
            visuals.ScatterCount = 440;
            visuals.ForestRadius = 13f;
            visuals.ForestDensityBias = 0.72f;
            visuals.ForestClearing = 4f;
            visuals.ScatterTargetCells = 2.4f;
            // 3,0 é o "prop médio" (árvore, poste) do token de escala. Era 5,0, e a diferença
            // aparece: uma árvore de 5 metros ao lado de um herói de 1,8 lê como floresta de
            // gigantes, não como mata que se atravessa.
            visuals.ScatterMaxHeightCells = 3f;
            // Pedreiras do mundo procedural, agora em DUAS listas. Ausente na pasta = cai para
            // primitiva sozinho, e o mundo continua existindo — nunca ha um estado "meio migrado"
            // em que a pedreira some.
            visuals.QuarryProps = rocks;
            visuals.CliffProps = boulders;

            visuals.QuarryTargetCells = 3.0f;
            visuals.QuarryMaxHeightCells = 2f;

            // 6,5 celulas: o rochedo tem de ler como MARCO a meia tela de distancia, senao ele e
            // so uma pedra grande. E o unico prop do mundo maior que a Prefeitura (5,5 de altura),
            // o que e proposital — a vila e a coisa mais alta do TABULEIRO, nao do mundo. E a
            // unica peca do jogo deliberadamente FORA da tabela de tokens de escala.
            visuals.CliffTargetCells = 6.5f;
            visuals.CliffMaxHeightCells = 6.5f;
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
                      $"{visuals.ScatterProps.Count} arvores + {visuals.QuarryProps.Count} pedras + " +
                      $"{visuals.CliffProps.Count} rochedos " +
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

        /// <summary>
        /// Entrada de um nó colhível a partir de um prefab já montado.
        ///
        /// Existe separada de <see cref="MakeEntry"/> porque aquela carrega por CAMINHO, e a arte
        /// própria é construída em tempo de setup — pedir o prefab pelo caminho funcionaria só a
        /// partir da segunda execução, e falharia em silêncio na primeira.
        ///
        /// Os FBX do Meshy vêm Z-up, medido, exatamente como o pack: mesma correção de eixo.
        /// </summary>
        private static VisualEntry NatureEntry(string defName, GameObject prefab, float cells,
                                               float maxHeight, List<string> missing, ref int mapped)
        {
            if (prefab == null) { missing.Add($"{defName}: nenhum prefab de natureza disponivel"); return default; }

            mapped++;
            var entry = VisualEntry.Create(defName, prefab, cells, maxHeight);
            entry.EulerAngles = PolylisedUpright;
            return entry;
        }

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
