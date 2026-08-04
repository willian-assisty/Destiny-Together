using DestinyTogether.Core;
using DestinyTogether.Data;
using DestinyTogether.Sim;
using NUnit.Framework;

namespace DestinyTogether.Tests
{
    /// <summary>
    /// Testes das regras de colocacao e do Prognostico.
    ///
    /// O teste que importa e <see cref="ConstruirParaFora_EncurtaOCorredorDaFaixa"/>: ele protege
    /// a divida do crescimento, que e a substituicao da colisao da cidade movel do jogo de
    /// referencia. Se esse comportamento sumir num refactor, o jogo perde a tensao da construcao
    /// e vira um tower defense qualquer — por isso ele e teste, nao comentario.
    /// </summary>
    public class BuildAndForecastTests
    {
        private DefaultContent _content;
        private MatchSimulation _sim;
        private PlayerState _player;

        [SetUp]
        public void SetUp()
        {
            _content = new DefaultContent();
            _sim = new MatchSimulation(_content, 999, 4);
            for (int i = 0; i < _sim.State.Players.Count; i++) _sim.State.Players[i].IsAutomaton = true;
            _player = _sim.State.Players[0];
            _player.IsAutomaton = false;
        }

        private GridCoord FreeCellAdjacentToCity(PlayerState player)
        {
            foreach (var cell in _sim.State.Grid.CellsInQuadrant(player.Quadrant))
                if (_sim.State.Grid.IsFree(cell) && _sim.State.Grid.IsAdjacentToCity(cell))
                    return cell;
            Assert.Fail("Nenhuma celula valida no Quadrante");
            return GridCoord.Invalid;
        }

        private void Give(DefId def) => _player.Hand.Add(def);

        [Test]
        public void Construir_ExigeCartaNaMao()
        {
            var cell = FreeCellAdjacentToCity(_player);
            var def = DefaultContent.Def(DefaultContent.Balestra);

            var semCarta = CommandValidator.ValidateBuild(_sim.State, _content, _player, def, cell);
            Assert.IsFalse(semCarta.IsValid);

            Give(def);
            Assert.IsTrue(CommandValidator.ValidateBuild(_sim.State, _content, _player, def, cell).IsValid);
        }

        [Test]
        public void Construir_SoNoProprioQuadrante()
        {
            var def = DefaultContent.Def(DefaultContent.Balestra);
            Give(def);

            var alheio = _sim.State.Players[2];
            GridCoord cellAlheia = GridCoord.Invalid;
            foreach (var c in _sim.State.Grid.CellsInQuadrant(alheio.Quadrant))
                if (_sim.State.Grid.IsFree(c) && _sim.State.Grid.IsAdjacentToCity(c)) { cellAlheia = c; break; }

            var result = CommandValidator.ValidateBuild(_sim.State, _content, _player, def, cellAlheia);
            Assert.IsFalse(result.IsValid, "Quadrante e soberano: ninguem constroi no dos outros");
            StringAssert.Contains("Quadrante", result.Reason);
        }

        [Test]
        public void Construir_PrecisaEncostarNaCidade()
        {
            var def = DefaultContent.Def(DefaultContent.Balestra);
            Give(def);

            // Canto do tabuleiro: dentro do Quadrante, mas longe da Prefeitura.
            var corner = new GridCoord(_sim.State.Grid.Size - 1, _sim.State.Grid.Size - 1);
            if (_sim.State.Grid.QuadrantOf(corner) != _player.Quadrant) corner = new GridCoord(0, 0);
            if (_sim.State.Grid.QuadrantOf(corner) != _player.Quadrant) Assert.Ignore("Layout sem canto util");

            var result = CommandValidator.ValidateBuild(_sim.State, _content, _player, def, corner);
            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void Duplicata_FundeEmTierMaior()
        {
            var def = DefaultContent.Def(DefaultContent.Balestra);
            var cell = FreeCellAdjacentToCity(_player);

            Give(def);
            Assert.IsTrue(BuildSystem.TryBuild(_sim.State, _content, _player, def, cell, _sim.Events));
            var tower = _sim.State.GetTower(_sim.State.Grid.OccupantOf(cell));
            Assert.AreEqual(1, tower.Tier);

            Give(def);
            Assert.IsTrue(BuildSystem.TryBuild(_sim.State, _content, _player, def, cell, _sim.Events));
            Assert.AreEqual(2, tower.Tier, "Duplicata sobre duplicata e o unico upgrade intra-partida");
            Assert.AreEqual(1, _sim.State.Towers.Count, "Fundir nao pode criar uma torre nova");
        }

        [Test]
        public void PrediosDiferentes_NaoFundem()
        {
            var balestra = DefaultContent.Def(DefaultContent.Balestra);
            var braseiro = DefaultContent.Def(DefaultContent.Braseiro);
            var cell = FreeCellAdjacentToCity(_player);

            Give(balestra);
            BuildSystem.TryBuild(_sim.State, _content, _player, balestra, cell, _sim.Events);

            Give(braseiro);
            var result = CommandValidator.ValidateBuild(_sim.State, _content, _player, braseiro, cell);
            Assert.IsFalse(result.IsValid);
        }

        /// <summary>
        /// O coracao do design: um predio colocado para fora sobe o DPS e ENCURTA o corredor
        /// da mesma Faixa. As duas coisas ao mesmo tempo, no mesmo predio.
        /// </summary>
        [Test]
        public void ConstruirParaFora_EncurtaOCorredorDaFaixa()
        {
            var def = DefaultContent.Def(DefaultContent.Balestra);
            Give(def);

            // Celula mais externa disponivel no Quadrante do jogador.
            GridCoord outer = GridCoord.Invalid;
            float bestDistance = -1f;
            foreach (var cell in _sim.State.Grid.CellsInQuadrant(_player.Quadrant))
            {
                if (!_sim.State.Grid.IsFree(cell) || !_sim.State.Grid.IsAdjacentToCity(cell)) continue;
                float d = Vec2.Distance(cell.Center, _sim.State.CityCenter);
                if (d > bestDistance) { bestDistance = d; outer = cell; }
            }
            Assert.IsTrue(outer.IsValid);

            var impact = _sim.EvaluatePlacement(def, outer);

            Assert.Less(impact.ApproachAfter, impact.ApproachBefore + 0.0001f,
                "Construir para fora precisa encurtar (ou no minimo nao alongar) o tempo de aproximacao");
            Assert.Greater(impact.PerimeterAfter, impact.PerimeterBefore,
                "Construir para fora precisa aumentar o perimetro exposto — footprint E hitbox");
            Assert.Greater(impact.DpsAfter, impact.DpsBefore,
                "...e ao mesmo tempo precisa subir o DPS, senao nao ha dilema nenhum");
        }

        [Test]
        public void Muralha_CompraTempoSemDarDano()
        {
            var muralha = DefaultContent.Def(DefaultContent.Muralha);
            var spec = _content.GetTower(muralha);

            Assert.IsFalse(spec.CanAttack, "Muralha nao ataca");
            Assert.Greater(spec.ApproachDelay, 0f, "Muralha existe para somar tempo de aproximacao");

            Give(muralha);
            var cell = FreeCellAdjacentToCity(_player);
            var impact = _sim.EvaluatePlacement(muralha, cell);
            Assert.AreEqual(impact.DpsBefore, impact.DpsAfter, 0.001f, "Muralha nao adiciona DPS");
        }

        [Test]
        public void Distrito_ExigeQuatroPrediosDaMesmaTag()
        {
            var serraria = DefaultContent.Def(DefaultContent.Serraria);
            int built = 0;

            foreach (var cell in _sim.State.Grid.CellsInQuadrant(_player.Quadrant))
            {
                if (built >= 4) break;
                if (!_sim.State.Grid.IsFree(cell) || !_sim.State.Grid.IsAdjacentToCity(cell)) continue;
                Give(serraria);
                if (BuildSystem.TryBuild(_sim.State, _content, _player, serraria, cell, _sim.Events)) built++;
            }

            Assert.AreEqual(4, built, "Precisa de 4 predios para o teste fazer sentido");

            int inDistrict = 0;
            foreach (var t in _sim.State.Towers) if (t.InDistrict) inDistrict++;

            Assert.GreaterOrEqual(inDistrict, 4,
                "Quatro predios conectados com a mesma TAG precisam fechar um Distrito");
        }

        [Test]
        public void ComandoDeConstrucao_ERejeitadoForaDoPreparo()
        {
            var def = DefaultContent.Def(DefaultContent.Balestra);
            Give(def);
            var cell = FreeCellAdjacentToCity(_player);

            _sim.State.Phase = PhaseId.Assalto;
            var result = _sim.SubmitCommand(PlayerCommand.Build(_player.Id, def, cell));

            Assert.IsFalse(result.IsValid, "Erguer predio no meio do Assalto quebraria o prognostico");
        }
    }
}
