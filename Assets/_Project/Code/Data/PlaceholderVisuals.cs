using System.Collections.Generic;
using DestinyTogether.Sim;
using UnityEngine;

namespace DestinyTogether.Data
{
    /// <summary>
    /// O vocabulario de silhuetas. As quatro primeiras vem do motor; as tres ultimas sao geradas
    /// por <c>ProceduralShapes</c>, porque quatro formas nao bastam para distinguir sete
    /// comportamentos — e a alternativa (mesma forma, cor diferente) quebraria a regra de que
    /// cor diz LADO e nunca funcao.
    /// </summary>
    public enum PrimitiveShape
    {
        Cube = 0,
        Sphere = 1,
        Capsule = 2,
        Cylinder = 3,
        /// <summary>Pontas nos seis sentidos: o cacador.</summary>
        Octaedro = 4,
        /// <summary>Marcador fincado no chao: o achado.</summary>
        Cone = 5,
        /// <summary>Massa e monumento: o Kaiju.</summary>
        Piramide = 6
    }

    public struct VisualStyle
    {
        public PrimitiveShape Shape;
        public Color Color;
        public float Scale;
        public float Height;
    }

    /// <summary>
    /// A gramatica de cor e silhueta dos placeholders.
    ///
    /// Isto NAO e arte temporaria — e sistema de gameplay. O maior risco do desenho escolhido e
    /// legibilidade em tempo real com primitivas, e o jogo de referencia e criticado exatamente
    /// por nao diferenciar seus inimigos visualmente. Entao a regra vem antes da arte:
    ///
    ///   SILHUETA diz o que a coisa FAZ:  esfera = enxame · capsula = explode · cubo = tanque
    ///                                    cilindro = ataca a distancia · esfera grande = gera
    ///                                    octaedro = CACA VOCE · piramide = Kaiju · cone = achado
    ///   COR diz de que LADO esta:        vermelho/laranja = ameaca · azul-verde = seu · cinza = neutro
    ///
    /// Quando a arte final chegar, ela herda esta gramatica. Se um monstro novo nao couber em
    /// nenhuma silhueta existente, isso e sinal de que ele precisa de um comportamento novo —
    /// nao de uma cor nova.
    /// </summary>
    public static class PlaceholderVisuals
    {
        private static readonly Dictionary<DefId, VisualStyle> _styles = new Dictionary<DefId, VisualStyle>();

        static PlaceholderVisuals()
        {
            // --- Predios: cubos. Cor pela TAG, para o Distrito ser legivel de cima. ---
            Register(DefaultContent.Balestra, PrimitiveShape.Cube, new Color(0.62f, 0.66f, 0.72f), 0.85f, 1.1f);
            Register(DefaultContent.BalistaDeImpacto, PrimitiveShape.Cube, new Color(0.45f, 0.50f, 0.58f), 0.85f, 0.9f);
            Register(DefaultContent.PostoDeVigia, PrimitiveShape.Cube, new Color(0.72f, 0.76f, 0.82f), 0.6f, 1.6f);
            Register(DefaultContent.Braseiro, PrimitiveShape.Cube, new Color(0.90f, 0.42f, 0.18f), 0.8f, 0.8f);
            Register(DefaultContent.TorreDeGelo, PrimitiveShape.Cube, new Color(0.42f, 0.78f, 0.92f), 0.8f, 1.2f);
            Register(DefaultContent.Serraria, PrimitiveShape.Cube, new Color(0.55f, 0.42f, 0.24f), 0.9f, 0.6f);
            Register(DefaultContent.Pedreira, PrimitiveShape.Cube, new Color(0.48f, 0.45f, 0.40f), 0.9f, 0.55f);
            Register(DefaultContent.Oficina, PrimitiveShape.Cube, new Color(0.80f, 0.66f, 0.30f), 0.85f, 0.7f);
            Register(DefaultContent.Muralha, PrimitiveShape.Cube, new Color(0.35f, 0.35f, 0.38f), 0.98f, 0.5f);
            Register(DefaultContent.Deposito, PrimitiveShape.Cube, new Color(0.30f, 0.40f, 0.45f), 0.9f, 0.65f);

            // --- Monstros: a silhueta E o aviso. ---
            Register(DefaultContent.Enxame, PrimitiveShape.Sphere, new Color(0.85f, 0.16f, 0.16f), 0.55f, 0.55f);
            Register(DefaultContent.Estourador, PrimitiveShape.Capsule, new Color(1.00f, 0.55f, 0.05f), 0.7f, 1.0f);
            Register(DefaultContent.Bruto, PrimitiveShape.Cube, new Color(0.30f, 0.30f, 0.33f), 1.15f, 1.3f);
            Register(DefaultContent.Cuspidor, PrimitiveShape.Cylinder, new Color(0.62f, 0.24f, 0.78f), 0.6f, 1.0f);
            // Octaedro e a unica silhueta pontuda em todos os eixos — e a leitura de "vem atras
            // de VOCE". Amarelo-acido para separa-lo do vermelho de horda: o Rondador nao e mais
            // um da onda, e um problema pessoal.
            Register(DefaultContent.Rondador, PrimitiveShape.Octaedro, new Color(0.95f, 0.85f, 0.15f), 0.62f, 0.9f);
            Register(DefaultContent.Ninho, PrimitiveShape.Sphere, new Color(0.09f, 0.06f, 0.12f), 1.5f, 1.5f);
            Register(DefaultContent.MaeAranha, PrimitiveShape.Piramide, new Color(0.05f, 0.03f, 0.07f), 3.0f, 2.6f);

            // --- Herois: capsulas. Uma cor por jogador, fixa e nomeavel em voz alta. ---
            Register(DefaultContent.Guarda, PrimitiveShape.Capsule, new Color(0.25f, 0.55f, 0.95f), 0.75f, 1.5f);
            Register(DefaultContent.Lenhador, PrimitiveShape.Capsule, new Color(0.95f, 0.60f, 0.20f), 0.75f, 1.5f);
            Register(DefaultContent.Golem, PrimitiveShape.Capsule, new Color(0.25f, 0.75f, 0.40f), 0.9f, 1.6f);
            Register(DefaultContent.Arauto, PrimitiveShape.Capsule, new Color(0.70f, 0.40f, 0.90f), 0.65f, 1.4f);
        }

        private static void Register(string name, PrimitiveShape shape, Color color, float scale, float height)
            => _styles[DefId.FromName(name)] = new VisualStyle { Shape = shape, Color = color, Scale = scale, Height = height };

        public static VisualStyle Get(DefId id)
            => _styles.TryGetValue(id, out var s)
                ? s
                : new VisualStyle { Shape = PrimitiveShape.Cube, Color = Color.magenta, Scale = 0.8f, Height = 0.8f };

        /// <summary>
        /// O Esconderijo revelado. Cone dourado que acende na mata — a unica coisa do jogo que
        /// e desenhada para ser vista de longe e alcancada depois.
        /// </summary>
        public static VisualStyle CacheStyle(CacheKind kind)
            => kind == CacheKind.Relicario
                ? new VisualStyle
                {
                    Shape = PrimitiveShape.Cone,
                    Color = new Color(1.00f, 0.86f, 0.35f),
                    Scale = 0.85f,
                    Height = 1.9f
                }
                : new VisualStyle
                {
                    Shape = PrimitiveShape.Cone,
                    Color = new Color(0.85f, 0.68f, 0.32f),
                    Scale = 0.6f,
                    Height = 1.1f
                };

        public static Color ResourceColor(HarvestNodeKind kind)
        {
            switch (kind)
            {
                case HarvestNodeKind.Arvore: return new Color(0.20f, 0.60f, 0.28f);
                case HarvestNodeKind.Rocha: return new Color(0.55f, 0.55f, 0.58f);
                default: return new Color(0.95f, 0.80f, 0.20f);
            }
        }

        public static Color PlayerColor(int index)
        {
            switch (index)
            {
                case 0: return new Color(0.25f, 0.55f, 0.95f);
                case 1: return new Color(0.95f, 0.60f, 0.20f);
                case 2: return new Color(0.25f, 0.75f, 0.40f);
                default: return new Color(0.70f, 0.40f, 0.90f);
            }
        }

        public static Color BandColor(ForecastBand band)
        {
            switch (band)
            {
                case ForecastBand.Segura: return new Color(0.30f, 0.85f, 0.45f);
                case ForecastBand.Vaza: return new Color(0.98f, 0.78f, 0.20f);
                default: return new Color(0.95f, 0.25f, 0.25f);
            }
        }
    }
}
