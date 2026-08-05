using UnityEngine;

namespace DestinyTogether.Data
{
    /// <summary>
    /// Atmosfera da arena: luz, névoa, ambiente e céu.
    ///
    /// Vive num asset em vez de constantes no Bootstrap porque tom é a coisa que mais muda durante
    /// a produção e a que mais depende de ver rodando. Trocar este asset troca o clima do jogo
    /// inteiro sem recompilar nada.
    ///
    /// A névoa aqui não é enfeite: ela é o que faz um monstro *aparecer* vindo do escuro em vez de
    /// simplesmente estar lá. Num jogo em que a tensão vem de saber o que vem e não saber se você
    /// aguenta, o horizonte fechado é gameplay — o mesmo papel que a névoa negra cumpre no jogo
    /// de referência.
    /// </summary>
    [CreateAssetMenu(menuName = "Destiny Together/Atmosfera", fileName = "Atmosfera_")]
    public sealed class AtmosphereProfile : ScriptableObject
    {
        // ------------------------------------------------------------------------------------
        // Os campos abaixo descrevem a NOITE, que e o estado extremo e o que ja estava calibrado.
        // O bloco "Dia" no fim descreve o outro extremo, e a apresentacao interpola entre os dois
        // pela curva de DayNightCycle. Um asset, dois climas: e o que garante que o entardecer
        // seja uma transicao continua em vez de um corte entre dois perfis.
        // ------------------------------------------------------------------------------------

        [Header("Sol (noite)")]
        public Color SunColor = new Color(0.62f, 0.68f, 0.85f);

        /// <summary>
        /// Luar. 0,30 e nao 0,75, e o motivo e a ORDEM de leitura, nao o brilho.
        ///
        /// O chao subiu de 12,6% para 41,1% de albedo (faixa de ambiente do token). Mantida a luz
        /// antiga, a noite passava a render um chao mais claro que o token de XP (28,4%) e que o
        /// de horda (12%) — ou seja, o cenario ficava por cima do gameplay exatamente na fase em
        /// que enxergar a horda e a unica coisa que importa. Baixar a direcional e o que devolve a
        /// ordem: com albedo 3,3x maior e luz 2,5x menor o chao noturno fica onde estava, e as
        /// pecas de gameplay, que nao mudaram de cor, voltam a se destacar dele.
        /// </summary>
        [Range(0f, 3f)] public float SunIntensity = 0.30f;
        /// <summary>
        /// X é IGNORADO — a elevação vem do arco em <c>DayNightCycle.Sun</c>. Y é o azimute do
        /// MEIO-DIA, do qual o nascer fica 90° para um lado e o poente 90° para o outro; Z é o
        /// roll, que num sol não faz nada e existe só para não perder o campo.
        ///
        /// 45° casa com o yaw da câmera: ao meio-dia o sol fica atrás de quem olha, que é a luz
        /// frontal que um jogo visto de cima quer. Nas pontas do dia ele fica a 90° do eixo da
        /// câmera, e é daí que vem a luz rasante do amanhecer e do entardecer.
        /// </summary>
        public Vector3 SunAngles = new Vector3(38f, 45f, 0f);

        /// <summary>
        /// Cor do sol rente ao horizonte. A atmosfera espalha o azul quando a luz atravessa mais
        /// ar, e sobra o laranja — é a única cor que o jogador já sabe ler como "começo" ou "fim
        /// do dia" sem que ninguém explique.
        /// </summary>
        public Color HorizonSunColor = new Color(1.00f, 0.58f, 0.28f);

        /// <summary>
        /// Névoa do amanhecer e do entardecer. Sem ela o sol fica laranja num ar cinzento, e o
        /// olho lê como luz colorida em vez de hora do dia: o que vende um poente é o AR quente,
        /// não a lâmpada.
        /// </summary>
        public Color HorizonFogColor = new Color(0.86f, 0.62f, 0.44f);
        public bool SunShadows = true;
        [Range(0f, 1f)] public float ShadowStrength = 0.75f;

        /// <summary>
        /// #343A52, 4,4% de luminancia linear. Subiu pouco de proposito (era 2,8%).
        ///
        /// A tentacao era subir muito, para "compensar" o chao mais claro. Medido, isso destroi a
        /// noite: com ambiente a 14% o quadro inteiro vira um banho cinza-azulado em que o Kaiju
        /// fica a tres niveis de 255 do chao em que pisa. O que a noite precisa nao e de brilho, e
        /// de ESPALHAMENTO — e espalhamento vem de manter o ambiente baixo enquanto as pecas de
        /// gameplay carregam a propria cor.
        /// </summary>
        [Header("Luz ambiente (noite)")]
        public Color AmbientColor = new Color(0.204f, 0.227f, 0.322f);

        [Header("Nevoa (noite)")]
        public bool FogEnabled = true;
        // #1D2130 (1,6%): fica ABAIXO do chão noturno de propósito, para que a distância
        // ESCUREÇA em vez de clarear. Névoa mais clara que o chão faz o horizonte brilhar, e é o
        // avesso do que uma noite fechada precisa.
        public Color FogColor = new Color(0.114f, 0.129f, 0.188f);
        public FogMode FogMode = FogMode.ExponentialSquared;
        // 0,022 e não 0,045 — o default estava velho, e é o asset que tinha o valor calibrado.
        //
        // A névoa do Unity mede da CÂMERA, não do herói, e a câmera fica a 18–25 unidades. Medido
        // em 0,045: sobra 37% de visibilidade no PRÓPRIO herói e 8,7% no pilar de Faixa do lado
        // oposto — ou seja, a telegrafia da ameaça some. Em 0,022 são 79% e 56%. O comentário
        // logo abaixo da linha que posiciona os pilares diz literalmente que eles não podem sumir
        // com névoa densa; 0,045 produzia exatamente isso. Se a noite precisar fechar mais, o teto
        // é ~0,030 (48% no herói, 34% no pilar distante).
        [Range(0f, 0.2f)] public float FogDensity = 0.022f;
        [Tooltip("Usado apenas no modo Linear.")]
        public float FogStart = 12f;
        public float FogEnd = 48f;

        [Header("Ceu e fundo")]
        public Material Skybox;
        [Tooltip("Cor de fundo quando nao ha skybox.")]
        public Color BackgroundColor = new Color(0.106f, 0.118f, 0.169f);
        [Tooltip("Usar o skybox como fundo da camera. Desligado = cor chapada, que reforca o clima fechado.")]
        public bool UseSkyboxAsBackground = false;

        // ------------------------------------------------------------------------------------
        // DIA
        //
        // O dia não é "a noite mais clara": é um clima diferente, e a diferença mais importante
        // não é o brilho, é o ALCANCE DE VISÃO. Com névoa em 0,010 o horizonte abre para ~140
        // unidades e a mata dos cantos fica visível do centro da cidade — o que transforma
        // explorar numa escolha informada ("vou até aquele bosque") em vez de um passeio no
        // escuro. À noite a névoa fecha em 0,045 e o mesmo bosque some, que é exatamente o que
        // faz atravessá-lo custar coragem.
        // ------------------------------------------------------------------------------------

        [Header("Sol (dia)")]
        public Color DaySunColor = new Color(1.00f, 0.96f, 0.86f);
        // 1,05 e nao 1,35: o chao triplicou de albedo (12,6% -> 41,1%) ao entrar na faixa de
        // ambiente, e manter o sol antigo estouraria o meio-dia — chao no limite do branco engole
        // a silhueta, que e a gramatica de leitura do jogo inteiro.
        [Range(0f, 3f)] public float DaySunIntensity = 1.05f;
        [Range(0f, 1f)] public float DayShadowStrength = 0.55f;

        [Header("Luz ambiente (dia)")]
        public Color DayAmbientColor = new Color(0.52f, 0.55f, 0.60f);

        [Header("Nevoa (dia)")]
        public Color DayFogColor = new Color(0.72f, 0.74f, 0.70f);
        [Range(0f, 0.2f)] public float DayFogDensity = 0.010f;
        public Color DayBackgroundColor = new Color(0.66f, 0.71f, 0.76f);

        /// <summary>Clima padrão: crepúsculo frio e fechado. Usado quando não há asset.</summary>
        public static AtmosphereProfile CreateDefaultDark()
        {
            var p = CreateInstance<AtmosphereProfile>();
            p.name = "Atmosfera_Padrao";
            return p;
        }
    }
}
