using DestinyTogether.Data;
using UnityEngine;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// Encaixa uma peça de arte comprada nas convenções do jogo.
    ///
    /// O contrato que a simulação assume é simples e não negociável: <b>1 célula = 1 unidade</b>,
    /// <b>pivot nos pés</b>, <b>+Z para a frente</b>. Nenhum pack da Asset Store respeita isso por
    /// acidente — árvores costumam vir com 8-15 unidades de altura e casas com pivot no centro do
    /// mesh. Em vez de exigir que alguém conserte cada FBX no import settings (trabalhoso, fácil de
    /// esquecer, invisível no diff), a normalização acontece aqui, uma vez, em runtime.
    ///
    /// Consequência prática: dá para trocar o pack de árvores por outro sem tocar em mais nada.
    /// </summary>
    public static class VisualFitter
    {
        /// <summary>
        /// Ajusta a instância já parentada para caber em <paramref name="entry"/>.TargetCells,
        /// com a base no chão e centrada no eixo XZ.
        /// </summary>
        public static void Fit(GameObject instance, in VisualEntry entry)
        {
            if (instance == null) return;

            var t = instance.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.Euler(entry.EulerAngles);
            t.localScale = Vector3.one;

            DisablePhysics(instance);

            if (entry.KeepOriginalScale)
            {
                t.localPosition = entry.Offset;
                return;
            }

            // Medir no espaço do PAI, não no do próprio objeto: medindo no espaço local dele a
            // rotação que acabamos de aplicar se cancelaria, e o encaixe usaria as dimensões
            // erradas — largura de peça deitada tratada como largura de peça em pé.
            if (!TryGetBoundsInParent(instance, out var bounds))
            {
                t.localPosition = entry.Offset;
                return;
            }

            if (entry.AutoUpright) StandUpright(t, ref bounds);

            float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
            float target = entry.TargetCells > 0.01f ? entry.TargetCells : 1f;
            float multiplier = entry.ScaleMultiplier > 0.001f ? entry.ScaleMultiplier : 1f;

            float scale = footprint > 0.0001f ? target / footprint : 1f;

            // Teto de altura: normalizar só pela largura faz uma peça alta e estreita virar um
            // arranha-céu. Uma árvore com base de 1 unidade e 14 de altura, normalizada para 1,8
            // célula de largura, sairia com 25 células de altura — mais alta que a cidade inteira.
            if (entry.MaxHeightCells > 0.01f && bounds.size.y > 0.0001f)
            {
                float heightScale = entry.MaxHeightCells / bounds.size.y;
                if (heightScale < scale) scale = heightScale;
            }

            scale *= multiplier;
            t.localScale = Vector3.one * scale;

            // Base no chão e centro em XZ: sem isso a peça flutua ou afunda conforme o pivot
            // que o artista escolheu, e cada pack escolhe um diferente.
            var offset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
            t.localPosition = offset + entry.Offset;
        }

        /// <summary>
        /// Endireita um humanoide MEDINDO, em vez de confiar numa rotação fixa.
        ///
        /// Existe porque a rotação certa não é uma propriedade do projeto, é uma propriedade de
        /// cada arquivo — e muda até dentro do mesmo personagem. Medido: a malha estática do Azure
        /// Sentinel veio Z-up (precisava de −90 em X); a versão riggada do MESMO personagem veio
        /// Y-up (em que o mesmo −90 a deitaria). Os dois declaram `UpAxis=Y` e os dois trazem
        /// `Lcl Rotation (−90,0,0)` no nó. Não há como saber lendo o cabeçalho.
        ///
        /// O invariante que resolve: **gente é sempre mais alta do que funda.** Se a profundidade
        /// medida passar a altura, a peça está deitada — não importa por quê. Uma correção de −90
        /// em X e mede de novo.
        ///
        /// Note que a LARGURA fica fora da conta de propósito: um personagem de braços abertos é
        /// mais largo que alto, e usar largura como referência daria falso positivo em T-pose.
        /// </summary>
        private static void StandUpright(Transform t, ref Bounds bounds)
        {
            if (bounds.size.y >= bounds.size.z) return;

            t.localRotation = Quaternion.Euler(-90f, 0f, 0f) * t.localRotation;
            if (!TryGetBoundsInParent(t.gameObject, out var corrected)) return;
            bounds = corrected;
        }

        /// <summary>
        /// Bounds combinados dos renderers, no espaço do PAI da instância — ou seja, JÁ com a
        /// rotação de correção aplicada. É o que permite endireitar uma peça deitada e ainda
        /// assim encaixá-la pela largura certa.
        /// </summary>
        public static bool TryGetBoundsInParent(GameObject instance, out Bounds bounds)
        {
            bounds = default;
            var renderers = instance.GetComponentsInChildren<Renderer>(includeInactive: false);
            if (renderers == null || renderers.Length == 0) return false;

            var reference = instance.transform.parent != null
                ? instance.transform.parent
                : instance.transform;
            var root = reference.worldToLocalMatrix;
            bool started = false;

            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;

                // Converte o AABB de mundo para o espaço local da raiz, cobrindo os 8 cantos:
                // usar só center/extents daria um volume errado quando há rotação nos filhos.
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    var local = root.MultiplyPoint3x4(corner);

                    if (!started) { bounds = new Bounds(local, Vector3.zero); started = true; }
                    else bounds.Encapsulate(local);
                }
            }

            return started;
        }

        /// <summary>
        /// Nada neste jogo usa física — a simulação resolve tudo em C# puro. Colliders de asset
        /// de loja só custam memória e podem interferir em raycast de UI, então saem de cena.
        /// </summary>
        private static void DisablePhysics(GameObject instance)
        {
            foreach (var c in instance.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var rb in instance.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        }
    }
}
