using System;
using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>Monta o estado inicial da partida. Toda partida comeca identica dada a mesma seed.</summary>
    public static class MatchFactory
    {
        /// <summary>
        /// Cartas na mao no turno 1. Calibrado por simulacao: com 2 cartas o time nao tem DPS
        /// para a primeira Investida e a Prefeitura cai antes de alguem entender o jogo.
        /// Quatro cartas dao ao Preparo inicial uma decisao de LAYOUT, que e o assunto do jogo.
        /// </summary>
        private const int StartingHandSize = 7;

        /// <param name="chosenHeroes">
        /// Heroi de cada assento, na ordem. Entradas invalidas (ou lista curta) caem para o
        /// proximo do pool — duplicatas sao permitidas de proposito: o time perde eficiencia,
        /// nunca viabilidade.
        /// </param>
        public static MatchState Create(IContentDatabase content, int seed, int playerCount,
                                        DefId[] chosenHeroes = null)
        {
            var rules = content.Rules;
            var arena = content.Arena;
            playerCount = MathUtil.Clamp(playerCount, 1, rules.MaxPlayers);

            var state = new MatchState
            {
                MatchSeed = seed,
                TurnNumber = 1,
                Grid = new BoardGrid(arena.GridSize, arena.TownHallSize),
                TownHallMaxHealth = rules.TownHallMaxHealth,
                TownHallHealth = rules.TownHallMaxHealth,
                MaxSingleHitFraction = rules.MaxSingleHitFraction,
                SiloCapacity = rules.SiloBaseCapacity,
                SiloWood = rules.SiloStartingWood,
                Stone = rules.StartingStone,
                CityLevel = 1
            };

            state.XpToNextLevel = EconomySystem.XpRequiredFor(content, 1, playerCount);

            var handRng = Rng.ForChannel(seed, 3, 0);
            var heroPool = content.HeroPool;

            for (int i = 0; i < playerCount; i++)
            {
                var player = new PlayerState
                {
                    Id = new PlayerId(i),
                    DisplayName = $"Jogador {i + 1}",
                    Quadrant = (Quadrant)(i % 4),
                    Gold = rules.StartingGoldPerPlayer
                };

                // Reparte os 4 quadrantes entre quem esta jogando: 1p leva os 4, 2p levam 2 cada,
                // 3p ficam com 2/1/1, 4p com 1 cada.
                for (int q = 0; q < 4; q++)
                    if (q % playerCount == i)
                        player.GrantQuadrant((Quadrant)q);
                player.GrantQuadrant(player.Quadrant);

                DefId heroDef = DefId.None;
                if (chosenHeroes != null && i < chosenHeroes.Length && content.GetHero(chosenHeroes[i]) != null)
                    heroDef = chosenHeroes[i];
                else if (heroPool != null && heroPool.Count > 0)
                    heroDef = heroPool[i % heroPool.Count];

                var heroSpec = content.GetHero(heroDef);

                var hero = new HeroState
                {
                    Id = state.NewEntityId(),
                    Def = heroDef,
                    Owner = player.Id,
                    Position = state.Grid.Center,
                    MaxHealth = heroSpec?.MaxHealth ?? 100f,
                    Health = heroSpec?.MaxHealth ?? 100f
                };

                player.Hero = hero.Id;
                player.Class = heroSpec?.Class ?? HeroClass.Guarda;

                if (content.TowerPool != null && content.TowerPool.Count > 0)
                    for (int c = 0; c < StartingHandSize; c++)
                        player.Hand.Add(content.TowerPool[handRng.Range(0, content.TowerPool.Count)]);

                state.Players.Add(player);
                state.RegisterHero(hero);
            }

            var nodeRng = Rng.ForChannel(seed, 4, 0);
            PopulateHarvestNodes(state, content, nodeRng, null);

            return state;
        }

        /// <summary>
        /// Cota FIXA por turno. Ficar 5 minutos no Preparo nao rende 1 de madeira a mais que ficar
        /// 60 segundos — e por isso que a fase pode nao ter timer duro sem virar exploravel.
        /// </summary>
        public static void RepopulateHarvestNodes(MatchState state, IContentDatabase content,
                                                  Rng rng, SimEventLog log)
        {
            for (int i = state.Nodes.Count - 1; i >= 0; i--)
                state.RemoveNode(state.Nodes[i]);

            PopulateHarvestNodes(state, content, rng, log);
        }

        private static void PopulateHarvestNodes(MatchState state, IContentDatabase content,
                                                 Rng rng, SimEventLog log)
        {
            var arena = content.Arena;

            // Distância do centro até o centro de cada bolsão de recursos: entre a borda da
            // cidade e o anel de spawn, mais perto da cidade — colher não pode significar estar
            // no lugar onde os monstros nascem.
            float cityEdge = state.Grid.Size * 0.5f;
            float corner = MathUtil.Lerp(cityEdge + 3f, arena.OutskirtsRadius, 0.45f);

            Add(state, rng, HarvestNodeKind.Arvore, arena.TreeCount, arena.WoodPerTree, corner, arena, log);
            Add(state, rng, HarvestNodeKind.Rocha, arena.RockCount, arena.StonePerRock, corner, arena, log);
            Add(state, rng, HarvestNodeKind.Bau, arena.ChestCount, arena.GoldPerChest, corner, arena, log);
        }

        /// <summary>
        /// Distribui os nós pelos QUATRO CANTOS (NE, SE, SO, NO), em rodízio, com dispersão
        /// dentro de cada canto.
        ///
        /// O rodízio importa: se cada tipo caísse todo num canto só, madeira e pedra teriam donos
        /// fixos e o time inteiro brigaria por um ponto. Alternando, cada canto tem um pouco de
        /// tudo, e escolher qual canto visitar vira uma decisão de rota — não de recurso.
        /// </summary>
        private static void Add(MatchState state, Rng rng, HarvestNodeKind kind, int count, float amount,
                                float cornerDistance, ArenaSpec arena, SimEventLog log)
        {
            // Diagonais: 45, 135, 225, 315 graus a partir do norte.
            for (int i = 0; i < count; i++)
            {
                float cornerAngle = 45f + 90f * (i % 4);
                var cornerCenter = state.Grid.Center + Vec2.FromCompassDegrees(cornerAngle) * cornerDistance;

                float spread = arena.CornerSpread;
                var offset = new Vec2(rng.Range(-spread, spread), rng.Range(-spread, spread));

                var node = new HarvestNodeState
                {
                    Id = state.NewEntityId(),
                    Kind = kind,
                    Position = cornerCenter + offset,
                    Remaining = amount,
                    TotalPerHarvest = amount
                };
                state.RegisterNode(node);
                log?.Emit(SimEventType.NodeHarvested, node.Id, 0f, node.Position, intValue: (int)kind);
            }
        }
    }
}
