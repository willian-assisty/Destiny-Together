using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// Monstros caminham ate o tile de cidade mais proximo e atacam o PREDIO mais proximo,
    /// nunca a Prefeitura diretamente. E isso que transforma cada predio colocado para fora
    /// em divida: encurta o corredor daquela Faixa e vira o proximo alvo.
    ///
    /// Sem NavMesh e sem A*: a cidade e um bloco convexo no centro de um campo aberto, entao
    /// direcao direta ao alvo estrutural mais proximo e a resposta correta — e escala para
    /// centenas de unidades sem custo de pathfinding.
    /// </summary>
    public static class MonsterSystem
    {
        private const float RetargetInterval = 0.5f;

        public static void Tick(MatchState state, IContentDatabase content, float dt, SimEventLog log)
        {
            for (int i = state.Monsters.Count - 1; i >= 0; i--)
            {
                var m = state.Monsters[i];

                if (!m.IsAlive)
                {
                    state.RemoveMonster(m);
                    continue;
                }

                var spec = content.GetMonster(m.Def);
                if (spec == null) continue;

                TickStatusEffects(state, content, m, dt, log);
                if (!m.IsAlive) { state.RemoveMonster(m); continue; }

                if (spec.IsStationary)
                {
                    TickNest(state, content, m, spec, dt, log);
                    continue;
                }

                Retarget(state, m, dt);

                // Heroi no alcance vira alvo preferencial. Para o Cuspidor (alcance 8) isso e o
                // sistema anti-camping: quem tenta segurar a Linha parado leva dano e precisa sair.
                //
                // O Rondador nao espera o heroi entrar no alcance: ele caca de qualquer distancia.
                // Sem isso, sair para explorar de madrugada seria apenas demorado — a punicao
                // viria do relogio, e relogio nao assusta ninguem.
                var hero = spec.HuntsHeroes
                    ? FindNearestHero(state, m.Position)
                    : FindHeroInRange(state, m.Position, spec.AttackRange);

                // Sem heroi vivo no mapa o cacador volta a ser um monstro comum e vai na cidade,
                // senao ele congelaria enquanto os quatro estivessem em Espectro.
                Vec2 targetPos = hero != null ? hero.Position : ResolveTargetPosition(state, m);
                float distance = Vec2.Distance(m.Position, targetPos);

                if (distance > spec.AttackRange)
                {
                    var dir = (targetPos - m.Position).Normalized;
                    m.Facing = dir;
                    m.Position += dir * (spec.Speed * m.SpeedMultiplier * dt);
                }
                else
                {
                    m.AttackCooldown -= dt;
                    if (m.AttackCooldown <= 0f)
                    {
                        m.AttackCooldown = spec.AttackInterval;
                        if (hero != null)
                            StrikeHero(state, content, m, spec, hero, log);
                        else
                            StrikeTarget(state, content, m, spec, log);
                    }
                }
            }
        }

        private static void TickStatusEffects(MatchState state, IContentDatabase content,
                                              MonsterState m, float dt, SimEventLog log)
        {
            if (m.SlowRemaining > 0f)
            {
                m.SlowRemaining -= dt;
                if (m.SlowRemaining <= 0f) m.SpeedMultiplier = 1f;
            }

            if (m.BurnRemaining > 0f)
            {
                m.BurnRemaining -= dt;
                Combat.DamageMonster(state, content, m, m.BurnDps * dt, log, ignoreArmor: true);
                if (m.BurnRemaining <= 0f) m.BurnDps = 0f;
            }
        }

        private static void TickNest(MatchState state, IContentDatabase content, MonsterState nest,
                                     MonsterSpec spec, float dt, SimEventLog log)
        {
            if (spec.SpawnInterval <= 0f || spec.SpawnCount <= 0 || !spec.SpawnsMonster.IsValid) return;

            nest.SpawnCooldown -= dt;
            if (nest.SpawnCooldown > 0f) return;
            nest.SpawnCooldown = spec.SpawnInterval;

            var childSpec = content.GetMonster(spec.SpawnsMonster);
            if (childSpec == null) return;

            for (int i = 0; i < spec.SpawnCount; i++)
            {
                float angle = 360f / spec.SpawnCount * i;
                var offset = Vec2.FromCompassDegrees(angle) * 0.8f;
                SpawnSystem.SpawnMonster(state, childSpec, nest.Position + offset, nest.Lane, log);
            }
        }

        private static void Retarget(MatchState state, MonsterState m, float dt)
        {
            m.RetargetCooldown -= dt;
            if (m.RetargetCooldown > 0f && m.TargetCell.IsValid && state.Grid.IsCityTile(m.TargetCell))
                return;

            m.RetargetCooldown = RetargetInterval;

            if (state.Grid.TryGetNearestCityTile(m.Position, out var cell, out _))
            {
                m.TargetCell = cell;
                m.TargetStructure = state.Grid.OccupantOf(cell);
            }
            else
            {
                m.TargetCell = GridCoord.Invalid;
                m.TargetStructure = EntityId.None;
            }
        }

        private static Vec2 ResolveTargetPosition(MatchState state, MonsterState m)
            => m.TargetCell.IsValid ? m.TargetCell.Center : state.CityCenter;

        /// <summary>Heroi ativo mais proximo, sem limite de distancia. Usado pelo Rondador.</summary>
        private static HeroState FindNearestHero(MatchState state, Vec2 from)
            => FindHeroInRange(state, from, float.MaxValue);

        private static HeroState FindHeroInRange(MatchState state, Vec2 from, float range)
        {
            HeroState best = null;
            float bestSqr = range * range;
            for (int i = 0; i < state.Heroes.Count; i++)
            {
                var h = state.Heroes[i];
                if (!h.IsActive) continue;
                float sqr = Vec2.SqrDistance(h.Position, from);
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = h;
                }
            }
            return best;
        }

        private static void StrikeHero(MatchState state, IContentDatabase content, MonsterState m,
                                       MonsterSpec spec, HeroState hero, SimEventLog log)
        {
            log.Emit(SimEventType.MonsterAttacked, m.Id, spec.ContactDamage, m.Position, other: hero.Id);

            if (spec.ExplodesOnDeath && spec.ExplosionRadius > 0f)
            {
                Combat.KillMonster(state, content, m, log);
                HeroSystem.DamageHero(state, content, hero, spec.ExplosionDamage * 0.5f, log);
                return;
            }

            HeroSystem.DamageHero(state, content, hero, spec.ContactDamage, log);
        }

        private static void StrikeTarget(MatchState state, IContentDatabase content, MonsterState m,
                                         MonsterSpec spec, SimEventLog log)
        {
            log.Emit(SimEventType.MonsterAttacked, m.Id, spec.ContactDamage, m.Position);

            // Estourador detona ao encostar: o dano vem da explosao, nao do contato.
            if (spec.ExplodesOnDeath && spec.ExplosionRadius > 0f)
            {
                Combat.KillMonster(state, content, m, log);
                return;
            }

            var tower = state.GetTower(state.Grid.OccupantOf(m.TargetCell));
            if (tower != null)
            {
                Combat.DamageTower(state, tower, spec.ContactDamage, log);
                return;
            }

            if (state.Grid.Get(m.TargetCell) == CellState.Prefeitura)
                Combat.DamageCity(state, spec.ContactDamage, log);
        }
    }
}
