using System;

namespace DestinyTogether.Core
{
    /// <summary>
    /// PRNG xorshift128 semeado explicitamente. Nao usamos System.Random nem UnityEngine.Random
    /// porque a simulacao precisa ser reproduzivel: mesma seed + mesmos comandos = mesma partida.
    /// Isso e o que permite replay, teste de regressao e (no multiplayer) spawn identico em todos
    /// os clientes sem transmitir a posicao de cada monstro.
    /// </summary>
    public sealed class Rng
    {
        private uint _x, _y, _z, _w;

        public Rng(int seed)
        {
            Reseed(seed);
        }

        public void Reseed(int seed)
        {
            unchecked
            {
                uint s = (uint)seed;
                if (s == 0) s = 0x9E3779B9u;
                _x = s;
                _y = s * 1812433253u + 1u;
                _z = _y * 1812433253u + 1u;
                _w = _z * 1812433253u + 1u;
            }
        }

        /// <summary>
        /// Le e restaura o estado interno.
        ///
        /// Existe para o arquivo de retomada: a semente sozinha nao basta, porque um gerador que ja
        /// sacou 4000 numeros nao volta ao mesmo ponto so por ser resemeado. Sem isto, recarregar
        /// uma partida do disco continuaria com outra sequencia de spawns e de draft — a mesma
        /// partida deixaria de ser a mesma partida no exato momento em que mais importa.
        /// </summary>
        public void GetState(out uint x, out uint y, out uint z, out uint w)
        {
            x = _x; y = _y; z = _z; w = _w;
        }

        public void SetState(uint x, uint y, uint z, uint w)
        {
            _x = x; _y = y; _z = z; _w = w;
        }

        /// <summary>Cria um gerador derivado, isolado por canal (spawn, draft, loot...).</summary>
        public static Rng ForChannel(int matchSeed, int channel, int round)
            => new Rng(unchecked(matchSeed * 73856093 ^ channel * 19349663 ^ round * 83492791));

        public uint NextUInt()
        {
            unchecked
            {
                uint t = _x ^ (_x << 11);
                _x = _y; _y = _z; _z = _w;
                _w = _w ^ (_w >> 19) ^ t ^ (t >> 8);
                return _w;
            }
        }

        /// <summary>Inteiro em [min, max).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint span = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % span);
        }

        /// <summary>Float em [0, 1).</summary>
        public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Float em [min, max).</summary>
        public float Range(float min, float max) => min + Next01() * (max - min);

        public bool Chance(float probability) => Next01() < probability;

        /// <summary>Ponto uniforme dentro de um anel, usado para posicionar recursos e spawns.</summary>
        public Vec2 PointInRing(Vec2 center, float innerRadius, float outerRadius)
        {
            float angle = Range(0f, 360f);
            float r = (float)Math.Sqrt(MathUtil.Lerp(innerRadius * innerRadius, outerRadius * outerRadius, Next01()));
            return center + Vec2.FromCompassDegrees(angle) * r;
        }

        /// <summary>Embaralha in-place (Fisher-Yates).</summary>
        public void Shuffle<T>(System.Collections.Generic.IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
