using NUnit.Framework;
using UnityEngine;
using Drush.Data;

namespace Drush.Tests.Editor
{
    /// <summary>
    /// Edit-mode data-driven tests validating balancing KPIs (§4.2).
    /// Each test loads a ScriptableObject dataset via [TestCaseSource].
    /// </summary>
    [TestFixture]
    public sealed class BalancingKPITests
    {
        private static BalancingData[] LoadAllBalancingData()
        {
            // Load every BalancingData asset under Assets/Tests/TestData at edit-time
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:BalancingData", new[] { "Assets/Tests/TestData" });

            if (guids.Length == 0)
            {
                // Fallback: load persistent asset from disk instead of creating temp instance
                // Temp instances are garbage-collected in batch mode before tests run
                var fallback = UnityEditor.AssetDatabase.LoadAssetAtPath<BalancingData>(
                    "Assets/Tests/TestData/BalancingData.asset");
                
                Assert.IsNotNull(fallback, 
                    "No BalancingData test assets found, and fallback (Assets/Test/TestData/BalancingData.asset) is missing");
                
                return new[] { fallback };
            }

            var results = new BalancingData[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                results[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<BalancingData>(path);
            }
            return results;
        }

        // Cache array to avoid recomputation on each test run
        private static BalancingData[] _cachedProfiles;
        private static BalancingData[] AllProfiles => _cachedProfiles ??= LoadAllBalancingData();

        // ── HP / Damage ratio ──────────────────────────────────────────

        [Test, TestCaseSource(nameof(AllProfiles))]
        public void HPDamageRatio_WithinAcceptableRange(BalancingData data)
        {
            float ratio = data.HPDamageRatio;

            Assert.That(ratio,
                Is.InRange(data.hpDamageRatioRange.x, data.hpDamageRatioRange.y),
                $"[{data.name}] HP/Damage ratio {ratio:F2} outside [{data.hpDamageRatioRange.x}, {data.hpDamageRatioRange.y}]");
        }

        // ── Time-to-Kill ───────────────────────────────────────────────

        [Test, TestCaseSource(nameof(AllProfiles))]
        public void TTK_WithinDesignBounds(BalancingData data)
        {
            float ttk = data.TheoreticalTTK;

            Assert.That(ttk,
                Is.InRange(data.timeToKillRange.x, data.timeToKillRange.y),
                $"[{data.name}] TTK {ttk:F2}s outside [{data.timeToKillRange.x}s, {data.timeToKillRange.y}s]");
        }

        // ── Economic inflation per game ────────────────────────────────

        [Test, TestCaseSource(nameof(AllProfiles))]
        public void EconomicInflation_NeverExceedsThreshold(BalancingData data)
        {
            for (int game = 2; game <= data.gameCount; game++)
            {
                float inflation = data.EconomicInflationAtGame(game);

                Assert.That(inflation,
                    Is.LessThanOrEqualTo(data.maxEconomicInflationPercent),
                    $"[{data.name}] Game {game}: inflation {inflation:F2}% exceeds {data.maxEconomicInflationPercent}%");
            }
        }

        // ── Base stats sanity ──────────────────────────────────────────

        [Test, TestCaseSource(nameof(AllProfiles))]
        public void BaseStats_AreStrictlyPositive(BalancingData data)
        {
            Assert.That(data.baseHP, Is.GreaterThan(0f), $"[{data.name}] baseHP must be > 0");
            Assert.That(data.baseDamage, Is.GreaterThan(0f), $"[{data.name}] baseDamage must be > 0");
            Assert.That(data.baseArmor, Is.GreaterThanOrEqualTo(0f), $"[{data.name}] baseArmor must be >= 0");
            Assert.That(data.baseSpeed, Is.GreaterThan(0f), $"[{data.name}] baseSpeed must be > 0");
        }

        // ── Gold curve monotonicity ────────────────────────────────────

        [Test, TestCaseSource(nameof(AllProfiles))]
        public void GoldCurve_IsMonotonicallyIncreasing(BalancingData data)
        {
            float previous = data.startingGold;

            for (int game = 2; game <= data.gameCount; game++)
            {
                float current = data.startingGold * Mathf.Pow(data.goldPerGameMultiplier, game - 1);
                Assert.That(current, Is.GreaterThan(previous),
                    $"[{data.name}] Gold decreased at game {game}");
                previous = current;
            }
        }

        // ── Edge cases and boundary tests ──────────────────────────

        [Test, TestCaseSource(nameof(AllProfiles))]
        public void Ranges_MinNotGreaterThanMax(BalancingData data)
        {
            Assert.That(data.hpDamageRatioRange.x, Is.LessThanOrEqualTo(data.hpDamageRatioRange.y),
                $"[{data.name}] HP/Damage ratio min > max");
            
            Assert.That(data.timeToKillRange.x, Is.LessThanOrEqualTo(data.timeToKillRange.y),
                $"[{data.name}] TTK min > max");
        }

        [Test, TestCaseSource(nameof(AllProfiles))]
        public void GoldMultiplier_IsPositive(BalancingData data)
        {
            Assert.That(data.goldPerGameMultiplier, Is.GreaterThan(0f),
                $"[{data.name}] Gold multiplier must be > 0");
        }

        [Test, TestCaseSource(nameof(AllProfiles))]
        public void GameCount_IsAtLeastOne(BalancingData data)
        {
            Assert.That(data.gameCount, Is.GreaterThanOrEqualTo(1),
                $"[{data.name}] Game count must be >= 1");
        }

        [Test, TestCaseSource(nameof(AllProfiles))]
        public void MaxInflationPercent_IsNonNegative(BalancingData data)
        {
            Assert.That(data.maxEconomicInflationPercent, Is.GreaterThanOrEqualTo(0f),
                $"[{data.name}] Max inflation must be >= 0%");
        }

        [Test, TestCaseSource(nameof(AllProfiles))]
        public void StartingGold_IsPositive(BalancingData data)
        {
            Assert.That(data.startingGold, Is.GreaterThan(0f),
                $"[{data.name}] Starting gold must be > 0");
        }
    }
}
