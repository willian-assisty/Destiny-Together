using System;
using System.Collections.Generic;
using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// Fachada unica da simulacao. Recebe comandos, avanca em tick FIXO e produz eventos.
    ///
    /// Tick fixo (20 Hz) e o que permite que o Assalto seja em tempo real sem abrir mao de
    /// testabilidade: a partida inteira roda num teste EditMode em milissegundos, e o host
    /// autoritativo do multiplayer avanca exatamente os mesmos ticks que o teste avanca.
    /// Nenhuma linha aqui conhece UnityEngine.
    /// </summary>
    public sealed class MatchSimulation
    {
        public const float TickRate = 20f;
        public const float FixedDelta = 1f / TickRate;

        private readonly IContentDatabase _content;
        private readonly ILogSink _log;
        private readonly SimEventLog _events = new SimEventLog();
        private readonly List<PlayerCommand> _pendingCommands = new List<PlayerCommand>(32);
        private readonly Rng _spawnRng;
        private readonly Rng _draftRng;

        private float _accumulator;
        private TurnWaveSpec _currentTurnWaves;
        private bool _clampApplied;
        private readonly int _playerCount;

        public MatchState State { get; }
        public IContentDatabase Content => _content;
        public SimEventLog Events => _events;
        public TurnWaveSpec CurrentTurnWaves => _currentTurnWaves;

        /// <summary>
        /// Ticks fixos decorridos desde o inicio da partida. E o relogio comum de host e clientes:
        /// quadro de movimento, lote de evento e reconciliacao de predicao sao todos carimbados com
        /// ele. Tempo real nao serve para isso — dois relogios de parede nunca concordam; contagem
        /// de tick concorda por definicao.
        /// </summary>
        public int Tick { get; private set; }

        public MatchSimulation(IContentDatabase content, int seed, int playerCount,
                               ILogSink log = null, DefId[] chosenHeroes = null)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _log = log ?? NullLogSink.Instance;
            _spawnRng = Rng.ForChannel(seed, 1, 0);
            _draftRng = Rng.ForChannel(seed, 2, 0);

            State = MatchFactory.Create(content, seed, playerCount, chosenHeroes);
            _playerCount = State.Players.Count;
            _currentTurnWaves = content.GetTurnWaves(State.TurnNumber, _playerCount);
            EnterPhase(PhaseId.Dia);
        }

        // ----------------------------------------------------------------------------------
        // Comandos
        // ----------------------------------------------------------------------------------

        /// <summary>
        /// Enfileira um comando. No single-player o router chama isto direto; no multiplayer
        /// o cliente manda por RPC e SO o host chama isto — a autoridade e uma so.
        /// </summary>
        public ValidationResult SubmitCommand(in PlayerCommand cmd)
        {
            var validation = CommandValidator.Validate(State, _content, cmd);
            if (!validation.IsValid) return validation;
            _pendingCommands.Add(cmd);
            return ValidationResult.Ok;
        }

        private void ApplyPendingCommands()
        {
            if (_pendingCommands.Count == 0) return;

            // Ordenacao determinista: por jogador, depois por sequencia. Remove a corrida entre
            // dois jogadores mirando a mesma celula no mesmo tick.
            _pendingCommands.Sort((a, b) =>
            {
                int byPlayer = a.Player.Index.CompareTo(b.Player.Index);
                return byPlayer != 0 ? byPlayer : a.Sequence.CompareTo(b.Sequence);
            });

            for (int i = 0; i < _pendingCommands.Count; i++)
                Apply(_pendingCommands[i]);

            _pendingCommands.Clear();
        }

        private void Apply(in PlayerCommand cmd)
        {
            var player = State.GetPlayer(cmd.Player);
            if (player == null) return;

            // Revalida no momento da aplicacao: o mundo pode ter mudado desde o envio.
            if (!CommandValidator.Validate(State, _content, cmd).IsValid) return;

            switch (cmd.Type)
            {
                case CommandType.BuildTower:
                    BuildSystem.TryBuild(State, _content, player, cmd.Def, cmd.Cell, _events);
                    break;

                case CommandType.Repair:
                    BuildSystem.TryRepair(State, _content, cmd.Cell, 5f, _events);
                    if (player != null) player.Repairs++;
                    break;

                case CommandType.ClearRubble:
                    BuildSystem.TryClearRubble(State, _content, cmd.Cell, _events);
                    break;

                case CommandType.SetReady:
                    player.IsReady = cmd.IntValue != 0;
                    ApplyReadyClamp();
                    break;

                case CommandType.MoveHero:
                {
                    var hero = State.GetHero(player.Hero);
                    if (hero != null) hero.MoveInput = cmd.Direction;
                    break;
                }

                case CommandType.DonateCard:
                {
                    var target = State.GetPlayer(new PlayerId(cmd.IntValue));
                    if (target != null && player.Hand.Remove(cmd.Def)) target.Hand.Add(cmd.Def);
                    break;
                }

                case CommandType.PickCard:
                    EconomySystem.TryPick(State, _content, player, cmd.IntValue, _draftRng, _events);
                    break;

                case CommandType.RerollDraft:
                    EconomySystem.TryReroll(State, _content, player, _draftRng, _events);
                    break;
            }
        }

        /// <summary>
        /// Quando o TERCEIRO jogador aperta Pronto, o Preparo trava em no maximo 15s restantes.
        /// Mata o monologo do veterano sem impor timer duro em ninguem.
        /// </summary>
        private void ApplyReadyClamp()
        {
            if (State.Phase != PhaseId.Dia || _clampApplied) return;

            int connected = Math.Max(1, State.ConnectedPlayerCount());
            int readyThreshold = connected >= 4 ? 3 : Math.Max(1, connected - 1);
            if (State.ReadyCount() < readyThreshold) return;

            float clamp = _content.Rules.PreparoClampOnThirdReady;
            float remaining = State.PhaseDuration - State.PhaseElapsed;
            if (remaining > clamp)
                State.PhaseDuration = State.PhaseElapsed + clamp;

            _clampApplied = true;
        }

        // ----------------------------------------------------------------------------------
        // Avanco de tempo
        // ----------------------------------------------------------------------------------

        /// <summary>Avanca a simulacao consumindo tempo real em ticks de tamanho fixo.</summary>
        public void Advance(float deltaTime)
        {
            if (State.IsOver) return;

            _accumulator += deltaTime;
            int guard = 0;
            while (_accumulator >= FixedDelta && guard++ < 8)
            {
                _accumulator -= FixedDelta;
                StepFixed();
            }
        }

        /// <summary>Um tick fixo. Usado direto nos testes para rodar uma partida sem relogio real.</summary>
        public void StepFixed()
        {
            if (State.IsOver) return;

            Tick++;
            ApplyPendingCommands();
            State.PhaseElapsed += FixedDelta;

            switch (State.Phase)
            {
                case PhaseId.Dia: TickDia(); break;
                case PhaseId.Noite: TickNoite(); break;
            }
        }

        // ----------------------------------------------------------------------------------
        // Fases
        // ----------------------------------------------------------------------------------

        private void EnterPhase(PhaseId phase)
        {
            State.Phase = phase;
            State.PhaseElapsed = 0f;
            _clampApplied = false;

            switch (phase)
            {
                case PhaseId.Dia:
                    State.PhaseDuration = _content.Rules.DiaSeconds;
                    _currentTurnWaves = _content.GetTurnWaves(State.TurnNumber, _playerCount);
                    MatchFactory.RepopulateHarvestNodes(State, _content, _spawnRng, _events);
                    MatchFactory.RepopulateCaches(State, _content, _spawnRng, _events);
                    ResetHeroesToTownHall();
                    for (int i = 0; i < State.Players.Count; i++)
                    {
                        State.Players[i].IsReady = false;
                        // O decaimento dos achados zera no amanhecer: cada dia tem os seus
                        // primeiros Esconderijos, que sao os que valem.
                        State.Players[i].CachesFoundToday = 0;
                    }
                    break;

                case PhaseId.Noite:
                    State.PhaseDuration = _content.Rules.NoiteSeconds;
                    State.SurgeIndex = 0;
                    State.SurgeElapsed = 0f;
                    State.InBreather = false;
                    _events.Emit(SimEventType.SurgeStarted, EntityId.None, 0f, State.CityCenter,
                                 intValue: 0);
                    break;

                case PhaseId.Fim:
                    State.PhaseDuration = 0f;
                    break;
            }

            _events.Emit(SimEventType.PhaseChanged, EntityId.None, State.PhaseDuration,
                         State.CityCenter, intValue: (int)phase);
        }

        /// <summary>
        /// O Dia. Nenhum inimigo no mapa: construir, colher, explorar a mata e escolher carta,
        /// tudo simultaneo e tudo opcional. O combate fica ligado mesmo sem monstros porque o
        /// auto-ataque tambem limpa nada — desligar so criaria um caminho de codigo a mais.
        /// </summary>
        private void TickDia()
        {
            HeroSystem.Tick(State, _content, FixedDelta, _events, combatEnabled: false);
            WorldStreamer.Tick(State, _content, _events);
            ExplorationSystem.Tick(State, _content, _events);

            int connected = Math.Max(1, State.ConnectedPlayerCount());
            bool everyoneReady = State.ReadyCount() >= connected;
            bool timeUp = State.PhaseElapsed >= State.PhaseDuration;

            if (everyoneReady || timeUp)
                EnterPhase(PhaseId.Noite);
        }

        /// <summary>
        /// A Noite. Dura exatamente <see cref="MatchRulesSpec.NoiteSeconds"/> — nunca acaba porque
        /// a lista de Investidas acabou, o que era verdade no modelo anterior. As Investidas agora
        /// preenchem a noite; se sobrarem, param no corte; se faltarem, o ultimo trecho e limpeza.
        /// </summary>
        private void TickNoite()
        {
            AdvanceSurges();

            TowerSystem.Tick(State, _content, FixedDelta, _events);
            MonsterSystem.Tick(State, _content, FixedDelta, _events);
            HeroSystem.Tick(State, _content, FixedDelta, _events, combatEnabled: true);
            // O mundo continua existindo de noite: quem ficou la fora continua achando coisa —
            // e continua sendo cacado por isso.
            WorldStreamer.Tick(State, _content, _events);
            ExplorationSystem.Tick(State, _content, _events);

            if (State.PhaseElapsed >= State.PhaseDuration)
                BreakDawn();
        }

        /// <summary>
        /// Toca a agenda de Investidas dentro da noite. Para de gerar antes do amanhecer para que
        /// o ultimo minuto seja limpeza de campo, e nao uma onda que nasce so para ser dissolvida.
        /// </summary>
        private void AdvanceSurges()
        {
            var surges = _currentTurnWaves?.Surges;
            if (surges == null || surges.Length == 0 || State.SurgeIndex >= surges.Length) return;

            float remaining = State.PhaseDuration - State.PhaseElapsed;
            if (remaining <= _content.Rules.SpawnCutoffBeforeDawn) return;

            var surge = surges[State.SurgeIndex];
            float previous = State.SurgeElapsed;
            State.SurgeElapsed += FixedDelta;

            if (!State.InBreather)
            {
                SpawnSystem.TickSurge(State, _content, surge, previous, State.SurgeElapsed, _spawnRng, _events);

                if (State.SurgeElapsed >= surge.DurationSeconds)
                {
                    State.InBreather = true;
                    State.SurgeElapsed = 0f;
                    _events.Emit(SimEventType.BreatherStarted, EntityId.None, surge.BreatherSeconds,
                                 State.CityCenter, intValue: State.SurgeIndex);
                }
                return;
            }

            if (State.SurgeElapsed < surge.BreatherSeconds) return;

            State.InBreather = false;
            State.SurgeElapsed = 0f;
            State.SurgeIndex++;

            if (State.SurgeIndex < surges.Length)
                _events.Emit(SimEventType.SurgeStarted, EntityId.None, 0f, State.CityCenter,
                             intValue: State.SurgeIndex);
        }

        /// <summary>
        /// O amanhecer. Resolve a noite inteira de uma vez e devolve o mapa ao jogador.
        ///
        /// Nao e uma fase: e um instante entre duas. O draft que ele oferece fica pendente e
        /// pode ser escolhido a qualquer momento do Dia — nao existe mais uma tela de Balanco
        /// que congela o mundo enquanto quatro pessoas leem tres cartas cada uma.
        /// </summary>
        private void BreakDawn()
        {
            // A luz dissolve o que sobrou. Ficcao e regra na mesma linha: o que nao foi morto
            // ate o alvorecer recua, e o jogador ve isso acontecer.
            SpawnSystem.DissolveAll(State, _events);
            AbsorbCarriedLoot();
            BuildSystem.CollectTurnProduction(State, _content, _events);
            EconomySystem.ResolveLevelUps(State, _content, _events);
            EconomySystem.OfferDrafts(State, _content, _draftRng, _events);
            PlantGraves();

            if (State.TurnNumber >= _content.Rules.TotalTurns)
            {
                State.Outcome = MatchOutcome.Vitoria;
                _events.Emit(SimEventType.MatchEnded, intValue: (int)MatchOutcome.Vitoria);
                EnterPhase(PhaseId.Fim);
                return;
            }

            State.TurnNumber++;
            for (int i = 0; i < State.Players.Count; i++) State.Players[i].TurnsSurvived++;
            _events.Emit(SimEventType.TurnStarted, EntityId.None, 0f, State.CityCenter,
                         intValue: State.TurnNumber);
            EnterPhase(PhaseId.Dia);
        }

        // ----------------------------------------------------------------------------------
        // Utilidades de fronteira de fase
        // ----------------------------------------------------------------------------------

        private void ResetHeroesToTownHall()
        {
            for (int i = 0; i < State.Heroes.Count; i++)
            {
                var hero = State.Heroes[i];
                var spec = _content.GetHero(hero.Def);
                hero.Position = State.CityCenter;
                hero.MoveInput = Vec2.Zero;
                hero.IsSpectre = false;
                hero.RespawnRemaining = 0f;
                hero.Health = spec?.MaxHealth ?? 100f;
            }
        }

        /// <summary>Aspiracao de loot no Balanco: tudo que os quatro carregavam e creditado.</summary>
        private void AbsorbCarriedLoot()
        {
            for (int i = 0; i < State.Heroes.Count; i++)
            {
                var hero = State.Heroes[i];
                if (hero.CarriedTotal <= 0f) continue;

                State.SiloWood = Math.Min(State.SiloWood + hero.CarriedWood, State.SiloCapacity);
                State.Stone += hero.CarriedStone;

                var player = State.GetPlayer(hero.Owner);
                if (player != null) player.Gold += hero.CarriedGold;

                hero.CarriedWood = 0f;
                hero.CarriedStone = 0f;
                hero.CarriedGold = 0f;
            }
        }

        /// <summary>
        /// Quem morreu e nao teve a Urna resgatada planta um Tumulo no proprio Quadrante.
        /// A punicao da morte e ESPACIAL e permanente, nunca um game over.
        /// </summary>
        private void PlantGraves()
        {
            for (int i = 0; i < State.Heroes.Count; i++)
            {
                var hero = State.Heroes[i];
                if (!hero.HasPendingUrn) continue;
                hero.HasPendingUrn = false;

                var player = State.GetPlayer(hero.Owner);
                if (player == null) continue;

                foreach (var cell in State.Grid.CellsInQuadrant(player.Quadrant))
                {
                    if (!State.Grid.IsFree(cell)) continue;
                    State.Grid.Set(cell, CellState.Tumulo, EntityId.None);
                    _events.Emit(SimEventType.HeroDied, hero.Id, 0f, cell.Center,
                                 player: player.Id, cell: cell, intValue: 1);
                    break;
                }
            }
        }

        // ----------------------------------------------------------------------------------
        // Consultas para UI e apresentacao (somente leitura)
        // ----------------------------------------------------------------------------------

        public SurgeSpec NextSurge()
        {
            var surges = _currentTurnWaves?.Surges;
            if (surges == null || surges.Length == 0) return null;
            int index = State.Phase == PhaseId.Dia ? 0 : Math.Min(State.SurgeIndex, surges.Length - 1);
            return surges[index];
        }

        public void EvaluateForecast(LaneForecast[] destination)
            => ForecastSystem.EvaluateAll(State, _content, NextSurge(), destination);

        /// <summary>Metricas do "e se eu colocar aqui" — alimenta o HUD durante o arrasto do predio.</summary>
        public LaneForecast PreviewPlacement(Lane lane, DefId def, GridCoord cell)
            => ForecastSystem.Evaluate(State, _content, lane, NextSurge(), cell, def);

        /// <summary>
        /// O numero que faz o jogo existir: o antes-e-depois de colocar um predio naquele tile.
        ///
        /// Construir para fora sobe o DPS e ENCURTA o corredor da mesma Faixa. O HUD imprime as
        /// duas colunas lado a lado enquanto o jogador ainda esta com a carta na mao — e o
        /// arrependimento antecipado, nao a punicao depois, que produz a decisao interessante.
        /// </summary>
        public PlacementImpact EvaluatePlacement(DefId def, GridCoord cell)
        {
            var lane = LaneGeometry.LaneOf(State.CityCenter, cell.Center);
            var surge = NextSurge();

            var before = ForecastSystem.Evaluate(State, _content, lane, surge);
            var after = ForecastSystem.Evaluate(State, _content, lane, surge, cell, def);

            return new PlacementImpact
            {
                Lane = lane,
                ApproachBefore = before.ApproachSeconds,
                ApproachAfter = after.ApproachSeconds,
                PerimeterBefore = State.Grid.ExposedPerimeter(),
                PerimeterAfter = ForecastSystem.PerimeterWith(State, cell),
                BandBefore = before.Band,
                BandAfter = after.Band,
                LeakBefore = before.EstimatedLeak,
                LeakAfter = after.EstimatedLeak,
                DpsBefore = before.LaneDps,
                DpsAfter = after.LaneDps
            };
        }

        public float SecondsRemainingInPhase() => Math.Max(0f, State.PhaseDuration - State.PhaseElapsed);

        // ----------------------------------------------------------------------------------
        // Atalhos de teste
        //
        // Existem porque uma partida completa leva ~30 minutos, e ninguem calibra o turno 8
        // jogando os sete anteriores toda vez. Ficam agrupados e prefixados aqui para que seja
        // obvio, em qualquer leitura futura, que nao fazem parte das regras do jogo.
        // ----------------------------------------------------------------------------------

        /// <summary>SO PARA TESTE: encerra a fase atual imediatamente.</summary>
        public void DebugSkipPhase()
        {
            switch (State.Phase)
            {
                case PhaseId.Dia:
                    State.PhaseElapsed = State.PhaseDuration + 1f;
                    break;

                case PhaseId.Noite:
                    SpawnSystem.DissolveAll(State, _events);
                    BreakDawn();
                    break;
            }
        }

        /// <summary>SO PARA TESTE: pula a noite inteira, indo direto para o Dia seguinte.</summary>
        public void DebugSkipTurn()
        {
            if (State.Phase == PhaseId.Dia) EnterPhase(PhaseId.Noite);
            if (State.Phase != PhaseId.Noite) return;

            SpawnSystem.DissolveAll(State, _events);
            EconomySystem.AutoResolvePendingDrafts(State, _content, _draftRng, _events);
            BreakDawn();
        }

        /// <summary>SO PARA TESTE: entrega uma carta aleatoria do pool ao jogador.</summary>
        public void DebugGrantCard(PlayerId player)
        {
            var pool = _content.TowerPool;
            var target = State.GetPlayer(player);
            if (pool == null || pool.Count == 0 || target == null) return;
            target.Hand.Add(pool[_draftRng.Range(0, pool.Count)]);
        }
    }
}
