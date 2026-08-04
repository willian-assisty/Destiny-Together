using System.Collections.Generic;
using DestinyTogether.Sim;
using UnityEngine;

namespace DestinyTogether.Data
{
    /// <summary>
    /// Asset que implementa IContentDatabase bakeando os ScriptableObjects em specs imutaveis.
    /// Qualquer lista vazia cai para o DefaultContent, entao o jogo nunca fica meio configurado:
    /// o designer pode substituir UM predio sem precisar autorar os outros nove.
    /// </summary>
    [CreateAssetMenu(menuName = "Destiny Together/Base de Conteudo", fileName = "ContentDatabase")]
    public sealed class ContentDatabase : ScriptableObject, IContentDatabase
    {
        [Header("Regras e arena (vazio = padrao embutido)")]
        public MatchRulesDefinition RulesAsset;
        public ArenaDefinition ArenaAsset;

        [Header("Conteudo")]
        public List<TowerDefinition> Towers = new List<TowerDefinition>();
        public List<MonsterDefinition> Monsters = new List<MonsterDefinition>();
        public List<HeroDefinition> Heroes = new List<HeroDefinition>();

        private readonly Dictionary<DefId, TowerSpec> _towers = new Dictionary<DefId, TowerSpec>();
        private readonly Dictionary<DefId, MonsterSpec> _monsters = new Dictionary<DefId, MonsterSpec>();
        private readonly Dictionary<DefId, HeroSpec> _heroes = new Dictionary<DefId, HeroSpec>();
        private readonly List<DefId> _towerPool = new List<DefId>();
        private readonly List<DefId> _heroPool = new List<DefId>();

        private DefaultContent _fallback;
        private bool _baked;

        public MatchRulesSpec Rules { get; private set; }
        public ArenaSpec Arena { get; private set; }
        public IReadOnlyList<DefId> TowerPool { get { EnsureBaked(); return _towerPool; } }
        public IReadOnlyList<DefId> HeroPool { get { EnsureBaked(); return _heroPool; } }
        public int ContentHash { get; private set; }

        private void OnEnable() => _baked = false;

        /// <summary>Rebake explicito: chamado ao entrar em Play e pelo validador do editor.</summary>
        public void Rebake()
        {
            _baked = false;
            EnsureBaked();
        }

        private void EnsureBaked()
        {
            if (_baked) return;
            _baked = true;

            _fallback = new DefaultContent();
            _towers.Clear(); _monsters.Clear(); _heroes.Clear();
            _towerPool.Clear(); _heroPool.Clear();

            Rules = RulesAsset != null ? RulesAsset.Bake() : _fallback.Rules;
            Arena = ArenaAsset != null ? ArenaAsset.Bake() : _fallback.Arena;

            foreach (var t in Towers)
            {
                if (t == null) continue;
                var spec = t.Bake();
                _towers[spec.Id] = spec;
                if (!_towerPool.Contains(spec.Id)) _towerPool.Add(spec.Id);
            }

            foreach (var m in Monsters)
            {
                if (m == null) continue;
                var spec = m.Bake();
                _monsters[spec.Id] = spec;
            }

            foreach (var h in Heroes)
            {
                if (h == null) continue;
                var spec = h.Bake();
                _heroes[spec.Id] = spec;
                if (!_heroPool.Contains(spec.Id)) _heroPool.Add(spec.Id);
            }

            if (_towerPool.Count == 0) _towerPool.AddRange(_fallback.TowerPool);
            if (_heroPool.Count == 0) _heroPool.AddRange(_fallback.HeroPool);

            unchecked
            {
                int hash = 17;
                foreach (var kv in _towers) hash = hash * 31 + kv.Key.Value;
                foreach (var kv in _monsters) hash = hash * 31 + kv.Key.Value;
                foreach (var kv in _heroes) hash = hash * 31 + kv.Key.Value;
                ContentHash = hash == 0 ? _fallback.ContentHash : hash;
            }
        }

        public TowerSpec GetTower(DefId id)
        {
            EnsureBaked();
            return _towers.TryGetValue(id, out var s) ? s : _fallback.GetTower(id);
        }

        public MonsterSpec GetMonster(DefId id)
        {
            EnsureBaked();
            return _monsters.TryGetValue(id, out var s) ? s : _fallback.GetMonster(id);
        }

        public HeroSpec GetHero(DefId id)
        {
            EnsureBaked();
            return _heroes.TryGetValue(id, out var s) ? s : _fallback.GetHero(id);
        }

        public TurnWaveSpec GetTurnWaves(int turnNumber, int playerCount)
        {
            EnsureBaked();
            return _fallback.GetTurnWaves(turnNumber, playerCount);
        }
    }
}
