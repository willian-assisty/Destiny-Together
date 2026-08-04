using System.IO;
using DestinyTogether.App;
using DestinyTogether.Data;
using DestinyTogether.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DestinyTogether.EditorTools
{
    /// <summary>
    /// Ferramentas de projeto. Nada aqui e necessario para jogar — o Bootstrap monta tudo em
    /// runtime. Sao atalhos para quando o conteudo precisar virar asset editavel na mao.
    /// </summary>
    public static class ProjectTools
    {
        private const string ContentPath = "Assets/_Project/Content";
        private const string ScenePath = "Assets/_Project/Scenes/Arena.unity";

        [MenuItem("Destiny Together/Abrir Arena", false, 0)]
        public static void OpenArena()
        {
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else RebuildArenaScene();
        }

        /// <summary>Recria a cena do zero. Serve de rede de seguranca se o arquivo for corrompido.</summary>
        [MenuItem("Destiny Together/Recriar cena Arena", false, 1)]
        public static void RebuildArenaScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Bootstrap");
            go.AddComponent<Bootstrap>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            var guid = AssetDatabase.AssetPathToGUID(ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            Debug.Log($"[Destiny Together] Cena Arena recriada ({guid}) e definida como cena 0 do build.");
        }

        /// <summary>
        /// Gera assets editaveis a partir do conteudo padrao embutido. So e util quando alguem
        /// for balancear na mao: ate la, o codigo e a fonte da verdade e nao ha asset para
        /// dessincronizar.
        /// </summary>
        [MenuItem("Destiny Together/Gerar assets de conteudo padrao", false, 20)]
        public static void GenerateContentAssets()
        {
            var content = new DefaultContent();

            EnsureFolder(ContentPath);
            EnsureFolder($"{ContentPath}/Towers");
            EnsureFolder($"{ContentPath}/Monsters");
            EnsureFolder($"{ContentPath}/Heroes");
            EnsureFolder($"{ContentPath}/Rules");

            var database = ScriptableObject.CreateInstance<ContentDatabase>();

            foreach (var id in content.TowerPool)
            {
                var spec = content.GetTower(id);
                var asset = ScriptableObject.CreateInstance<TowerDefinition>();
                asset.DefName = NameOf(spec.DisplayName);
                asset.DisplayName = spec.DisplayName;
                asset.Description = spec.Description;
                asset.Tag = spec.Tag;
                asset.MaxHealth = spec.MaxHealth;
                asset.CanAttack = spec.CanAttack;
                asset.Damage = spec.Damage;
                asset.Range = spec.Range;
                asset.ShotInterval = spec.ShotInterval;
                asset.SplashRadius = spec.SplashRadius;
                asset.Targeting = spec.Targeting;
                asset.WoodPerShot = spec.WoodPerShot;
                asset.SlowFactor = spec.SlowFactor;
                asset.SlowDuration = spec.SlowDuration;
                asset.Knockback = spec.Knockback;
                asset.ProducesResource = spec.ProducesResource;
                asset.ProductionPerTurn = spec.ProductionPerTurn;
                asset.ProductionPerSameNeighbour = spec.ProductionPerSameNeighbour;
                asset.AdjacentFireRateBonus = spec.AdjacentFireRateBonus;
                asset.AdjacentRangeBonus = spec.AdjacentRangeBonus;
                asset.SiloCapacityBonus = spec.SiloCapacityBonus;
                asset.ApproachDelay = spec.ApproachDelay;
                asset.PlaceholderColor = PlaceholderVisuals.Get(id).Color;

                AssetDatabase.CreateAsset(asset, $"{ContentPath}/Towers/Predio_{asset.DefName}.asset");
                database.Towers.Add(asset);
            }

            foreach (var id in content.HeroPool)
            {
                var spec = content.GetHero(id);
                var asset = ScriptableObject.CreateInstance<HeroDefinition>();
                asset.DefName = NameOf(spec.DisplayName);
                asset.DisplayName = spec.DisplayName;
                asset.Class = spec.Class;
                asset.MoveSpeed = spec.MoveSpeed;
                asset.MaxHealth = spec.MaxHealth;
                asset.AttackDamage = spec.AttackDamage;
                asset.AttackRadius = spec.AttackRadius;
                asset.AttackInterval = spec.AttackInterval;
                asset.AttackConeHalfAngle = spec.AttackConeHalfAngle;
                asset.CarryCapacity = spec.CarryCapacity;
                asset.HarvestSpeedMultiplier = spec.HarvestSpeedMultiplier;
                asset.DepositTime = spec.DepositTime;
                asset.RespawnDelay = spec.RespawnDelay;
                asset.PlaceholderColor = PlaceholderVisuals.Get(id).Color;

                AssetDatabase.CreateAsset(asset, $"{ContentPath}/Heroes/Heroi_{asset.DefName}.asset");
                database.Heroes.Add(asset);
            }

            foreach (var name in new[]
            {
                DefaultContent.Enxame, DefaultContent.Estourador, DefaultContent.Bruto,
                DefaultContent.Cuspidor, DefaultContent.Ninho, DefaultContent.MaeAranha
            })
            {
                var spec = content.GetMonster(DefaultContent.Def(name));
                if (spec == null) continue;

                var asset = ScriptableObject.CreateInstance<MonsterDefinition>();
                asset.DefName = name;
                asset.DisplayName = spec.DisplayName;
                asset.Archetype = spec.Archetype;
                asset.MaxHealth = spec.MaxHealth;
                asset.Speed = spec.Speed;
                asset.Armor = spec.Armor;
                asset.SlowResistance = spec.SlowResistance;
                asset.BodyRadius = spec.BodyRadius;
                asset.ContactDamage = spec.ContactDamage;
                asset.AttackInterval = spec.AttackInterval;
                asset.AttackRange = spec.AttackRange;
                asset.ExplodesOnDeath = spec.ExplodesOnDeath;
                asset.ExplosionRadius = spec.ExplosionRadius;
                asset.ExplosionDamage = spec.ExplosionDamage;
                asset.IsStationary = spec.IsStationary;
                asset.SpawnInterval = spec.SpawnInterval;
                asset.SpawnCount = spec.SpawnCount;
                asset.XpReward = spec.XpReward;
                asset.GoldReward = spec.GoldReward;
                asset.PlaceholderColor = PlaceholderVisuals.Get(spec.Id).Color;

                AssetDatabase.CreateAsset(asset, $"{ContentPath}/Monsters/Monstro_{name}.asset");
                database.Monsters.Add(asset);
            }

            var rules = ScriptableObject.CreateInstance<MatchRulesDefinition>();
            AssetDatabase.CreateAsset(rules, $"{ContentPath}/Rules/Regras_Padrao.asset");
            database.RulesAsset = rules;

            var arena = ScriptableObject.CreateInstance<ArenaDefinition>();
            AssetDatabase.CreateAsset(arena, $"{ContentPath}/Rules/Arena_Padrao.asset");
            database.ArenaAsset = arena;

            AssetDatabase.CreateAsset(database, $"{ContentPath}/ContentDatabase.asset");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = database;
            Debug.Log($"[Destiny Together] Assets gerados em {ContentPath}. " +
                      "Arraste o ContentDatabase para o campo Content do Bootstrap para usa-los.");
        }

        /// <summary>Roda uma partida inteira sem abrir cena — o teste de fumaca mais rapido que existe.</summary>
        [MenuItem("Destiny Together/Simular partida no console", false, 40)]
        public static void SimulateHeadless()
        {
            var content = new DefaultContent();
            var sim = new MatchSimulation(content, 12345, 4);
            for (int i = 0; i < sim.State.Players.Count; i++) sim.State.Players[i].IsAutomaton = true;

            int steps = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!sim.State.IsOver && steps < 200000)
            {
                sim.StepFixed();
                sim.Events.Clear();
                steps++;
            }
            watch.Stop();

            Debug.Log($"[Destiny Together] Partida headless: {sim.State.Outcome} no turno " +
                      $"{sim.State.TurnNumber}, {steps} ticks ({steps * MatchSimulation.FixedDelta:0}s de jogo) " +
                      $"em {watch.ElapsedMilliseconds}ms reais. HP final {sim.State.TownHallHealth:0}.");
        }

        private static string NameOf(string displayName) => displayName.Replace(" ", "").Replace("-", "");

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
