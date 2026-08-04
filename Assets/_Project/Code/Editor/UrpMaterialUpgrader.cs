using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DestinyTogether.EditorTools
{
    /// <summary>
    /// Converte materiais de pipeline built-in para URP.
    ///
    /// Packs da Asset Store publicados antes do URP (ou que suportam os dois) vêm com o shader
    /// Standard. Num projeto URP isso renderiza <b>magenta sólido</b> — o "material rosa" clássico.
    /// O Unity tem o Render Pipeline Converter para isso, mas ele processa o projeto inteiro e
    /// pede vários cliques; aqui a conversão é restrita às pastas dos packs e roda junto com o
    /// resto do setup de arte.
    ///
    /// A conversão ALTERA os arquivos .mat dos packs. É reversível: reimportar o .unitypackage
    /// restaura os originais.
    /// </summary>
    public static class UrpMaterialUpgrader
    {
        private static readonly string[] TargetFolders =
        {
            "Assets/Polylised - Medieval Desert City",
            "Assets/Fantasy Forest Environment Free Sample"
        };

        // Standard → URP/Lit. Só o que importa para arte estilizada: cor, albedo, normal, emissão.
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");
        private static readonly int LegacyMainTex = Shader.PropertyToID("_MainTex");
        private static readonly int LegacyGlossiness = Shader.PropertyToID("_Glossiness");
        private static readonly int LegacyMetallic = Shader.PropertyToID("_Metallic");
        private static readonly int LegacyBumpMap = Shader.PropertyToID("_BumpMap");
        private static readonly int LegacyEmission = Shader.PropertyToID("_EmissionColor");

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        [MenuItem("Destiny Together/Converter materiais importados para URP", false, 6)]
        public static void ConvertMenu()
        {
            int n = Convert(out int total);
            EditorUtility.DisplayDialog("Destiny Together",
                $"{n} de {total} materiais convertidos para URP.\n\n" +
                "Reimportar o .unitypackage desfaz a conversão.", "OK");
        }

        /// <returns>Quantos materiais foram efetivamente convertidos.</returns>
        public static int Convert(out int total)
        {
            total = 0;

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[Destiny Together] Shader URP/Lit nao encontrado. O projeto esta em URP?");
                return 0;
            }

            var folders = new List<string>();
            foreach (var f in TargetFolders)
                if (AssetDatabase.IsValidFolder(f)) folders.Add(f);

            if (folders.Count == 0) return 0;

            var guids = AssetDatabase.FindAssets("t:Material", folders.ToArray());
            total = guids.Length;
            int converted = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null || mat.shader == null) continue;
                    if (mat.shader.name.StartsWith("Universal Render Pipeline")) continue;

                    EditorUtility.DisplayProgressBar("Convertendo materiais para URP",
                                                     path, (float)i / guids.Length);

                    // Lê ANTES de trocar: a troca de shader descarta propriedades sem correspondente.
                    Color color = mat.HasProperty(LegacyColor) ? mat.GetColor(LegacyColor) : Color.white;
                    Texture albedo = mat.HasProperty(LegacyMainTex) ? mat.GetTexture(LegacyMainTex) : null;
                    Vector2 scale = albedo != null ? mat.GetTextureScale(LegacyMainTex) : Vector2.one;
                    Vector2 offset = albedo != null ? mat.GetTextureOffset(LegacyMainTex) : Vector2.zero;
                    float gloss = mat.HasProperty(LegacyGlossiness) ? mat.GetFloat(LegacyGlossiness) : 0.15f;
                    float metal = mat.HasProperty(LegacyMetallic) ? mat.GetFloat(LegacyMetallic) : 0f;
                    Texture normal = mat.HasProperty(LegacyBumpMap) ? mat.GetTexture(LegacyBumpMap) : null;
                    Color emission = mat.HasProperty(LegacyEmission) ? mat.GetColor(LegacyEmission) : Color.black;
                    bool hadEmission = mat.IsKeywordEnabled("_EMISSION");

                    mat.shader = lit;

                    if (mat.HasProperty(BaseColor)) mat.SetColor(BaseColor, color);
                    if (albedo != null && mat.HasProperty(BaseMap))
                    {
                        mat.SetTexture(BaseMap, albedo);
                        mat.SetTextureScale(BaseMap, scale);
                        mat.SetTextureOffset(BaseMap, offset);
                    }
                    if (mat.HasProperty(Smoothness)) mat.SetFloat(Smoothness, gloss);
                    if (mat.HasProperty(Metallic)) mat.SetFloat(Metallic, metal);
                    if (normal != null && mat.HasProperty(BumpMap))
                    {
                        mat.SetTexture(BumpMap, normal);
                        mat.EnableKeyword("_NORMALMAP");
                    }
                    if (hadEmission && mat.HasProperty(EmissionColor))
                    {
                        mat.SetColor(EmissionColor, emission);
                        mat.EnableKeyword("_EMISSION");
                        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }

                    EditorUtility.SetDirty(mat);
                    converted++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            if (converted > 0)
                Debug.Log($"[Destiny Together] {converted} de {total} materiais convertidos de built-in para URP/Lit.");

            return converted;
        }
    }
}
