using System;
using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// XP sobe o nivel da CIDADE (nunca do heroi). Cada nivel entrega uma carta para CADA jogador,
    /// em drafts PRIVADOS e simultaneos — nunca um draft compartilhado, que viraria monologo do
    /// veterano. A curva de XP e multiplicada pelo numero de jogadores, entao a agencia per capita
    /// e identica jogando com 1, 2, 3 ou 4.
    /// </summary>
    public static class EconomySystem
    {
        public static float XpRequiredFor(IContentDatabase content, int cityLevel, int playerCount)
        {
            var rules = content.Rules;
            float baseCost = rules.XpPerCityLevelBase * (float)Math.Pow(rules.XpPerCityLevelGrowth, cityLevel - 1);
            return baseCost * Math.Max(1, playerCount);
        }

        public static void ResolveLevelUps(MatchState state, IContentDatabase content, SimEventLog log)
        {
            int playerCount = Math.Max(1, state.ConnectedPlayerCount());
            int guard = 0;

            while (state.Xp >= state.XpToNextLevel && guard++ < 50)
            {
                state.Xp -= state.XpToNextLevel;
                state.CityLevel++;
                state.XpToNextLevel = XpRequiredFor(content, state.CityLevel, playerCount);

                log.Emit(SimEventType.CityLevelUp, EntityId.None, state.CityLevel, state.CityCenter,
                         intValue: state.CityLevel);

                for (int i = 0; i < state.Players.Count; i++)
                {
                    var p = state.Players[i];
                    if (!p.IsConnected || p.IsAutomaton) continue;
                    p.PendingDraftPicks++;
                }
            }
        }

        /// <summary>Monta as opcoes do draft privado de cada jogador que tem escolha pendente.</summary>
        public static void OfferDrafts(MatchState state, IContentDatabase content, Rng rng, SimEventLog log)
        {
            var pool = content.TowerPool;
            if (pool == null || pool.Count == 0) return;

            for (int i = 0; i < state.Players.Count; i++)
            {
                var p = state.Players[i];
                if (p.PendingDraftPicks <= 0 || p.DraftOptions.Count > 0) continue;

                FillOptions(p, content, rng);
                log.Emit(SimEventType.DraftOffered, EntityId.None, p.DraftOptions.Count,
                         state.CityCenter, player: p.Id);
            }
        }

        private static void FillOptions(PlayerState player, IContentDatabase content, Rng rng)
        {
            player.DraftOptions.Clear();
            var pool = content.TowerPool;
            int want = Math.Min(content.Rules.DraftOptions, pool.Count);

            var indices = new int[pool.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            for (int i = indices.Length - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            for (int i = 0; i < want; i++)
                player.DraftOptions.Add(pool[indices[i]]);
        }

        public static bool TryPick(MatchState state, IContentDatabase content, PlayerState player,
                                   int optionIndex, Rng rng, SimEventLog log)
        {
            if (player.PendingDraftPicks <= 0) return false;
            if (optionIndex < 0 || optionIndex >= player.DraftOptions.Count) return false;

            var pick = player.DraftOptions[optionIndex];
            player.Hand.Add(pick);
            player.PendingDraftPicks--;
            player.DraftOptions.Clear();

            log.Emit(SimEventType.CardPicked, EntityId.None, 0f, state.CityCenter, pick, player.Id);

            if (player.PendingDraftPicks > 0)
                FillOptions(player, content, rng);
            else
                player.DraftResolved = true;

            return true;
        }

        public static bool TryReroll(MatchState state, IContentDatabase content, PlayerState player,
                                     Rng rng, SimEventLog log)
        {
            float cost = content.Rules.DraftRerollCost;
            if (player.Gold < cost || player.PendingDraftPicks <= 0) return false;

            player.Gold -= cost;
            FillOptions(player, content, rng);
            log.Emit(SimEventType.DraftOffered, EntityId.None, player.DraftOptions.Count,
                     state.CityCenter, player: player.Id);
            return true;
        }

        /// <summary>
        /// Timeout do Balanco: quem nao escolheu leva a primeira opcao. Nunca deixa um jogador
        /// ausente travar o turno dos outros tres.
        /// </summary>
        public static void AutoResolvePendingDrafts(MatchState state, IContentDatabase content,
                                                    Rng rng, SimEventLog log)
        {
            for (int i = 0; i < state.Players.Count; i++)
            {
                var p = state.Players[i];
                int guard = 0;
                while (p.PendingDraftPicks > 0 && guard++ < 20)
                {
                    if (p.DraftOptions.Count == 0) FillOptions(p, content, rng);
                    if (!TryPick(state, content, p, 0, rng, log)) break;
                }
            }
        }
    }
}
