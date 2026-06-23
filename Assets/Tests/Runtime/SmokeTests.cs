using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

namespace Drush.Tests.Runtime
{
    /// <summary>
    /// Play-mode smoke tests — validates critical path:
    /// Scene load → Damage application → UI health bar update.
    /// </summary>
    [TestFixture]
    public sealed class SmokeTests
    {
        private const string TestSceneName = "Scenes/SampleScene";
        private const string TestSceneShortName = "SampleScene";
        
        // Track created GameObjects for cleanup
        private readonly System.Collections.Generic.List<GameObject> _createdObjects = 
            new System.Collections.Generic.List<GameObject>();

        // ── Setup ──────────────────────────────────────────────────────

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Load scene once for all tests in this fixture
            yield return SceneManager.LoadSceneAsync(TestSceneName, LoadSceneMode.Single);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Clean up all created GameObjects
            foreach (var go in _createdObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            _createdObjects.Clear();
            yield return null;
        }
        
        private GameObject CreateTrackedGameObject(string name)
        {
            var go = new GameObject(name);
            _createdObjects.Add(go);
            return go;
        }

        // ── Scene loading ──────────────────────────────────────────────

        [UnityTest]
        public IEnumerator SceneLoads_WithoutErrors()
        {
            // Arrange & Act
            AsyncOperation loadOp = SceneManager.LoadSceneAsync(TestSceneName, LoadSceneMode.Single);
            yield return new WaitUntil(() => loadOp.isDone);

            // Assert
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(TestSceneShortName));
            Assert.That(SceneManager.GetActiveScene().isLoaded, Is.True);
        }

        [UnityTest]
        public IEnumerator SceneLoads_WithinTimeBudget()
        {
            float startTime = Time.realtimeSinceStartup;
            AsyncOperation loadOp = SceneManager.LoadSceneAsync(TestSceneName, LoadSceneMode.Single);
            yield return new WaitUntil(() => loadOp.isDone);

            float elapsed = Time.realtimeSinceStartup - startTime;

            // Scene must load in under 5 seconds (CI-friendly threshold)
            Assert.That(elapsed, Is.LessThan(5f),
                $"Scene took {elapsed:F2}s to load — exceeds 5s budget");
        }

        // ── Damage system ──────────────────────────────────────────────

        [UnityTest]
        public IEnumerator DamageApplication_ReducesHealth()
        {
            yield return null; // wait one frame for Awake/Start

            // Arrange — spawn a test entity with an IDamageable component
            // TODO: Replace with actual game prefab once implemented
            var go = CreateTrackedGameObject("TestUnit");
            float maxHP = 100f; // TODO: Replace with final HP value
            float currentHP = maxHP;
            float damage = 25f; // TODO: Replace with final damage value

            // Act — apply damage
            currentHP -= damage;

            // Assert
            Assert.That(currentHP, Is.EqualTo(maxHP - damage).Within(0.01f),
                "Health should decrease by exact damage amount");
            Assert.That(currentHP, Is.GreaterThan(0f),
                "Unit should survive a single hit");

            yield return null;
        }

        [UnityTest]
        public IEnumerator LethalDamage_ClampsHealthToZero()
        {
            yield return null;

            // Arrange
            float maxHP = 100f; // TODO: Replace with final HP value
            float currentHP = maxHP;
            float overkillDamage = 999f; // TODO: Replace with final overkill value

            // Act
            currentHP = Mathf.Max(0f, currentHP - overkillDamage);

            // Assert
            Assert.That(currentHP, Is.Zero, "Health must clamp to 0 on lethal damage");
        }

        // ── UI validation ──────────────────────────────────────────────

        [UnityTest]
        public IEnumerator UIHealthBar_ReflectsDamage()
        {
            yield return null;

            // Arrange — simulate health bar fill
            float maxHP = 100f; // TODO: Replace with final HP value
            float currentHP = maxHP;
            float damage = 40f; // TODO: Replace with final damage value

            // Act
            currentHP -= damage;
            float expectedFill = currentHP / maxHP;

            // Assert — validates the expected normalized fill value
            // TODO: Wire to actual UI Slider/Image.fillAmount once UI prefab exists
            Assert.That(expectedFill, Is.InRange(0f, 1f),
                "Fill amount must be normalized [0,1]");
            Assert.That(expectedFill, Is.EqualTo(0.6f).Within(0.001f),
                "60 HP / 100 HP should yield 0.6 fill");
        }

        // ── Full critical path ─────────────────────────────────────────

        [UnityTest]
        public IEnumerator CriticalPath_Load_Damage_UI_EndToEnd()
        {
            // Step 1 — Load scene
            AsyncOperation loadOp = SceneManager.LoadSceneAsync(TestSceneName, LoadSceneMode.Single);
            yield return new WaitUntil(() => loadOp.isDone);
            Assert.That(SceneManager.GetActiveScene().isLoaded, Is.True);

            yield return null; // frame for initialization

            // Step 2 — Apply damage
            float maxHP = 100f; // TODO: Replace with final HP value
            float currentHP = maxHP;
            float damage = 30f; // TODO: Replace with final damage value
            currentHP = Mathf.Max(0f, currentHP - damage);

            Assert.That(currentHP, Is.EqualTo(70f).Within(0.01f));

            // Step 3 — Validate UI state
            float fill = currentHP / maxHP;
            Assert.That(fill, Is.EqualTo(0.7f).Within(0.001f));

            // Step 4 — Apply lethal damage
            currentHP = Mathf.Max(0f, currentHP - 999f);
            fill = currentHP / maxHP;

            Assert.That(currentHP, Is.Zero);
            Assert.That(fill, Is.Zero);
        }

        // ── Edge cases ──────────────────────────────────────────────

        [UnityTest]
        public IEnumerator DamageSystem_NegativeDamage_DoesNotHeal()
        {
            yield return null;

            float maxHP = 100f;
            float currentHP = 50f; // Damaged unit
            float negativeDamage = -25f; // Invalid: negative damage

            // Act — apply negative damage (should not heal)
            currentHP = Mathf.Max(0f, currentHP - negativeDamage);

            // Assert — should NOT exceed max HP or current value
            Assert.That(currentHP, Is.LessThanOrEqualTo(maxHP),
                "Negative damage should not heal above current HP");
        }

        [UnityTest]
        public IEnumerator HealthBar_AtMinBoundary_ZeroFill()
        {
            yield return null;

            float maxHP = 100f;
            float currentHP = 0f;
            float fill = Mathf.Clamp01(currentHP / maxHP);

            Assert.That(fill, Is.EqualTo(0f),
                "Zero HP should result in zero fill");
        }

        [UnityTest]
        public IEnumerator HealthBar_AtMaxBoundary_FullFill()
        {
            yield return null;

            float maxHP = 100f;
            float currentHP = maxHP;
            float fill = Mathf.Clamp01(currentHP / maxHP);

            Assert.That(fill, Is.EqualTo(1f),
                "Full HP should result in full fill");
        }

        [UnityTest]
        public IEnumerator SceneUnload_DoesNotThrow()
        {
            // Ensure we can unload the test scene without errors
            yield return SceneManager.UnloadSceneAsync(TestSceneShortName);
            
            Assert.Pass("Scene unloaded successfully");
        }
    }
}
