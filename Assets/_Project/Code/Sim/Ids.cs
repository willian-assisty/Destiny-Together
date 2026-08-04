using System;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// Identidade opaca de uma entidade viva na simulacao (torre, monstro, heroi, recurso).
    /// E a UNICA ponte de identidade entre a simulacao e a apresentacao: a view nao conhece
    /// o estado, so o EntityId que o ViewRegistry usa para achar o objeto na cena.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public readonly int Value;
        public EntityId(int value) => Value = value;

        public static readonly EntityId None = new EntityId(0);
        public bool IsValid => Value != 0;

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => $"E{Value}";
        public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;
        public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;
    }

    /// <summary>
    /// Identidade estavel de uma DEFINICAO de conteudo (a Balestra, o Enxame).
    /// Derivado por hash do nome — estavel entre sessoes, serializavel em rede como int,
    /// e legivel em teste sem carregar nenhum asset.
    /// </summary>
    public readonly struct DefId : IEquatable<DefId>
    {
        public readonly int Value;
        public DefId(int value) => Value = value;

        public static readonly DefId None = new DefId(0);
        public bool IsValid => Value != 0;

        /// <summary>Hash FNV-1a de 32 bits — estavel entre plataformas, ao contrario de string.GetHashCode().</summary>
        public static DefId FromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return None;
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < name.Length; i++)
                {
                    hash ^= name[i];
                    hash *= 16777619u;
                }
                int v = (int)hash;
                return new DefId(v == 0 ? 1 : v);
            }
        }

        public bool Equals(DefId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is DefId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => $"D{Value}";
        public static bool operator ==(DefId a, DefId b) => a.Value == b.Value;
        public static bool operator !=(DefId a, DefId b) => a.Value != b.Value;
    }

    /// <summary>Assento de jogador (0..3). Usado em ownership de Quadrante e ordenacao determinista de comandos.</summary>
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public readonly int Index;
        public PlayerId(int index) => Index = index;

        public static readonly PlayerId None = new PlayerId(-1);
        public bool IsValid => Index >= 0;

        public bool Equals(PlayerId other) => Index == other.Index;
        public override bool Equals(object obj) => obj is PlayerId o && Equals(o);
        public override int GetHashCode() => Index;
        public override string ToString() => $"P{Index}";
        public static bool operator ==(PlayerId a, PlayerId b) => a.Index == b.Index;
        public static bool operator !=(PlayerId a, PlayerId b) => a.Index != b.Index;
    }
}
