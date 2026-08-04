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

        public MatchSimulation(IContentDatabase content, int seed, int playerCount, ILogSink log = null)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _log = log ?? NullLogSink.Instance;
            _spawnRng = Rng.ForChannel(seed, 1, 0);
            _draftRng = Rng.ForChannel(seed, 2, 0);

            State = MatchFactory.Create(content, seed, playerCount);
            _playerCount = State.Players.Count;
            _currentTurnWaves = content.GetTurnWaves(State.TurnNumber, _playerCount);
            EnterPhase(PhaseId.Preparo);
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
            if (State.Phase != PhaseId.Preparo || _clampApplied) return;

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

            ApplyPendingCommands();
            State.PhaseElapsed += FixedDelta;

            switch (State.Phase)
            {
                case PhaseId.Preparo: TickPreparo(); break;
                case PhaseId.Assalto: TickAssalto(); break;
                case PhaseId.Balanco: TickBalanco(); break;
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
                case PhaseId.Preparo:
                    State.PhaseDuration = State.TurnNumber <= 3
                        ? _content.Rules.PreparoFirstActSeconds
                        : _content.Rules.PreparoMaxSeconds;
                    _currentTurnWaves = _content.GetTurnWaves(State.TurnNumber, _playerCount);
                    MatchFactory.RepopulateHarvestNodes(State, _content, _spawnRng, _events);
                    ResetHeroesToTownHall();
                    for (int i = 0; i < State.Players.Count; i++) State.Players[i].IsReady = false;
                    break;

                case PhaseId.Assalto:
                    State.PhaseDuration = 0f; // duracao vem das Investidas
                    State.SurgeIndex = 0;
                    State.SurgeElapsed = 0f;
                    State.InBreather = false;
                    _events.Emit(SimEventType.SurgeStarted, EntityId.None, 0f, State.CityCenter,
                                 intValue: 0);
                    break;

                case PhaseId.Balanco:
                    State.PhaseDuration = _content.Rules.BalancoSeconds;
                    SpawnSystem.DissolveAll(State, _events);
                    AbsorbCarriedLoot();
                    BuildSystem.CollectTurnProduction(State, _content, _events);
                    EconomySystem.ResolveLevelUps(State, _content, _events);
                    EconomySystem.OfferDrafts(State, _content, _draftRng, _events);
                    PlantGraves();
                    break;

                case PhaseId.Fim:
                    State.PhaseDuration = 0f;
                    break;
            }

            _events.Emit(SimEventType.PhaseChanged, EntityId.None, State.PhaseDuration,
                         State.CityCenter, intValue: (int)phase);
        }

        private void TickPreparo()
        {
            HeroSystem.Tick(State, _content, FixedDelta, _events, combatEnabled: false);

            int connected = Math.Max(1, State.ConnectedPlayerCount());
            bool everyoneReady = State.ReadyCount() >= connected;
            bool timeUp = State.PhaseElapsed >= State.PhaseDuration;

            if (everyoneReady || timeUp)
                EnterPhase(PhaseId.Assalto);
        }

        private void TickAssalto()
        {
            var surges = _currentTurnWaves?.Surges;
            if (surges == null || surges.Length == 0)
            {
                EnterPhase(PhaseId.Balanco);
                return;
            }

            if (State.SurgeIndex >= surges.Length)
            {
                EnterPhase(PhaseId.Balanco);
                return;
            }

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
            }
            else if (State.SurgeElapsed >= surge.BreatherSeconds)
            {
                State.InBreather = false;
                State.SurgeElapsed = 0f;
                State.SurgeIndex++;

                if (State.SurgeIndex >= surges.Length)
                {
                    EnterPhase(PhaseId.Balanco);
                    return;
                }

                _events.Emit(SimEventType.SurgeStarted, EntityId.None, 0f, State.CityCenter,
                             intValue: State.SurgeIndex);
            }

            TowerSystem.Tick(State, _content, FixedDelta, _events);
            MonsterSystem.Tick(State, _content, FixedDelta, _events);
            HeroSystem.Tick(State, _content, FixedDelta, _events, combatEnabled: true);
        }

        private void TickBalanco()
        {
            bool allResolved = true;
            for (int i = 0; i < State.Players.Count; i++)
                if (State.Players[i].PendingDraftPicks > 0) { allResolved = false; break; }

            if (!allResolved && State.PhaseElapsed < State.PhaseDuration) return;

            if (!allResolved)
                EconomySystem.AutoResolvePendingDrafts(State, _content, _draftRng, _events);

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
            EnterPhase(PhaseId.Preparo);
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
            int index = State.Phase == PhaseId.Preparo ? 0 : Math.Min(State.SurgeIndex, surges.Length - 1);
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
    }
}
