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
        [Range(0f, 3f)] public float SunIntensity = 0.75f;
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

        [Header("Luz ambiente (noite)")]
        public Color AmbientColor = new Color(0.16f, 0.18f, 0.26f);

        [Header("Nevoa (noite)")]
        public bool FogEnabled = true;
        public Color FogColor = new Color(0.09f, 0.10f, 0.14f);
        public FogMode FogMode = FogMode.ExponentialSquared;
        // 0.045 fecha o mundo a ~30 unidades: a horda surge do escuro em vez de estar sempre à
        // vista. Os pilares de Faixa foram trazidos para dentro desse alcance justamente para
        // continuarem legíveis — névoa que engole a telegrafia da ameaça é clima caro demais.
        [Range(0f, 0.2f)] public float FogDensity = 0.045f;
        [Tooltip("Usado apenas no modo Linear.")]
        public float FogStart = 12f;
        public float FogEnd = 48f;

        [Header("Ceu e fundo")]
        public Material Skybox;
        [Tooltip("Cor de fundo quando nao ha skybox.")]
        public Color BackgroundColor = new Color(0.05f, 0.055f, 0.075f);
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
        [Range(0f, 3f)] public float DaySunIntensity = 1.35f;
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
