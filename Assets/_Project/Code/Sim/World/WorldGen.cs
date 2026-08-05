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

        /// <summary>
        /// Qual malha da lista desenhar. Sai do gerador, e nao de um sorteio da apresentacao, por
        /// dois motivos: reconstruir o mesmo chunk tem de dar exatamente a mesma mata (voltar sobre
        /// os proprios passos nao pode mostrar outra floresta), e no multiplayer os quatro clientes
        /// precisam ver a mesma arvore no mesmo lugar sem trocar um byte sobre ela.
        /// </summary>
        public int Variant;
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

        /// <summary>
        /// Candidatos por chunk. Cada um so vira arvore com probabilidade igual a densidade local,
        /// entao isto e o TETO de um nucleo fechado, nao a media.
        ///
        /// 120 num chunk de 24x24 celulas da uma arvore a cada ~2,2 celulas — com copa de 2,2
        /// celulas, e mata fechada de verdade: da para se perder dentro. O numero so e pagavel
        /// porque a densidade cai em degrade (ver <see cref="Ramp"/>): a maior parte do mundo fica
        /// bem abaixo do teto, e clareira continua sendo clareira.
        /// </summary>
        private const int MaxPropsPerChunk = 120;

        /// <summary>Ate onde vai o cinturao de mata que emoldura a vila, em celulas.</summary>
        private const float CornerForestRange = 140f;

        /// <summary>Quanto o cinturao soma na densidade, no pico das diagonais.</summary>
        private const float CornerForestStrength = 0.62f;

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

        /// <summary>
        /// Densidade de floresta somada ao cinturao que emoldura a vila. E esta que o gerador usa;
        /// a de cima continua sendo a mata "pura" para quem so quer consultar o bioma.
        /// </summary>
        public static float ForestDensity(int seed, Vec2 point, Vec2 cityCenter, float keepClear)
            => MathUtil.Clamp01(ForestDensity(seed, point) + CornerForest(point, cityCenter, keepClear));

        public static float QuarryDensity(int seed, Vec2 point)
            => Ramp(Noise01(seed ^ 0x2B7C, point.X, point.Y, QuarryScale), QuarryThreshold);

        /// <summary>
        /// Corta no limiar e depois amacia com smoothstep.
        ///
        /// O smoothstep nao e enfeite: ele AFASTA os dois extremos. Borda de bosque fica mais rala
        /// (0,20 vira 0,10) e nucleo fica mais fechado (0,83 vira 0,92), que e exatamente a
        /// diferenca entre "arvores espalhadas por toda parte" e "mata fechada com clareira do
        /// lado". Sem ele, subir o teto de props so engrossaria o mundo inteiro por igual — e um
        /// mundo uniformemente denso nao tem para onde explorar.
        /// </summary>
        private static float Ramp(float value, float threshold)
        {
            if (value <= threshold) return 0f;
            float r = MathUtil.Clamp01((value - threshold) / (1f - threshold));
            return r * r * (3f - 2f * r);
        }

        /// <summary>
        /// O cinturao de mata das quatro diagonais, logo depois da clareira da vila.
        ///
        /// Existe porque a vila estava no meio de um campo aberto ate onde a vista alcanca, e o
        /// mundo procedural so comecava a ficar interessante longe demais para se ver de casa. O
        /// cinturao da fundo ao tabuleiro e coloca mata cerrada a uma corrida de distancia.
        ///
        /// **Nas diagonais, e nao nos eixos.** O peso e |sen(2t)| ao quadrado, que fecha os cantos
        /// e deixa Norte, Sul, Leste e Oeste abertos. Nao e estetica: a horda vem do anel inteiro e
        /// os pilares de Faixa sao a telegrafia da ameaca — emoldurar e bom, tapar a informacao de
        /// jogo nao. Os cantos sao justamente onde nao ha nada que precise ser visto de longe.
        /// </summary>
        private static float CornerForest(Vec2 point, Vec2 cityCenter, float keepClear)
        {
            float dx = point.X - cityCenter.X;
            float dz = point.Y - cityCenter.Y;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);

            if (distance <= keepClear || distance >= keepClear + CornerForestRange) return 0f;

            float ux = dx / distance, uz = dz / distance;
            float diagonal = Math.Abs(2f * ux * uz);   // 1 nas diagonais, 0 nos eixos

            // Sobe depois da clareira e volta a zero no fim do cinturao, para o mundo procedural
            // assumir sem uma emenda visivel.
            float t = (distance - keepClear) / CornerForestRange;
            float band = (float)Math.Sin(t * Math.PI);

            return CornerForestStrength * diagonal * diagonal * band;
        }

        // ------------------------------------------------------------------------------------
        // Geracao
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Monta o conteudo de um chunk. Puro: mesma entrada, mesma saida, sempre.
        ///
        /// <paramref name="into"/> e reutilizado pelo chamador para nao alocar por chunk — a
        /// apresentacao chama isto dezenas de vezes por segundo enquanto o jogador anda.
        /// </summary>
        /// <param name="includeProps">
        /// False monta so o que a simulacao precisa. Cenario nao e entidade: a simulacao nunca
        /// tocou num prop, e agora que um chunk fechado tem 120 candidatos em vez de 26, gerar a
        /// mata para depois joga-la fora custaria 4x mais em cada chunk que um heroi atravessa —
        /// inclusive nas milhares de partidas do harness de balanceamento.
        ///
        /// Isto so e seguro porque os dois sorteios sao INDEPENDENTES (ver
        /// <see cref="GenerateSpawns"/>): pular a mata nao move um Esconderijo um centimetro.
        /// </param>
        public static void Generate(int seed, int cx, int cz, ArenaSpec arena, Vec2 cityCenter,
                                    ChunkContent into, bool includeProps = true)
        {
            into.Props.Clear();
            into.Spawns.Clear();

            // A vila e o anel de spawn tem conteudo proprio, calibrado a mao. O mundo procedural
            // comeca depois deles — senao a mata brotaria em cima do tabuleiro e do campo de tiro.
            float keepClear = arena.OutskirtsRadius + arena.WorldClearance;

            if (includeProps) GenerateProps(seed, cx, cz, cityCenter, keepClear, into);
            GenerateSpawns(seed, cx, cz, arena, cityCenter, keepClear, into);
        }

        private static void GenerateProps(int seed, int cx, int cz, Vec2 cityCenter, float keepClear,
                                          ChunkContent into)
        {
            var rng = new Rng(unchecked(seed * 73856093 ^ cx * 19349663 ^ cz * 83492791));
            float x0 = cx * ChunkSize, z0 = cz * ChunkSize;

            for (int i = 0; i < MaxPropsPerChunk; i++)
            {
                var p = new Vec2(x0 + rng.Range(0f, ChunkSize), z0 + rng.Range(0f, ChunkSize));

                // O sorteio de variante e feito SEMPRE, aceito o prop ou nao. Consumir a mesma
                // quantidade de aleatoriedade por candidato e o que mantem o resto do chunk igual
                // quando um unico candidato cai fora.
                int variant = rng.Range(0, 1 << 16);
                float yaw = rng.Range(0f, 360f);
                float roll = rng.Next01();
                float size = rng.Next01();

                if (Vec2.Distance(p, cityCenter) < keepClear) continue;

                float forest = ForestDensity(seed, p, cityCenter, keepClear);
                float quarry = QuarryDensity(seed, p);

                // Pedreira ganha da floresta onde as duas se sobrepoem: rocha exposta e o que
                // impede a mata de crescer, e ver os dois misturados leria como bug de mapa.
                if (quarry > 0.05f && quarry >= forest)
                {
                    if (roll > quarry) continue;
                    into.Props.Add(new WorldProp
                    {
                        Kind = (variant & 7) < 2 ? WorldPropKind.Penhasco : WorldPropKind.Pedra,
                        Position = p,
                        Yaw = yaw,
                        Scale = 0.7f + size * 0.9f,
                        Variant = variant
                    });
                }
                else if (forest > 0.05f)
                {
                    if (roll > forest) continue;
                    into.Props.Add(new WorldProp
                    {
                        Kind = (variant & 7) < 2 ? WorldPropKind.Pinheiro : WorldPropKind.Arvore,
                        Position = p,
                        Yaw = yaw,
                        // Faixa larga de propósito: mata fechada com todas as arvores do mesmo
                        // tamanho le como padrao de papel de parede, nao como floresta.
                        Scale = 0.65f + size * 0.85f,
                        Variant = variant
                    });
                }
            }
        }

        /// <summary>
        /// O que a simulacao materializa: nos colhiveis e Esconderijos.
        ///
        /// Tem sorteio PROPRIO, independente do da mata. Antes os dois dividiam um Rng e a ordem
        /// importava — mudar a densidade da floresta movia todos os Esconderijos do mundo, e o
        /// balanceamento medido ia junto. Separar deixa arte e economia livres uma da outra.
        /// </summary>
        private static void GenerateSpawns(int seed, int cx, int cz, ArenaSpec arena, Vec2 cityCenter,
                                           float keepClear, ChunkContent into)
        {
            var rng = new Rng(unchecked(seed * 19349663 ^ cx * 83492791 ^ cz * 73856093 ^ 0x5BD1E995));

            var center = ChunkCenter(cx, cz);
            float distance = Vec2.Distance(center, cityCenter);
            if (distance < keepClear) return;

            // O gradiente da fronteira: quanto mais longe, mais denso e mais rico o achado. Zero
            // no comeco do mundo procedural, teto em WorldRichnessRange celulas depois. E o que
            // transforma "explorar" numa direcao, e nao apenas num passeio.
            float reach = MathUtil.Clamp01((distance - keepClear) / Math.Max(1f, arena.WorldRichnessRange));

            // Densidade PURA, sem o cinturao das diagonais. O cinturao e cenario: se ele tambem
            // spawnasse no colhivel, a vila ganharia um campo de madeira encostado nela — que e
            // exatamente a regra "nada perto de casa rende com o tempo" indo pelo ralo, e o
            // balanceamento medido junto.
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
