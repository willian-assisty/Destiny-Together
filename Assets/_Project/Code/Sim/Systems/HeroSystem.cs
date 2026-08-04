using System;
using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// O heroi move em 8 direcoes e ataca/colhe AUTOMATICAMENTE no que estiver ao alcance.
    /// Nao existe botao de ataque, nao existe mira: o vetor de ataque segue o vetor de MOVIMENTO.
    /// Toda a expressao mecanica do jogador colapsa numa unica pergunta continua — onde eu estou?
    ///
    /// A carga tem teto e precisa ser depositada na Prefeitura. Isso e o que transforma a economia
    /// numa corrida fisica de 10-20s em vez de um contador subindo na UI.
    /// </summary>
    public static class HeroSystem
    {
        /// <summary>Distancia da Prefeitura em que o deposito acontece.</summary>
        private const float DepositRadius = 2.2f;
        /// <summary>Raio em que uma Urna e recolhida ao passar por cima.</summary>
        private const float UrnPickupRadius = 1.2f;

        public static void Tick(MatchState state, IContentDatabase content, float dt,
                                SimEventLog log, bool combatEnabled)
        {
            for (int i = 0; i < state.Heroes.Count; i++)
            {
                var hero = state.Heroes[i];
                var spec = content.GetHero(hero.Def);
                if (spec == null) continue;

                if (hero.IsSpectre)
                {
                    TickSpectre(state, hero, spec, dt, log);
                    continue;
                }

                AutomatonSystem.DriveIfNeeded(state, hero, spec);
                Move(state, hero, spec, dt);
                Harvest(state, hero, spec, dt, log);
                Deposit(state, content, hero, spec, dt, log);
                TryRecoverUrns(state, hero, log);

                if (combatEnabled)
                    AutoAttack(state, content, hero, spec, dt, log);
            }
        }

        /// <summary>
        /// Delega a <see cref="HeroMotion"/>. O corpo mora la porque o cliente vai chamar a MESMA
        /// funcao ao prever o proprio heroi — ver o comentario de HeroMotion.
        /// </summary>
        private static void Move(MatchState state, HeroState hero, HeroSpec spec, float dt)
            => HeroMotion.Step(state, hero, spec, hero.MoveInput, dt);

        /// <summary>Colheita automatica: o que estiver no raio e drenado sem input. Limpar e colher e o mesmo verbo.</summary>
        private static void Harvest(MatchState state, HeroState hero, HeroSpec spec, float dt, SimEventLog log)
        {
            if (hero.CarriedTotal >= spec.CarryCapacity) return;

            for (int i = state.Nodes.Count - 1; i >= 0; i--)
            {
                var node = state.Nodes[i];
                if (node.IsDepleted) continue;
                if (Vec2.SqrDistance(node.Position, hero.Position) > spec.AttackRadius * spec.AttackRadius) continue;

                float rate = 6f * spec.HarvestSpeedMultiplier;
                float take = Math.Min(node.Remaining, rate * dt);
                float room = spec.CarryCapacity - hero.CarriedTotal;
                if (take > room) take = room;
                if (take <= 0f) return;

                node.Remaining -= take;
                switch (node.Kind)
                {
                    case HarvestNodeKind.Arvore: hero.CarriedWood += take; break;
                    case HarvestNodeKind.Rocha: hero.CarriedStone += take; break;
                    case HarvestNodeKind.Bau: hero.CarriedGold += take; break;
                }

                log.Emit(SimEventType.NodeHarvested, node.Id, take, node.Position,
                         player: hero.Owner, intValue: (int)node.Kind);

                if (node.IsDepleted)
                {
                    // Nó do mundo procedural nao volta: o consumo e lembrado por chave, para que
                    // sair da clareira e voltar nao ressuscite a madeira.
                    state.MarkConsumed(node.WorldKey);
                    log.Emit(SimEventType.NodeDepleted, node.Id, 0f, node.Position);
                    state.RemoveNode(node);
                }
                return; // um no por tick mantem o feedback legivel
            }
        }

        private static void Deposit(MatchState state, IContentDatabase content, HeroState hero,
                                    HeroSpec spec, float dt, SimEventLog log)
        {
            if (hero.CarriedTotal <= 0f) return;
            if (Vec2.Distance(hero.Position, state.CityCenter) > DepositRadius + state.Grid.TownHallSize * 0.5f)
            {
                hero.DepositProgress = 0f;
                return;
            }

            hero.DepositProgress += dt;
            if (hero.DepositProgress < spec.DepositTime) return;
            hero.DepositProgress = 0f;

            float deposited = hero.CarriedTotal;

            state.SiloWood = Math.Min(state.SiloWood + hero.CarriedWood, state.SiloCapacity);
            state.Stone += hero.CarriedStone;

            var player = state.GetPlayer(hero.Owner);
            if (player != null)
            {
                player.Gold += hero.CarriedGold;
                player.Deposits++;
            }

            // Depositar sobe o nivel da CIDADE. E o que impede a Logistica de ser uma tarefa de
            // segunda classe: quem abastece o Silo tambem produz cartas para o time inteiro.
            state.Xp += deposited * content.Rules.XpPerResourceDeposited;

            log.Emit(SimEventType.HeroDeposited, hero.Id, deposited, hero.Position, player: hero.Owner);

            hero.CarriedWood = 0f;
            hero.CarriedStone = 0f;
            hero.CarriedGold = 0f;
        }

        /// <summary>
        /// Auto-ataque em cone na direcao do movimento. Sem alvo selecionado, sem clique.
        /// </summary>
        private static void AutoAttack(MatchState state, IContentDatabase content, HeroState hero,
                                       HeroSpec spec, float dt, SimEventLog log)
        {
            hero.AttackCooldown -= dt;
            if (hero.AttackCooldown > 0f) return;

            float sqrRadius = spec.AttackRadius * spec.AttackRadius;
            float cosLimit = (float)Math.Cos(spec.AttackConeHalfAngle * Math.PI / 180.0);
            bool hitAnything = false;

            for (int i = state.Monsters.Count - 1; i >= 0; i--)
            {
                var m = state.Monsters[i];
                if (!m.IsAlive) continue;

                var delta = m.Position - hero.Position;
                if (delta.SqrMagnitude > sqrRadius) continue;
                if (spec.AttackConeHalfAngle < 179f && Vec2.Dot(delta.Normalized, hero.Facing) < cosLimit) continue;

                Combat.DamageMonster(state, content, m, spec.AttackDamage, log);
                hitAnything = true;
            }

            if (hitAnything)
                log.Emit(SimEventType.HeroAttacked, hero.Id, spec.AttackDamage, hero.Position, player: hero.Owner);

            hero.AttackCooldown = spec.AttackInterval;
        }

        public static void DamageHero(MatchState state, IContentDatabase content, HeroState hero,
                                      float amount, SimEventLog log)
        {
            if (hero == null || hero.IsSpectre || amount <= 0f) return;

            hero.Health -= amount;
            log.Emit(SimEventType.HeroDamaged, hero.Id, amount, hero.Position, player: hero.Owner);

            if (hero.Health > 0f) return;

            var spec = content.GetHero(hero.Def);
            hero.Health = 0f;
            hero.IsSpectre = true;
            hero.RespawnRemaining = spec?.RespawnDelay ?? 6f;
            hero.HasPendingUrn = true;
            hero.UrnPosition = hero.Position;
            // A carga cai junto com a Urna: a perda e economica, nao um game over.
            hero.CarriedWood = 0f;
            hero.CarriedStone = 0f;
            hero.CarriedGold = 0f;

            log.Emit(SimEventType.HeroDied, hero.Id, 0f, hero.Position, player: hero.Owner);
        }

        private static void TickSpectre(MatchState state, HeroState hero, HeroSpec spec, float dt, SimEventLog log)
        {
            hero.RespawnRemaining -= dt;
            if (hero.RespawnRemaining > 0f) return;

            hero.IsSpectre = false;
            hero.Health = spec.MaxHealth;
            hero.Position = state.CityCenter;
            log.Emit(SimEventType.HeroRespawned, hero.Id, 0f, hero.Position, player: hero.Owner);
        }

        /// <summary>
        /// Levar a Urna de um companheiro ate a Prefeitura ANULA a morte dele (nenhum Tumulo).
        /// E o unico resgate do jogo, e por isso precisa custar a alguem sair da Linha.
        /// </summary>
        private static void TryRecoverUrns(MatchState state, HeroState carrier, SimEventLog log)
        {
            bool nearTownHall = Vec2.Distance(carrier.Position, state.CityCenter)
                                <= DepositRadius + state.Grid.TownHallSize * 0.5f;

            for (int i = 0; i < state.Heroes.Count; i++)
            {
                var owner = state.Heroes[i];
                if (!owner.HasPendingUrn) continue;

                if (Vec2.SqrDistance(carrier.Position, owner.UrnPosition) <= UrnPickupRadius * UrnPickupRadius)
                    owner.UrnPosition = carrier.Position; // urna passa a acompanhar quem a pegou

                if (nearTownHall &&
                    Vec2.SqrDistance(carrier.Position, owner.UrnPosition) <= UrnPickupRadius * UrnPickupRadius)
                {
                    owner.HasPendingUrn = false;
                    log.Emit(SimEventType.UrnRecovered, owner.Id, 0f, carrier.Position, player: carrier.Owner);
                }
            }
        }
    }
}
