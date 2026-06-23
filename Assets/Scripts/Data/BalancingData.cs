using UnityEngine;

namespace Drush.Data
{
    /// <summary>
    /// Data-driven balancing config consumed by both runtime and test assemblies.
    /// </summary>
    [CreateAssetMenu(fileName = "BalancingData", menuName = "Drush/Data/Balancing Data")]
    public sealed class BalancingData : ScriptableObject
    {
        // TODO: Update default values with final game design data
        [Header("Unit Stats")]
        public float baseHP = 100f; // TODO: Replace with final HP value
        public float baseDamage = 20f; // TODO: Replace with final damage value
        public float baseArmor = 5f; // TODO: Replace with final armor value
        public float baseSpeed = 3.5f; // TODO: Replace with final speed value

        [Header("KPI Thresholds")]
        [Tooltip("Acceptable HP/Damage ratio range")]
        public Vector2 hpDamageRatioRange = new(3f, 7f); // TODO: Replace with final ratio range

        [Tooltip("Max tolerated economic inflation per game (%)")]
        public float maxEconomicInflationPercent = 15f; // TODO: Replace with final threshold

        [Tooltip("Target TTK range in seconds")]
        public Vector2 timeToKillRange = new(2f, 8f); // TODO: Replace with final TTK range

        // TODO: Update economy values with final game design data
        [Header("Economy")]
        public int startingGold = 100; // TODO: Replace with final starting gold
        public float goldPerGameMultiplier = 1.12f; // TODO: Replace with final multiplier
        public int gameCount = 20; // TODO: Replace with final game count

        // Derived KPIs
        public float HPDamageRatio => baseHP / baseDamage;
        public float EffectiveHP => baseHP * (1f + baseArmor * 0.06f);
        public float TheoreticalTTK => EffectiveHP / baseDamage;

        public float EconomicInflationAtGame(int game)
        {
            if (game <= 1) return 0f;
            float previousGold = startingGold * Mathf.Pow(goldPerGameMultiplier, game - 2);
            float currentGold  = startingGold * Mathf.Pow(goldPerGameMultiplier, game - 1);
            return (currentGold - previousGold) / previousGold * 100f;
        }
    }
}
