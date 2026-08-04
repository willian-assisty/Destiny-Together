using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    public enum CommandType
    {
        None = 0,
        /// <summary>Erguer predio da mao no proprio Quadrante (custo zero: o XP ja pagou).</summary>
        BuildTower,
        /// <summary>Reparar predio ou Prefeitura gastando pedra.</summary>
        Repair,
        /// <summary>Limpar Escombro (dois jogadores cortam o tempo pela metade).</summary>
        ClearRubble,
        /// <summary>Doar carta para a bandeja do vizinho.</summary>
        DonateCard,
        /// <summary>Marcar-se como Pronto. O terceiro Pronto trava o relogio do Preparo.</summary>
        SetReady,
        /// <summary>Input continuo de movimento do heroi (enviado a cada tick durante o Assalto).</summary>
        MoveHero,
        /// <summary>Comecar a colher o no sob o heroi.</summary>
        StartHarvest,
        /// <summary>Escolher carta no draft privado do Balanco.</summary>
        PickCard,
        /// <summary>Rerrolar o draft gastando ouro pessoal.</summary>
        RerollDraft
    }

    /// <summary>
    /// Intencao do jogador. E o UNICO canal que sobe (UI -> simulacao).
    /// No multiplayer e isto que trafega — dezenas de bytes — e nao o estado do mundo.
    /// </summary>
    public struct PlayerCommand
    {
        public CommandType Type;
        public PlayerId Player;
        public DefId Def;
        public GridCoord Cell;
        public Vec2 Direction;
        public int IntValue;
        /// <summary>Sequencia local do jogador: garante ordenacao determinista no lock-in.</summary>
        public int Sequence;

        public static PlayerCommand Build(PlayerId p, DefId def, GridCoord cell)
            => new PlayerCommand { Type = CommandType.BuildTower, Player = p, Def = def, Cell = cell };

        public static PlayerCommand Ready(PlayerId p, bool ready)
            => new PlayerCommand { Type = CommandType.SetReady, Player = p, IntValue = ready ? 1 : 0 };

        public static PlayerCommand Move(PlayerId p, Vec2 dir)
            => new PlayerCommand { Type = CommandType.MoveHero, Player = p, Direction = dir };

        public static PlayerCommand Pick(PlayerId p, int optionIndex)
            => new PlayerCommand { Type = CommandType.PickCard, Player = p, IntValue = optionIndex };

        public static PlayerCommand RepairAt(PlayerId p, GridCoord cell)
            => new PlayerCommand { Type = CommandType.Repair, Player = p, Cell = cell };
    }
}
