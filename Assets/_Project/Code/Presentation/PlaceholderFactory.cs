using System.Collections.Generic;
using DestinyTogether.Data;
using UnityEngine;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// Fabrica de views placeholder feitas de primitivas.
    ///
    /// A hierarquia gerada e exatamente o "prefab-contrato" que a arte final vai substituir:
    ///
    ///   Raiz  (EntityView, sem arte)
    ///     +-- Visual        <- TODA a arte mora aqui dentro
    ///
    /// Trocar placeholder por arte final = trocar o conteudo de "Visual" por um prefab variant.
    /// Nenhum presenter, nenhuma view e nenhuma linha da simulacao muda.
    /// Convencoes obrigatorias para a arte final: 1 unidade = 1 celula, pivot nos pes, +Z e frente.
    /// </summary>
    public sealed class PlaceholderFactory
    {
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
        private readonly Transform _root;
        private Shader _shader;

        public PlaceholderFactory(Transform root)
        {
            _root = root;
        }

        private Shader Shader
        {
            get
            {
                if (_shader == null)
                {
                    _shader = UnityEngine.Shader.Find("Universal Render Pipeline/Lit")
                              ?? UnityEngine.Shader.Find("Standard")
                              ?? UnityEngine.Shader.Find("Sprites/Default");
                }
                return _shader;
            }
        }

        public Material GetMaterial(Color color)
        {
            if (_materials.TryGetValue(color, out var cached)) return cached;
            var mat = new Material(Shader) { color = color, name = $"PH_{ColorUtility.ToHtmlStringRGB(color)}" };
            mat.SetColor(ShaderIds.BaseColor, color);
            _materials[color] = mat;
            return mat;
        }

        public EntityView CreateView(string name, VisualStyle style, Vector3 position)
        {
            var root = new GameObject(name);
            root.transform.SetParent(_root, false);
            root.transform.position = position;

            var view = root.AddComponent<EntityView>();
            var visual = CreatePrimitive(style);
            visual.transform.SetParent(root.transform, false);

            var renderer = visual.GetComponent<Renderer>();
            view.SetVisual(visual.transform, renderer, style.Color);
            return view;
        }

        public GameObject CreatePrimitive(VisualStyle style)
        {
            var go = ProceduralShapes.IsProcedural(style.Shape)
                ? CreateProcedural(style.Shape)
                : CreateEnginePrimitive(style.Shape);

            go.name = "Visual";

            float w = style.Scale;
            float h = Mathf.Max(0.05f, style.Height);
            // Capsula e cilindro do Unity ja tem 2 unidades de altura: metade para casar com o
            // resto. Os solidos procedurais nascem no cubo unitario justamente para nao precisarem
            // de excecao aqui.
            float yScale = style.Shape is PrimitiveShape.Capsule or PrimitiveShape.Cylinder ? h * 0.5f : h;
            go.transform.localScale = new Vector3(w, yScale, w);
            go.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);

            go.GetComponent<Renderer>().sharedMaterial = GetMaterial(style.Color);
            return go;
        }

        private static GameObject CreateEnginePrimitive(PrimitiveShape shape)
        {
            var type = shape switch
            {
                PrimitiveShape.Sphere => PrimitiveType.Sphere,
                PrimitiveShape.Capsule => PrimitiveType.Capsule,
                PrimitiveShape.Cylinder => PrimitiveType.Cylinder,
                _ => PrimitiveType.Cube
            };

            var go = GameObject.CreatePrimitive(type);

            // Colliders sao ruido no placeholder: nada aqui usa fisica, a simulacao resolve tudo.
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            return go;
        }

        private static GameObject CreateProcedural(PrimitiveShape shape)
        {
            var go = new GameObject();
            go.AddComponent<MeshFilter>().sharedMesh = ProceduralShapes.Get(shape);
            go.AddComponent<MeshRenderer>();
            return go;
        }

        public void Dispose()
        {
            foreach (var mat in _materials.Values)
                if (mat != null) Object.Destroy(mat);
            _materials.Clear();
            ProceduralShapes.Dispose();
        }
    }
}
