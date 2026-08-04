using System.Collections.Generic;
using DestinyTogether.Data;
using UnityEngine;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// Solidos geometricos que o Unity nao traz de fabrica.
    ///
    /// O motor oferece cubo, esfera, capsula, cilindro, plano e quad — seis silhuetas, das quais
    /// quatro sao usaveis de cima. Isso e pouco para a gramatica de placeholder deste projeto,
    /// onde a SILHUETA e quem diz o que a coisa faz. Sem formas novas, dois comportamentos
    /// diferentes acabariam com o mesmo corpo e cores diferentes — e cor, aqui, ja esta ocupada
    /// dizendo de que LADO a coisa esta.
    ///
    /// Todos os solidos sao gerados dentro do cubo unitario centrado na origem (-0.5 a 0.5 em
    /// cada eixo), exatamente como as primitivas do Unity, para que o mesmo codigo de escala e
    /// deslocamento sirva para os dois casos sem excecao.
    ///
    /// Normais sao FACETADAS de proposito: cada triangulo tem os proprios vertices. Solido
    /// geometrico com sombreado suave vira uma bolha indistinta a dez metros de altura, que e
    /// justamente a distancia de onde este jogo e visto.
    /// </summary>
    public static class ProceduralShapes
    {
        private static readonly Dictionary<PrimitiveShape, Mesh> _cache = new Dictionary<PrimitiveShape, Mesh>();

        /// <summary>Lados da base do cone. 16 le como circulo sem custar nada.</summary>
        private const int ConeSegments = 16;

        public static bool IsProcedural(PrimitiveShape shape)
            => shape is PrimitiveShape.Octaedro or PrimitiveShape.Cone or PrimitiveShape.Piramide;

        public static Mesh Get(PrimitiveShape shape)
        {
            if (_cache.TryGetValue(shape, out var cached) && cached != null) return cached;

            var mesh = shape switch
            {
                PrimitiveShape.Octaedro => BuildOctahedron(),
                PrimitiveShape.Cone => BuildCone(),
                PrimitiveShape.Piramide => BuildPyramid(),
                _ => BuildOctahedron()
            };

            mesh.name = $"PH_{shape}";
            _cache[shape] = mesh;
            return mesh;
        }

        public static void Dispose()
        {
            foreach (var mesh in _cache.Values)
                if (mesh != null) Object.Destroy(mesh);
            _cache.Clear();
        }

        /// <summary>
        /// Octaedro: pontas nos seis sentidos. E o unico solido do conjunto que e agressivo em
        /// TODAS as direcoes — por isso ele veste o cacador, que chega de qualquer lado.
        /// </summary>
        private static Mesh BuildOctahedron()
        {
            var up = new Vector3(0f, 0.5f, 0f);
            var down = new Vector3(0f, -0.5f, 0f);
            var px = new Vector3(0.5f, 0f, 0f);
            var nx = new Vector3(-0.5f, 0f, 0f);
            var pz = new Vector3(0f, 0f, 0.5f);
            var nz = new Vector3(0f, 0f, -0.5f);

            var builder = new FacetBuilder(8);
            builder.Tri(up, pz, px);
            builder.Tri(up, px, nz);
            builder.Tri(up, nz, nx);
            builder.Tri(up, nx, pz);
            builder.Tri(down, px, pz);
            builder.Tri(down, nz, px);
            builder.Tri(down, nx, nz);
            builder.Tri(down, pz, nx);
            return builder.ToMesh();
        }

        /// <summary>Cone de base circular. Le como marcador fincado no chao — serve ao Esconderijo.</summary>
        private static Mesh BuildCone()
        {
            var apex = new Vector3(0f, 0.5f, 0f);
            var builder = new FacetBuilder(ConeSegments * 2);

            var rim = new Vector3[ConeSegments];
            for (int i = 0; i < ConeSegments; i++)
            {
                float a = i / (float)ConeSegments * Mathf.PI * 2f;
                rim[i] = new Vector3(Mathf.Cos(a) * 0.5f, -0.5f, Mathf.Sin(a) * 0.5f);
            }

            var baseCenter = new Vector3(0f, -0.5f, 0f);
            for (int i = 0; i < ConeSegments; i++)
            {
                var a = rim[i];
                var b = rim[(i + 1) % ConeSegments];
                builder.Tri(apex, b, a);          // lateral
                builder.Tri(baseCenter, a, b);    // tampa
            }

            return builder.ToMesh();
        }

        /// <summary>Piramide de base quadrada: massa e monumento. Veste o Kaiju.</summary>
        private static Mesh BuildPyramid()
        {
            var apex = new Vector3(0f, 0.5f, 0f);
            var a = new Vector3(-0.5f, -0.5f, -0.5f);
            var b = new Vector3(0.5f, -0.5f, -0.5f);
            var c = new Vector3(0.5f, -0.5f, 0.5f);
            var d = new Vector3(-0.5f, -0.5f, 0.5f);

            var builder = new FacetBuilder(6);
            builder.Tri(apex, b, a);
            builder.Tri(apex, c, b);
            builder.Tri(apex, d, c);
            builder.Tri(apex, a, d);
            builder.Tri(a, b, c);
            builder.Tri(a, c, d);
            return builder.ToMesh();
        }

        /// <summary>
        /// Acumula triangulos independentes. Cada um traz os proprios tres vertices, o que produz
        /// facetas duras sem precisar calcular nada — a normal sai do produto vetorial da aresta.
        /// </summary>
        private struct FacetBuilder
        {
            private readonly List<Vector3> _vertices;
            private readonly List<Vector3> _normals;
            private readonly List<Vector2> _uvs;
            private readonly List<int> _indices;

            public FacetBuilder(int triangleCount)
            {
                int v = triangleCount * 3;
                _vertices = new List<Vector3>(v);
                _normals = new List<Vector3>(v);
                _uvs = new List<Vector2>(v);
                _indices = new List<int>(v);
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var normal = Vector3.Cross(b - a, c - a).normalized;
                int start = _vertices.Count;

                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
                _normals.Add(normal); _normals.Add(normal); _normals.Add(normal);
                _uvs.Add(new Vector2(0f, 0f)); _uvs.Add(new Vector2(1f, 0f)); _uvs.Add(new Vector2(0.5f, 1f));
                _indices.Add(start); _indices.Add(start + 1); _indices.Add(start + 2);
            }

            public Mesh ToMesh()
            {
                var mesh = new Mesh();
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_indices, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
