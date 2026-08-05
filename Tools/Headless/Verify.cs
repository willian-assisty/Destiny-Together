using System;
using System.Collections.Generic;
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
        EconomiaNaoDependeDaPaisagem();
        RegioesDividemOMundo();
        SolCruzaOCeu();
        Console.WriteLine(_falhas == 0
            ? "  todos passaram"
            : $"  {_falhas} FALHA(S)");
        Console.WriteLine();
        return _falhas;
    }

    /// <summary>
    /// A economia do mundo e CEGA a paisagem.
    ///
    /// E a garantia estrutural que permite mexer em bioma sem medo: se nenhum numero de cenario
    /// alcanca um spawn, nenhuma tabela de regiao pode mover o balanceamento medido.
    ///
    /// O teste existe porque o contrario ja era verdade e ninguem via. Os saques ficavam DENTRO
    /// dos `&&` (`forest > 0.25f && rng.Next01() < 0.55f`), entao o curto-circuito do C# fazia o
    /// numero de saques depender da densidade daquele chunk, e o sorteio do Esconderijo caia numa
    /// posicao diferente do fluxo. Mexer na densidade da mata movia os Esconderijos do mundo.
    /// </summary>
    private static void EconomiaNaoDependeDaPaisagem()
    {
        var content = new DefaultContent();
        var center = new Vec2(10.5f, 10.5f);
        var buffer = new ChunkContent();

        // 1) Gerar COM e SEM props tem de dar exatamente os mesmos spawns. Prova que os dois
        //    sorteios sao independentes de verdade, e nao so em intencao.
        int chunks = 0, spawns = 0;
        bool identico = true;

        for (int cz = -14; cz <= 14 && identico; cz++)
        {
            for (int cx = -14; cx <= 14 && identico; cx++)
            {
                WorldGen.Generate(4242, cx, cz, content.Arena, center, buffer, includeProps: true);
                var comProps = buffer.Spawns.ToArray();

                WorldGen.Generate(4242, cx, cz, content.Arena, center, buffer, includeProps: false);
                var semProps = buffer.Spawns.ToArray();

                chunks++;
                spawns += semProps.Length;

                if (comProps.Length != semProps.Length) { identico = false; break; }
                for (int i = 0; i < comProps.Length; i++)
                {
                    if (comProps[i].Index == semProps[i].Index &&
                        comProps[i].Position.X == semProps[i].Position.X &&
                        comProps[i].Position.Y == semProps[i].Position.Y &&
                        comProps[i].Amount == semProps[i].Amount) continue;
                    identico = false;
                    break;
                }
            }
        }

        Console.WriteLine($"  [economia] {chunks} chunks, {spawns} spawns conferidos");
        Check(identico, "desenhar a mata nao move um unico Esconderijo");

        // 2) A posicao de um item depende do SLOT dele, nunca de quantos vizinhos existem.
        //    Antes, `ScatterIn` sacava do mesmo Rng: aceitar o no de madeira deslocava a pedra E o
        //    Esconderijo daquele chunk.
        int comparados = 0;
        bool porSlot = true;

        for (int cz = -14; cz <= 14 && porSlot; cz++)
        {
            for (int cx = -14; cx <= 14 && porSlot; cx++)
            {
                WorldGen.Generate(4242, cx, cz, content.Arena, center, buffer, includeProps: false);
                foreach (var s in buffer.Spawns)
                {
                    // A posicao tem de cair na janela do chunk com a margem de 2 celulas, e o
                    // Index tem de ser um dos slots declarados — nunca um contador.
                    float lx = s.Position.X - cx * WorldGen.ChunkSize;
                    float lz = s.Position.Y - cz * WorldGen.ChunkSize;
                    bool dentro = lx >= 2f && lx <= WorldGen.ChunkSize - 2f &&
                                  lz >= 2f && lz <= WorldGen.ChunkSize - 2f;
                    bool slotValido = s.Index == WorldGen.SlotWood ||
                                      s.Index == WorldGen.SlotRock ||
                                      s.Index == WorldGen.SlotCache;
                    if (!dentro || !slotValido) { porSlot = false; break; }

                    // O slot bate com o TIPO: madeira sempre no 0, pedra no 1, Esconderijo no 2.
                    bool coerente = s.IsCache
                        ? s.Index == WorldGen.SlotCache
                        : (s.NodeKind == HarvestNodeKind.Arvore ? s.Index == WorldGen.SlotWood
                                                                : s.Index == WorldGen.SlotRock);
                    if (!coerente) { porSlot = false; break; }
                    comparados++;
                }
            }
        }

        Check(porSlot, "cada item ocupa um SLOT fixo, e nao uma posicao numa fila",
              $"{comparados} itens");
    }

    /// <summary>
    /// As regioes dividem o mundo em territorios, e a divisao e a MESMA de qualquer lugar.
    /// </summary>
    private static void RegioesDividemOMundo()
    {
        var center = new Vec2(10.5f, 10.5f);
        const int seed = 90210;

        // 1) Determinismo: o mesmo ponto consultado em qualquer ordem da a mesma regiao.
        bool estavel = true;
        for (int i = 0; i < 500 && estavel; i++)
        {
            var p = new Vec2(-3000f + i * 13.7f, 1800f - i * 9.1f);
            var a = WorldLattice.RegionAt(seed, p, center);
            var b = WorldLattice.RegionAt(seed, p, center);
            if (a != b) estavel = false;
        }
        Check(estavel, "a regiao de um ponto e sempre a mesma");

        // 2) A vila e Mata, em qualquer semente. Sem isso a partida comeca cercada de Pantano por
        //    sorteio, e a medicao de balanceamento vira loteria.
        bool casaEMata = true;
        for (int s = 1; s <= 64; s++)
            if (WorldLattice.RegionAt(s * 7919, center, center) != RegionKind.Mata) casaEMata = false;
        Check(casaEMata, "a vila nasce em Mata em toda semente");

        // 3) Nenhuma regiao pode ser esvaziada por acidente.
        //
        //    Este teste ja foi uma media ponderada das massas, e era um PROXY: no dia em que o teto
        //    de props triplicou, a media continuou perto de 1 enquanto a densidade absoluta mudou
        //    tres vezes. Massa e razao, e razao nao mede quantidade. O que importa e o resultado
        //    absoluto, e ele e medido em `MundoTemMataEPedreira`; aqui fica so a guarda de que
        //    nenhuma regiao foi zerada — regiao sem nada nao e bioma, e um buraco no mundo.
        foreach (RegionKind k in Enum.GetValues(typeof(RegionKind)))
        {
            var spec = WorldLattice.SpecOf(k);
            Check(spec.ForestMass > 0.02f && spec.RockMass > 0.02f && spec.Weight > 0.02f,
                  $"a regiao {k} tem paisagem propria, nao um vazio",
                  $"mata {spec.ForestMass:0.00} rocha {spec.RockMass:0.00} peso {spec.Weight:0.00}");
        }

        // 4) Territorio: atravessar o mundo em linha reta encontra varias regioes, e todas as
        //    quatro existem. Media sobre varios raios e sementes — um raio de uma semente mede
        //    sorte, nao territorio.
        var vistas = new HashSet<RegionKind>();
        int trocasTotais = 0, raios = 0;

        for (int s = 1; s <= 6; s++)
        {
            for (int dir = 0; dir < 8; dir++)
            {
                double ang = dir * Math.PI / 4.0;
                var anterior = RegionKind.Mata;
                int trocas = 0;

                for (int step = 0; step <= 400; step++)
                {
                    float d = step * 10f;
                    var p = new Vec2(center.X + (float)Math.Cos(ang) * d,
                                     center.Y + (float)Math.Sin(ang) * d);
                    var r = WorldLattice.RegionAt(s * 7919, p, center);
                    vistas.Add(r);
                    if (step > 0 && r != anterior) trocas++;
                    anterior = r;
                }

                trocasTotais += trocas;
                raios++;
            }
        }

        float media = trocasTotais / (float)raios;
        Console.WriteLine($"  [regioes] {raios} travessias de 4000 celulas: " +
                          $"{media:0.0} trocas em media, {vistas.Count} de 4 regioes vistas");

        Check(vistas.Count == 4, "as quatro regioes existem no mundo", $"{vistas.Count} de 4");
        Check(media >= 2f && media <= 14f, "regiao e territorio: nem mosaico, nem um bioma so",
              $"{media:0.0} trocas por travessia");
    }

    /// <summary>
    /// O sol atravessa o ceu ao longo do Dia inteiro, e a emenda com a Noite e continua.
    ///
    /// Este teste existe porque a versao anterior parecia certa e nao era: o sol era dirigido pela
    /// curva de LUZ, que fica em zero nos primeiros 70% do Dia, entao ele ficava parado no mesmo
    /// ponto do ceu por tres minutos e meio e depois despencava. "Nasce e se poe" e uma afirmacao
    /// sobre POSICAO ao longo do relogio, e so um teste sobre posicao a pega.
    /// </summary>
    private static void SolCruzaOCeu()
    {
        var state = new MatchState { PhaseDuration = 300f };

        DayNightCycle.SunOrientation Em(PhaseId fase, float t)
        {
            state.Phase = fase;
            state.PhaseElapsed = t * state.PhaseDuration;
            return DayNightCycle.Sun(state);
        }

        var nascer = Em(PhaseId.Dia, 0f);
        var meioDia = Em(PhaseId.Dia, 0.5f);
        var poente = Em(PhaseId.Dia, 1f);

        Check(nascer.AzimuthFromNoon < -80f && poente.AzimuthFromNoon > 80f,
              "o sol nasce de um lado e se poe do outro",
              $"{nascer.AzimuthFromNoon:0} -> {poente.AzimuthFromNoon:0} graus");

        Check(meioDia.Elevation > nascer.Elevation + 30f && meioDia.Elevation > poente.Elevation + 30f,
              "ao meio-dia o sol esta bem mais alto que nas pontas",
              $"{nascer.Elevation:0} / {meioDia.Elevation:0} / {poente.Elevation:0} graus");

        Check(nascer.Horizon01 > 0.95f && poente.Horizon01 > 0.95f && meioDia.Horizon01 < 0.05f,
              "o alaranjado acende so nas pontas do dia");

        // Nenhum quadro em que o sol salta: o fim do Dia e o comeco da Noite tem de coincidir, e o
        // fim da Noite tem de coincidir com o nascer do dia seguinte.
        var noiteInicio = Em(PhaseId.Noite, 0f);
        var noiteFim = Em(PhaseId.Noite, 1f);

        Check(Math.Abs(noiteInicio.Elevation - poente.Elevation) < 0.01f &&
              Math.Abs(noiteInicio.AzimuthFromNoon - poente.AzimuthFromNoon) < 0.01f,
              "a virada do Dia para a Noite e continua — o sol nao salta");

        Check(Math.Abs(noiteFim.Elevation - nascer.Elevation) < 0.01f &&
              Math.Abs(noiteFim.AzimuthFromNoon - (nascer.AzimuthFromNoon + 360f)) < 0.01f,
              "a Noite fecha a volta e devolve o sol ao ponto do nascer");

        Check(Em(PhaseId.Noite, 0.5f).Elevation < 0f,
              "no meio da noite o sol esta abaixo do horizonte");

        // O arco anda o Dia INTEIRO. Se ele ficasse preso a curva de luz, o primeiro terco do dia
        // teria azimute constante — que era exatamente o defeito.
        float a25 = Em(PhaseId.Dia, 0.25f).AzimuthFromNoon;
        Check(a25 > nascer.AzimuthFromNoon + 30f && a25 < meioDia.AzimuthFromNoon - 30f,
              "o sol ja andou um quarto do arco no primeiro quarto do Dia",
              $"{a25:0} graus");
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

        int arvores = 0, pedras = 0, chunksComMata = 0, chunksVazios = 0, total = 0, pico = 0;

        // Quantas malhas diferentes a mata pede. Se a variante nao variar, cinco arvores
        // importadas desenham uma floresta com uma — que foi exatamente o bug do hash por tipo.
        var variantes = new HashSet<int>();

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
                    variantes.Add(prop.Variant % 5);
                }
                arvores += a;
                pedras += p;
                if (a + p == 0) chunksVazios++;

                // Um chunk tem 24x24 = 576 celulas e uma copa ocupa ~3,8. 70 arvores cobrem quase
                // metade do chao: da para se perder dentro, e ainda da para andar. E este o
                // numero que "mata fechada" quer dizer — o limiar antigo (5) foi calibrado quando
                // o teto era 26 e hoje aceitaria um bosque ralo como floresta densa.
                if (a >= 70) chunksComMata++;
                if (a > pico) pico = a;
            }
        }

        Console.WriteLine($"  [mundo] {total} chunks: {arvores} arvores ({arvores / (float)total:0.0}/chunk, " +
                          $"pico {pico}), {pedras} pedras, {chunksComMata} de mata fechada, " +
                          $"{chunksVazios} clareiras, {variantes.Count} malhas pedidas");

        // Por REGIAO, que e o numero que responde "a floresta densa ficou densa?". A media global
        // cai de proposito quando entram pasto e pedreira — o que nao pode cair e o nucleo da Mata.
        var arvoresPorRegiao = new int[4];
        var pedrasPorRegiao = new int[4];
        var chunksPorRegiao = new int[4];

        for (int cz = -12; cz <= 12; cz++)
        {
            for (int cx = -12; cx <= 12; cx++)
            {
                var centro = WorldGen.ChunkCenter(cx, cz);
                if (Vec2.Distance(centro, center) <
                    content.Arena.OutskirtsRadius + content.Arena.WorldClearance) continue;

                WorldGen.Generate(777, cx, cz, content.Arena, center, buffer);
                int r = (int)WorldLattice.RegionAt(777, centro, center);
                chunksPorRegiao[r]++;

                foreach (var prop in buffer.Props)
                {
                    if (prop.Kind is WorldPropKind.Pedra or WorldPropKind.Penhasco) pedrasPorRegiao[r]++;
                    else arvoresPorRegiao[r]++;
                }
            }
        }

        string[] nomes = { "Mata", "Pedreira", "Pasto", "Pantano" };
        for (int r = 0; r < 4; r++)
        {
            if (chunksPorRegiao[r] == 0) continue;
            Console.WriteLine($"     {nomes[r],-9} {chunksPorRegiao[r],4} chunks · " +
                              $"{arvoresPorRegiao[r] / (float)chunksPorRegiao[r],5:0.0} arvores/chunk · " +
                              $"{pedrasPorRegiao[r] / (float)chunksPorRegiao[r],5:0.0} pedras/chunk");
        }

        // A Mata tem de ser a mais arborizada e a Pedreira a mais rochosa. Se a tabela for
        // reajustada e isso inverter, a regiao deixou de significar o que o nome diz.
        int maisArvores = 0, maisPedras = 0;
        for (int r = 1; r < 4; r++)
        {
            if (chunksPorRegiao[r] == 0) continue;
            if (arvoresPorRegiao[r] / (float)chunksPorRegiao[r] >
                arvoresPorRegiao[maisArvores] / (float)Math.Max(1, chunksPorRegiao[maisArvores])) maisArvores = r;
            if (pedrasPorRegiao[r] / (float)chunksPorRegiao[r] >
                pedrasPorRegiao[maisPedras] / (float)Math.Max(1, chunksPorRegiao[maisPedras])) maisPedras = r;
        }

        Check(maisArvores == (int)RegionKind.Mata, "a Mata e a regiao mais arborizada",
              $"e {nomes[maisArvores]}");
        Check(maisPedras == (int)RegionKind.Pedreira, "a Pedreira e a regiao mais rochosa",
              $"e {nomes[maisPedras]}");

        Check(arvores > 0 && pedras > 0, "o mundo gera floresta E pedreira");
        Check(chunksVazios > total / 12, "existem clareiras — nao e um borrifo uniforme",
              $"{chunksVazios} de {total}");
        Check(variantes.Count >= 5, "a mata pede malhas variadas, nao a mesma arvore repetida",
              $"{variantes.Count} de 5");
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
