using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// Torres adquirem alvo e atiram SOZINHAS, drenando madeira do Silo compartilhado.
    /// O jogador nunca mira. Silo vazio derruba todas para 50% de cadencia — e por isso que
    /// Logistica (reabastecer durante o combate) e um emprego de verdade e nao tarefa de sobra.
    /// </summary>
    public static class TowerSystem
    {
        public static void Tick(MatchState state, IContentDatabase content, float dt, SimEventLog log)
        {
            bool siloEmpty = state.SiloWood <= 0f;
            float cadenceScale = siloEmpty ? 0.5f : 1f;

            for (int i = 0; i < state.Towers.Count; i++)
            {
                var tower = state.Towers[i];
                if (!tower.IsAlive) continue;

                var spec = content.GetTower(tower.Def);
                if (spec == null || !spec.CanAttack) continue;

                tower.ShotCooldown -= dt * tower.FireRateMultiplier * cadenceScale;
                if (tower.ShotCooldown > 0f) continue;

                float range = spec.Range + tower.BonusRange;
                var target = FindTarget(state, tower, spec, range);
                if (target == null)
                {
                    tower.ShotCooldown = 0f;
                    continue;
                }

                Fire(state, content, tower, spec, target, log);
                tower.ShotCooldown = spec.ShotInterval / TierFireRate(tower.Tier);
            }
        }

        private static float TierFireRate(int tier) => 1f + 0.35f * (tier - 1);

        public static MonsterState FindTarget(MatchState state, TowerState tower, TowerSpec spec, float range)
        {
            float sqrRange = range * range;
            MonsterState best = null;
            float bestScore = float.MaxValue;
            Vec2 origin = tower.Position;
            Vec2 cityCenter = state.CityCenter;

            for (int i = 0; i < state.Monsters.Count; i++)
            {
                var m = state.Monsters[i];
                if (!m.IsAlive) continue;

                float sqr = Vec2.SqrDistance(m.Position, origin);
                if (sqr > sqrRange) continue;

                float score;
                switch (spec.Targeting)
                {
                    case TargetingRule.MaisProximo:
                        score = sqr;
                        break;
                    case TargetingRule.MaisForte:
                        score = -m.Health;
                        break;
                    case TargetingRule.MaisFraco:
                        score = m.Health;
                        break;
                    default: // MaisAvancado: quem esta mais perto da cidade morre primeiro
                        score = Vec2.SqrDistance(m.Position, cityCenter);
                        break;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = m;
                }
            }

            return best;
        }

        private static void Fire(MatchState state, IContentDatabase content, TowerState tower,
                                 TowerSpec spec, MonsterState target, SimEventLog log)
        {
            // Consumo de madeira escala com o tier: torre forte queima mais municao.
            float woodCost = spec.WoodPerShot * tower.Tier;
            if (woodCost > 0f)
            {
                if (state.SiloWood <= 0f)
                {
                    log.Emit(SimEventType.SiloEmpty, tower.Id, 0f, tower.Position);
                }
                else
                {
                    state.SiloWood -= woodCost;
                    if (state.SiloWood < 0f) state.SiloWood = 0f;
                }
            }

            log.Emit(SimEventType.TowerFired, tower.Id, 0f, tower.Position, tower.Def,
                     cell: tower.Cell, other: target.Id);

            float damage = spec.Damage * TierDamage(tower.Tier);
            var targetSpec = content.GetMonster(target.Def);

            if (damage > 0f)
            {
                // Distrito Ferro: ignora metade da armadura — a resposta desenhada para o Bruto.
                bool ignoreArmor = tower.InDistrict && tower.DistrictTag == BuildingTag.Ferro;
                Combat.DamageMonster(state, content, target, damage, log, ignoreArmor);
            }

            if (spec.SlowFactor > 0f)
                Combat.ApplySlow(target, targetSpec, spec.SlowFactor, spec.SlowDuration);

            // Distrito Gelo: quem atravessa sai lento por 4s, mesmo que a torre nao seja de gelo.
            if (tower.InDistrict && tower.DistrictTag == BuildingTag.Gelo)
                Combat.ApplySlow(target, targetSpec, 0.4f, 4f);

            // Distrito Igneo: Queimadura que stacka — regra nova, nao "+X% de dano".
            if (tower.InDistrict && tower.DistrictTag == BuildingTag.Igneo)
                Combat.ApplyBurn(target, damage * 0.25f, 3f);

            if (spec.SplashRadius > 0f)
                SplashAround(state, content, target.Position, spec.SplashRadius, damage * 0.6f, target.Id, log);

            if (spec.Knockback > 0f && target.IsAlive)
            {
                var away = (target.Position - tower.Position).Normalized;
                target.Position += away * spec.Knockback;
            }
        }

        private static float TierDamage(int tier) => 1f + 0.8f * (tier - 1);

        private static void SplashAround(MatchState state, IContentDatabase content, Vec2 center,
                                         float radius, float damage, EntityId exclude, SimEventLog log)
        {
            float sqr = radius * radius;
            for (int i = state.Monsters.Count - 1; i >= 0; i--)
            {
                var m = state.Monsters[i];
                if (!m.IsAlive || m.Id == exclude) continue;
                if (Vec2.SqrDistance(m.Position, center) <= sqr)
                    Combat.DamageMonster(state, content, m, damage, log);
            }
        }
    }
}
