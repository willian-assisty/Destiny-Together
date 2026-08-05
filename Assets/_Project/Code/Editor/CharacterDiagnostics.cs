using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DestinyTogether.EditorTools
{
    /// <summary>
    /// Imprime por que um personagem riggado nao anima.
    ///
    /// Existe porque o estado de import e INVISIVEL: `avatarSetup`, `animationType` e o conjunto de
    /// caminhos de curva de um clipe nao aparecem em lugar nenhum do inspector lado a lado com a
    /// hierarquia do prefab — e e exatamente a divergencia entre esses dois que quebra a animacao.
    /// Diagnosticar isso lendo YAML e adivinhacao; aqui e medicao.
    /// </summary>
    public static class CharacterDiagnostics
    {
        [MenuItem("Destiny Together/Diagnosticar personagens")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== DIAGNOSTICO DE PERSONAGENS ===");

            foreach (var folder in AssetDatabase.GetSubFolders("Assets/_Project/Art/Final/Characters"))
            {
                string name = System.IO.Path.GetFileName(folder);
                sb.AppendLine().AppendLine($"--- {name} ---");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{name}.prefab");
                if (prefab == null) { sb.AppendLine("  SEM PREFAB"); continue; }

                var animator = prefab.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    sb.AppendLine("  sem Animator (malha estatica — locomocao procedural assume)");
                    continue;
                }

                sb.AppendLine($"  Animator: avatar={(animator.avatar != null ? animator.avatar.name : "NULO")} " +
                              $"valido={(animator.avatar != null && animator.avatar.isValid)} " +
                              $"humano={(animator.avatar != null && animator.avatar.isHuman)}");
                sb.AppendLine($"  Controller: {(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "NULO")}");

                // Todos os caminhos de transform que o prefab REALMENTE tem.
                var paths = new HashSet<string>();
                CollectPaths(prefab.transform, prefab.transform, paths);

                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null) continue;

                    sb.AppendLine($"  [{System.IO.Path.GetFileName(path)}] tipo={importer.animationType} " +
                                  $"avatarSetup={importer.avatarSetup} " +
                                  $"sourceAvatar={(importer.sourceAvatar != null ? importer.sourceAvatar.name : "-")} " +
                                  $"motionNode='{importer.motionNodeName}'");

                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;

                        var bindings = AnimationUtility.GetCurveBindings(clip);
                        int miss = 0;
                        string firstMiss = null;
                        var seen = new HashSet<string>();

                        foreach (var b in bindings)
                        {
                            if (!seen.Add(b.path)) continue;
                            if (paths.Contains(b.path)) continue;
                            miss++;
                            firstMiss ??= b.path;
                        }

                        sb.AppendLine($"      clipe '{clip.name}' {clip.length:0.00}s " +
                                      $"caminhos={seen.Count} SEM CORRESPONDENCIA={miss}" +
                                      (firstMiss != null ? $"  ex: '{firstMiss}'" : ""));

                        sb.AppendLine("      " + SampleReport(prefab, clip));
                    }
                }
            }

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Amostra o clipe no prefab e mede quanto o corpo REALMENTE se move.
        ///
        /// E a unica prova de que o clipe anima este prefab: caminho que casa ainda pode ter curva
        /// constante. Mede tambem a deriva do quadril, que e o que faz um personagem "andar e
        /// voltar" quando a translacao do Hips nao foi extraida como root motion.
        /// </summary>
        private static string SampleReport(GameObject prefab, AnimationClip clip)
        {
            var probe = Object.Instantiate(prefab);
            try
            {
                var hips = FindDeep(probe.transform, "Hips");
                var poses = new List<Vector3>();
                float maxBoneDelta = 0f;
                Quaternion[] previous = null;

                var joints = probe.GetComponentsInChildren<Transform>(true);

                for (int step = 0; step <= 4; step++)
                {
                    clip.SampleAnimation(probe, clip.length * step / 4f);
                    if (hips != null) poses.Add(hips.localPosition);

                    var now = new Quaternion[joints.Length];
                    for (int i = 0; i < joints.Length; i++) now[i] = joints[i].localRotation;
                    if (previous != null)
                        for (int i = 0; i < joints.Length; i++)
                            maxBoneDelta = Mathf.Max(maxBoneDelta, Quaternion.Angle(previous[i], now[i]));
                    previous = now;
                }

                float drift = 0f;
                for (int i = 1; i < poses.Count; i++)
                    drift = Mathf.Max(drift, Vector3.Distance(poses[0], poses[i]));

                var bounds = MeasureBounds(probe);

                return $"amostragem: rotacao maxima de osso {maxBoneDelta:0.0} graus · " +
                       $"deriva do quadril {drift:0.00} unidades · " +
                       $"altura {bounds.size.y:0.00} base y={bounds.min.y:0.00} " +
                       (maxBoneDelta < 0.5f ? "<<< CLIPE NAO MEXE NADA" : "(clipe anima)") +
                       Gabarito(probe, clip);
            }
            finally { Object.DestroyImmediate(probe); }
        }

        /// <summary>
        /// Posicao de alguns ossos ao longo do clipe, para conferir contra o gabarito que o
        /// conversor imprime a partir do GLB.
        ///
        /// E a unica prova de que a conversao de quaternion para Euler saiu certa: um erro de
        /// convencao de ordem de rotacao ainda produz um clipe que "anima", com ossos girando e
        /// caminhos casando — so que na pose errada. Sem numeros dos dois lados, isso passa.
        /// </summary>
        private static string Gabarito(GameObject probe, AnimationClip clip)
        {
            string[] watch = { "Bone_000", "Bone_018", "Bone_021", "Bone_009", "Bone_014" };

            var found = new List<Transform>();
            foreach (var name in watch)
            {
                var bone = FindDeep(probe.transform, name);
                if (bone != null) found.Add(bone);
            }
            if (found.Count == 0) return "";

            var sb = new StringBuilder();
            foreach (float fraction in new[] { 0f, 0.25f, 0.5f })
            {
                clip.SampleAnimation(probe, clip.length * fraction);
                sb.Append($"\n        t={clip.length * fraction:0.000}  ");
                foreach (var bone in found)
                {
                    var p = probe.transform.InverseTransformPoint(bone.position);
                    sb.Append($"{bone.name}=({p.x:+0.000;-0.000},{p.y:+0.000;-0.000},{p.z:+0.000;-0.000})  ");
                }
            }
            return sb.ToString();
        }

        private static Bounds MeasureBounds(GameObject probe)
        {
            var renderers = probe.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds();

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindDeep(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static void CollectPaths(Transform root, Transform t, HashSet<string> into)
        {
            into.Add(t == root ? "" : AnimationUtility.CalculateTransformPath(t, root));
            for (int i = 0; i < t.childCount; i++) CollectPaths(root, t.GetChild(i), into);
        }
    }
}
