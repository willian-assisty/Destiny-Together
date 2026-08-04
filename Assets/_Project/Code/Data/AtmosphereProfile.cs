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
        [Header("Sol")]
        public Color SunColor = new Color(0.62f, 0.68f, 0.85f);
        [Range(0f, 3f)] public float SunIntensity = 0.75f;
        public Vector3 SunAngles = new Vector3(38f, 26f, 0f);
        public bool SunShadows = true;
        [Range(0f, 1f)] public float ShadowStrength = 0.75f;

        [Header("Luz ambiente")]
        public Color AmbientColor = new Color(0.16f, 0.18f, 0.26f);

        [Header("Nevoa")]
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

        /// <summary>Clima padrão: crepúsculo frio e fechado. Usado quando não há asset.</summary>
        public static AtmosphereProfile CreateDefaultDark()
        {
            var p = CreateInstance<AtmosphereProfile>();
            p.name = "Atmosfera_Padrao";
            return p;
        }
    }
}
