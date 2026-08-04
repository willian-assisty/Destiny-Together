namespace DestinyTogether.Sim
{
    /// <summary>
    /// O ciclo do turno. Duas fases, nao tres: DIA e NOITE.
    ///
    /// O turno inteiro e um ciclo de 24h comprimido. De dia nao ha inimigo nenhum e o mapa e
    /// seguro — e a janela de construir, colher e EXPLORAR as florestas. De noite os monstros
    /// vem do anel inteiro. A troca nao e cosmetica: e a unica coisa que diz ao jogador se ele
    /// deve estar longe de casa ou perto dela.
    /// </summary>
    public enum PhaseId
    {
        None = 0,
        /// <summary>Sem inimigos. Construir, colher, explorar e draftar, todos ao mesmo tempo.
        /// Termina no relogio ou quando todos marcam Pronto — quem quer a noite mais cedo, tem.</summary>
        Dia = 1,
        /// <summary>Tempo real. A horda vem de todas as direcoes. Termina no relogio, sempre.</summary>
        Noite = 2,
        /// <summary>Partida encerrada.</summary>
        Fim = 3
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

    /// <summary>
    /// O que um Esconderijo guarda.
    ///
    /// Explorar paga nas moedas de PROGRESSO (XP e Ouro); colher paga nas de MANUTENCAO
    /// (Madeira e Pedra). Essa separacao e o que da ao dia duas atividades diferentes em vez de
    /// duas fontes do mesmo recurso: quem colhe mantem a maquina rodando, quem explora faz a
    /// cidade subir de nivel — e nivel de cidade da carta para TODO MUNDO, entao o achado de um
    /// e o ganho dos quatro.
    /// </summary>
    public enum CacheKind
    {
        /// <summary>Comum. XP e um pouco de Ouro.</summary>
        Suprimento = 0,
        /// <summary>Raro, no fundo da mata. XP grande — costuma valer um nivel inteiro de cidade.</summary>
        Relicario = 1
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
        /// <summary>Kaiju da noite final.</summary>
        Kaiju = 5,
        /// <summary>
        /// Cacador. Ignora predio e vai atras de HEROI, de qualquer distancia. Existe por causa
        /// do ciclo dia/noite: sem ele, explorar de madrugada seria so demorado, nunca arriscado.
        /// </summary>
        Rondador = 6
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
