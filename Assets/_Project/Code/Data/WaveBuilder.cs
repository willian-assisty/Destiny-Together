using System.Collections.Generic;
using DestinyTogether.Core;
using DestinyTogether.Sim;

namespace DestinyTogether.Data
{
    /// <summary>
    /// A curva de dificuldade das cinco noites, em codigo e deterministica.
    ///
    /// A noite dura 5 minutos fixos e o corte de spawn cai 45s antes do amanhecer, entao ha
    /// ~255s de agenda para preencher. Isso e ~3x o que o modelo anterior de "Assalto" cobria, e
    /// a diferenca nao e so volume: uma noite longa precisa de FORMA, senao vira um jorro
    /// uniforme que cansa antes de acabar.
    ///
    /// A forma escolhida, e o porque de cada peca:
    /// - Seis Investidas de ~32s separadas por Respiros de ~10s. O Respiro e onde se repara,
    ///   se reagrupa e se decide se da tempo de buscar a Urna de alguem.
    /// - Cada Investida sobe de intensidade. A ultima e a Culminancia, e e a unica que traz
    ///   TODAS as direcoes ao mesmo tempo.
    /// - Cada noite introduz UM arquetipo novo. Volume nunca substitui novidade.
    /// - A partir da noite 2 entra o Rondador, que ignora predio e caca heroi. E o preco de
    ///   ainda estar na mata quando escureceu.
    /// - Da noite 3 em diante a Culminancia traz um Ninho fora do alcance das torres, forcando o
    ///   time a se dividir.
    /// - Tudo isso e revelado na Bussola de Ameaca durante o Dia, antes de escurecer.
    /// </summary>
    public static class WaveBuilder
    {
        /// <summary>
        /// Investidas por noite. 5 x (34 + 14) = 240s, dentro da janela util de ~255s.
        ///
        /// Respiro de 14s, e nao de 8s como no modelo antigo, porque agora reparar e permitido a
        /// noite: o Respiro deixou de ser so uma pausa e virou a janela em que a Pedra e gasta.
        /// Sem tempo suficiente nele, a Pedra viraria um recurso que so se usa quando ja nao
        /// importa — e a defesa so poderia degradar ao longo dos cinco minutos, nunca se manter.
        /// </summary>
        private const int SurgesPerNight = 5;
        private const float SurgeSeconds = 34f;
        private const float BreatherSeconds = 14f;

        public static TurnWaveSpec BuildTurn(int turn, int totalTurns, int playerCount = 1)
        {
            var rng = Rng.ForChannel(0x5EED, 7, turn);
            var surges = new List<SurgeSpec>(SurgesPerNight);
            playerCount = MathUtil.Clamp(playerCount, 1, 4);

            for (int s = 0; s < SurgesPerNight; s++)
            {
                bool isCulmination = s == SurgesPerNight - 1;
                surges.Add(BuildSurge(turn, s, isCulmination, totalTurns, playerCount, rng));
            }

            return new TurnWaveSpec
            {
                TurnNumber = turn,
                Surges = surges.ToArray(),
                Label = LabelFor(turn, totalTurns)
            };
        }

        private static SurgeSpec BuildSurge(int night, int surgeIndex, bool isCulmination,
                                            int totalNights, int playerCount, Rng rng)
        {
            var entries = new List<WaveEntry>(12);
            var lanes = PickLanes(night, surgeIndex, isCulmination, rng);

            // Tres fatores multiplicam, e cada um responde a uma pergunta diferente:
            //
            //   nightScale  — a noite 5 e mais dura que a 1? (+45% por noite)
            //   rampScale   — o fim da noite e mais duro que o comeco? (+12% por Investida)
            //   partySize   — quatro herois enfrentam mais que um? (sublinear, de proposito)
            //
            // O ramp e a peca nova. Sem ele, seis Investidas iguais fariam a noite inteira ter a
            // mesma temperatura, e cinco minutos de temperatura constante e monotonia — mesmo
            // sendo alta. Com ele, a Culminancia chega ~80% mais pesada que a abertura.
            float nightScale = 1f + 0.22f * (night - 1);
            float rampScale = 1f + 0.12f * surgeIndex;
            float partySize = 1f + 0.5f * (playerCount - 1);
            float scale = nightScale * rampScale * partySize;

            // Por Investida o pacote e PEQUENO, e tem de ser: seis Investidas por noite contra as
            // duas ou tres do modelo anterior. Dimensionar cada uma como se fosse um Assalto
            // inteiro triplicaria a horda concorrente — foi exatamente o que a primeira medicao
            // mostrou (524 vivos no pico, com a cidade caindo na noite 2 com quatro jogadores).

            for (int i = 0; i < lanes.Count; i++)
            {
                var lane = lanes[i];
                float stagger = i * 1.2f;

                // AnyDirection na Culminancia: a ultima Investida deixa de vir por setores e vira
                // cerco. E a diferenca entre "defenda o Norte" e "estamos cercados".
                bool encircle = isCulmination;

                int swarm = (int)(1.5f * scale) + (isCulmination ? 2 : 0);
                entries.Add(new WaveEntry
                {
                    Monster = DefaultContent.Def(DefaultContent.Enxame),
                    Count = swarm,
                    Lane = lane,
                    DelaySeconds = 0.5f + stagger,
                    AnyDirection = encircle
                });

                if (surgeIndex >= 2)
                {
                    entries.Add(new WaveEntry
                    {
                        Monster = DefaultContent.Def(DefaultContent.Estourador),
                        Count = MathUtil.Clamp((int)(0.3f * scale), 1, 3),
                        Lane = lane,
                        DelaySeconds = 8f + stagger,
                        AnyDirection = encircle
                    });
                }

                // O cacador. Entra cedo na Investida de proposito: quem ficou na mata precisa
                // descobrir isso enquanto ainda da tempo de correr.
                // O cacador entra cedo na Investida de proposito: quem ficou na mata precisa
                // descobrir isso enquanto ainda da tempo de correr. Sai em UMA Faixa a cada duas
                // — ele nao e volume, e um recado.
                if (night >= 2 && i % 2 == 0)
                {
                    entries.Add(new WaveEntry
                    {
                        Monster = DefaultContent.Def(DefaultContent.Rondador),
                        Count = MathUtil.Clamp((int)(0.22f * scale), 1, 3),
                        Lane = lane,
                        DelaySeconds = 3f + stagger,
                        AnyDirection = true
                    });
                }

                if (night >= 2 && surgeIndex >= 3)
                {
                    entries.Add(new WaveEntry
                    {
                        Monster = DefaultContent.Def(DefaultContent.Bruto),
                        Count = MathUtil.Clamp((int)(0.25f * scale), 1, 3),
                        Lane = lane,
                        DelaySeconds = 14f + stagger,
                        AnyDirection = encircle
                    });
                }

                if (night >= 3 && i % 2 == 1)
                {
                    entries.Add(new WaveEntry
                    {
                        Monster = DefaultContent.Def(DefaultContent.Cuspidor),
                        Count = MathUtil.Clamp((int)(0.3f * scale), 1, 3),
                        Lane = lane,
                        DelaySeconds = 19f + stagger,
                        AnyDirection = encircle
                    });
                }
            }

            // O Ninho: nasce fora do alcance de qualquer torre. So mao humana mata.
            if (isCulmination && night >= 3)
            {
                entries.Add(new WaveEntry
                {
                    Monster = DefaultContent.Def(DefaultContent.Ninho),
                    Count = night >= 5 ? 2 : 1,
                    Lane = lanes[rng.Range(0, lanes.Count)],
                    DelaySeconds = 4f,
                    SpawnOutsideTowerRange = true
                });
            }

            // Kaiju na Culminancia da ultima noite.
            if (night >= totalNights && isCulmination)
            {
                entries.Add(new WaveEntry
                {
                    Monster = DefaultContent.Def(DefaultContent.MaeAranha),
                    Count = 1,
                    Lane = lanes[0],
                    DelaySeconds = 6f
                });
            }

            return new SurgeSpec
            {
                Entries = entries.ToArray(),
                DurationSeconds = isCulmination ? SurgeSeconds + 6f : SurgeSeconds,
                BreatherSeconds = BreatherSeconds
            };
        }

        /// <summary>
        /// Quantas e quais Faixas.
        ///
        /// A regra de justica do diretor continua: nenhum Quadrante ativo pode ficar sem trabalho.
        /// O que mudou e o piso — no modelo antigo uma Investida podia vir por UMA Faixa so, o que
        /// deixava tres jogadores assistindo. Numa noite de cinco minutos isso e insuportavel,
        /// entao o minimo agora e tres, e a Culminancia usa as oito.
        /// </summary>
        private static List<Lane> PickLanes(int night, int surgeIndex, bool isCulmination, Rng rng)
        {
            var all = new List<Lane>(8);
            for (int i = 0; i < LaneGeometry.LaneCount; i++) all.Add((Lane)i);

            // O cerco total e um CLIMAX, nao o padrao. Se a noite 1 ja viesse pelos oito lados,
            // nao sobraria escalada nenhuma para as noites seguintes — e o jogador aprenderia na
            // primeira noite que posicao nao importa, porque nao ha lado seguro.
            int count = isCulmination
                ? MathUtil.Clamp(3 + night, 4, 8)
                : MathUtil.Clamp(2 + night / 2 + surgeIndex / 2, 2, 6);

            if (count >= 8) return all;

            // Espaca as Faixas em vez de sortear vizinhas: quatro Faixas coladas seriam
            // um unico problema com quatro nomes.
            var picked = new List<Lane>(count);
            int start = rng.Range(0, LaneGeometry.LaneCount);
            int step = MathUtil.Clamp(LaneGeometry.LaneCount / count, 1, 4);

            for (int i = 0; i < count; i++)
                picked.Add((Lane)((start + i * step) % LaneGeometry.LaneCount));

            return picked;
        }

        private static string LabelFor(int night, int totalNights)
        {
            if (night >= totalNights) return "Kaiju";
            if (night >= 4) return "Cerco";
            if (night >= 3) return "Ninho e Cuspidores";
            if (night >= 2) return "Rondadores";
            return "Primeira noite";
        }
    }
}
