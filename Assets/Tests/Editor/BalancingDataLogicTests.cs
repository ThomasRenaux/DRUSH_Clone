using NUnit.Framework;
using UnityEngine;
using Drush.Data;

namespace Drush.Tests.Editor
{
    /// <summary>
    /// Edit-mode unit tests for the pure-logic getters and formulas of BalancingData.
    /// Uses in-memory ScriptableObject instances to bypass asset loading and isolate
    /// the math from any project-specific dataset. Complements BalancingKPITests
    /// which validates real datasets against design thresholds.
    /// </summary>
    [TestFixture]
    public sealed class BalancingDataLogicTests
    {
        private BalancingData data;

        [SetUp]
        public void SetUp()
        {
            data = ScriptableObject.CreateInstance<BalancingData>();
        }

        [TearDown]
        public void TearDown()
        {
            if (data != null) Object.DestroyImmediate(data);
        }

        // ── HPDamageRatio ─────────────────────────────────────────────

        [Test]
        public void HPDamageRatio_ReturnsHpDividedByDamage()
        {
            data.baseHP = 120f;
            data.baseDamage = 30f;

            Assert.AreEqual(4f, data.HPDamageRatio, 1e-4f);
        }

        // ── EffectiveHP ───────────────────────────────────────────────

        [Test]
        public void EffectiveHP_NoArmor_EqualsBaseHP()
        {
            data.baseHP = 100f;
            data.baseArmor = 0f;

            Assert.AreEqual(100f, data.EffectiveHP, 1e-4f);
        }

        [Test]
        public void EffectiveHP_WithArmor_AppliesSixPercentMultiplier()
        {
            data.baseHP = 100f;
            data.baseArmor = 5f; // 1 + 5 * 0.06 = 1.30 → 130

            Assert.AreEqual(130f, data.EffectiveHP, 1e-4f);
        }

        [Test]
        public void EffectiveHP_NegativeArmor_StillAppliesFormula()
        {
            // Edge case: prevents accidental Mathf.Max being introduced silently
            data.baseHP = 100f;
            data.baseArmor = -1f;

            Assert.AreEqual(94f, data.EffectiveHP, 1e-4f);
        }

        // ── TheoreticalTTK ────────────────────────────────────────────

        [Test]
        public void TheoreticalTTK_EqualsEffectiveHpOverDamage()
        {
            data.baseHP = 100f;
            data.baseArmor = 5f;
            data.baseDamage = 26f; // 130 / 26 = 5

            Assert.AreEqual(5f, data.TheoreticalTTK, 1e-4f);
        }

        // ── EconomicInflationAtGame ───────────────────────────────────

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(-3)]
        public void EconomicInflationAtGame_GameLessThanOrEqualOne_ReturnsZero(int game)
        {
            data.startingGold = 100;
            data.goldPerGameMultiplier = 1.12f;

            Assert.AreEqual(0f, data.EconomicInflationAtGame(game));
        }

        [Test]
        public void EconomicInflationAtGame_GameTwo_EqualsMultiplierMinusOnePercent()
        {
            data.startingGold = 100;
            data.goldPerGameMultiplier = 1.12f; // 12% expected

            float inflation = data.EconomicInflationAtGame(2);

            Assert.AreEqual(12f, inflation, 1e-3f);
        }

        [Test]
        public void EconomicInflationAtGame_IsConstantAcrossGames_ForFixedMultiplier()
        {
            // Geometric progression: inflation rate is multiplier-independent of game index.
            data.startingGold = 100;
            data.goldPerGameMultiplier = 1.08f;

            float g3 = data.EconomicInflationAtGame(3);
            float g10 = data.EconomicInflationAtGame(10);

            Assert.AreEqual(g3, g10, 1e-3f);
        }

        [Test]
        public void EconomicInflationAtGame_MultiplierOne_ReturnsZero()
        {
            data.startingGold = 100;
            data.goldPerGameMultiplier = 1f;

            Assert.AreEqual(0f, data.EconomicInflationAtGame(5), 1e-4f);
        }
    }
}
