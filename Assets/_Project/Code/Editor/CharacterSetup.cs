using System.Collections.Generic;
using System.IO;
using DestinyTogether.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DestinyTogether.EditorTools
{
    /// <summary>
    /// Monta o prefab jogável de um personagem a partir do que o Meshy (ou qualquer pipeline de
    /// rig) entrega: uma malha riggada, um FBX de animação por clipe e um punhado de PNGs.
    ///
    /// Existe porque o `VisualsProfile` aponta para UM prefab por definição, e nada em
    /// "FBX + texturas soltas" é jogável sozinho — falta material URP, falta AnimatorController,
    /// falta o Animator ligado. Fazer isso à mão no inspector funciona uma vez e apodrece na
    /// segunda: reimportar o FBX, trocar de personagem ou clonar para outra classe recomeça tudo.
    /// Em código, é um clique.
    ///
    /// O prefab gerado é DERIVADO — pode ser apagado a qualquer momento e volta igual.
    /// </summary>
    public static class CharacterSetup
    {
        /// <summary>
        /// Recria o material, o controlador e o prefab de um personagem.
        /// </summary>
        /// <param name="folder">Pasta do personagem, com "/" no fim.</param>
        /// <param name="name">Nome base — a malha tem de se chamar assim.</param>
        /// <param name="clipFbxSuffixes">Sufixos dos FBX de animação, na ordem em que entram.</param>
        /// <returns>O prefab pronto, ou null se não houver malha na pasta.</returns>
        public static GameObject Build(string folder, string name, params string[] clipFbxSuffixes)
        {
            if (LoadMesh(folder, name) == null) return null;

            EnsureRig(folder, name, clipFbxSuffixes);
            EnsureMeshBudget(folder, name);

            var model = LoadMesh(folder, name);
            var material = BuildMaterial(folder, name);
            var controller = BuildController(folder, name, clipFbxSuffixes, out bool clipsExpected);
            return BuildPrefab(folder, name, model, material, controller, clipsExpected);
        }

        /// <summary>
        /// A malha do personagem: FBX se houver, OBJ como alternativa.
        ///
        /// O OBJ existe porque o Unity **não importa .glb** — não nativamente e não sem pacote
        /// extra. Quando um personagem chega em glTF, a malha é convertida para OBJ e entra por
        /// aqui: dá para ver silhueta, proporção e textura na hora, o que é o que se quer ao
        /// avaliar um personagem novo.
        ///
        /// O que o OBJ NÃO carrega é esqueleto. Ele é prévia, não destino — some no dia em que a
        /// versão riggada em FBX chegar, e a preferência pelo FBX nesta busca faz essa troca
        /// acontecer sozinha, sem ninguém precisar apagar nada.
        /// </summary>
        private static GameObject LoadMesh(string folder, string name)
        {
            foreach (var ext in MeshExtensions)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}{name}{ext}");
                if (model != null) return model;
            }
            return null;
        }

        private static readonly string[] MeshExtensions = { ".fbx", ".obj" };

        // ------------------------------------------------------------------------------
        // Rig
        // ------------------------------------------------------------------------------

        /// <summary>
        /// Garante que malha e clipes estejam importados como rig, reimportando o que estiver
        /// errado.
        ///
        /// **Postprocessador é palpite; setup é garantia.** `OnPreprocessModel` roda ANTES de o
        /// arquivo ser lido, então na primeira passada ele decide sem saber: `transformPaths` vem
        /// vazio e o avatar do personagem ainda não existe. Um palpite errado ali grava um .meta
        /// dizendo "malha estática" que nunca mais é revisto — o personagem fica sem esqueleto e o
        /// clipe sem curva, sem um único erro no console.
        ///
        /// Aqui o arquivo JÁ foi lido, então a pergunta tem resposta. Este é o lugar que corrige.
        /// </summary>
        private static void EnsureRig(string folder, string name, string[] clipSuffixes)
        {
            // Quem CHAMA diz se espera animação — uma árvore passa a lista de clipes vazia, um
            // herói passa {"Run"}. É o discriminador certo: sem ele, o mesmo montador que serve
            // aos dois forçaria rig Generic e um Avatar em cada tronco da floresta.
            if (clipSuffixes == null || clipSuffixes.Length == 0) return;

            string meshPath = $"{folder}{name}.fbx";
            if (!File.Exists(meshPath)) return;   // prévia em OBJ não tem rig para garantir

            bool meshChanged = Configure(meshPath, importer =>
            {
                if (importer.animationType == ModelImporterAnimationType.Generic &&
                    importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel) return false;

                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                return true;
            });

            if (meshChanged)
                Debug.Log($"[CharacterSetup] '{name}' foi reimportado como rig — o import anterior " +
                          $"o tinha marcado como malha estática.");

            var avatar = FindAvatar(meshPath);

            foreach (var suffix in clipSuffixes)
            {
                string clipPath = $"{folder}{name}_{suffix}.fbx";
                if (!File.Exists(clipPath)) continue;

                Configure(clipPath, importer =>
                {
                    bool ok = importer.animationType == ModelImporterAnimationType.Generic &&
                              importer.importAnimation &&
                              (avatar == null || importer.sourceAvatar == avatar);
                    if (ok) return false;

                    importer.animationType = ModelImporterAnimationType.Generic;
                    importer.importAnimation = true;
                    importer.avatarSetup = avatar != null
                        ? ModelImporterAvatarSetup.CopyFromOther
                        : ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.sourceAvatar = avatar;
                    return true;
                });
            }
        }

        /// <summary>
        /// Teto de triangulos de uma peca de personagem.
        ///
        /// 60 mil e calibrado pelo que ja esta em campo: o Arqueiro tem 45 mil e desenha bem numa
        /// camera em que um heroi ocupa ~12% da altura da tela. E o Mago chegou com **412.814** —
        /// nove vezes isso, para a mesma area de tela.
        /// </summary>
        private const int TriangleBudget = 60000;

        /// <summary>
        /// Corta triangulos de uma malha gorda usando o **Mesh LOD** do proprio Unity.
        ///
        /// Mesh LOD gera niveis simplificados DENTRO da mesma malha — sem LODGroup, sem prefabs
        /// extras, sem objeto por nivel — e o renderizador escolhe o nivel pelo tamanho em tela.
        /// `maximumMeshLod` e o que resolve o problema aqui: ele DESCARTA os niveis mais
        /// detalhados no import, entao a peca passa a existir ja simplificada em vez de so
        /// simplificar de longe.
        ///
        /// Vale distinguir do vizinho de nome parecido: `meshCompression` comprime o
        /// ARMAZENAMENTO e nao remove um triangulo sequer, e `optimizeMesh*` so reordena para
        /// coerencia de cache. Nenhum dos dois responde "a malha e pesada demais".
        ///
        /// Cada nivel tem cerca de metade dos triangulos do anterior, entao o nivel necessario e
        /// o log2 do excesso. O numero real e MEDIDO depois do reimport e vai para o console — a
        /// razao de 2 e uma aproximacao do gerador, nao um contrato.
        /// </summary>
        private static void EnsureMeshBudget(string folder, string name)
        {
            var model = LoadMesh(folder, name);
            if (model == null) return;

            int before = CountTriangles(model);
            if (before <= TriangleBudget) return;

            string path = AssetDatabase.GetAssetPath(model);
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer) return;

            int level = 0;
            for (int t = before; t > TriangleBudget && level < 4; level++) t /= 2;

            if (importer.generateMeshLods && importer.maximumMeshLod >= level) return;

            importer.generateMeshLods = true;
            importer.maximumMeshLod = level;
            importer.SaveAndReimport();

            int after = CountTriangles(LoadMesh(folder, name));

            if (after < before)
            {
                Debug.Log($"[CharacterSetup] '{name}': {before:N0} -> {after:N0} triangulos " +
                          $"(Mesh LOD nivel {level}, teto {TriangleBudget:N0})." +
                          (after > TriangleBudget
                              ? "  Ainda acima do teto: a malha precisa vir mais leve da origem."
                              : ""));
                return;
            }

            // Nao caiu. `maximumMeshLod` significa o contrario do que este codigo assume, ou a
            // malha nao gerou niveis. Avisar em vez de deixar quieto: o modo de falha silencioso
            // seria acreditar que a peca emagreceu quando ela nao emagreceu.
            Debug.LogWarning($"[CharacterSetup] '{name}' continua com {after:N0} triangulos depois de " +
                             $"maximumMeshLod={level}. O corte nao aconteceu — ou o gerador de Mesh LOD " +
                             $"nao produziu niveis para esta malha, ou o campo conta na direcao oposta " +
                             $"(nesse caso o conserto e uma linha em EnsureMeshBudget).");
        }

        /// <summary>
        /// Triangulos de um modelo importado.
        ///
        /// Le <c>GetIndexCount</c> em vez de <c>mesh.triangles</c>: a propriedade aloca um vetor
        /// com um int por indice, e numa malha de 412 mil triangulos isso e um vetor de 1,2 milhao
        /// de posicoes criado so para ser contado e descartado.
        /// </summary>
        private static int CountTriangles(GameObject model)
        {
            if (model == null) return 0;

            long indices = 0;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                indices += IndexCount(filter.sharedMesh);
            foreach (var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                indices += IndexCount(skinned.sharedMesh);

            return (int)(indices / 3);
        }

        private static long IndexCount(Mesh mesh)
        {
            if (mesh == null) return 0;
            long total = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) total += (long)mesh.GetIndexCount(i);
            return total;
        }

        /// <summary>Aplica um ajuste no importador e reimporta se algo mudou.</summary>
        private static bool Configure(string path, System.Func<ModelImporter, bool> change)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer) return false;
            if (!change(importer)) return false;

            importer.SaveAndReimport();
            return true;
        }

        private static Avatar FindAvatar(string path)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Avatar avatar) return avatar;
            return null;
        }

        // ------------------------------------------------------------------------------
        // Material
        // ------------------------------------------------------------------------------

        /// <summary>
        /// Material URP a partir das texturas soltas.
        ///
        /// O FBX do Meshy referencia a textura por um caminho de máquina dele
        /// (`/tmp/.../Character_output.fbm/texture_0.png`), que obviamente não existe aqui — por
        /// isso a importação de materiais do FBX fica desligada e o material nasce daqui.
        ///
        /// Só albedo e normal entram. Metallic e roughness vêm em PNGs SEPARADOS, e o URP Lit
        /// espera os dois num mapa só (metallic em RGB, smoothness no ALPHA). Empacotar exigiria
        /// ler e recombinar dois PNGs de 16 MB; enquanto isso não acontece, escalares conservadores
        /// dão um resultado honesto — e um personagem fosco é muito melhor que um espelhado.
        /// </summary>
        private static Material BuildMaterial(string folder, string name)
        {
            string path = $"{folder}{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;

            var albedo = LoadTexture(folder, name, "BaseColor");
            var normal = LoadTexture(folder, name, "Normal");

            if (albedo != null)
            {
                if (material.HasProperty(ShaderIds.BaseMap)) material.SetTexture(ShaderIds.BaseMap, albedo);
                if (material.HasProperty(ShaderIds.MainTex)) material.SetTexture(ShaderIds.MainTex, albedo);
            }

            if (normal != null && material.HasProperty(ShaderIds.BumpMap))
            {
                material.SetTexture(ShaderIds.BumpMap, normal);
                material.EnableKeyword("_NORMALMAP");
            }

            // Cor base BRANCA: ela multiplica o albedo, e qualquer outra coisa tingiria a arte.
            //
            // SEM albedo, porem, branco não é neutro — é branco. Em URP um material sem _BaseMap
            // e com _BaseColor branco renderiza a peça em 100% de luminância, que é o valor mais
            // alto possível e o dobro do teto da faixa de ambiente. Hoje isso atinge três peças
            // reais (PedraMontanha, RochedoCume, RochedoFacetado): o `_BaseColor.png` delas é um
            // WebP renomeado, que o Unity não decodifica, então elas entram no mundo como blocos
            // brancos — e um rochedo é justamente a maior peça da tela.
            //
            // O cinza abaixo está na faixa de ambiente e some sozinho no dia em que as texturas
            // virarem PNG de verdade, porque aí `albedo` deixa de ser nulo.
            if (material.HasProperty(ShaderIds.BaseColor))
                material.SetColor(ShaderIds.BaseColor,
                                  albedo != null ? Color.white : new Color(0.672f, 0.669f, 0.742f));
            if (material.HasProperty(ShaderIds.Metallic)) material.SetFloat(ShaderIds.Metallic, 0.1f);
            if (material.HasProperty(ShaderIds.Smoothness)) material.SetFloat(ShaderIds.Smoothness, 0.35f);

            // Instancing de GPU. Num personagem nao muda nada — existem quatro. Numa arvore muda
            // tudo: mata fechada poe milhares da MESMA malha com o MESMO material em tela, que e
            // exatamente o caso que o instancing resolve. Sem isso cada arvore vira uma chamada de
            // desenho e a floresta densa fica cara pelo motivo errado (CPU, nao pixels).
            material.enableInstancing = true;

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Textura por papel, aceitando PNG ou JPG.
        ///
        /// A extensão não é escolha nossa: o Meshy grava PNG ao lado do FBX e JPEG embutido no
        /// GLB. Aceitar as duas evita que o material saia sem albedo por causa de três letras.
        /// </summary>
        private static Texture LoadTexture(string folder, string name, string suffix)
        {
            foreach (var ext in TextureExtensions)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture>($"{folder}Textures/{name}_{suffix}{ext}");
                if (tex != null) return tex;
            }
            return null;
        }

        private static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg" };

        // ------------------------------------------------------------------------------
        // Animator
        // ------------------------------------------------------------------------------

        /// <summary>
        /// Controlador com um estado por clipe encontrado.
        ///
        /// Deliberadamente sem máquina de transições: a taxa de reprodução é dirigida por código
        /// a partir da velocidade real do corpo (ver <c>EntityView.TickAnimator</c>). Com um clipe
        /// só, uma máquina de estados seria cerimônia em volta de nada — e quando o Idle chegar,
        /// acrescentar o segundo estado e um `Speed` de transição é uma edição pequena aqui.
        /// </summary>
        private static AnimatorController BuildController(string folder, string name, string[] suffixes,
                                                          out bool clipsExpected)
        {
            string path = $"{folder}{name}.controller";
            AssetDatabase.DeleteAsset(path);

            // Personagem sem clipe nenhum não ganha controlador. Um Animator com controlador
            // vazio não é neutro: ele ASSUME o comando das transformações e congela o corpo na
            // pose de bind, o que apaga até a locomoção procedural que serviria de plano B.
            //
            // Mas "sem clipe" e "clipe ainda não importado" são estados DIFERENTES que se parecem
            // daqui, e confundi-los custa caro: este setup roda de dentro de um postprocessador de
            // import, então na primeira compilação depois de largar os arquivos na pasta o FBX de
            // animação pode não ter sido processado ainda. Tratar isso como "não tem animação"
            // grava um prefab sem Animator — e como o gate de versão já foi satisfeito, ele nunca
            // mais é reconstruído. O personagem perde a corrida em definitivo, sem um único erro
            // no console. Por isso a pergunta é feita ao DISCO, não ao AssetDatabase.
            var clips = new List<(string suffix, AnimationClip clip)>();
            clipsExpected = false;

            foreach (var suffix in suffixes)
            {
                string fbx = $"{folder}{name}_{suffix}.fbx";
                if (!File.Exists(fbx)) continue;
                clipsExpected = true;

                var clip = FindClip(fbx);
                if (clip == null)
                {
                    // O arquivo está lá e não veio clipe: import atrasado. Forçar e reler.
                    AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceSynchronousImport);
                    clip = FindClip(fbx);
                }

                if (clip != null) clips.Add((suffix, clip));
                else Debug.LogError($"[CharacterSetup] '{fbx}' existe mas não produziu AnimationClip. " +
                                    $"O Animator foi PRESERVADO para não apagar a animação em silêncio.");
            }

            if (clips.Count == 0) return null;

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            var layer = controller.layers[0].stateMachine;
            for (int i = 0; i < clips.Count; i++)
            {
                var state = layer.AddState(clips[i].suffix);
                state.motion = clips[i].clip;
                if (i == 0) layer.defaultState = state;
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>
        /// Primeiro AnimationClip do FBX, ignorando o clipe de pré-visualização que o Unity
        /// esconde nos assets. Pegar pelo tipo em vez de pelo nome importa: o Meshy nomeia a take
        /// como "Armature|Armature|...|running|baselayer", que ninguém quer digitar nem depender.
        /// </summary>
        private static AnimationClip FindClip(string fbxPath)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            return null;
        }

        // ------------------------------------------------------------------------------
        // Prefab
        // ------------------------------------------------------------------------------

        /// <summary>
        /// Subpasta opcional com pecas que ORBITAM o personagem em vez de fazer parte do corpo.
        ///
        /// Subpasta e nao sufixo de arquivo porque a raiz da pasta ja tem dois significados
        /// ocupados — o arquivo com o nome da pasta e a malha, qualquer outro FBX e clipe — e um
        /// terceiro significado ali dentro seria mais uma regra para lembrar.
        /// </summary>
        private const string CrystalsFolder = "Cristais";

        /// <summary>
        /// Pendura os cristais no ROOT do personagem e liga o <see cref="CrystalOrbit"/>.
        ///
        /// No root, e nao num osso: o componente escreve a posicao deles em espaco de MUNDO todo
        /// frame, entao ser filho de um osso somaria a transformacao do osso por cima e o cristal
        /// dispararia. E o mesmo motivo pelo qual a orbita continua girando com o corpo parado —
        /// ela nunca dependeu do esqueleto.
        /// </summary>
        private static void AttachOrbitingCrystals(string folder, GameObject instance, Material material)
        {
            string dir = $"{folder}{CrystalsFolder}";
            if (!Directory.Exists(dir)) return;

            var meshes = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
            {
                var mesh = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (mesh != null) meshes.Add(mesh);
            }

            if (meshes.Count == 0) return;

            // Ordem estavel: o componente distribui os cristais igualmente pela orbita a partir do
            // indice, e uma ordem que muda a cada import trocaria quem fica onde sem motivo.
            meshes.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            var transforms = new Transform[meshes.Count];

            for (int i = 0; i < meshes.Count; i++)
            {
                var crystal = Object.Instantiate(meshes[i], instance.transform);
                crystal.name = $"Cristal{i + 1}";
                crystal.transform.localPosition = Vector3.zero;
                crystal.transform.localRotation = Quaternion.identity;

                if (material != null)
                {
                    foreach (var r in crystal.GetComponentsInChildren<Renderer>(true))
                    {
                        var slots = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                        for (int s = 0; s < slots.Length; s++) slots[s] = material;
                        r.sharedMaterials = slots;
                    }
                }

                transforms[i] = crystal.transform;
            }

            var orbit = instance.GetComponent<CrystalOrbit>() ?? instance.AddComponent<CrystalOrbit>();
            orbit.Crystals = transforms;
            orbit.Animator = instance.GetComponentInChildren<Animator>();

            Debug.Log($"[CharacterSetup] {meshes.Count} cristais em orbita ligados a " +
                      $"'{instance.name}'.");
        }

        private static GameObject BuildPrefab(string folder, string name, GameObject model,
                                              Material material, AnimatorController controller,
                                              bool clipsExpected)
        {
            string path = $"{folder}{name}.prefab";
            var instance = Object.Instantiate(model);
            instance.name = name;

            try
            {
                foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (material == null) break;
                    var slots = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                    for (int i = 0; i < slots.Length; i++) slots[i] = material;
                    r.sharedMaterials = slots;
                }

                var animator = instance.GetComponentInChildren<Animator>();

                if (controller != null)
                {
                    animator ??= instance.AddComponent<Animator>();
                    animator.runtimeAnimatorController = controller;
                    // A posição vem da simulação, sempre. Root motion faria a animação disputar
                    // o controle do corpo com o servidor — e no multiplayer, perder.
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                }
                else if (animator != null && !clipsExpected)
                {
                    // Malha sem clipe NENHUM no disco: o Animator que veio no import é REMOVIDO,
                    // não deixado vazio. Animator sem controlador ainda assume as transformações e
                    // trava o corpo na pose de bind — e sem ele a locomoção procedural volta a
                    // valer, que é exatamente o que se quer numa prévia sem rig.
                    //
                    // Quando há FBX de animação no disco mas ele falhou em importar, o Animator
                    // FICA: perder a animação é pior que um corpo parado, e o erro já foi logado.
                    Object.DestroyImmediate(animator);
                }

                AttachOrbitingCrystals(folder, instance, material);

                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                return PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
