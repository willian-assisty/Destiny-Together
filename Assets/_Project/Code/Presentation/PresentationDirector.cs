using System.Collections.Generic;
using DestinyTogether.Core;
using DestinyTogether.Data;
using DestinyTogether.Sim;
using UnityEngine;

// Desambigua do UnityEngine.EntityId introduzido no Unity 6.
using EntityId = DestinyTogether.Sim.EntityId;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// Traduz a simulacao em mundo visivel. Le eventos e estado; NUNCA escreve na simulacao.
    ///
    /// Dois canais distintos, de proposito:
    ///   1. EVENTOS (SimEventLog) -> coisas que ACONTECEM: nasceu, morreu, atirou, explodiu.
    ///      Sao discretos e nao podem ser inferidos de estado — se perdidos, o feedback some.
    ///   2. ESTADO -> onde as coisas ESTAO. Sincronizado por frame com interpolacao, porque a
    ///      simulacao anda a 20 Hz e a tela a 60+. Se um pacote de rede se perder, a posicao
    ///      se corrige sozinha no proximo sync; e por isso que posicao nao trafega como evento.
    /// </summary>
    public sealed class PresentationDirector
    {
        private readonly MatchSimulation _sim;
        private readonly ViewFactory _views;
        private readonly PlaceholderFactory _factory;
        private readonly BoardRenderer _board;
        private readonly EffectPool _effects;
        private readonly Transform _root;

        private readonly Dictionary<EntityId, EntityView> _viewsById = new Dictionary<EntityId, EntityView>();
        private readonly List<SimEvent> _drained = new List<SimEvent>(256);
        private readonly List<EntityId> _toRemove = new List<EntityId>(32);

        private bool _boardDirty = true;

        public BoardRenderer Board => _board;

        /// <param name="profile">
        /// Perfil visual. Null (ou entradas vazias) mantém tudo em primitivas — a arte pode
        /// entrar peça por peça sem nunca deixar o jogo em estado não-rodável.
        /// </param>
        public PresentationDirector(MatchSimulation sim, Transform root, VisualsProfile profile = null)
        {
            _sim = sim;
            _root = root;
            _views = new ViewFactory(root, profile);
            _factory = _views.Placeholders;
            _board = new BoardRenderer(_factory, root, sim.State.Grid, sim.Content, profile);
            _effects = new EffectPool(root, _factory);

            SpawnInitialViews();

            _views.ScatterProps(sim.State.CityCenter,
                                sim.State.Grid.Size * 0.5f + 3f,
                                sim.Content.Arena.OutskirtsRadius + 6f,
                                sim.State.MatchSeed);
        }

        private void SpawnInitialViews()
        {
            foreach (var hero in _sim.State.Heroes) EnsureHeroView(hero);
            foreach (var node in _sim.State.Nodes) EnsureNodeView(node);

            var grid = _sim.State.Grid;
            _views.CreateTownHall(GridToWorld.ToWorld(grid.Center), grid.TownHallSize);
        }

        // ------------------------------------------------------------------------------
        // Loop
        // ------------------------------------------------------------------------------

        public void Tick(float deltaTime)
        {
            ConsumeEvents();
            SyncTransforms();
            _effects.Tick(deltaTime);
            _board.TickPillars(deltaTime);

            if (_boardDirty)
            {
                _board.Refresh();
                _boardDirty = false;
            }
        }

        private void ConsumeEvents()
        {
            _drained.Clear();
            _sim.Events.DrainInto(_drained);

            for (int i = 0; i < _drained.Count; i++)
                Handle(_drained[i]);
        }

        private void Handle(in SimEvent e)
        {
            switch (e.Type)
            {
                case SimEventType.MonsterSpawned:
                {
                    var monster = _sim.State.GetMonster(e.Entity);
                    if (monster != null) EnsureMonsterView(monster);
                    _board.PulseLane((Lane)e.IntValue);
                    break;
                }

                case SimEventType.MonsterDied:
                    if (_viewsById.TryGetValue(e.Entity, out var deadView))
                    {
                        deadView.Despawn();
                        _viewsById.Remove(e.Entity);
                    }
                    break;

                case SimEventType.MonsterExploded:
                    _effects.Ring(GridToWorld.ToWorld(e.Position), e.Amount,
                                  new Color(1f, 0.55f, 0.1f), 0.35f);
                    break;

                case SimEventType.TowerFired:
                {
                    var target = _sim.State.GetMonster(e.Other);
                    if (target != null)
                    {
                        var style = PlaceholderVisuals.Get(e.Def);
                        _effects.Tracer(GridToWorld.ToWorld(e.Position),
                                        GridToWorld.ToWorld(target.Position), style.Color);
                    }
                    if (_viewsById.TryGetValue(e.Entity, out var towerView))
                        towerView.PlayAction(ViewActionId.Attack, 0.14f);
                    break;
                }

                case SimEventType.TowerBuilt:
                {
                    var tower = _sim.State.GetTower(e.Entity);
                    if (tower != null)
                    {
                        var view = EnsureTowerView(tower);
                        view.PlayAction(ViewActionId.Build, 0.28f);
                    }
                    _boardDirty = true;
                    break;
                }

                case SimEventType.TowerMerged:
                    if (_viewsById.TryGetValue(e.Entity, out var merged))
                    {
                        merged.PlayAction(ViewActionId.Build, 0.3f);
                        // Tier maior = mais alto. A silhueta conta o poder sem numero na tela.
                        merged.ScaleVisual(new Vector3(1f, 1.3f, 1f));
                    }
                    break;

                case SimEventType.TowerDestroyed:
                    if (_viewsById.TryGetValue(e.Entity, out var destroyed))
                    {
                        destroyed.Despawn();
                        _viewsById.Remove(e.Entity);
                    }
                    _effects.Ring(GridToWorld.ToWorld(e.Position), 1.2f, new Color(0.6f, 0.2f, 0.15f));
                    _boardDirty = true;
                    break;

                case SimEventType.TowerDamaged:
                    if (_viewsById.TryGetValue(e.Entity, out var hurt))
                        hurt.PlayAction(ViewActionId.Hit, 0.12f);
                    break;

                case SimEventType.RubbleCleared:
                case SimEventType.CityLevelUp:
                    _boardDirty = true;
                    break;

                case SimEventType.HeroAttacked:
                    if (_viewsById.TryGetValue(e.Entity, out var heroView))
                        heroView.PlayAction(ViewActionId.Attack, 0.12f);
                    break;

                case SimEventType.HeroDied:
                    if (_viewsById.TryGetValue(e.Entity, out var died))
                        died.PlayAction(ViewActionId.Die, 0.4f);
                    _effects.Ring(GridToWorld.ToWorld(e.Position), 1.5f, new Color(0.9f, 0.9f, 0.95f));
                    _boardDirty = true;
                    break;

                case SimEventType.HeroDeposited:
                    _effects.Ring(GridToWorld.ToWorld(e.Position), 1.8f, new Color(0.4f, 0.9f, 0.6f), 0.3f);
                    break;

                case SimEventType.NodeDepleted:
                    if (_viewsById.TryGetValue(e.Entity, out var node))
                    {
                        node.Despawn();
                        _viewsById.Remove(e.Entity);
                    }
                    break;

                case SimEventType.CityDamaged:
                    _effects.Ring(GridToWorld.ToWorld(e.Position), 2.4f, new Color(0.95f, 0.2f, 0.2f), 0.25f);
                    break;

                case SimEventType.PhaseChanged:
                    // Fronteira de fase: o mundo pode ter sido repovoado por completo.
                    RebuildTransientViews();
                    _boardDirty = true;
                    break;
            }
        }

        /// <summary>
        /// Reconcilia views com o estado. Chamado nas fronteiras de fase, quando entidades
        /// aparecem/somem em bloco (nos de recurso do turno, dissolucao dos monstros).
        /// </summary>
        private void RebuildTransientViews()
        {
            _toRemove.Clear();
            foreach (var kv in _viewsById)
            {
                var id = kv.Key;
                bool exists = _sim.State.GetMonster(id) != null
                              || _sim.State.GetTower(id) != null
                              || _sim.State.GetHero(id) != null
                              || _sim.State.GetNode(id) != null;
                if (!exists) _toRemove.Add(id);
            }

            for (int i = 0; i < _toRemove.Count; i++)
            {
                if (_viewsById.TryGetValue(_toRemove[i], out var view)) view.Despawn();
                _viewsById.Remove(_toRemove[i]);
            }

            foreach (var node in _sim.State.Nodes) EnsureNodeView(node);
            foreach (var hero in _sim.State.Heroes) EnsureHeroView(hero);
            foreach (var tower in _sim.State.Towers) EnsureTowerView(tower);
        }

        private void SyncTransforms()
        {
            var state = _sim.State;

            for (int i = 0; i < state.Monsters.Count; i++)
            {
                var m = state.Monsters[i];
                if (!_viewsById.TryGetValue(m.Id, out var view)) { EnsureMonsterView(m); continue; }
                view.SetWorldPosition(GridToWorld.ToWorld(m.Position));
                view.SetFacing(GridToWorld.DirectionToWorld(m.Facing));
                view.SetHealthRatio(m.MaxHealth > 0f ? m.Health / m.MaxHealth : 0f);
            }

            for (int i = 0; i < state.Heroes.Count; i++)
            {
                var h = state.Heroes[i];
                if (!_viewsById.TryGetValue(h.Id, out var view)) { EnsureHeroView(h); continue; }
                view.SetWorldPosition(GridToWorld.ToWorld(h.Position));
                view.SetFacing(GridToWorld.DirectionToWorld(h.Facing));

                // Espectro some, mas a view continua viva: o morto mantem camera e ping.
                // Nunca desativar a raiz — ela carrega o script que faz a view existir.
                var visual = view.Visual;
                if (visual != null && visual != view.transform)
                    visual.gameObject.SetActive(!h.IsSpectre);
            }
        }

        // ------------------------------------------------------------------------------
        // Criacao de views
        // ------------------------------------------------------------------------------

        private EntityView EnsureMonsterView(MonsterState m)
        {
            if (_viewsById.TryGetValue(m.Id, out var existing)) return existing;

            var pos = GridToWorld.ToWorld(m.Position);
            var view = _views.CreateEntityView($"Monstro_{m.Id}", m.Def, PlaceholderVisuals.Get(m.Def), pos);
            return Register(m.Id, view, pos);
        }

        private EntityView EnsureTowerView(TowerState t)
        {
            if (_viewsById.TryGetValue(t.Id, out var existing)) return existing;

            var pos = GridToWorld.ToWorld(t.Cell);
            var view = _views.CreateEntityView($"Predio_{t.Id}", t.Def, PlaceholderVisuals.Get(t.Def), pos);
            return Register(t.Id, view, pos);
        }

        private EntityView EnsureHeroView(HeroState h)
        {
            if (_viewsById.TryGetValue(h.Id, out var existing)) return existing;

            var pos = GridToWorld.ToWorld(h.Position);
            // A cor do jogador continua mandando no placeholder: saber de quem e o heroi e
            // informacao de jogo, nao decoracao. Com arte real, a distincao vem do modelo.
            var view = _views.CreateEntityView($"Heroi_{h.Owner.Index}", h.Def, PlaceholderVisuals.Get(h.Def),
                                               pos, PlaceholderVisuals.PlayerColor(h.Owner.Index));
            return Register(h.Id, view, pos);
        }

        private EntityView EnsureNodeView(HarvestNodeState n)
        {
            if (_viewsById.TryGetValue(n.Id, out var existing)) return existing;

            var style = new VisualStyle
            {
                Shape = n.Kind == HarvestNodeKind.Arvore ? PrimitiveShape.Cylinder
                      : n.Kind == HarvestNodeKind.Rocha ? PrimitiveShape.Sphere
                      : PrimitiveShape.Cube,
                Color = PlaceholderVisuals.ResourceColor(n.Kind),
                Scale = n.Kind == HarvestNodeKind.Arvore ? 0.5f : 0.7f,
                Height = n.Kind == HarvestNodeKind.Arvore ? 2f : 0.7f
            };

            var pos = GridToWorld.ToWorld(n.Position);
            var view = _views.CreateNodeView($"Recurso_{n.Kind}_{n.Id}", n.Kind, style, pos);
            return Register(n.Id, view, pos);
        }

        private EntityView Register(EntityId id, EntityView view, Vector3 position)
        {
            view.Bind(id);
            view.SnapTo(position);
            _viewsById[id] = view;
            return view;
        }

        public void Dispose()
        {
            _effects.Dispose();
            _views.Dispose();
        }
    }
}
