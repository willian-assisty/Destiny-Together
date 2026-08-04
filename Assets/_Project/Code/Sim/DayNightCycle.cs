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

        /// <summary>Rotacao do sol em graus, de 0 (meio-dia) a 1 (meia-noite). So a apresentacao usa.</summary>
        public static float SunPitch(float nightAmount) => MathUtil.Lerp(52f, -12f, MathUtil.Clamp01(nightAmount));

        private static float SmoothStep(float t)
        {
            t = MathUtil.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
