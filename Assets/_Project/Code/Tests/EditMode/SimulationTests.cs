using DestinyTogether.Core;
using DestinyTogether.Data;
using DestinyTogether.Sim;
using NUnit.Framework;

namespace DestinyTogether.Tests
{
    /// <summary>
    /// A partida inteira roda aqui, sem cena, sem GameObject e sem Play Mode — em milissegundos.
    /// E o retorno concreto de ter mantido DT.Sim livre de UnityEngine.
    /// </summary>
    public class SimulationTests
    {
        private DefaultContent _content;

        [SetUp]
        public void SetUp() => _content = new DefaultContent();

        private static MatchSimulation NewSim(DefaultContent content, int players = 4, int seed = 4242)
        {
            var sim = new MatchSimulation(content, seed, players);
            for (int i = 0; i < sim.State.Players.Count; i++) sim.State.Players[i].IsAutomaton = true;
            return sim;
        }

        [Test]
        public void PartidaComeca_NoPreparoDoTurnoUm()
        {
            var sim = NewSim(_content);
            Assert.AreEqual(PhaseId.Dia, sim.State.Phase);
            Assert.AreEqual(1, sim.State.TurnNumber);
            Assert.AreEqual(_content.Rules.TownHallMaxHealth, sim.State.TownHallHealth);
        }

        [Test]
        public void CadaJogadorTem_HeroiQuadranteEMaoInicial()
        {
            var sim = NewSim(_content);
            Assert.AreEqual(4, sim.State.Players.Count);
            Assert.AreEqual(4, sim.State.Heroes.Count);

            var quadrants = new System.Collections.Generic.HashSet<Quadrant>();
            foreach (var p in sim.State.Players)
            {
                Assert.IsNotNull(sim.State.GetHero(p.Hero), "Jogador sem heroi");
                Assert.IsNotEmpty(p.Hand, "Jogador sem carta inicial nao tem decisao no turno 1");
                quadrants.Add(p.Quadrant);
            }
            Assert.AreEqual(4, quadrants.Count, "Cada jogador precisa do proprio Quadrante");
        }

        [Test]
        public void CicloDiaNoite_AlternaSemTravar()
        {
            var sim = NewSim(_content);
            var seen = new System.Collections.Generic.List<PhaseId>();
            var last = PhaseId.None;

            for (int i = 0; i < 40000 && sim.State.TurnNumber < 2; i++)
            {
                sim.StepFixed();
                sim.Events.Clear();
                if (sim.State.Phase != last)
                {
                    last = sim.State.Phase;
                    seen.Add(last);
                }
            }

            CollectionAssert.Contains(seen, PhaseId.Noite, "A Noite nunca comecou");
            Assert.AreEqual(PhaseId.Dia, sim.State.Phase, "O amanhecer nunca devolveu o Dia");
        }

        /// <summary>
        /// A Noite dura exatamente o que as regras dizem — nunca o que a lista de Investidas
        /// dita. Era o contrario no modelo antigo, e o ciclo dia/noite depende de o relogio ser
        /// a autoridade: um jogador que sai para explorar precisa saber quanto tempo tem.
        /// </summary>
        [Test]
        public void Noite_DuraOTempoDasRegras_NaoOTempoDasInvestidas()
        {
            var sim = NewSim(_content);

            while (sim.State.Phase != PhaseId.Noite && sim.Tick < 40000)
            { sim.StepFixed(); sim.Events.Clear(); }

            Assert.AreEqual(PhaseId.Noite, sim.State.Phase, "Nunca anoiteceu");
            int start = sim.Tick;

            while (sim.State.Phase == PhaseId.Noite && sim.Tick < 40000)
            { sim.StepFixed(); sim.Events.Clear(); }

            float duration = (sim.Tick - start) * MatchSimulation.FixedDelta;
            Assert.AreEqual(_content.Rules.NoiteSeconds, duration, 1f,
                "A Noite tem de acabar no relogio, nao quando as Investidas terminam");
        }

        /// <summary>
        /// Os monstros param de nascer antes do amanhecer, e o ceu clareia na mesma janela.
        /// Sem o corte, a ultima onda nasceria so para ser dissolvida — e o time aprenderia que
        /// esperar e melhor que lutar.
        /// </summary>
        [Test]
        public void UltimoTrechoDaNoite_NaoGeraMonstroNovo()
        {
            var sim = NewSim(_content);
            while (sim.State.Phase != PhaseId.Noite && sim.Tick < 40000)
            { sim.StepFixed(); sim.Events.Clear(); }

            // Avanca ate a janela sem spawn.
            float cutoff = _content.Rules.SpawnCutoffBeforeDawn;
            while (sim.State.Phase == PhaseId.Noite &&
                   sim.State.PhaseDuration - sim.State.PhaseElapsed > cutoff * 0.9f)
            { sim.StepFixed(); sim.Events.Clear(); }

            Assert.AreEqual(PhaseId.Noite, sim.State.Phase);

            int spawned = 0;
            while (sim.State.Phase == PhaseId.Noite && sim.Tick < 40000)
            {
                sim.StepFixed();
                for (int i = 0; i < sim.Events.Count; i++)
                    if (sim.Events[i].Type == SimEventType.MonsterSpawned) spawned++;
                sim.Events.Clear();
            }

            // Ninho ainda pode parir filhote — isso e o Ninho funcionando, nao spawn de onda.
            Assert.LessOrEqual(spawned, 12,
                "A agenda de Investidas continuou gerando dentro da janela de amanhecer");
        }

        /// <summary>
        /// O ciclo de luz e a mesma funcao para todo mundo: ceu, HUD e (depois) trilha. Se ela
        /// nao for monotona nos extremos, o sol nasce e se poe duas vezes na mesma noite.
        /// </summary>
        [Test]
        public void CurvaDeLuz_ComecaClaraEFechaNaNoite()
        {
            var sim = NewSim(_content);
            var rules = _content.Rules;

            Assert.AreEqual(0f, DayNightCycle.NightAmount(sim.State, rules), 0.001f,
                "O primeiro Dia comeca com luz plena");

            sim.State.Phase = PhaseId.Dia;
            sim.State.PhaseDuration = rules.DiaSeconds;
            sim.State.PhaseElapsed = rules.DiaSeconds;
            float fimDoDia = DayNightCycle.NightAmount(sim.State, rules);
            Assert.Greater(fimDoDia, 0.7f, "O fim do Dia tem de estar quase escuro");

            sim.State.Phase = PhaseId.Noite;
            sim.State.PhaseDuration = rules.NoiteSeconds;
            sim.State.PhaseElapsed = rules.NoiteSeconds * 0.5f;
            Assert.AreEqual(1f, DayNightCycle.NightAmount(sim.State, rules), 0.001f,
                "O meio da noite e escuridao plena");

            sim.State.PhaseElapsed = rules.NoiteSeconds;
            Assert.Less(DayNightCycle.NightAmount(sim.State, rules), 0.05f,
                "O amanhecer tem de devolver a luz antes de a fase virar");
        }

        /// <summary>
        /// Explorar paga em XP e Ouro; colher paga em Madeira e Pedra. Se um Esconderijo
        /// entregasse recurso de manutencao, o Dia teria uma atividade so feita de dois jeitos.
        /// </summary>
        [Test]
        public void Esconderijo_PagaEmProgresso_NuncaEmManutencao()
        {
            var sim = NewSim(_content);
            Assert.IsNotEmpty(sim.State.Caches, "O amanhecer precisa esconder alguma coisa na mata");

            var cache = sim.State.Caches[0];
            var hero = sim.State.Heroes[0];
            float xpAntes = sim.State.Xp;
            float madeiraAntes = sim.State.SiloWood;
            float pedraAntes = sim.State.Stone;

            hero.Position = cache.Position;
            ExplorationSystem.Tick(sim.State, _content, sim.Events);

            Assert.Greater(sim.State.Xp, xpAntes, "Esconderijo tem de render XP");
            Assert.AreEqual(madeiraAntes, sim.State.SiloWood, 0.001f, "Esconderijo nao rende Madeira");
            Assert.AreEqual(pedraAntes, sim.State.Stone, 0.001f, "Esconderijo nao rende Pedra");
        }

        /// <summary>
        /// Todo Esconderijo tem de ser alcancavel. Nasceram fora do limite de movimento do heroi
        /// uma vez — os Relicarios eram literalmente impossiveis de pegar, e nada no codigo
        /// avisava. Este teste existe para que isso nao volte em silencio.
        /// </summary>
        [Test]
        public void TodoEsconderijo_CabeDentroDoMundoAlcancavel()
        {
            var sim = NewSim(_content);
            foreach (var cache in sim.State.Caches)
            {
                float d = Vec2.Distance(cache.Position, sim.State.CityCenter);
                Assert.LessOrEqual(d, sim.State.WorldRadius,
                    $"Esconderijo a {d:0.0} celulas, alem do limite de {sim.State.WorldRadius:0.0} " +
                    "que o heroi consegue andar");
            }

            foreach (var node in sim.State.Nodes)
            {
                float d = Vec2.Distance(node.Position, sim.State.CityCenter);
                Assert.LessOrEqual(d, sim.State.WorldRadius, "No de recurso fora do alcance do heroi");
            }
        }

        [Test]
        public void PartidaTermina_SempreComUmDesfecho()
        {
            var sim = NewSim(_content);

            int steps = 0;
            while (!sim.State.IsOver && steps < 400000)
            {
                sim.StepFixed();
                sim.Events.Clear();
                steps++;
            }

            Assert.IsTrue(sim.State.IsOver, "A partida nao terminou em tempo razoavel — ha um deadlock de fase");
            Assert.AreNotEqual(MatchOutcome.EmAndamento, sim.State.Outcome);
        }

        [Test]
        public void Noite_GeraMonstros_EODiaNao()
        {
            var sim = NewSim(_content);
            int maxDeDia = 0, maxDeNoite = 0;

            for (int i = 0; i < 40000 && sim.State.TurnNumber < 2; i++)
            {
                sim.StepFixed();
                sim.Events.Clear();

                if (sim.State.Phase == PhaseId.Dia)
                {
                    if (sim.State.Monsters.Count > maxDeDia) maxDeDia = sim.State.Monsters.Count;
                }
                else if (sim.State.Monsters.Count > maxDeNoite)
                {
                    maxDeNoite = sim.State.Monsters.Count;
                }
            }

            Assert.Greater(maxDeNoite, 0, "Nenhum monstro nasceu na primeira Noite");
            Assert.AreEqual(0, maxDeDia,
                "O Dia tem de ser seguro — e a premissa que faz explorar valer a pena");
        }

        [Test]
        public void Amanhecer_NaoDeixaMonstroPendente()
        {
            var sim = NewSim(_content);
            bool jaFoiNoite = false;

            for (int i = 0; i < 40000; i++)
            {
                sim.StepFixed();
                sim.Events.Clear();
                if (sim.State.Phase == PhaseId.Noite) jaFoiNoite = true;
                if (jaFoiNoite && sim.State.Phase == PhaseId.Dia)
                {
                    Assert.AreEqual(0, sim.State.Monsters.Count,
                        "O Dia comecou com monstros vivos — o Dia tem de ser seguro, " +
                        "e a fronteira limpa tambem e requisito de netcode");
                    return;
                }
            }

            Assert.Fail("Nunca amanheceu");
        }

        [Test]
        public void MesmaSeed_ProduzMesmaPartida()
        {
            var a = NewSim(new DefaultContent(), 4, 777);
            var b = NewSim(new DefaultContent(), 4, 777);

            for (int i = 0; i < 6000; i++)
            {
                a.StepFixed(); a.Events.Clear();
                b.StepFixed(); b.Events.Clear();
            }

            Assert.AreEqual(a.State.TurnNumber, b.State.TurnNumber);
            Assert.AreEqual(a.State.Phase, b.State.Phase);
            Assert.AreEqual(a.State.Monsters.Count, b.State.Monsters.Count);
            Assert.AreEqual(a.State.TownHallHealth, b.State.TownHallHealth, 0.001f,
                "Mesma seed precisa produzir a mesma partida — sem isso nao ha replay nem netcode confiavel");
        }

        /// <summary>
        /// A predicao do cliente vai integrar o proprio heroi com HeroMotion.Step enquanto o host
        /// integra com HeroSystem.Tick. Se os dois caminhos derivarem um do outro, a reconciliacao
        /// mede latencia; se derivarem de codigos diferentes, mede divergencia — e o heroi corrige
        /// sozinho a cada quadro mesmo com a rede perfeita. Este teste e o que impede alguem de
        /// reintroduzir movimento dentro do HeroSystem sem passar pelo integrador comum.
        /// </summary>
        [Test]
        public void HeroMotion_ReproduzIntegradorDoHeroSystem()
        {
            var autoritativo = new MatchSimulation(_content, 1234, 4);
            var previsto = new MatchSimulation(_content, 1234, 4);
            foreach (var p in autoritativo.State.Players) p.IsAutomaton = false;
            foreach (var p in previsto.State.Players) p.IsAutomaton = false;

            var heroA = autoritativo.State.Heroes[0];
            var heroB = previsto.State.Heroes[0];
            var spec = _content.GetHero(heroA.Def);
            var log = new SimEventLog();

            // Diagonal nao normalizada de proposito: exercita o clamp de magnitude dos dois lados.
            var direcao = new Vec2(0.8f, -0.6f);

            for (int i = 0; i < 100; i++)
            {
                heroA.MoveInput = direcao;
                HeroSystem.Tick(autoritativo.State, _content, MatchSimulation.FixedDelta, log, false);
                log.Clear();

                HeroMotion.Step(previsto.State, heroB, spec, direcao, MatchSimulation.FixedDelta);
            }

            Assert.AreEqual(heroA.Position.X, heroB.Position.X, 0f,
                "Predicao e autoridade divergiram em X — ha uma segunda copia do integrador");
            Assert.AreEqual(heroA.Position.Y, heroB.Position.Y, 0f,
                "Predicao e autoridade divergiram em Y — ha uma segunda copia do integrador");
            Assert.AreNotEqual(autoritativo.State.CityCenter.X, heroA.Position.X,
                "O heroi nao saiu do lugar: o teste nao provou nada");
        }

        /// <summary>
        /// O arquivo de retomada precisa reproduzir a sequencia exata, nao apenas a semente. Um
        /// gerador que ja sacou 1000 numeros nao volta ao mesmo ponto so por ser resemeado.
        /// </summary>
        [Test]
        public void Rng_RestauraEstado_DepoisDeMilSaques()
        {
            var rng = new Rng(99);
            for (int i = 0; i < 1000; i++) rng.NextUInt();

            rng.GetState(out uint x, out uint y, out uint z, out uint w);
            var esperado = new uint[16];
            for (int i = 0; i < esperado.Length; i++) esperado[i] = rng.NextUInt();

            rng.SetState(x, y, z, w);
            for (int i = 0; i < esperado.Length; i++)
                Assert.AreEqual(esperado[i], rng.NextUInt(),
                    "A sequencia mudou depois de restaurar o estado — o save nao reproduz a partida");
        }

        /// <summary>
        /// O contador de tick e o relogio comum de host e cliente. Se ele contar tempo real em vez
        /// de passos fixos, todo carimbo de quadro de movimento e de lote de evento mente.
        /// </summary>
        [Test]
        public void Tick_ContaPassosFixos_NaoTempoReal()
        {
            var sim = NewSim(_content);
            Assert.AreEqual(0, sim.Tick, "A partida comeca no tick zero");

            for (int i = 0; i < 250; i++) { sim.StepFixed(); sim.Events.Clear(); }
            Assert.AreEqual(250, sim.Tick);

            // Advance consome tempo real e so avanca em multiplos do passo fixo.
            sim.Advance(MatchSimulation.FixedDelta * 3f);
            sim.Events.Clear();
            Assert.AreEqual(253, sim.Tick);

            // Meio passo nao avanca tick nenhum: fica no acumulador.
            sim.Advance(MatchSimulation.FixedDelta * 0.5f);
            sim.Events.Clear();
            Assert.AreEqual(253, sim.Tick);
        }

        [Test]
        public void CurvaDeXp_EscalaComNumeroDeJogadores()
        {
            // A agencia per capita precisa ser identica com 1, 2, 3 ou 4 jogadores.
            float solo = EconomySystem.XpRequiredFor(_content, 1, 1);
            float quarteto = EconomySystem.XpRequiredFor(_content, 1, 4);
            Assert.AreEqual(solo * 4f, quarteto, 0.001f);
        }

        [Test]
        public void DanoNaPrefeitura_TemTetoPorGolpe()
        {
            var sim = NewSim(_content);
            float max = sim.State.TownHallMaxHealth;

            Combat.DamageCity(sim.State, 999999f, sim.Events);

            Assert.AreEqual(max * 0.75f, sim.State.TownHallHealth, 0.01f,
                "Nenhum golpe unico pode tirar mais de 25% do HP maximo");
        }
    }
}
