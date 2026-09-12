using Godot;
using MyGame.Combat;
using MyGame.Gameplay;

namespace MyGame.Tests
{
    /// <summary>
    /// The sin modifier table moved from fourteen hardcoded fields to a designer-owned array in
    /// SinTuning.json. The array bought a real failure the fields could not have: a row can now go
    /// missing, and every lookup still succeeds - it just returns neutral. A sin that stops mattering
    /// looks identical to a sin that was never pressed, which is the whole reason these exist.
    ///
    /// No scene: the controller is a plain node with no scene dependencies, so each test builds one
    /// host and tears it down.
    ///
    /// UNITS: nothing here is a distance. Multipliers, seconds and flags all cross unscaled, so every
    /// number below is the authored Unity number unchanged.
    /// </summary>
    public sealed class GameplaySinTuningTests
    {
        private Node _host;

        [TearDown]
        public void TearDown()
        {
            if (GodotObject.IsInstanceValid(_host))
                _host.Free();

            _host = null;
        }

        /// <summary>
        /// The shipped file has to reach the game. A JSON typo or a renamed field shows up here as a
        /// null catalog entry rather than as three sins quietly running on their fallback values.
        /// </summary>
        [Test]
        public void SinTuningJson_LoadsAndCarriesAllSevenSins()
        {
            SinTuningData tuning = GameplayTuningCatalog.Load()?.SinTuning;
            Assert.NotNull(tuning, "SinTuning.json should load through the tuning catalog.");
            Assert.NotNull(tuning.sins, "The authored file should carry a sin table.");
            Assert.AreEqual(7, tuning.sins.Length,
                "All seven sins need a row; a sin with no row runs neutral no matter what the enum says.");

            for (var sin = SinState.Wrath; sin <= SinState.Lust; sin++)
            {
                bool found = false;
                foreach (SinModifiers row in tuning.sins)
                {
                    if (row != null && row.sin == sin)
                        found = true;
                }

                Assert.IsTrue(found, $"SinTuning.json is missing a row for {sin}.");
            }
        }

        /// <summary>
        /// The three shipped sins carry numbers the fight was tuned against. Rewriting them is a
        /// designer decision; losing them to a bad merge or a re-seed is not, and nothing else notices.
        /// </summary>
        [Test]
        public void SinTuningJson_KeepsTheShippedNumbersForTheThreeLiveSins()
        {
            SinTuningData tuning = GameplayTuningCatalog.Load()?.SinTuning;
            Assert.NotNull(tuning, "SinTuning.json should load through the tuning catalog.");

            SinModifiers wrath = RowFor(tuning, SinState.Wrath);
            Assert.AreEqual(1.5f, wrath.damageMultiplier, 0.001f, "Wrath's damage bonus is what makes it worth the humanity.");
            Assert.IsTrue(wrath.disableHeal, "Wrath forbidding the flask is the trade it is built on.");

            SinModifiers sloth = RowFor(tuning, SinState.Sloth);
            Assert.AreEqual(0.7f, sloth.speedMultiplier, 0.001f, "Sloth pays for its parry window in movement speed.");
            Assert.AreEqual(1.5f, sloth.parryWindowMultiplier, 0.001f, "A Sloth that does not widen the parry window does nothing at all.");

            SinModifiers pride = RowFor(tuning, SinState.Pride);
            Assert.AreEqual(1.5f, pride.damageTakenMultiplier, 0.001f, "Pride's whole cost is the damage it takes.");
            Assert.IsTrue(pride.perfectParryBonus, "Pride's upside is the perfect-parry bonus.");
        }

        /// <summary>
        /// The defect the warning was added for. A designer trimming the table - deleting the four
        /// neutral rows to tidy the file, or truncating it in a merge - takes a live sin down with it,
        /// and the sin still activates, still spends resonance and humanity, and changes nothing.
        /// </summary>
        [Test]
        public void ShorteningTheTable_LeavesTheDroppedSinDetectable()
        {
            SinResonanceController sin = BuildController();

            var trimmed = new SinTuningData
            {
                sins = new[]
                {
                    new SinModifiers { sin = SinState.Wrath, damageMultiplier = 1.5f, disableHeal = true }
                }
            };

            sin.ApplyTuning(trimmed);

            Assert.IsTrue(sin.HasRow(SinState.Wrath), "The row that survived the trim should still be found.");
            Assert.IsFalse(sin.HasRow(SinState.Pride),
                "A sin dropped from the table has to be reportable; without this it activates and silently does nothing.");
        }

        /// <summary>
        /// A file someone is still filling in is not an instruction to run every sin neutral. The
        /// difference matters because the empty case is what a freshly created resource looks like.
        /// </summary>
        [Test]
        public void ApplyTuning_WithNoRows_KeepsTheShippedTable()
        {
            SinResonanceController sin = BuildController();

            var empty = new SinTuningData
            {
                sins = System.Array.Empty<SinModifiers>(),
                resonanceCost = 12f,
            };

            sin.ApplyTuning(empty);

            Assert.IsTrue(sin.HasRow(SinState.Wrath),
                "An authored file with an empty table should leave the shipped rows in place.");
        }

        /// <summary>
        /// Null is what a scene with no design file yields, and it is the state every synthetic test
        /// player is built in. It has to be a no-op, not a wipe.
        /// </summary>
        [Test]
        public void ApplyTuning_WithNoFile_ChangesNothing()
        {
            SinResonanceController sin = BuildController();

            sin.ApplyTuning(null);

            Assert.IsTrue(sin.HasRow(SinState.Pride), "A missing design file must leave the shipped defaults alone.");
        }

        /// <summary>
        /// Unity built one GameObject and added the component to it. A component is a child node here,
        /// so the host is a node and the controller hangs under it. Deliberately left out of the scene
        /// tree: nothing under test here ticks - ApplyTuning and HasRow are both pure - and a fixture
        /// that needs no frames should not be taking one.
        /// </summary>
        private SinResonanceController BuildController()
        {
            _host = new Node { Name = "SinTuningTestHost" };
            var controller = new SinResonanceController { Name = nameof(SinResonanceController) };
            _host.AddChild(controller);

            // The shipped file first, exactly as the spawner hands it over. Required since K7b - the
            // controller has no initialisers left - and this fixture never enters the tree, so the
            // deferred check that would have caught it never runs here. Each test below re-tunes on
            // top with the synthetic table it is measuring.
            PlayerFixture.Configure(controller);
            return controller;
        }

        private static SinModifiers RowFor(SinTuningData tuning, SinState sin)
        {
            foreach (SinModifiers row in tuning.sins)
            {
                if (row != null && row.sin == sin)
                    return row;
            }

            Assert.Fail($"SinTuning.json is missing a row for {sin}.");
            return null;
        }
    }
}
