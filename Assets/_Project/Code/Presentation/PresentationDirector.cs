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
        private readonly PlaceholderFactory _factory;
        private readonly BoardRenderer _board;
        private readonly EffectPool _effects;
        private readonly Transform _root;

        private readonly Dictionary<EntityId, EntityView> _views = new Dictionary<EntityId, EntityView>();
        private readonly List<SimEvent> _drained = new List<SimEvent>(256);
        private readonly List<EntityId> _toRemove = new List<EntityId>(32);

        private bool _boardDirty = true;

        public BoardRenderer Board => _board;

        public PresentationDirector(MatchSimulation sim, Transform root)
        {
            _sim = sim;
            _root = root;
            _factory = new PlaceholderFactory(root);
            _board = new BoardRenderer(_factory, root, sim.State.Grid, sim.Content);
            _effects = new EffectPool(root, _factory);

            SpawnInitialViews();
        }

        private void SpawnInitialViews()
        {
            foreach (var hero in _sim.State.Heroes) EnsureHeroView(hero);
            foreach (var node in _sim.State.Nodes) EnsureNodeView(node);
            EnsureTownHallView();
        }

        private void EnsureTownHallView()
        {
            var grid = _sim.State.Grid;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Prefeitura";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root, false);
            float size = grid.TownHallSize * 0.92f;
            go.transform.localScale = new Vector3(size, 1.6f, size);
            go.transform.position = GridToWorld.ToWorld(grid.Center, 0.8f);
            go.GetComponent<Renderer>().sharedMaterial =
                _factory.GetMaterial(new Color(0.88f, 0.82f, 0.55f));
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
                    if (_views.TryGetValue(e.Entity, out var deadView))
                    {
                        deadView.Despawn();
                        _views.Remove(e.Entity);
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
                    if (_views.TryGetValue(e.Entity, out var towerView))
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
                    if (_views.TryGetValue(e.Entity, out var merged))
                    {
                        merged.PlayAction(ViewActionId.Build, 0.3f);
                        // Tier maior = mais alto. A silhueta conta o poder sem numero na tela.
                        var t = merged.Visual;
                        t.localScale = new Vector3(t.localScale.x, t.localScale.y * 1.3f, t.localScale.z);
                        t.localPosition = new Vector3(0f, t.localScale.y * 0.5f, 0f);
                    }
                    break;

                case SimEventType.TowerDestroyed:
                    if (_views.TryGetValue(e.Entity, out var destroyed))
                    {
                        destroyed.Despawn();
                        _views.Remove(e.Entity);
                    }
                    _effects.Ring(GridToWorld.ToWorld(e.Position), 1.2f, new Color(0.6f, 0.2f, 0.15f));
                    _boardDirty = true;
                    break;

                case SimEventType.TowerDamaged:
                    if (_views.TryGetValue(e.Entity, out var hurt))
                        hurt.PlayAction(ViewActionId.Hit, 0.12f);
                    break;

                case SimEventType.RubbleCleared:
                case SimEventType.CityLevelUp:
                    _boardDirty = true;
                    break;

                case SimEventType.HeroAttacked:
                    if (_views.TryGetValue(e.Entity, out var heroView))
                        heroView.PlayAction(ViewActionId.Attack, 0.12f);
                    break;

                case SimEventType.HeroDied:
                    if (_views.TryGetValue(e.Entity, out var died))
                        died.PlayAction(ViewActionId.Die, 0.4f);
                    _effects.Ring(GridToWorld.ToWorld(e.Position), 1.5f, new Color(0.9f, 0.9f, 0.95f));
                    _boardDirty = true;
                    break;

                case SimEventType.HeroDeposited:
                    _effects.Ring(GridToWorld.ToWorld(e.Position), 1.8f, new Color(0.4f, 0.9f, 0.6f), 0.3f);
                    break;

                case SimEventType.NodeDepleted:
                    if (_views.TryGetValue(e.Entity, out var node))
                    {
                        node.Despawn();
                        _views.Remove(e.Entity);
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
            foreach (var kv in _views)
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
                if (_views.TryGetValue(_toRemove[i], out var view)) view.Despawn();
                _views.Remove(_toRemove[i]);
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
                if (!_views.TryGetValue(m.Id, out var view)) { EnsureMonsterView(m); continue; }
                view.SetWorldPosition(GridToWorld.ToWorld(m.Position));
                view.SetFacing(GridToWorld.DirectionToWorld(m.Facing));
                view.SetHealthRatio(m.MaxHealth > 0f ? m.Health / m.MaxHealth : 0f);
            }

            for (int i = 0; i < state.Heroes.Count; i++)
            {
                var h = state.Heroes[i];
                if (!_views.TryGetValue(h.Id, out var view)) { EnsureHeroView(h); continue; }
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
            if (_views.TryGetValue(m.Id, out var existing)) return existing;

            var style = PlaceholderVisuals.Get(m.Def);
            var view = _factory.CreateView($"Monstro_{m.Id}", style, GridToWorld.ToWorld(m.Position));
            view.Bind(m.Id);
            view.SnapTo(GridToWorld.ToWorld(m.Position));
            _views[m.Id] = view;
            return view;
        }

        private EntityView EnsureTowerView(TowerState t)
        {
            if (_views.TryGetValue(t.Id, out var existing)) return existing;

            var style = PlaceholderVisuals.Get(t.Def);
            var view = _factory.CreateView($"Predio_{t.Id}", style, GridToWorld.ToWorld(t.Cell));
            view.Bind(t.Id);
            view.SnapTo(GridToWorld.ToWorld(t.Cell));
            _views[t.Id] = view;
            return view;
        }

        private EntityView EnsureHeroView(HeroState h)
        {
            if (_views.TryGetValue(h.Id, out var existing)) return existing;

            var style = PlaceholderVisuals.Get(h.Def);
            style.Color = PlaceholderVisuals.PlayerColor(h.Owner.Index);
            var view = _factory.CreateView($"Heroi_{h.Owner.Index}", style, GridToWorld.ToWorld(h.Position));
            view.Bind(h.Id);
            view.SnapTo(GridToWorld.ToWorld(h.Position));
            _views[h.Id] = view;
            return view;
        }

        private EntityView EnsureNodeView(HarvestNodeState n)
        {
            if (_views.TryGetValue(n.Id, out var existing)) return existing;

            var style = new VisualStyle
            {
                Shape = n.Kind == HarvestNodeKind.Arvore ? PrimitiveShape.Cylinder
                      : n.Kind == HarvestNodeKind.Rocha ? PrimitiveShape.Sphere
                      : PrimitiveShape.Cube,
                Color = PlaceholderVisuals.ResourceColor(n.Kind),
                Scale = n.Kind == HarvestNodeKind.Arvore ? 0.5f : 0.7f,
                Height = n.Kind == HarvestNodeKind.Arvore ? 2f : 0.7f
            };

            var view = _factory.CreateView($"Recurso_{n.Kind}_{n.Id}", style, GridToWorld.ToWorld(n.Position));
            view.Bind(n.Id);
            view.SnapTo(GridToWorld.ToWorld(n.Position));
            _views[n.Id] = view;
            return view;
        }

        public void Dispose()
        {
            _effects.Dispose();
            _factory.Dispose();
        }
    }
}
