using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// Os Esconderijos da mata: revelar e recolher.
    ///
    /// Este e o sistema que faz o Dia durar cinco minutos sem virar espera. Colher tem cota fixa
    /// por dia — depois dela, ficar no mapa nao rende mais nada. Explorar nao tem cota: rende
    /// enquanto houver mata por vasculhar. Entao a pergunta do Dia deixa de ser "ja peguei tudo?"
    /// e vira "da tempo de ir mais longe antes de escurecer?", que e uma decisao e nao uma tarefa.
    ///
    /// O pagamento e deliberadamente em XP e Ouro, nunca em Madeira ou Pedra. XP sobe o nivel da
    /// CIDADE, e nivel de cidade da carta para os quatro jogadores — logo o achado de um vira o
    /// ganho do time. Isso mantem as quatro moedas ortogonais e, mais importante, mantem intacta
    /// a regra de que construir vem exclusivamente de subir de nivel: um Relicario nao entrega
    /// predio, entrega progresso.
    /// </summary>
    public static class ExplorationSystem
    {
        public static void Tick(MatchState state, IContentDatabase content, SimEventLog log)
        {
            if (state.Caches.Count == 0) return;

            var arena = content.Arena;
            float revealSqr = arena.CacheRevealRadius * arena.CacheRevealRadius;
            float pickupSqr = arena.CachePickupRadius * arena.CachePickupRadius;

            for (int i = state.Caches.Count - 1; i >= 0; i--)
            {
                var cache = state.Caches[i];
                if (cache.Collected) continue;

                var finder = NearestActiveHero(state, cache.Position, out float sqr);
                if (finder == null) continue;

                if (!cache.Revealed && sqr <= revealSqr)
                {
                    cache.Revealed = true;
                    log.Emit(SimEventType.CacheRevealed, cache.Id, 0f, cache.Position,
                             player: finder.Owner, intValue: (int)cache.Kind);
                }

                if (sqr > pickupSqr) continue;

                Collect(state, cache, finder, log);
                state.RemoveCache(cache);
            }
        }

        /// <summary>
        /// Quanto cada achado do dia perde em relacao ao anterior. 0,82 faz o quinto valer ~37% do
        /// primeiro e o decimo ~14%, o que poe um teto pratico de ~5,5 achados "cheios" por
        /// pessoa por dia — sem nunca dizer nao ao jogador, e sem um numero de cota na tela.
        /// </summary>
        private const float DiminishingPerFind = 0.82f;

        private static void Collect(MatchState state, CacheState cache, HeroState finder, SimEventLog log)
        {
            cache.Collected = true;
            // Lembra o consumo por chave, nao por entidade: o chunk pode ser descarregado e
            // reconstruido dez vezes, e este Esconderijo nao volta.
            state.MarkConsumed(cache.WorldKey);

            var player = state.GetPlayer(finder.Owner);
            float multiplier = player != null ? Pow(DiminishingPerFind, player.CachesFoundToday) : 1f;
            float xp = cache.Xp * multiplier;

            // XP entra direto no contador da cidade em vez de virar carga do heroi. Esconderijo
            // fica longe de casa; exigir que a descoberta fosse carregada de volta faria o
            // Rondador transformar toda exploracao em perda total, e explorar viraria armadilha.
            state.Xp += xp;

            if (player != null)
            {
                player.Gold += cache.Gold * multiplier;
                player.CachesFound++;
                player.CachesFoundToday++;
            }

            log.Emit(SimEventType.CacheCollected, cache.Id, xp, cache.Position,
                     player: finder.Owner, intValue: (int)cache.Kind);
            log.Emit(SimEventType.ResourceGained, cache.Id, xp, cache.Position,
                     player: finder.Owner, intValue: (int)ResourceKind.Xp);
        }

        /// <summary>Potencia inteira. Evita System.Math.Pow por um expoente que e sempre pequeno.</summary>
        private static float Pow(float baseValue, int exponent)
        {
            float result = 1f;
            for (int i = 0; i < exponent && i < 64; i++) result *= baseValue;
            return result;
        }

        private static HeroState NearestActiveHero(MatchState state, Vec2 point, out float sqrDistance)
        {
            HeroState best = null;
            sqrDistance = float.MaxValue;

            for (int i = 0; i < state.Heroes.Count; i++)
            {
                var h = state.Heroes[i];
                if (!h.IsActive) continue;

                float sqr = Vec2.SqrDistance(h.Position, point);
                if (sqr >= sqrDistance) continue;

                sqrDistance = sqr;
                best = h;
            }

            return best;
        }
    }
}
