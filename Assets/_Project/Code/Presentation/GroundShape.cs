using DestinyTogether.Core;
using DestinyTogether.Sim;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// A altura do chao num ponto do mundo.
    ///
    /// **Funcao pura da POSICAO NO MUNDO**, e nao do pedaco de malha que a desenha. E o que
    /// resolve o problema central de um chao que persegue a camera: o relevo fica ancorado no
    /// mundo, entao a malha pode ser reconstruida a cada passo de snap que a superficie continua
    /// exatamente onde estava. A alternativa — relevo em coordenada local — faria as colinas
    /// deslizarem sob os pes, que e a versao tridimensional do artefato que o offset de UV do
    /// chao antigo ja existia para evitar.
    ///
    /// A simulacao NAO conhece isto. Ela e plana: heroi anda em Vec2, distancia de ataque e
    /// medida no plano, e o Prognostico nao sabe o que e uma ladeira. O relevo e leitura, nao
    /// regra — e por isso mora na apresentacao.
    ///
    /// O centro fica CHAPADO de proposito. Predio em terreno inclinado ou flutua ou afunda, e o
    /// tabuleiro e uma grade de pecas de 1x1: a vila e construida, o lado de fora e bruto. A
    /// fronteira entre os dois vira leitura de graca.
    /// </summary>
    public sealed class GroundShape
    {
        /// <summary>Chao perfeitamente plano. E o padrao — quem nao configurar nada nao ve relevo.</summary>
        public static readonly GroundShape Flat = new GroundShape(Vec2.Zero, 0f, 0f);

        /// <summary>
        /// Lado de uma faceta, em celulas. Mora AQUI e nao no renderizador porque a oitava curta
        /// tem exatamente este periodo — ver <see cref="FacetWeight"/>.
        /// </summary>
        public const float FacetSize = 3.5f;

        /// <summary>Semente fixa: o relevo e cenario, nao conteudo sorteado por partida.</summary>
        private const int Seed = 0x6C4A21;

        /// <summary>Ondulacao larga — as colinas que se leem de longe.</summary>
        private const float BroadScale = 45f;

        /// <summary>Ondulacao media — o que impede as colinas de virarem dunas lisas.</summary>
        private const float MidScale = 14f;

        private const float BroadWeight = 0.33f;
        private const float MidWeight = 0.25f;

        /// <summary>
        /// A oitava que FACETA, e o motivo de ela existir separada.
        ///
        /// O periodo dela e o lado de uma faceta, entao os quatro cantos de um triangulo caem em
        /// pontos VIZINHOS da grade do ruido e recebem valores independentes — e e essa
        /// independencia que faz triangulos vizinhos terem inclinacoes diferentes e pegarem a luz
        /// de jeitos diferentes.
        ///
        /// Sem ela o chao fica geometricamente correto e visualmente CHAPADO: medido, so com as
        /// oitavas larga e media a inclinacao media era 0,9 grau, porque um periodo de 13 celulas
        /// mal muda de altura ao longo de 3,5. Faceta nao vem de amplitude, vem de FREQUENCIA
        /// comparavel ao tamanho do triangulo.
        /// </summary>
        private const float FacetWeight = 0.42f;

        /// <summary>Ao longo de quantas celulas o chao sai do plano ate o relevo cheio.</summary>
        private const float Blend = 26f;

        private readonly Vec2 _center;
        private readonly float _flatRadius;
        private readonly float _amplitude;

        public GroundShape(Vec2 center, float flatRadius, float amplitude)
        {
            _center = center;
            _flatRadius = flatRadius;
            _amplitude = amplitude;
        }

        public float HeightAt(Vec2 point)
        {
            if (_amplitude <= 0.0001f) return 0f;

            float distance = Vec2.Distance(point, _center);
            if (distance <= _flatRadius) return 0f;

            float t = MathUtil.Clamp01((distance - _flatRadius) / Blend);
            float ramp = t * t * (3f - 2f * t);

            // O mesmo ruido de valor do mundo procedural. Reusar em vez de escrever outro nao e
            // economia de linhas: e a garantia de que o chao e a mata concordam sobre onde as
            // coisas ficam, hoje e quando o relevo influenciar o bioma.
            float broad = WorldGen.Noise01(Seed, point.X, point.Y, BroadScale) - 0.5f;
            float mid = WorldGen.Noise01(Seed ^ 0x1F35, point.X, point.Y, MidScale) - 0.5f;
            float facet = WorldGen.Noise01(Seed ^ 0x7A93, point.X, point.Y, FacetSize) - 0.5f;

            float sum = broad * BroadWeight + mid * MidWeight + facet * FacetWeight;
            return sum * 2f * _amplitude * ramp;
        }
    }
}
