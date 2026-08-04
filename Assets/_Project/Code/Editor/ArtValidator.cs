using System.Collections.Generic;
using System.Text;
using DestinyTogether.Data;
using UnityEditor;
using UnityEngine;

namespace DestinyTogether.EditorTools
{
    /// <summary>
    /// Mede cada peça mapeada e diz o tamanho com que ela vai aparecer em jogo.
    ///
    /// Existe porque "está mapeado" e "está do tamanho certo" são coisas diferentes, e a segunda
    /// não dá para verificar lendo código — asset de pack traz a escala que o artista escolheu,
    /// que pode ser 0,3 ou 40. Sem medir, o único jeito de descobrir é dar Play e encontrar um
    /// penhasco cobrindo a tela.
    ///
    /// A medição sai do mesh, sem instanciar nada em cena, e reproduz exatamente a conta do
    /// <see cref="Presentation.VisualFitter"/>.
    /// </summary>
    public static class ArtValidator
    {
        /// <summary>Acima disto uma peça de 1 tile começa a tapar o tabuleiro visto de cima.</summary>
        private const float SuspiciousHeightCells = 5f;

        /// <summary>Largura dividida por altura. Acima disto a peça lê como deitada.</summary>
        private const float FlatRatio = 2.5f;

        [MenuItem("Destiny Together/Validar tamanhos da arte", false, 7)]
        public static void ValidateMenu()
        {
            var profile = FindProfile();
            if (profile == null)
            {
                Debug.LogWarning("[Destiny Together] Nenhum VisualsProfile encontrado.");
                return;
            }
            Debug.Log(Validate(profile));
        }

        public static VisualsProfile FindProfile()
        {
            var guids = AssetDatabase.FindAssets("t:VisualsProfile");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<VisualsProfile>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        public static string Validate(VisualsProfile profile)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Destiny Together] Tamanhos da arte (1 celula = 1 unidade Unity)");
            sb.AppendLine("peca                 nativo (L x A x P)        ->  em jogo (L x A)   escala");
            sb.AppendLine(new string('-', 92));

            var warnings = new List<string>();

            foreach (var entry in profile.Entries)
                Row(sb, warnings, entry.DefName, entry);

            Row(sb, warnings, "Prefeitura", profile.TownHall);
            Row(sb, warnings, "Arvore", profile.Tree);
            Row(sb, warnings, "Rocha", profile.Rock);
            Row(sb, warnings, "Bau", profile.Chest);

            // Cenário: usa os mesmos parâmetros do ViewFactory.ScatterProps.
            sb.AppendLine(new string('-', 92));
            sb.AppendLine($"cenario: {profile.ScatterProps.Count} props x {profile.ScatterCount} instancias, " +
                          $"alvo {profile.ScatterTargetCells:0.#} cel (variacao " +
                          $"{profile.ScatterMinScale:0.#}-{profile.ScatterMaxScale:0.#})");

            foreach (var prop in profile.ScatterProps)
            {
                if (prop == null) continue;
                var scatterEntry = new VisualEntry
                {
                    DefName = prop.name,
                    Prefab = prop,
                    TargetCells = profile.ScatterTargetCells,
                    ScaleMultiplier = profile.ScatterMaxScale
                };
                Row(sb, warnings, "  " + prop.name, scatterEntry);
            }

            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"ATENCAO ({warnings.Count}):");
                foreach (var w in warnings) sb.AppendLine("  - " + w);
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("Nenhuma peca fora de proporcao.");
            }

            return sb.ToString();
        }

        private static void Row(StringBuilder sb, List<string> warnings, string label, VisualEntry entry)
        {
            if (entry.Prefab == null)
            {
                sb.AppendLine($"{label,-20} (sem prefab — continua em primitiva)");
                return;
            }

            if (!TryMeasure(entry.Prefab, out var bounds))
            {
                sb.AppendLine($"{label,-20} SEM MESH — nada sera desenhado");
                warnings.Add($"{label}: prefab '{entry.Prefab.name}' nao tem MeshFilter com mesh. " +
                             "A view fica invisivel; troque a peca.");
                return;
            }

            // A correção de eixo troca altura por profundidade. Sem aplicá-la aqui, o relatório
            // mostraria as dimensões da peça deitada e mentiria sobre o resultado.
            var size = Rotate(bounds, entry.EulerAngles).size;
            float footprint = Mathf.Max(size.x, size.z);
            float target = entry.TargetCells > 0.01f ? entry.TargetCells : 1f;
            float multiplier = entry.ScaleMultiplier > 0.001f ? entry.ScaleMultiplier : 1f;

            float scale = footprint > 0.0001f ? target / footprint : 1f;
            if (entry.MaxHeightCells > 0.01f && size.y > 0.0001f)
            {
                float h = entry.MaxHeightCells / size.y;
                if (h < scale) scale = h;
            }
            scale *= multiplier;

            float finalW = footprint * scale;
            float finalH = size.y * scale;

            sb.AppendLine($"{label,-20} {size.x,6:0.0} x {size.y,6:0.0} x {size.z,6:0.0}  ->  " +
                          $"{finalW,5:0.00} x {finalH,5:0.00}   x{scale:0.0000}");

            if (finalH > SuspiciousHeightCells)
                warnings.Add($"{label}: fica com {finalH:0.0} celulas de altura. " +
                             $"Defina MaxHeightCells (sugerido {SuspiciousHeightCells:0.#}).");

            if (footprint < 0.0001f)
                warnings.Add($"{label}: mesh com largura zero — escala indefinida.");

            // Peça muito mais larga que alta lê como "deitada" vista de cima: um prédio que
            // deveria ser um marco vira uma laje. Foi o caso da citadela no centro da vila.
            if (size.y > 0.0001f && footprint / size.y > FlatRatio)
                warnings.Add($"{label}: proporcao {footprint / size.y:0.0}:1 (larga demais para a altura) — " +
                             "vai parecer deitada vista de cima. Considere outra peca.");
        }

        /// <summary>Aplica uma rotação aos 8 cantos e devolve o AABB resultante.</summary>
        private static Bounds Rotate(Bounds bounds, Vector3 eulerAngles)
        {
            if (eulerAngles == Vector3.zero) return bounds;

            var rotation = Quaternion.Euler(eulerAngles);
            var min = bounds.min;
            var max = bounds.max;
            Bounds result = default;

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                var p = rotation * corner;
                if (i == 0) result = new Bounds(p, Vector3.zero);
                else result.Encapsulate(p);
            }

            return result;
        }

        /// <summary>
        /// Bounds combinados dos meshes, no espaço local da raiz do prefab. Lê o asset direto:
        /// não instancia, não abre cena, não suja nada.
        /// </summary>
        public static bool TryMeasure(GameObject prefab, out Bounds bounds)
        {
            bounds = default;
            bool started = false;

            var toRoot = prefab.transform.worldToLocalMatrix;

            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;

                var matrix = toRoot * filter.transform.localToWorldMatrix;
                var b = mesh.bounds;

                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    var local = matrix.MultiplyPoint3x4(corner);

                    if (!started) { bounds = new Bounds(local, Vector3.zero); started = true; }
                    else bounds.Encapsulate(local);
                }
            }

            return started;
        }
    }
}
