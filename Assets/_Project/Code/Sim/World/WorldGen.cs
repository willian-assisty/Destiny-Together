using System;
using System.Collections.Generic;
using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>O que um prop de cenario e. A apresentacao escolhe a malha; a simulacao so nomeia.</summary>
    public enum WorldPropKind
    {
        Arvore = 0,
        Pinheiro = 1,
        Pedra = 2,
        Penhasco = 3
    }

    /// <summary>Uma peca de cenario. Nao existe para a simulacao — e devolvida so para quem desenha.</summary>
    public struct WorldProp
    {
        public WorldPropKind Kind;
        public Vec2 Position;
        public float Yaw;
        public float Scale;
    }

    /// <summary>Um no colhivel ou um Esconderijo que um chunk quer que exista.</summary>
    public struct WorldSpawn
    {
        /// <summary>Indice estavel dentro do chunk. Junto com o chunk, identifica o item para sempre.</summary>
        public int Index;
        public Vec2 Position;
        /// <summary>True = Esconderijo; false = no colhivel.</summary>
        public bool IsCache;
        public HarvestNodeKind NodeKind;
        public CacheKind CacheKind;
        public float Amount;
        public float Gold;
    }

    /// <summary>O conteudo de um chunk. Struct de listas, montado sob demanda e descartavel.</summary>
    public sealed class ChunkContent
    {
        public readonly List<WorldProp> Props = new List<WorldProp>(48);
        public readonly List<WorldSpawn> Spawns = new List<WorldSpawn>(8);
    }

    /// <summary>
    /// O mundo alem da vila: florestas e pedreiras que nao acabam, em todas as direcoes.
    ///
    /// Nada disto e armazenado. O conteudo de um chunk e uma FUNCAO PURA de (semente, cx, cz) —
    /// entrar, sair e voltar produz exatamente a mesma mata. E o que torna "quase infinito"
    /// barato: nao existe mundo salvo, existe uma formula. E o que torna multiplayer barato pelo
    /// mesmo motivo: os quatro clientes derivam a mesma floresta sem trocar um byte sobre ela.
    ///
    /// Duas decisoes de design que o codigo aqui carrega:
    ///
    /// 1. **A fronteira paga em Esconderijo, nao em carga.** Um heroi tem carga limitada, entao ir
    ///    longe NUNCA traz mais madeira por viagem — so gasta mais tempo. Se a recompensa da
    ///    distancia fosse recurso carregavel, explorar seria matematicamente pior que colher no
    ///    quintal, por mais bonita que fosse a floresta. Esconderijo credita XP e Ouro na hora, sem
    ///    viagem de volta: e a unica moeda em que distancia pode pagar.
    ///
    /// 2. **Nada aqui renasce.** Os bolsoes da vila repovoam a cada amanhecer (cota fixa, e o que
    ///    impede o Dia de virar farm); o mundo la fora e de uso unico. Um bosque exaurido continua
    ///    exaurido, e valor novo exige ir MAIS longe. Isso mantem a exploracao expansiva em vez de
    ///    repetitiva — e mantem de pe a regra de que tempo parado perto de casa nao rende nada.
    /// </summary>
    public static class WorldGen
    {
        /// <summary>Lado do chunk em celulas. 24 = uma tela e pouco de zoom padrao.</summary>
        public const float ChunkSize = 24f;

        /// <summary>Escala das manchas de floresta, em celulas. Grande de proposito: bosque, nao ruido.</summary>
        private const float ForestScale = 96f;
        /// <summary>Escala das manchas de pedreira. Menor: afloramentos sao mais localizados.</summary>
        private const float QuarryScale = 68f;

        /// <summary>Abaixo disto e clareira. Acima, a densidade cresce ate 1.</summary>
        private const float ForestThreshold = 0.42f;
        private const float QuarryThreshold = 0.63f;

        private const int MaxPropsPerChunk = 26;

        // ------------------------------------------------------------------------------------
        // Coordenadas
        // ------------------------------------------------------------------------------------

        public static int ChunkOf(float worldCoord) => (int)Math.Floor(worldCoord / ChunkSize);

        public static void ChunkOf(Vec2 position, out int cx, out int cz)
        {
            cx = ChunkOf(position.X);
            cz = ChunkOf(position.Y);
        }

        /// <summary>Chave estavel de um chunk. Cabe num long e serve de indice em HashSet.</summary>
        public static long KeyOf(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        /// <summary>Chave estavel de um ITEM dentro de um chunk. E o que permite lembrar so o que foi consumido.</summary>
        public static long ItemKey(int cx, int cz, int index)
            => unchecked(KeyOf(cx, cz) * 397L + index + 1);

        public static Vec2 ChunkCenter(int cx, int cz)
            => new Vec2((cx + 0.5f) * ChunkSize, (cz + 0.5f) * ChunkSize);

        // ------------------------------------------------------------------------------------
        // Ruido
        // ------------------------------------------------------------------------------------

        /// <summary>Hash inteiro -> [0,1). Determinista entre plataformas, ao contrario de GetHashCode.</summary>
        private static float Hash01(int seed, int x, int z)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u;
                h ^= (uint)x * 0x9E3779B1u;
                h = (h ^ (h >> 15)) * 0x85EBCA6Bu;
                h ^= (uint)z * 0xC2B2AE35u;
                h = (h ^ (h >> 13)) * 0xC2B2AE35u;
                h ^= h >> 16;
                return (h >> 8) * (1f / 16777216f);
            }
        }

        /// <summary>
        /// Ruido de valor bilinear. Simples de proposito: floresta nao precisa de Perlin, precisa
        /// de MANCHA — regioes largas em que a densidade sobe e desce devagar, para que o jogador
        /// consiga dizer "aquilo ali e um bosque" e andar na direcao dele.
        /// </summary>
        public static float Noise01(int seed, float x, float z, float scale)
        {
            float fx = x / scale, fz = z / scale;
            int x0 = (int)Math.Floor(fx), z0 = (int)Math.Floor(fz);
            float tx = fx - x0, tz = fz - z0;

            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);

            float a = Hash01(seed, x0, z0);
            float b = Hash01(seed, x0 + 1, z0);
            float c = Hash01(seed, x0, z0 + 1);
            float d = Hash01(seed, x0 + 1, z0 + 1);

            return MathUtil.Lerp(MathUtil.Lerp(a, b, tx), MathUtil.Lerp(c, d, tx), tz);
        }

        /// <summary>Densidade de floresta em [0,1] num ponto. Publica: o HUD e a IA podem consultar.</summary>
        public static float ForestDensity(int seed, Vec2 point)
            => Ramp(Noise01(seed ^ 0x5F3A, point.X, point.Y, ForestScale), ForestThreshold);

        public static float QuarryDensity(int seed, Vec2 point)
            => Ramp(Noise01(seed ^ 0x2B7C, point.X, point.Y, QuarryScale), QuarryThreshold);

        private static float Ramp(float value, float threshold)
            => value <= threshold ? 0f : MathUtil.Clamp01((value - threshold) / (1f - threshold));

        // ------------------------------------------------------------------------------------
        // Geracao
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Monta o conteudo de um chunk. Puro: mesma entrada, mesma saida, sempre.
        ///
        /// <paramref name="into"/> e reutilizado pelo chamador para nao alocar por chunk — a
        /// apresentacao chama isto dezenas de vezes por segundo enquanto o jogador anda.
        /// </summary>
        public static void Generate(int seed, int cx, int cz, ArenaSpec arena, Vec2 cityCenter,
                                    ChunkContent into)
        {
            into.Props.Clear();
            into.Spawns.Clear();

            var rng = new Rng(unchecked(seed * 73856093 ^ cx * 19349663 ^ cz * 83492791));
            float x0 = cx * ChunkSize, z0 = cz * ChunkSize;

            // A vila e o anel de spawn tem conteudo proprio, calibrado a mao. O mundo procedural
            // comeca depois deles — senao a mata brotaria em cima do tabuleiro e do campo de tiro.
            float keepClear = arena.OutskirtsRadius + arena.WorldClearance;

            for (int i = 0; i < MaxPropsPerChunk; i++)
            {
                var p = new Vec2(x0 + rng.Range(0f, ChunkSize), z0 + rng.Range(0f, ChunkSize));
                if (Vec2.Distance(p, cityCenter) < keepClear) continue;

                float forest = ForestDensity(seed, p);
                float quarry = QuarryDensity(seed, p);

                // Pedreira ganha da floresta onde as duas se sobrepoem: rocha exposta e o que
                // impede a mata de crescer, e ver os dois misturados leria como bug de mapa.
                if (quarry > 0.05f && quarry >= forest)
                {
                    if (rng.Next01() > quarry) continue;
                    into.Props.Add(new WorldProp
                    {
                        Kind = rng.Next01() < 0.3f ? WorldPropKind.Penhasco : WorldPropKind.Pedra,
                        Position = p,
                        Yaw = rng.Range(0f, 360f),
                        Scale = rng.Range(0.7f, 1.6f)
                    });
                }
                else if (forest > 0.05f)
                {
                    if (rng.Next01() > forest) continue;
                    into.Props.Add(new WorldProp
                    {
                        Kind = rng.Next01() < 0.28f ? WorldPropKind.Pinheiro : WorldPropKind.Arvore,
                        Position = p,
                        Yaw = rng.Range(0f, 360f),
                        Scale = rng.Range(0.75f, 1.4f)
                    });
                }
            }

            GenerateSpawns(seed, cx, cz, arena, cityCenter, rng, into);
        }

        private static void GenerateSpawns(int seed, int cx, int cz, ArenaSpec arena, Vec2 cityCenter,
                                           Rng rng, ChunkContent into)
        {
            var center = ChunkCenter(cx, cz);
            float distance = Vec2.Distance(center, cityCenter);
            float keepClear = arena.OutskirtsRadius + arena.WorldClearance;
            if (distance < keepClear) return;

            // O gradiente da fronteira: quanto mais longe, mais denso e mais rico o achado. Zero
            // no comeco do mundo procedural, teto em WorldRichnessRange celulas depois. E o que
            // transforma "explorar" numa direcao, e nao apenas num passeio.
            float reach = MathUtil.Clamp01((distance - keepClear) / Math.Max(1f, arena.WorldRichnessRange));

            float forest = ForestDensity(seed, center);
            float quarry = QuarryDensity(seed, center);
            int index = 0;

            // Nos colhiveis: esparsos e de uso unico. Nao sao o motivo de vir — sao o que se
            // aproveita por estar passando, e o teto de carga do heroi garante que continuem
            // sendo isso.
            if (forest > 0.25f && rng.Next01() < 0.55f)
            {
                into.Spawns.Add(new WorldSpawn
                {
                    Index = index++,
                    Position = ScatterIn(cx, cz, rng),
                    IsCache = false,
                    NodeKind = HarvestNodeKind.Arvore,
                    Amount = arena.WoodPerTree * MathUtil.Lerp(1f, 1.8f, reach)
                });
            }

            if (quarry > 0.25f && rng.Next01() < 0.6f)
            {
                into.Spawns.Add(new WorldSpawn
                {
                    Index = index++,
                    Position = ScatterIn(cx, cz, rng),
                    IsCache = false,
                    NodeKind = HarvestNodeKind.Rocha,
                    Amount = arena.StonePerRock * MathUtil.Lerp(1f, 1.8f, reach)
                });
            }

            // Esconderijos: o motivo de vir. Chance e valor sobem com a distancia, e o Relicario
            // so aparece de verdade la fora.
            float cacheChance = MathUtil.Lerp(arena.WorldCacheChanceNear, arena.WorldCacheChanceFar, reach);
            if (rng.Next01() >= cacheChance) return;

            bool relic = rng.Next01() < MathUtil.Lerp(0.05f, 0.45f, reach);
            into.Spawns.Add(new WorldSpawn
            {
                Index = index,
                Position = ScatterIn(cx, cz, rng),
                IsCache = true,
                CacheKind = relic ? CacheKind.Relicario : CacheKind.Suprimento,
                Amount = (relic ? arena.XpPerRelicCache : arena.XpPerSupplyCache)
                         * MathUtil.Lerp(1f, 2.2f, reach),
                Gold = (relic ? arena.GoldPerRelicCache : arena.GoldPerSupplyCache)
                       * MathUtil.Lerp(1f, 2.2f, reach)
            });
        }

        /// <summary>Ponto dentro do chunk, com margem para nada nascer colado na borda.</summary>
        private static Vec2 ScatterIn(int cx, int cz, Rng rng)
            => new Vec2(cx * ChunkSize + rng.Range(2f, ChunkSize - 2f),
                        cz * ChunkSize + rng.Range(2f, ChunkSize - 2f));
    }
}
