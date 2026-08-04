using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// Executa as Investidas do Assalto. A composicao inteira ja foi revelada ao time durante
    /// o Preparo pela Bussola de Ameaca — nada aqui e surpresa. Telegrafia total e uma decisao
    /// de design: a tensao vem de "sabemos o que vem e nao sabemos se aguentamos".
    /// </summary>
    public static class SpawnSystem
    {
        public static void TickSurge(MatchState state, IContentDatabase content, SurgeSpec surge,
                                     float previousElapsed, float currentElapsed, Rng rng, SimEventLog log)
        {
            if (surge == null) return;

            for (int i = 0; i < surge.Entries.Length; i++)
            {
                ref readonly var entry = ref surge.Entries[i];
                // Dispara exatamente uma vez, no tick em que o relogio cruza o atraso da entrada.
                if (entry.DelaySeconds < previousElapsed || entry.DelaySeconds >= currentElapsed) continue;

                var spec = content.GetMonster(entry.Monster);
                if (spec == null) continue;

                for (int n = 0; n < entry.Count; n++)
                {
                    Vec2 pos;
                    Lane lane = entry.Lane;

                    if (entry.SpawnOutsideTowerRange)
                    {
                        pos = OutOfReachPoint(state, entry.Lane, rng);
                    }
                    else if (entry.AnyDirection)
                    {
                        // Cerco: sorteia o angulo primeiro e DERIVA a Faixa dele, para que o
                        // monstro continue pertencendo ao setor onde de fato apareceu.
                        pos = LaneGeometry.RingSpawnPoint(state.CityCenter, content.Arena.OutskirtsRadius, rng);
                        lane = LaneGeometry.LaneOf(state.CityCenter, pos);
                    }
                    else
                    {
                        pos = LaneGeometry.SpawnPoint(state.CityCenter, content.Arena.OutskirtsRadius,
                                                      entry.Lane, rng);
                    }

                    // Dispersao para o pacote nao nascer todo em cima de si mesmo.
                    pos += new Vec2(rng.Range(-0.9f, 0.9f), rng.Range(-0.9f, 0.9f));
                    SpawnMonster(state, spec, pos, lane, log);
                }
            }
        }

        /// <summary>
        /// Ponto propositalmente fora do alcance de qualquer torre: e assim que o Ninho forca
        /// o time a se dividir 2/2 em vez de todo mundo empilhar na mesma Faixa.
        /// </summary>
        private static Vec2 OutOfReachPoint(MatchState state, Lane lane, Rng rng)
        {
            float safeRadius = state.Grid.Size * 0.5f + 6f;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float spread = rng.Range(-25f, 25f);
                var candidate = state.CityCenter +
                                Vec2.FromCompassDegrees(LaneGeometry.DegreesOf(lane) + spread) * safeRadius;
                if (!IsCoveredByAnyTower(state, candidate)) return candidate;
                safeRadius += 1.5f;
            }
            return state.CityCenter + LaneGeometry.DirectionOf(lane) * safeRadius;
        }

        private static bool IsCoveredByAnyTower(MatchState state, Vec2 point)
        {
            for (int i = 0; i < state.Towers.Count; i++)
            {
                var t = state.Towers[i];
                if (!t.IsAlive) continue;
                // Usa um raio generoso: o Ninho precisa estar claramente fora, nao na borda.
                float reach = 5f + t.BonusRange;
                if (Vec2.SqrDistance(t.Position, point) <= reach * reach) return true;
            }
            return false;
        }

        public static MonsterState SpawnMonster(MatchState state, MonsterSpec spec, Vec2 position,
                                                Lane lane, SimEventLog log)
        {
            var m = new MonsterState
            {
                Id = state.NewEntityId(),
                Def = spec.Id,
                Lane = lane,
                Position = position,
                Facing = (state.CityCenter - position).Normalized,
                Health = spec.MaxHealth,
                MaxHealth = spec.MaxHealth,
                AttackCooldown = 0f,
                SpawnCooldown = spec.SpawnInterval
            };

            state.RegisterMonster(m);
            log.Emit(SimEventType.MonsterSpawned, m.Id, spec.MaxHealth, position, spec.Id,
                     intValue: (int)lane);
            return m;
        }

        /// <summary>Fronteira de fase limpa: nenhum monstro pendente atravessa para o Balanco.</summary>
        public static void DissolveAll(MatchState state, SimEventLog log)
        {
            for (int i = state.Monsters.Count - 1; i >= 0; i--)
            {
                var m = state.Monsters[i];
                log.Emit(SimEventType.MonsterDied, m.Id, 0f, m.Position, m.Def, intValue: 1);
                state.RemoveMonster(m);
            }
        }
    }
}
