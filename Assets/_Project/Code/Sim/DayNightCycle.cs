using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// A curva do ciclo: quanto de "noite" existe neste instante, de 0 a 1.
    ///
    /// Vive na simulacao, e nao na apresentacao, porque tres consumidores precisam da MESMA
    /// resposta — a luz da cena, o relogio do HUD e, depois, a trilha. Se cada um tivesse a
    /// propria curva, o sol se poria num momento e a musica virasse noutro, e o jogador leria
    /// isso como bug antes de ler como clima.
    ///
    /// Nao contem UnityEngine e nao altera estado: e uma funcao pura sobre a fase e o relogio.
    ///
    /// O desenho da curva tem um alinhamento deliberado: o clarear do ceu comeca EXATAMENTE
    /// quando os monstros param de nascer. O jogador nao precisa de um aviso na tela dizendo
    /// "ultima onda" — ele olha para cima. Informacao de jogo entregue por clima e a unica que
    /// nao compete por espaco no HUD.
    /// </summary>
    public static class DayNightCycle
    {
        /// <summary>Fracao final do Dia em que o sol comeca a cair. 0.30 de 300s = 90s de entardecer.</summary>
        private const float DuskFraction = 0.30f;

        /// <summary>Escuridao ja atingida quando a noite formalmente comeca.</summary>
        private const float DuskPeak = 0.90f;

        /// <summary>Fracao inicial da Noite em que ela termina de fechar.</summary>
        private const float NightfallFraction = 0.05f;

        public static float NightAmount(MatchState state, MatchRulesSpec rules)
        {
            if (state == null) return 0f;

            switch (state.Phase)
            {
                case PhaseId.Dia:
                {
                    float t = state.PhaseProgress01;
                    if (t <= 1f - DuskFraction) return 0f;
                    float k = (t - (1f - DuskFraction)) / DuskFraction;
                    return SmoothStep(k) * DuskPeak;
                }

                case PhaseId.Noite:
                {
                    float t = state.PhaseProgress01;

                    // O amanhecer ocupa exatamente a janela sem spawn. Ceu clareando e "a ultima
                    // onda ja entrou" sao a mesma informacao, dita uma vez so.
                    float dawnFraction = rules != null && rules.NoiteSeconds > 0.01f
                        ? MathUtil.Clamp01(rules.SpawnCutoffBeforeDawn / rules.NoiteSeconds)
                        : 0.15f;

                    if (t < NightfallFraction)
                        return MathUtil.Lerp(DuskPeak, 1f, t / NightfallFraction);

                    float dawnStart = 1f - dawnFraction;
                    if (t <= dawnStart) return 1f;

                    return 1f - SmoothStep((t - dawnStart) / dawnFraction);
                }

                default:
                    return 0f;
            }
        }

        // ----------------------------------------------------------------------------------
        // O ARCO DO SOL
        // ----------------------------------------------------------------------------------

        /// <summary>Altura do sol a pino, em graus.</summary>
        private const float NoonElevation = 58f;

        /// <summary>
        /// Altura no nascer e no pôr. Rente, mas nao colado: a 0 grau a sombra tende ao infinito e
        /// o mapa inteiro vira uma mancha escura, que le como bug de iluminacao e nao como manha.
        /// </summary>
        private const float HorizonElevation = 8f;

        /// <summary>Altura no meio da noite. Negativo = abaixo do horizonte.</summary>
        private const float NightElevation = -14f;

        /// <summary>Quanto o sol anda em azimute do nascer ao poente. Meia volta.</summary>
        private const float HalfTurn = 90f;

        /// <summary>
        /// Onde o sol esta agora. Angulos em graus; <see cref="Horizon01"/> e 1 rente ao horizonte
        /// e 0 a pino, que e o sinal de "esta alaranjado".
        /// </summary>
        public readonly struct SunOrientation
        {
            public readonly float Elevation;
            /// <summary>Azimute RELATIVO ao meio-dia: -90 no nascer, +90 no poente.</summary>
            public readonly float AzimuthFromNoon;
            public readonly float Horizon01;

            public SunOrientation(float elevation, float azimuthFromNoon, float horizon01)
            {
                Elevation = elevation;
                AzimuthFromNoon = azimuthFromNoon;
                Horizon01 = horizon01;
            }
        }

        /// <summary>
        /// O arco completo, dirigido pelo RELOGIO DA FASE e nao pela curva de luz.
        ///
        /// Sao coisas diferentes e precisavam ser separadas: <see cref="NightAmount"/> fica em zero
        /// nos primeiros 70% do Dia — e o que mantem o mapa claro e seguro — entao um sol preso a
        /// ela ficava PARADO no mesmo ponto do ceu por três minutos e meio e depois despencava. O
        /// arco anda o Dia inteiro; a escuridao continua chegando so no fim.
        ///
        /// Nasce a leste-da-tela e se poe a oeste-da-tela varrendo 180 graus, e a noite completa a
        /// volta por baixo. Elevacao em seno: rente nas pontas, a pino no meio. Como as duas fases
        /// comecam e terminam em <see cref="HorizonElevation"/>, a emenda entre Dia e Noite e
        /// continua — nao existe um quadro em que o sol salta.
        /// </summary>
        public static SunOrientation Sun(MatchState state)
        {
            if (state == null) return new SunOrientation(NoonElevation, 0f, 0f);

            float t = MathUtil.Clamp01(state.PhaseProgress01);
            float arc = (float)System.Math.Sin(t * System.Math.PI);
            float horizon = 1f - arc;

            switch (state.Phase)
            {
                case PhaseId.Dia:
                    return new SunOrientation(MathUtil.Lerp(HorizonElevation, NoonElevation, arc),
                                              MathUtil.Lerp(-HalfTurn, HalfTurn, t),
                                              horizon);

                case PhaseId.Noite:
                    return new SunOrientation(MathUtil.Lerp(HorizonElevation, NightElevation, arc),
                                              MathUtil.Lerp(HalfTurn, HalfTurn + 180f, t),
                                              horizon);

                default:
                    return new SunOrientation(NoonElevation, 0f, 0f);
            }
        }

        private static float SmoothStep(float t)
        {
            t = MathUtil.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
