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
        /// <param name="name">Nome base — o FBX tem de se chamar assim.</param>
        /// <param name="clipFbxSuffixes">Sufixos dos FBX de animação, na ordem em que entram.</param>
        /// <returns>O prefab pronto, ou null se o FBX principal não estiver lá.</returns>
        public static GameObject Build(string folder, string name, params string[] clipFbxSuffixes)
        {
            string meshPath = $"{folder}{name}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(meshPath);
            if (model == null) return null;

            var material = BuildMaterial(folder, name);
            var controller = BuildController(folder, name, clipFbxSuffixes);
            return BuildPrefab(folder, name, model, material, controller);
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
            if (material.HasProperty(ShaderIds.BaseColor)) material.SetColor(ShaderIds.BaseColor, Color.white);
            if (material.HasProperty(ShaderIds.Metallic)) material.SetFloat(ShaderIds.Metallic, 0.1f);
            if (material.HasProperty(ShaderIds.Smoothness)) material.SetFloat(ShaderIds.Smoothness, 0.35f);

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture LoadTexture(string folder, string name, string suffix)
            => AssetDatabase.LoadAssetAtPath<Texture>($"{folder}Textures/{name}_{suffix}.png");

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
        private static AnimatorController BuildController(string folder, string name, string[] suffixes)
        {
            string path = $"{folder}{name}.controller";

            // Recria do zero: editar um controlador existente acumula estados órfãos a cada
            // execução, e o asset é derivado de qualquer forma.
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            var layer = controller.layers[0].stateMachine;
            bool first = true;

            foreach (var suffix in suffixes)
            {
                var clip = FindClip($"{folder}{name}_{suffix}.fbx");
                if (clip == null) continue;

                var state = layer.AddState(suffix);
                state.motion = clip;
                if (first)
                {
                    layer.defaultState = state;
                    first = false;
                }
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

        private static GameObject BuildPrefab(string folder, string name, GameObject model,
                                              Material material, AnimatorController controller)
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

                var animator = instance.GetComponentInChildren<Animator>() ?? instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                // A posição vem da simulação, sempre. Root motion faria a animação disputar o
                // controle do corpo com o servidor — e no multiplayer, perder.
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

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
