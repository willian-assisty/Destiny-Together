using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>Aplicacao de dano centralizada. Toda perda de HP do jogo passa por aqui.</summary>
    public static class Combat
    {
        public static void DamageMonster(MatchState state, IContentDatabase content, MonsterState m,
                                         float amount, SimEventLog log, bool ignoreArmor = false)
        {
            if (m == null || !m.IsAlive || amount <= 0f) return;

            var spec = content.GetMonster(m.Def);
            float armor = ignoreArmor ? 0f : (spec?.Armor ?? 0f);
            float applied = amount > armor ? amount - armor : amount * 0.1f; // armadura nunca zera o dano

            m.Health -= applied;
            log.Emit(SimEventType.MonsterDamaged, m.Id, applied, m.Position);

            if (m.Health <= 0f)
                KillMonster(state, content, m, log);
        }

        public static void KillMonster(MatchState state, IContentDatabase content, MonsterState m, SimEventLog log)
        {
            if (m == null) return;
            m.Health = 0f;
            var spec = content.GetMonster(m.Def);

            log.Emit(SimEventType.MonsterDied, m.Id, 0f, m.Position, m.Def);

            if (spec != null)
            {
                if (spec.ExplodesOnDeath && spec.ExplosionRadius > 0f)
                    Explode(state, content, m.Position, spec.ExplosionRadius, spec.ExplosionDamage, log, m.Id);

                state.Xp += spec.XpReward;
                if (spec.GoldReward > 0f)
                    log.Emit(SimEventType.ResourceGained, m.Id, spec.GoldReward, m.Position,
                             intValue: (int)ResourceKind.Ouro);
            }
        }

        /// <summary>
        /// Explosao do Estourador: atinge PREDIOS (e outros monstros), nao mata o heroi de imediato.
        /// Matar o Estourador dentro do pacote e o contra-jogo — a explosao limpa a horda.
        /// </summary>
        public static void Explode(MatchState state, IContentDatabase content, Vec2 center, float radius,
                                   float damage, SimEventLog log, EntityId source)
        {
            log.Emit(SimEventType.MonsterExploded, source, radius, center);

            float sqr = radius * radius;

            for (int i = state.Monsters.Count - 1; i >= 0; i--)
            {
                var other = state.Monsters[i];
                if (!other.IsAlive || other.Id == source) continue;
                if (Vec2.SqrDistance(other.Position, center) <= sqr)
                    DamageMonster(state, content, other, damage * 0.5f, log);
            }

            for (int i = state.Towers.Count - 1; i >= 0; i--)
            {
                var t = state.Towers[i];
                if (!t.IsAlive) continue;
                if (Vec2.SqrDistance(t.Position, center) <= sqr)
                    DamageTower(state, t, damage, log);
            }

            if (Vec2.SqrDistance(state.CityCenter, center) <= sqr + 2f)
                DamageCity(state, damage * 0.5f, log);
        }

        public static void DamageTower(MatchState state, TowerState t, float amount, SimEventLog log)
        {
            if (t == null || !t.IsAlive || amount <= 0f) return;
            t.Health -= amount;
            log.Emit(SimEventType.TowerDamaged, t.Id, amount, t.Position, t.Def, cell: t.Cell);

            if (t.Health <= 0f)
            {
                t.Health = 0f;
                // Distrito Pedra: o predio nao vira Escombro, vira Ruina reparavel — regra, nao numero.
                bool leavesRubble = t.DistrictTag != BuildingTag.Pedra;
                state.Grid.Set(t.Cell, leavesRubble ? CellState.Escombro : CellState.Vazio, EntityId.None);
                state.RemoveTower(t);
                log.Emit(SimEventType.TowerDestroyed, t.Id, 0f, t.Position, t.Def, cell: t.Cell,
                         intValue: leavesRubble ? 1 : 0);
            }
        }

        /// <summary>
        /// Dano na Prefeitura — a UNICA barra de vida da partida.
        /// Teto por golpe: nada tira mais que MaxSingleHitFraction do maximo. Sem one-shot barato.
        /// </summary>
        public static void DamageCity(MatchState state, float amount, SimEventLog log)
        {
            if (amount <= 0f || state.Outcome != MatchOutcome.EmAndamento) return;

            float cap = state.TownHallMaxHealth * state.MaxSingleHitFraction;
            float applied = amount > cap ? cap : amount;

            state.TownHallHealth -= applied;
            log.Emit(SimEventType.CityDamaged, EntityId.None, applied, state.CityCenter);

            if (state.TownHallHealth <= 0f)
            {
                state.TownHallHealth = 0f;
                state.Outcome = MatchOutcome.Derrota;
                log.Emit(SimEventType.MatchEnded, intValue: (int)MatchOutcome.Derrota);
            }
        }

        public static void ApplySlow(MonsterState m, MonsterSpec spec, float factor, float duration)
        {
            if (m == null || factor <= 0f) return;
            float resisted = factor * (1f - (spec?.SlowResistance ?? 0f));
            float multiplier = MathUtil.Clamp(1f - resisted, 0.15f, 1f);
            if (multiplier < m.SpeedMultiplier || m.SlowRemaining <= 0f)
                m.SpeedMultiplier = multiplier;
            if (duration > m.SlowRemaining) m.SlowRemaining = duration;
        }

        public static void ApplyBurn(MonsterState m, float dps, float duration)
        {
            if (m == null || dps <= 0f) return;
            m.BurnDps += dps;
            if (duration > m.BurnRemaining) m.BurnRemaining = duration;
        }
    }
}
