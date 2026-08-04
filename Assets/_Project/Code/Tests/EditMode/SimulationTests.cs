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
            Assert.AreEqual(PhaseId.Preparo, sim.State.Phase);
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
        public void CicloDeFases_PreparoAssaltoBalanco_SemTravar()
        {
            var sim = NewSim(_content);
            var seen = new System.Collections.Generic.List<PhaseId>();
            var last = PhaseId.None;

            for (int i = 0; i < 20000 && sim.State.TurnNumber < 2; i++)
            {
                sim.StepFixed();
                sim.Events.Clear();
                if (sim.State.Phase != last)
                {
                    last = sim.State.Phase;
                    seen.Add(last);
                }
            }

            CollectionAssert.Contains(seen, PhaseId.Assalto, "O Assalto nunca comecou");
            CollectionAssert.Contains(seen, PhaseId.Balanco, "O Balanco nunca aconteceu");
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
        public void Assalto_GeraMonstros()
        {
            var sim = NewSim(_content);
            int maxAlive = 0;

            for (int i = 0; i < 20000 && sim.State.TurnNumber < 2; i++)
            {
                sim.StepFixed();
                sim.Events.Clear();
                if (sim.State.Monsters.Count > maxAlive) maxAlive = sim.State.Monsters.Count;
            }

            Assert.Greater(maxAlive, 0, "Nenhum monstro nasceu no primeiro Assalto");
        }

        [Test]
        public void FronteiraDeFase_NaoDeixaMonstroPendente()
        {
            var sim = NewSim(_content);

            for (int i = 0; i < 40000; i++)
            {
                sim.StepFixed();
                sim.Events.Clear();
                if (sim.State.Phase == PhaseId.Balanco)
                {
                    Assert.AreEqual(0, sim.State.Monsters.Count,
                        "O Balanco comecou com monstros vivos — requisito de netcode violado");
                    return;
                }
            }

            Assert.Fail("Nunca chegou ao Balanco");
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
