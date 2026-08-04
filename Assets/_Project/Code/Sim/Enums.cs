namespace DestinyTogether.Sim
{
    /// <summary>Fases do turno. Unico pedaco da state machine que a rede replica.</summary>
    public enum PhaseId
    {
        None = 0,
        /// <summary>Sem inimigos. Os 4 jogadores constroem e colhem SIMULTANEAMENTE. Termina em 4 Prontos ou timeout.</summary>
        Preparo = 1,
        /// <summary>Tempo real. Investidas de ~25s separadas por Respiros de ~8s. Torres atiram sozinhas.</summary>
        Assalto = 2,
        /// <summary>Mundo congelado. Deposito automatico, cartao do turno e draft privado simultaneo.</summary>
        Balanco = 3,
        /// <summary>Partida encerrada.</summary>
        Fim = 4
    }

    public enum MatchOutcome
    {
        EmAndamento = 0,
        Vitoria = 1,
        Derrota = 2
    }

    /// <summary>
    /// As 8 Faixas de aproximacao. O nome da Faixa e a unidade de comunicacao do time
    /// ("vaza no Norte") — por isso e enum e nao angulo solto.
    /// </summary>
    public enum Lane
    {
        Norte = 0,
        Nordeste = 1,
        Leste = 2,
        Sudeste = 3,
        Sul = 4,
        Sudoeste = 5,
        Oeste = 6,
        Noroeste = 7
    }

    /// <summary>Quadrante de soberania: cada jogador so constroi no seu.</summary>
    public enum Quadrant
    {
        Nordeste = 0,
        Noroeste = 1,
        Sudoeste = 2,
        Sudeste = 3
    }

    /// <summary>
    /// TAG de predio. Conectar 4+ predios da mesma TAG fecha um Distrito, que concede
    /// uma REGRA (nunca um numero) — a licao central extraida do jogo de referencia.
    /// </summary>
    public enum BuildingTag
    {
        Nenhuma = 0,
        Ferro = 1,
        Igneo = 2,
        Gelo = 3,
        Oficio = 4,
        Pedra = 5
    }

    public enum TargetingRule
    {
        /// <summary>Mais proximo da torre.</summary>
        MaisProximo = 0,
        /// <summary>Mais avancado em direcao a cidade — o padrao correto para defesa.</summary>
        MaisAvancado = 1,
        /// <summary>Maior HP atual — bom para torres anti-Bruto.</summary>
        MaisForte = 2,
        /// <summary>Menor HP atual — bom para limpar restos.</summary>
        MaisFraco = 3
    }

    public enum ResourceKind
    {
        /// <summary>Sobe o nivel da CIDADE (nunca do heroi) -> uma carta para CADA jogador.</summary>
        Xp = 0,
        /// <summary>Municao do Silo compartilhado: torres gastam a cada disparo.</summary>
        Madeira = 1,
        /// <summary>Reparo de predios e da Prefeitura. Unica cura do jogo.</summary>
        Pedra = 2,
        /// <summary>Pessoal: reroll de draft e Feira.</summary>
        Ouro = 3
    }

    /// <summary>No de recurso colhivel espalhado pelos Arredores.</summary>
    public enum HarvestNodeKind
    {
        Arvore = 0,
        Rocha = 1,
        Bau = 2
    }

    public enum MonsterArchetype
    {
        /// <summary>Volume. HP baixo, chega em pacotes de 12-20. Alimenta o combo.</summary>
        Enxame = 0,
        /// <summary>O assassino. Detona em area e mata PREDIO, nao heroi. Premia matar dentro do pacote.</summary>
        Estourador = 1,
        /// <summary>Esponja de dano. Resiste a slow. So morre com foco.</summary>
        Bruto = 2,
        /// <summary>Para a distancia e atira. Obriga o heroi a SAIR da Linha — anti-camping.</summary>
        Cuspidor = 3,
        /// <summary>Estatico, nasce FORA do alcance das torres, gera Enxames. So mao humana mata.</summary>
        Ninho = 4,
        /// <summary>Kaiju do turno final.</summary>
        Kaiju = 5
    }

    public enum HeroClass
    {
        Guarda = 0,
        Lenhador = 1,
        Golem = 2,
        Arauto = 3
    }

    /// <summary>Estado de um alvo estrutural — usado por prognostico e IA de monstro.</summary>
    public enum StructureKind
    {
        Predio = 0,
        Prefeitura = 1
    }

    /// <summary>Faixa de risco calculada por Faixa durante o Preparo. E o coracao social do jogo.</summary>
    public enum ForecastBand
    {
        /// <summary>As torres cobrem a onda inteira antes dela encostar.</summary>
        Segura = 0,
        /// <summary>Parte da onda alcanca os predios.</summary>
        Vaza = 1,
        /// <summary>A onda chega inteira. Sem intervencao humana, algo cai.</summary>
        Arromba = 2
    }
}
