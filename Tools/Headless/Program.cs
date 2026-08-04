using System;
using System.Collections.Generic;
using DestinyTogether.Core;
using DestinyTogether.Data;
using DestinyTogether.Sim;

/// <summary>
/// Roda partidas inteiras fora do Unity para medir o ciclo dia/noite.
///
/// Duas perguntas, e a segunda so existe por causa do ciclo:
///
///   1. Quantas noites um time competente sobrevive? Cinco e a vitoria.
///   2. Explorar compensa? Comparo o MESMO time jogando de dois jeitos — um que so colhe e
///      outro que tambem vasculha a mata. Se as duas curvas de nivel de cidade forem iguais,
///      o sistema de Esconderijos e enfeite e precisa de mais peso.
///
/// Tambem mede o pico de monstros vivos, que e o insumo do orcamento de banda em Docs/Netcode.md.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        int seeds = args.Length > 0 ? int.Parse(args[0]) : 8;

        if (Verify.Run() != 0) return 1;

        Console.WriteLine("Destiny Together — ciclo dia/noite");
        Console.WriteLine($"{seeds} seeds por configuracao");
        Console.WriteLine();

        int globalPeak = 0;
        foreach (int players in new[] { 1, 2, 4 })
        {
            var r = Measure(players, seeds, Style.Completo, false);
            if (r.peak > globalPeak) globalPeak = r.peak;
        }

        Console.WriteLine(new string('=', 78));
        Console.WriteLine("EXPLORAR COMPENSA? — 4 jogadores, mesmas seeds, so muda o comportamento");
        Console.WriteLine(new string('=', 78));
        var semExplorar = Measure(4, seeds, Style.SoColhe, false);
        var explorando = Measure(4, seeds, Style.Completo, false);
        Console.WriteLine($"  so colhendo ......... noite {semExplorar.night:0.0}   nivel de cidade {semExplorar.level:0.0}");
        Console.WriteLine($"  colhendo+explorando . noite {explorando.night:0.0}   nivel de cidade {explorando.level:0.0}" +
                          $"   ({explorando.caches:0.0} Esconderijos/partida)");
        float delta = semExplorar.level > 0.01f ? (explorando.level / semExplorar.level - 1f) * 100f : 0f;
        Console.WriteLine($"  -> explorar vale {delta:+0.0;-0.0}% de nivel de cidade");
        Console.WriteLine();

        Console.WriteLine(new string('=', 78));
        Console.WriteLine("TRACO DE UMA PARTIDA — 4 jogadores, onde o HP e a madeira vao embora");
        Console.WriteLine(new string('=', 78));
        Trace();
        Console.WriteLine();

        Console.WriteLine(new string('=', 78));
        Console.WriteLine("TETO DE VOLUME — cidade invulneravel, 5 noites completas, 4 jogadores");
        Console.WriteLine(new string('=', 78));
        var teto = Measure(4, seeds, Style.Completo, true);
        if (teto.peak > globalPeak) globalPeak = teto.peak;

        Console.WriteLine($"PICO GLOBAL MEDIDO: {globalPeak} monstros vivos");
        int frame = globalPeak * 5 + 6;
        Console.WriteLine($"  quadro de Motion: {frame} B  (teto nao-fragmentado do NGO = 1296 B)");
        Console.WriteLine($"  a 5 Hz -> {frame * 5 / 1024f:0.00} KB/s por cliente");
        return 0;
    }

    /// <summary>
    /// Uma partida so, com o estado impresso em cada fronteira de fase. Serve para responder
    /// "onde exatamente o time perde" em vez de tentar deduzir da media.
    /// </summary>
    private static void Trace()
    {
        var content = new DefaultContent();
        var sim = new MatchSimulation(content, 20260803, 4);
        var phase = sim.State.Phase;
        int siloEmptyTicks = 0, nightTicks = 0;

        Console.WriteLine("  fase          HP cidade   silo   pedra   nivel   predios   monstros   silo vazio");

        while (!sim.State.IsOver && sim.Tick < 200000)
        {
            if (sim.State.Phase == PhaseId.Dia) { GreedyBuild(sim); GreedyDraft(sim); }
            else GreedyRepair(sim);
            DriveHeroes(sim, Style.Completo);

            sim.StepFixed();
            sim.Events.Clear();

            if (sim.State.IsNight)
            {
                nightTicks++;
                if (sim.State.SiloWood <= 0f) siloEmptyTicks++;
            }

            if (sim.State.Phase == phase) continue;

            string tag = sim.State.Phase == PhaseId.Dia ? $"amanhecer {sim.State.TurnNumber}"
                       : sim.State.Phase == PhaseId.Noite ? $"noite {sim.State.TurnNumber}"
                       : "fim";
            float emptyPct = nightTicks > 0 ? siloEmptyTicks * 100f / nightTicks : 0f;

            Console.WriteLine($"  {tag,-12}  {sim.State.TownHallHealth,9:0}   {sim.State.SiloWood,4:0}   " +
                              $"{sim.State.Stone,5:0}   {sim.State.CityLevel,5}   {sim.State.Towers.Count,7}   " +
                              $"{sim.State.Monsters.Count,8}   {emptyPct,9:0}%");
            phase = sim.State.Phase;
        }

        Console.WriteLine($"  -> {sim.State.Outcome} na noite {sim.State.TurnNumber}");
    }

    private enum Style { SoColhe, Completo }

    private readonly struct Result
    {
        public readonly float night, level, caches;
        public readonly int peak;
        public Result(float night, float level, float caches, int peak)
        { this.night = night; this.level = level; this.caches = caches; this.peak = peak; }
    }

    private static Result Measure(int players, int seeds, Style style, bool invulneravel)
    {
        var perNight = new Dictionary<int, (int sum, int count, int max)>();
        int globalPeak = 0;
        float nightSum = 0f, levelSum = 0f, cacheSum = 0f;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int s = 0; s < seeds; s++)
        {
            var content = new DefaultContent();
            var sim = new MatchSimulation(content, 20260803 + s * 7717, players);

            int night = sim.State.TurnNumber, peak = 0;

            while (!sim.State.IsOver && sim.Tick < 200000)
            {
                if (sim.State.Phase == PhaseId.Dia) { GreedyBuild(sim); GreedyDraft(sim); }
                else GreedyRepair(sim);   // reparar agora vale a noite inteira
                DriveHeroes(sim, style);

                sim.StepFixed();
                sim.Events.Clear();
                if (invulneravel) sim.State.TownHallHealth = sim.State.TownHallMaxHealth;

                if (sim.State.Monsters.Count > peak) peak = sim.State.Monsters.Count;

                if (sim.State.TurnNumber != night)
                {
                    Record(perNight, night, peak);
                    if (peak > globalPeak) globalPeak = peak;
                    night = sim.State.TurnNumber;
                    peak = 0;
                }
            }

            Record(perNight, night, peak);
            if (peak > globalPeak) globalPeak = peak;

            nightSum += sim.State.TurnNumber;
            levelSum += sim.State.CityLevel;
            foreach (var p in sim.State.Players) cacheSum += p.CachesFound;
        }

        string label = style == Style.SoColhe ? "so colhe" : "completo";
        Console.WriteLine($"--- {players} jogador{(players > 1 ? "es" : "")} ({label}" +
                          $"{(invulneravel ? ", invulneravel" : "")}) — noite media {nightSum / seeds:0.0}, " +
                          $"nivel {levelSum / seeds:0.0}, {cacheSum / seeds:0.0} esconderijos, " +
                          $"{sw.ElapsedMilliseconds} ms ---");
        Console.WriteLine("  noite    pico medio    pico maximo");
        var nights = new List<int>(perNight.Keys);
        nights.Sort();
        foreach (int n in nights)
        {
            var e = perNight[n];
            Console.WriteLine($"  {n,5}    {e.sum / (float)e.count,10:0.0}    {e.max,11}");
        }
        Console.WriteLine();

        return new Result(nightSum / seeds, levelSum / seeds, cacheSum / seeds, globalPeak);
    }

    private static void Record(Dictionary<int, (int sum, int count, int max)> map, int night, int peak)
    {
        if (!map.TryGetValue(night, out var e)) e = (0, 0, 0);
        e.sum += peak;
        e.count++;
        if (peak > e.max) e.max = peak;
        map[night] = e;
    }

    private static void GreedyBuild(MatchSimulation sim)
    {
        // Um time real usa boa parte do Dia antes de chamar a noite. Marcar Pronto no primeiro
        // tick mediria um jogo que ninguem joga.
        bool readyToCommit = sim.State.PhaseElapsed >= sim.State.PhaseDuration * 0.75f;

        foreach (var player in sim.State.Players)
        {
            if (player.IsReady) continue;

            while (player.Hand.Count > 0)
            {
                var def = player.Hand[0];
                GridCoord best = GridCoord.Invalid;
                float bestScore = float.MinValue;

                foreach (var cell in sim.State.Grid.AllCells())
                {
                    if (!CommandValidator.ValidateBuild(sim.State, sim.Content, player, def, cell).IsValid) continue;

                    // COBERTURA antes de tier. Erguer sobre um predio igual funde em tier maior e
                    // consome a carta sem cobrir uma celula nova — otimo quando a ameaca vinha por
                    // duas Faixas, ruinoso agora que ela vem do anel inteiro. Sem esta preferencia
                    // o time chegava a nivel 8 de cidade com 25 predios: cinquenta cartas viraram
                    // altura num nucleo denso enquanto o perimetro seguia descoberto.
                    bool empty = sim.State.Grid.Get(cell) == CellState.Vazio;
                    float score = (empty ? 1000f : 0f) - Vec2.Distance(cell.Center, sim.State.CityCenter);
                    if (score > bestScore) { bestScore = score; best = cell; }
                }

                if (!best.IsValid) break;
                if (!BuildSystem.TryBuild(sim.State, sim.Content, player, def, best, sim.Events)) break;
            }

            if (readyToCommit) player.IsReady = true;
        }

        GreedyRepair(sim);
    }

    /// <summary>Gasta Pedra no que estiver mais machucado. Vale de dia e de noite.</summary>
    private static void GreedyRepair(MatchSimulation sim)
    {
        if (sim.State.Stone <= 0f) return;

        foreach (var tower in new List<TowerState>(sim.State.Towers))
            if (tower.Health < tower.MaxHealth * 0.6f)
                BuildSystem.TryRepair(sim.State, sim.Content, tower.Cell, 5f, sim.Events);
    }

    private static void GreedyDraft(MatchSimulation sim)
    {
        foreach (var player in sim.State.Players)
        {
            if (player.PendingDraftPicks <= 0 || player.DraftOptions.Count == 0) continue;
            EconomySystem.TryPick(sim.State, sim.Content, player, 0, new Rng(player.Id.Index + 1), sim.Events);
        }
    }

    /// <summary>
    /// Steering de um jogador atento. De dia colhe ate encher, deposita e — no estilo completo —
    /// sai atras de Esconderijo com o tempo que sobra. De noite volta para o anel de defesa, que
    /// e onde as torres o cobrem.
    /// </summary>
    private static void DriveHeroes(MatchSimulation sim, Style style)
    {
        bool night = sim.State.IsNight;

        foreach (var hero in sim.State.Heroes)
        {
            var spec = sim.Content.GetHero(hero.Def);
            if (spec == null || hero.IsSpectre) continue;

            Vec2 target;

            if (night)
            {
                // Um heroi e destacado para cacar Ninho. Sem isso a medicao mente: o Ninho nasce
                // FORA do alcance das torres e so mao humana o mata, entao um time de robos que
                // nunca sai da linha morre para um arquetipo que existe justamente para obrigar
                // alguem a sair dela. Media medida sem essa linha: 3,4 noites.
                var nest = hero.Owner.Index == 0 ? NearestNest(sim, hero.Position) : null;

                if (nest != null)
                {
                    target = nest.Position;
                }
                else
                {
                    // Anel de defesa: perto o bastante para as torres cobrirem, longe o bastante
                    // da Prefeitura para interceptar antes de encostar.
                    var toCenter = hero.Position - sim.State.CityCenter;
                    float d = toCenter.Magnitude;
                    float ring = sim.State.Grid.Size * 0.5f + 3f;
                    target = d > ring + 2f || d < ring - 2f
                        ? sim.State.CityCenter + (d > 0.01f ? toCenter.Normalized : new Vec2(0f, 1f)) * ring
                        : hero.Position;
                }
            }
            else
            {
                // A pergunta que este teste existe para responder: DIVIDIR FUNCOES compensa?
                //
                // No estilo completo, metade do time vira batedor e nunca colhe; a outra metade
                // faz a rota de colheita normal. Mandar todo mundo colher e so ir aos
                // Esconderijos "quando sobrar tempo" nao mede nada — a cota de colheita quase
                // preenche o dia inteiro, entao o galho de exploracao nunca executava e as duas
                // configuracoes davam resultado identico.
                bool scout = style == Style.Completo && hero.Owner.Index % 2 == 0;

                if (scout)
                {
                    var cache = NearestCache(sim, hero.Position);
                    target = cache?.Position ?? sim.State.CityCenter;
                }
                else if (hero.CarriedTotal >= spec.CarryCapacity - 0.01f)
                {
                    target = sim.State.CityCenter;
                }
                else
                {
                    var node = NearestNode(sim, hero.Position);
                    target = node?.Position ?? sim.State.CityCenter;
                }
            }

            var delta = target - hero.Position;
            hero.MoveInput = delta.SqrMagnitude < 0.04f ? Vec2.Zero : delta.Normalized;
        }
    }

    private static HarvestNodeState NearestNode(MatchSimulation sim, Vec2 from)
    {
        HarvestNodeState best = null;
        float bestSqr = float.MaxValue;
        foreach (var n in sim.State.Nodes)
        {
            if (n.IsDepleted) continue;
            float sqr = Vec2.SqrDistance(n.Position, from);
            if (sqr < bestSqr) { bestSqr = sqr; best = n; }
        }
        return best;
    }

    /// <summary>Ninho vivo mais proximo. Estatico e fora do alcance das torres, por desenho.</summary>
    private static MonsterState NearestNest(MatchSimulation sim, Vec2 from)
    {
        MonsterState best = null;
        float bestSqr = float.MaxValue;
        foreach (var m in sim.State.Monsters)
        {
            if (!m.IsAlive) continue;
            var spec = sim.Content.GetMonster(m.Def);
            if (spec == null || !spec.IsStationary) continue;
            float sqr = Vec2.SqrDistance(m.Position, from);
            if (sqr < bestSqr) { bestSqr = sqr; best = m; }
        }
        return best;
    }

    private static CacheState NearestCache(MatchSimulation sim, Vec2 from)
    {
        CacheState best = null;
        float bestSqr = float.MaxValue;
        foreach (var c in sim.State.Caches)
        {
            if (c.Collected) continue;
            float sqr = Vec2.SqrDistance(c.Position, from);
            if (sqr < bestSqr) { bestSqr = sqr; best = c; }
        }
        return best;
    }
}
