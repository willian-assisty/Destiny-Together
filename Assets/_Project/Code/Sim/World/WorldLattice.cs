using System;
using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// Qual regiao do mundo. Ordem importa: Mata e 0 porque e o bioma de casa e o alvo de todo
    /// fallback — lista vazia, tabela incompleta ou arte faltando cai aqui, nunca em Pantano.
    /// </summary>
    public enum RegionKind : byte
    {
        Mata = 0,
        Pedreira = 1,
        Pasto = 2,
        Pantano = 3
    }

    /// <summary>Quanto de cada massa de cenario uma regiao carrega, e como ela se comporta.</summary>
    public readonly struct RegionSpec
    {
        public readonly RegionKind Id;

        /// <summary>Multiplicadores da densidade de mata e de rocha. 1 = como era antes das regioes.</summary>
        public readonly float ForestMass;
        public readonly float RockMass;

        /// <summary>Peso no sorteio de qual regiao um sitio recebe.</summary>
        public readonly float Weight;

        /// <summary>
        /// Multiplicador de velocidade de quem atravessa. 1 em tudo hoje.
        ///
        /// O campo existe porque o Pantano vai precisar dele, e existe ZERADO porque terreno que
        /// muda velocidade mexe na regra 2 — "o unico verbo e ESTAR" — e essa e uma decisao de
        /// design que ainda nao foi tomada. Deixar o campo pronto e barato; liga-lo sem decidir
        /// seria acrescentar uma segunda variavel de posicionamento ao jogo de lado.
        /// </summary>
        public readonly float MoveFactor;

        public RegionSpec(RegionKind id, float forestMass, float rockMass, float weight,
                          float moveFactor = 1f)
        {
            Id = id;
            ForestMass = forestMass;
            RockMass = rockMass;
            Weight = weight;
            MoveFactor = moveFactor;
        }
    }

    /// <summary>
    /// A malha grossa que divide o mundo em regioes.
    ///
    /// **Malha de sitios, e nao um terceiro campo de ruido.** Mais uma camada de ruido daria
    /// mancha do mesmo jeito, mas cada bioma novo viraria mais uma linha na arbitragem
    /// "quem ganha de quem" — e em quatro regioes isso ja e uma cascata de ifs. Com uma malha, a
    /// pergunta e sempre a mesma: de qual sitio este ponto esta mais perto. Acrescentar a quinta
    /// regiao e uma linha na tabela.
    ///
    /// Continua sendo FUNCAO PURA de (semente, ponto): nada e guardado, e os quatro clientes
    /// derivam a mesma divisao do mundo sem trocar um byte sobre ela.
    ///
    /// A malha tem uma segunda propriedade que a torna a escolha certa a longo prazo: um sitio e
    /// um CENTRO. E centro e o que uma construcao especial precisa para nascer em algum lugar que
    /// signifique alguma coisa, em vez de num ponto sorteado no vazio.
    /// </summary>
    public static class WorldLattice
    {
        /// <summary>
        /// Lado de uma celula de regiao, em celulas de jogo.
        ///
        /// 640 e ritmo, nao numero redondo: um heroi a 5,5 celulas/s atravessa isso em ~2 minutos,
        /// entao um Dia de 5 minutos cabe umas duas regioes. Menor virava mosaico (regiao deixa de
        /// ler como lugar); maior e o jogador passar a partida inteira sem ver uma fronteira.
        /// </summary>
        public const float CellSize = 640f;

        /// <summary>
        /// Raio em volta da vila que e Mata na marra.
        ///
        /// 120 celulas fica DENTRO do cinturao de mata que ja emoldura a vila, entao o circulo e
        /// invisivel — ele nao cria uma borda, so garante que a partida nunca comece cercada de
        /// Pantano por sorteio. Garantia geometrica, nao probabilistica: um vies por raio de
        /// sitio nao garantiria nada, porque com jitter o sitio mais proximo da vila pode estar a
        /// mais de uma celula de distancia e pertencer a qualquer vizinho.
        /// </summary>
        public const float HomeRadius = 120f;

        // Deformacao do dominio: o ponto e torcido ANTES da consulta. Sem isso a fronteira entre
        // duas regioes e o lado reto de um poligono de Voronoi, que le como corte de mapa.
        private const float WarpScale = 150f;
        private const float WarpAmplitude = 55f;

        private const int SeedSalt = 0x51A7;

        /// <summary>
        /// A tabela. Acrescentar regiao e acrescentar uma linha.
        ///
        /// Pasto e Pantano ainda nao tem vegetacao propria; ate ela chegar, os dois se leem pela
        /// COR DO CHAO e pela densidade — pasto e campo aberto, pantano e mata rala e escura. Sao
        /// leituras honestas com a arte que existe, e nao um lugar-nenhum esperando asset.
        /// </summary>
        /// As massas sao calibradas para que a MEDIA ponderada fique em 1: regiao REDISTRIBUI a
        /// paisagem, nunca a reduz. A primeira tabela que escrevi tinha todas as massas abaixo de
        /// 1 exceto uma, e a medicao acusou na hora — o mundo perdeu 37% das arvores e 70% das
        /// pedras, e a "mata fechada" caiu pela metade. Massa media 1 e um invariante da tabela,
        /// nao um acidente: ver o teste `PaisagemERedistribuida`.
        private static readonly RegionSpec[] Table =
        {
            new RegionSpec(RegionKind.Mata,     forestMass: 2.40f, rockMass: 0.18f, weight: 0.36f),
            new RegionSpec(RegionKind.Pedreira, forestMass: 0.10f, rockMass: 0.85f, weight: 0.22f),
            new RegionSpec(RegionKind.Pasto,    forestMass: 0.16f, rockMass: 0.10f, weight: 0.26f),
            new RegionSpec(RegionKind.Pantano,  forestMass: 0.44f, rockMass: 0.18f, weight: 0.16f),
        };

        public static RegionSpec SpecOf(RegionKind kind)
        {
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].Id == kind) return Table[i];
            return Table[0];
        }

        /// <summary>Hash estavel da tabela. Entra no ContentHash: cliente com outra divisao e rejeitado.</summary>
        public static int TableHash()
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < Table.Length; i++)
                {
                    var r = Table[i];
                    hash = hash * 31 + (int)r.Id;
                    hash = hash * 31 + (int)(r.ForestMass * 1000f);
                    hash = hash * 31 + (int)(r.RockMass * 1000f);
                    hash = hash * 31 + (int)(r.Weight * 1000f);
                    hash = hash * 31 + (int)(r.MoveFactor * 1000f);
                }
                return hash;
            }
        }

        // ------------------------------------------------------------------------------------

        /// <summary>A regiao de um ponto do mundo.</summary>
        public static RegionKind RegionAt(int seed, Vec2 point, Vec2 cityCenter)
            => RegionAt(seed, point, cityCenter, out _);

        /// <summary>
        /// A regiao de um ponto, mais a folga ate a fronteira em celulas.
        ///
        /// A folga serve a quem desenha: ela diz se um chunk inteiro esta seguramente dentro de
        /// uma regiao, o que permite resolver a regiao UMA vez em vez de por prop.
        /// </summary>
        public static RegionKind RegionAt(int seed, Vec2 point, Vec2 cityCenter, out float margin)
        {
            if (Vec2.Distance(point, cityCenter) < HomeRadius)
            {
                margin = HomeRadius - Vec2.Distance(point, cityCenter);
                return RegionKind.Mata;
            }

            // Torce o ponto e resolve no espaco torcido.
            float wx = point.X + (Noise(seed ^ 0x2C1, point.X, point.Y, WarpScale) - 0.5f) * WarpAmplitude;
            float wz = point.Y + (Noise(seed ^ 0x7D3, point.X, point.Y, WarpScale) - 0.5f) * WarpAmplitude;

            int cx = (int)Math.Floor(wx / CellSize);
            int cz = (int)Math.Floor(wz / CellSize);

            float best = float.MaxValue, second = float.MaxValue;
            RegionKind winner = RegionKind.Mata;

            // 3x3 basta, e isto e demonstravel: com o jitter preso em [0,25 .. 0,75] da celula, o
            // sitio da propria celula esta no maximo a 1,06 celula; qualquer sitio fora do 3x3
            // esta a pelo menos 1,25. Com jitter em [0..1] a garantia cai e a fronteira ganha
            // lascas perto dos cantos.
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    SiteAt(seed, cx + dx, cz + dz, out float sx, out float sz, out var kind);

                    float ddx = wx - sx, ddz = wz - sz;
                    float d2 = ddx * ddx + ddz * ddz;

                    if (d2 < best)
                    {
                        second = best;
                        best = d2;
                        winner = kind;
                    }
                    else if (d2 < second)
                    {
                        second = d2;
                    }
                }
            }

            // Meia diferenca entre o mais proximo e o segundo: quanto da para andar antes de trocar.
            margin = ((float)Math.Sqrt(second) - (float)Math.Sqrt(best)) * 0.5f;
            return winner;
        }

        /// <summary>Posicao e tipo do sitio dono de uma celula da malha.</summary>
        private static void SiteAt(int seed, int cx, int cz, out float x, out float z, out RegionKind kind)
        {
            float jx = 0.25f + Hash01(seed ^ SeedSalt, cx, cz) * 0.5f;
            float jz = 0.25f + Hash01(seed ^ (SeedSalt + 1), cx, cz) * 0.5f;

            x = (cx + jx) * CellSize;
            z = (cz + jz) * CellSize;
            kind = PickRegion(Hash01(seed ^ (SeedSalt + 2), cx, cz));
        }

        private static RegionKind PickRegion(float roll)
        {
            float total = 0f;
            for (int i = 0; i < Table.Length; i++) total += Table[i].Weight;

            float acc = 0f;
            for (int i = 0; i < Table.Length; i++)
            {
                acc += Table[i].Weight / total;
                if (roll < acc) return Table[i].Id;
            }
            return Table[Table.Length - 1].Id;
        }

        // O mesmo ruido e o mesmo hash do resto do mundo. Reusar mantem uma so definicao de
        // "aleatorio determinista" no projeto inteiro.
        private static float Noise(int seed, float x, float z, float scale)
            => WorldGen.Noise01(seed, x, z, scale);

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
    }
}
