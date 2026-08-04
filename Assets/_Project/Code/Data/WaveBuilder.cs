using System.Collections.Generic;
using DestinyTogether.Core;
using DestinyTogether.Sim;

namespace DestinyTogether.Data
{
    /// <summary>
    /// A curva de dificuldade dos 9 turnos, em codigo e deterministica.
    ///
    /// Regras que sustentam a curva:
    /// - Cada Ato introduz UM arquetipo novo. Volume nunca substitui novidade.
    /// - A ultima Investida do turno e a Culminancia: mais Faixas simultaneas que qualquer outra.
    /// - A partir do Ato 2 a Culminancia traz um Ninho FORA do alcance das torres, forcando o
    ///   time a se dividir 2/2 — a pressao e agendada no Preparo, nunca emergente.
    /// - Tudo isso e revelado na Bussola de Ameaca antes do Assalto comecar.
    /// </summary>
    public static class WaveBuilder
    {
        public static TurnWaveSpec BuildTurn(int turn, int totalTurns, int playerCount = 1)
        {
            var rng = Rng.ForChannel(0x5EED, 7, turn);
            int surgeCount = turn <= 3 ? 2 : 3;
            var surges = new List<SurgeSpec>(surgeCount);
            playerCount = MathUtil.Clamp(playerCount, 1, 4);

            for (int s = 0; s < surgeCount; s++)
            {
                bool isCulmination = s == surgeCount - 1;
                surges.Add(BuildSurge(turn, s, isCulmination, totalTurns, playerCount, rng));
            }

            return new TurnWaveSpec
            {
                TurnNumber = turn,
                Surges = surges.ToArray(),
                Label = LabelFor(turn, totalTurns)
            };
        }

        private static SurgeSpec BuildSurge(int turn, int surgeIndex, bool isCulmination,
                                            int totalTurns, int playerCount, Rng rng)
        {
            var entries = new List<WaveEntry>(8);
            var lanes = PickLanes(turn, surgeIndex, isCulmination, rng);

            // Escala suave: ~18% de volume a mais por turno. Numeros baixos de proposito —
            // e mais facil subir depois do primeiro playtest do que descobrir que ninguem le a tela.
            //
            // O fator de jogadores e SUBLINEAR (nao 4x para 4 pessoas): quatro herois cobrem mais
            // area do que um sozinho, entao dobrar a horda a cada jogador puniria jogar junto.
            // 1p = 1.0 · 2p = 1.5 · 3p = 2.0 · 4p = 2.5
            float partySize = 1f + 0.5f * (playerCount - 1);
            float scale = (1f + 0.18f * (turn - 1)) * partySize;

            for (int i = 0; i < lanes.Count; i++)
            {
                var lane = lanes[i];
                float stagger = i * 1.5f;

                int swarm = (int)(4 * scale) + (isCulmination ? 3 : 0);
                entries.Add(new WaveEntry
                {
                    Monster = DefaultContent.Def(DefaultContent.Enxame),
                    Count = swarm,
                    Lane = lane,
                    DelaySeconds = 0.5f + stagger
                });

                if (turn >= 2)
                {
                    int bombers = turn >= 5 ? 2 : 1;
                    entries.Add(new WaveEntry
                    {
                        Monster = DefaultContent.Def(DefaultContent.Estourador),
                        Count = bombers,
                        Lane = lane,
                        DelaySeconds = 6f + stagger
                    });
                }

                if (turn >= 4 && (isCulmination || i == 0))
                {
                    entries.Add(new WaveEntry
                    {
                        Monster = DefaultContent.Def(DefaultContent.Bruto),
                        Count = turn >= 7 ? 2 : 1,
                        Lane = lane,
                        DelaySeconds = 10f + stagger
                    });
                }

                if (turn >= 5 && i % 2 == 1)
                {
                    entries.Add(new WaveEntry
                    {
                        Monster = DefaultContent.Def(DefaultContent.Cuspidor),
                        Count = turn >= 8 ? 3 : 2,
                        Lane = lane,
                        DelaySeconds = 13f + stagger
                    });
                }
            }

            // O Ninho: nasce fora do alcance de qualquer torre. So mao humana mata.
            if (isCulmination && turn >= 6)
            {
                entries.Add(new WaveEntry
                {
                    Monster = DefaultContent.Def(DefaultContent.Ninho),
                    Count = turn >= 8 ? 2 : 1,
                    Lane = lanes[rng.Range(0, lanes.Count)],
                    DelaySeconds = 4f,
                    SpawnOutsideTowerRange = true
                });
            }

            // Kaiju no turno final.
            if (turn >= totalTurns && isCulmination)
            {
                entries.Add(new WaveEntry
                {
                    Monster = DefaultContent.Def(DefaultContent.MaeAranha),
                    Count = 1,
                    Lane = lanes[0],
                    DelaySeconds = 2f
                });
            }

            return new SurgeSpec
            {
                Entries = entries.ToArray(),
                DurationSeconds = isCulmination ? 30f : 25f,
                BreatherSeconds = 8f
            };
        }

        /// <summary>
        /// Quantas e quais Faixas. A regra de justica do diretor: nenhum Quadrante ativo pode
        /// ficar sem trabalho, entao a Culminancia sempre distribui entre os quatro lados.
        /// </summary>
        private static List<Lane> PickLanes(int turn, int surgeIndex, bool isCulmination, Rng rng)
        {
            int count;
            if (isCulmination) count = MathUtil.Clamp(2 + turn / 2, 2, 8);
            else count = MathUtil.Clamp(1 + turn / 3, 1, 4);

            var all = new List<Lane>(8);
            for (int i = 0; i < LaneGeometry.LaneCount; i++) all.Add((Lane)i);

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

        private static string LabelFor(int turn, int totalTurns)
        {
            if (turn >= totalTurns) return "Kaiju";
            if (turn >= 6) return "Ninho";
            if (turn >= 5) return "Cuspidores";
            if (turn >= 4) return "Brutos";
            if (turn >= 2) return "Estouradores";
            return "Primeiro contato";
        }
    }
}
