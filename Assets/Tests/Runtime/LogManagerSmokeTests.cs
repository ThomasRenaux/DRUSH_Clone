using System;
using System.IO;
using System.Linq;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Drush.Core.Logging;

namespace Drush.Tests.Runtime
{
    [TestFixture]
    public sealed class LogManagerSmokeTests
    {
        private string _tempDir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "DrushLogsTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Restore default factory
            LogManager.WriterFactory = () => new FileLogWriter();

            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { }
            
            yield return null;
        }

        [UnityTest]
        public IEnumerator LogManager_WritesFile_WhenLogging()
        {
            // Use factory to point writer to our temp dir
            LogManager.WriterFactory = () => new FileLogWriter(_tempDir);

            // Expect logs from LogManager initialization and message logging
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Drush\] \[System\] LogManager initialized\."));
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Drush\] \[Smoke\] playmode message"));

            var go = new GameObject("LogManagerTest");
            var lm = go.AddComponent<LogManager>();

            // allow Awake to run
            yield return null;

            // write a log
            LogManager.LogInfo("Smoke", "playmode message");

            // give a short time for the enqueue/flush to happen via timer
            yield return new WaitForSeconds(0.5f);

            // Use the public testing API instead of reflection
            var writer = LogManager.GetCurrentWriter();
            Assert.IsNotNull(writer, "FileLogWriter was not created");

            // call Flush to ensure writes
            writer.Flush();

            // check file exists and contains message
            var file = Directory.GetFiles(_tempDir, $"drush_{DateTime.Now:yyyy-MM-dd}.log").FirstOrDefault();
            Assert.IsNotNull(file, "Log file not found");
            var content = File.ReadAllText(file);
            Assert.IsTrue(content.Contains("playmode message"));

            UnityEngine.Object.DestroyImmediate(go);
            yield return null;
        }

        // ── Additional edge case tests ────────────────────────────────

        [UnityTest]
        public IEnumerator LogManager_MultipleMessages_AllWritten()
        {
            LogManager.WriterFactory = () => new FileLogWriter(_tempDir);

            // Expect logs from LogManager initialization and message logging
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Drush\] \[System\] LogManager initialized\."));
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Drush\] \[Test\] info message"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[Drush\] \[Test\] warning message"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[Drush\] \[Test\] error message"));

            var go = new GameObject("LogManagerTest2");
            var lm = go.AddComponent<LogManager>();

            yield return null;

            // Log multiple messages with different levels
            LogManager.LogInfo("Test", "info message");
            LogManager.LogWarning("Test", "warning message");
            LogManager.LogError("Test", "error message");

            yield return new WaitForSeconds(0.5f);

            var writer = LogManager.GetCurrentWriter();
            writer?.Flush();

            var file = Directory.GetFiles(_tempDir, $"drush_{DateTime.Now:yyyy-MM-dd}.log").FirstOrDefault();
            Assert.IsNotNull(file);
            var content = File.ReadAllText(file);

            Assert.IsTrue(content.Contains("info message"));
            Assert.IsTrue(content.Contains("warning message"));
            Assert.IsTrue(content.Contains("error message"));

            UnityEngine.Object.DestroyImmediate(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LogManager_FactoryChange_UsesNewWriter()
        {
            // First writer
            var tempDir1 = Path.Combine(Path.GetTempPath(), "DrushLogsTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir1);
            
            LogManager.WriterFactory = () => new FileLogWriter(tempDir1);

            // Expect logs from LogManager initialization and message logging
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Drush\] \[System\] LogManager initialized\."));
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Drush\] \[Test\] first message"));
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Drush\] \[Test\] second message"));

            var go = new GameObject("LogManagerFactoryTest");
            var lm = go.AddComponent<LogManager>();
            
            yield return null;
            LogManager.LogInfo("Test", "first message");
            yield return new WaitForSeconds(0.5f);

            // Verify first writer was used
            var file1 = Directory.GetFiles(tempDir1, $"drush_{DateTime.Now:yyyy-MM-dd}.log").FirstOrDefault();
            Assert.IsNotNull(file1, "First writer should have created a file");

            // Switch writer
            var tempDir2 = Path.Combine(Path.GetTempPath(), "DrushLogsTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir2);
            LogManager.WriterFactory = () => new FileLogWriter(tempDir2);

            LogManager.LogInfo("Test", "second message");
            yield return new WaitForSeconds(0.5f);

            // Cleanup
            UnityEngine.Object.DestroyImmediate(go);
            try { Directory.Delete(tempDir1, true); } catch { }
            try { Directory.Delete(tempDir2, true); } catch { }
            
            yield return null;
        }
    }
}
