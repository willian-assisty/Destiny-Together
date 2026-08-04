using System;
using DestinyTogether.Core;
using DestinyTogether.Data;
using DestinyTogether.Sim;

/// <summary>
/// Roda o corpo dos tres testes novos do Passo 1 fora do NUnit.
/// O nunit.framework.dll do Unity e net40 e nao compila contra netstandard, entao o Test Runner
/// so confirma isto com o editor aberto — aqui a logica e verificada em 300 ms.
/// </summary>
internal static class Verify
{
    private static int _falhas;

    public static int Run()
    {
        Console.WriteLine("=== Passo 1 — verificacao das fundacoes ===");
        HeroMotionReproduzIntegrador();
        RngRestauraEstado();
        TickContaPassosFixos();
        SondaDeExploracao();
        TudoAlcancavel();
        EsconderijoPagaEmProgresso();
        MundoEDeterminista();
        MundoStreamaESeLembra();
        MundoTemMataEPedreira();
        Console.WriteLine(_falhas == 0
            ? "  todos passaram"
            : $"  {_falhas} FALHA(S)");
        Console.WriteLine();
        return _falhas;
    }

    private static void Check(bool ok, string label, string detalhe = "")
    {
        Console.WriteLine($"  [{(ok ? "ok  " : "FALHA")}] {label}{(ok || detalhe.Length == 0 ? "" : " — " + detalhe)}");
        if (!ok) _falhas++;
    }

    private static void HeroMotionReproduzIntegrador()
    {
        var content = new DefaultContent();
        var autoritativo = new MatchSimulation(content, 1234, 4);
        var previsto = new MatchSimulation(content, 1234, 4);
        foreach (var p in autoritativo.State.Players) p.IsAutomaton = false;
        foreach (var p in previsto.State.Players) p.IsAutomaton = false;

        var heroA = autoritativo.State.Heroes[0];
        var heroB = previsto.State.Heroes[0];
        var spec = content.GetHero(heroA.Def);
        var log = new SimEventLog();
        var partida = heroA.Position;
        var direcao = new Vec2(0.8f, -0.6f);

        for (int i = 0; i < 100; i++)
        {
            heroA.MoveInput = direcao;
            HeroSystem.Tick(autoritativo.State, content, MatchSimulation.FixedDelta, log, false);
            log.Clear();
            HeroMotion.Step(previsto.State, heroB, spec, direcao, MatchSimulation.FixedDelta);
        }

        Check(heroA.Position.X == heroB.Position.X && heroA.Position.Y == heroB.Position.Y,
              "HeroMotion reproduz o integrador do HeroSystem bit a bit",
              $"host ({heroA.Position.X:0.00000}, {heroA.Position.Y:0.00000}) vs " +
              $"predito ({heroB.Position.X:0.00000}, {heroB.Position.Y:0.00000})");

        Check(Vec2.Distance(partida, heroA.Position) > 1f,
              "o heroi realmente andou (senao o teste nao prova nada)",
              $"andou {Vec2.Distance(partida, heroA.Position):0.00} celulas");
    }

    /// <summary>Um unico dia com quatro batedores dedicados. Quantos Esconderijos saem?</summary>
    private static void SondaDeExploracao()
    {
        var content = new DefaultContent();
        var sim = new MatchSimulation(content, 4242, 4);

        int nascidos = sim.State.Caches.Count;
        int ticks = 0;

        while (sim.State.Phase == PhaseId.Dia && ticks < 8000)
        {
            foreach (var hero in sim.State.Heroes)
            {
                CacheState best = null;
                float bestSqr = float.MaxValue;
                foreach (var c in sim.State.Caches)
                {
                    float sqr = Vec2.SqrDistance(c.Position, hero.Position);
                    if (sqr < bestSqr) { bestSqr = sqr; best = c; }
                }
                var d = (best?.Position ?? sim.State.CityCenter) - hero.Position;
                hero.MoveInput = d.SqrMagnitude < 0.04f ? Vec2.Zero : d.Normalized;
            }

            sim.StepFixed();
            sim.Events.Clear();
            ticks++;
        }

        int achados = 0;
        foreach (var p in sim.State.Players) achados += p.CachesFound;

        Console.WriteLine($"  [sonda] dia de {ticks * MatchSimulation.FixedDelta:0}s, " +
                          $"{nascidos} esconderijos nascidos, {achados} recolhidos por 4 batedores");
        Check(achados >= nascidos / 2,
              "quatro batedores dedicados recolhem ao menos metade dos Esconderijos do dia",
              $"{achados} de {nascidos}");
    }

    /// <summary>
    /// Todo Esconderijo e todo no de recurso tem de caber dentro do raio que o heroi consegue
    /// andar. Nasceram fora uma vez — os Relicarios eram literalmente impossiveis de pegar, e
    /// nada avisava: o dia so rendia menos do que deveria, em silencio.
    /// </summary>
    private static void TudoAlcancavel()
    {
        var content = new DefaultContent();
        var sim = new MatchSimulation(content, 31337, 4);

        float piorCache = 0f, piorNode = 0f;
        foreach (var c in sim.State.Caches)
            piorCache = Math.Max(piorCache, Vec2.Distance(c.Position, sim.State.CityCenter));
        foreach (var n in sim.State.Nodes)
            piorNode = Math.Max(piorNode, Vec2.Distance(n.Position, sim.State.CityCenter));

        Check(piorCache <= sim.State.WorldRadius,
              "todo Esconderijo cabe dentro do mundo alcancavel",
              $"mais distante a {piorCache:0.0}, limite {sim.State.WorldRadius:0.0}");
        Check(piorNode <= sim.State.WorldRadius,
              "todo no de recurso cabe dentro do mundo alcancavel",
              $"mais distante a {piorNode:0.0}, limite {sim.State.WorldRadius:0.0}");
    }

    /// <summary>Explorar paga em XP e Ouro; colher paga em Madeira e Pedra. Nunca o contrario.</summary>
    private static void EsconderijoPagaEmProgresso()
    {
        var content = new DefaultContent();
        var sim = new MatchSimulation(content, 555, 4);
        if (sim.State.Caches.Count == 0) { Check(false, "o amanhecer esconde algo na mata"); return; }

        var cache = sim.State.Caches[0];
        var hero = sim.State.Heroes[0];
        float xp = sim.State.Xp, madeira = sim.State.SiloWood, pedra = sim.State.Stone;

        hero.Position = cache.Position;
        ExplorationSystem.Tick(sim.State, content, sim.Events);
        sim.Events.Clear();

        Check(sim.State.Xp > xp, "Esconderijo rende XP (que vira carta para o time inteiro)");
        Check(Math.Abs(sim.State.SiloWood - madeira) < 0.001f &&
              Math.Abs(sim.State.Stone - pedra) < 0.001f,
              "Esconderijo nao rende Madeira nem Pedra — moedas ortogonais");
    }

    /// <summary>
    /// O mundo e uma funcao pura. Se nao for, sair de uma clareira e voltar mostra outra mata —
    /// e no multiplayer os quatro clientes desenham florestas diferentes.
    /// </summary>
    private static void MundoEDeterminista()
    {
        var content = new DefaultContent();
        var a = new ChunkContent();
        var b = new ChunkContent();
        var center = new Vec2(10.5f, 10.5f);

        bool igual = true;
        for (int cz = -6; cz <= 6 && igual; cz++)
        {
            for (int cx = -6; cx <= 6 && igual; cx++)
            {
                WorldGen.Generate(4242, cx, cz, content.Arena, center, a);
                WorldGen.Generate(4242, cx, cz, content.Arena, center, b);

                if (a.Props.Count != b.Props.Count || a.Spawns.Count != b.Spawns.Count) { igual = false; break; }
                for (int i = 0; i < a.Props.Count; i++)
                    if (a.Props[i].Position.X != b.Props[i].Position.X ||
                        a.Props[i].Kind != b.Props[i].Kind) { igual = false; break; }
            }
        }

        Check(igual, "gerar o mesmo chunk duas vezes da exatamente a mesma mata");

        // Sementes diferentes tem de dar mundos diferentes, senao a seed nao significa nada.
        WorldGen.Generate(1, 5, 5, content.Arena, center, a);
        WorldGen.Generate(2, 5, 5, content.Arena, center, b);
        bool difere = a.Props.Count != b.Props.Count ||
                      (a.Props.Count > 0 && a.Props[0].Position.X != b.Props[0].Position.X);
        Check(difere, "sementes diferentes produzem mundos diferentes");
    }

    /// <summary>
    /// O ciclo completo: andar para longe materializa mundo, voltar o descarrega, e o que foi
    /// consumido nao ressuscita quando o chunk e reconstruido.
    /// </summary>
    private static void MundoStreamaESeLembra()
    {
        var content = new DefaultContent();
        var sim = new MatchSimulation(content, 90210, 4);
        var hero = sim.State.Heroes[0];

        // Teleporta o heroi para bem longe e deixa o streamer materializar.
        var longe = sim.State.CityCenter + new Vec2(220f, 160f);
        for (int i = 0; i < 4; i++) { hero.Position = longe; sim.StepFixed(); sim.Events.Clear(); }

        int chunks = sim.State.LoadedChunks.Count;
        int nosDoMundo = 0, cachesDoMundo = 0;
        foreach (var n in sim.State.Nodes) if (n.FromWorld) nosDoMundo++;
        foreach (var c in sim.State.Caches) if (c.FromWorld) cachesDoMundo++;

        Check(chunks > 0, "andar para longe materializa chunks", $"{chunks} chunks");
        Check(nosDoMundo + cachesDoMundo > 0,
              "o mundo longe da vila tem conteudo",
              $"{nosDoMundo} nos, {cachesDoMundo} esconderijos");

        // Consome um esconderijo do mundo.
        CacheState alvo = null;
        foreach (var c in sim.State.Caches) if (c.FromWorld) { alvo = c; break; }
        long chave = alvo?.WorldKey ?? 0L;
        if (alvo != null)
        {
            hero.Position = alvo.Position;
            sim.StepFixed();
            sim.Events.Clear();
        }

        Check(chave != 0L && sim.State.IsConsumed(chave),
              "recolher um Esconderijo do mundo marca a chave como consumida");

        // Volta para a vila: os chunks distantes tem de sumir.
        for (int i = 0; i < 6; i++)
        {
            hero.Position = sim.State.CityCenter;
            sim.StepFixed();
            sim.Events.Clear();
        }
        int aindaCarregados = sim.State.LoadedChunks.Count;

        // E volta para la: o esconderijo consumido NAO pode reaparecer.
        for (int i = 0; i < 4; i++) { hero.Position = longe; sim.StepFixed(); sim.Events.Clear(); }
        bool ressuscitou = false;
        foreach (var c in sim.State.Caches) if (c.WorldKey == chave) ressuscitou = true;

        Check(aindaCarregados < chunks, "voltar para a vila descarrega o mundo distante",
              $"{chunks} -> {aindaCarregados} chunks");
        Check(!ressuscitou, "o que foi recolhido nao volta quando o chunk e reconstruido");
    }

    /// <summary>
    /// A geracao tem de produzir as DUAS coisas que o mundo promete, e em manchas — nao um
    /// borrifo uniforme. Bosque que nao le como bosque nao da direcao a ninguem.
    /// </summary>
    private static void MundoTemMataEPedreira()
    {
        var content = new DefaultContent();
        var buffer = new ChunkContent();
        var center = new Vec2(10.5f, 10.5f);

        int arvores = 0, pedras = 0, chunksComMata = 0, chunksVazios = 0, total = 0;

        for (int cz = -12; cz <= 12; cz++)
        {
            for (int cx = -12; cx <= 12; cx++)
            {
                WorldGen.Generate(777, cx, cz, content.Arena, center, buffer);
                if (Vec2.Distance(WorldGen.ChunkCenter(cx, cz), center) <
                    content.Arena.OutskirtsRadius + content.Arena.WorldClearance) continue;

                total++;
                int a = 0, p = 0;
                foreach (var prop in buffer.Props)
                {
                    if (prop.Kind is WorldPropKind.Pedra or WorldPropKind.Penhasco) p++;
                    else a++;
                }
                arvores += a;
                pedras += p;
                if (a + p == 0) chunksVazios++;
                if (a >= 5) chunksComMata++;
            }
        }

        Console.WriteLine($"  [mundo] {total} chunks: {arvores} arvores, {pedras} pedras, " +
                          $"{chunksComMata} de mata fechada, {chunksVazios} clareiras");

        Check(arvores > 0 && pedras > 0, "o mundo gera floresta E pedreira");
        Check(chunksVazios > total / 12, "existem clareiras — nao e um borrifo uniforme",
              $"{chunksVazios} de {total}");
        Check(chunksComMata > total / 12, "existem manchas de mata fechada",
              $"{chunksComMata} de {total}");
    }

    private static void RngRestauraEstado()
    {
        var rng = new Rng(99);
        for (int i = 0; i < 1000; i++) rng.NextUInt();

        rng.GetState(out uint x, out uint y, out uint z, out uint w);
        var esperado = new uint[16];
        for (int i = 0; i < esperado.Length; i++) esperado[i] = rng.NextUInt();

        rng.SetState(x, y, z, w);
        bool ok = true;
        for (int i = 0; i < esperado.Length; i++) if (esperado[i] != rng.NextUInt()) ok = false;

        Check(ok, "Rng reproduz a sequencia depois de restaurar o estado");
    }

    private static void TickContaPassosFixos()
    {
        var content = new DefaultContent();
        var sim = new MatchSimulation(content, 4242, 4);
        foreach (var p in sim.State.Players) p.IsAutomaton = true;

        Check(sim.Tick == 0, "a partida comeca no tick zero", $"veio {sim.Tick}");

        for (int i = 0; i < 250; i++) { sim.StepFixed(); sim.Events.Clear(); }
        Check(sim.Tick == 250, "250 StepFixed = tick 250", $"veio {sim.Tick}");

        sim.Advance(MatchSimulation.FixedDelta * 3f);
        sim.Events.Clear();
        Check(sim.Tick == 253, "Advance de 3 passos avanca 3 ticks", $"veio {sim.Tick}");

        sim.Advance(MatchSimulation.FixedDelta * 0.5f);
        sim.Events.Clear();
        Check(sim.Tick == 253, "meio passo nao avanca tick (fica no acumulador)", $"veio {sim.Tick}");
    }
}
