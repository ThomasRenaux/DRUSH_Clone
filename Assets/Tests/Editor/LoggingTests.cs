using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using Drush.Core.Logging;

namespace Drush.Tests.Editor
{
    [TestFixture]
    public class LoggingTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "DrushLogsTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { /* best-effort cleanup */ }
        }

        [Test]
        public void LogEntry_ToConsoleString_IncludesTagAndMessage()
        {
            var entry = new LogEntry(LogLevel.Info, "Unit", "hello");
            Assert.AreEqual("[Drush] [Unit] hello", entry.ToConsoleString());
        }

        [Test]
        public void LogEntry_ToFileString_ContainsLevelTagMessage()
        {
            var entry = new LogEntry(LogLevel.Warning, "MyTag", "msg");
            var s = entry.ToFileString();
            Assert.IsTrue(s.Contains("[WARN]"));
            Assert.IsTrue(s.Contains("[MyTag]"));
            Assert.IsTrue(s.EndsWith("msg") || s.Contains(" msg"));
        }

        // ── Edge cases and negative tests ──────────────────────────

        [Test]
        public void LogEntry_WithEmptyTag_StillFormats()
        {
            var entry = new LogEntry(LogLevel.Info, "", "message");
            var consoleStr = entry.ToConsoleString();
            var fileStr = entry.ToFileString();
            
            Assert.IsNotNull(consoleStr);
            Assert.IsNotNull(fileStr);
            Assert.IsTrue(consoleStr.Contains("message"));
            Assert.IsTrue(fileStr.Contains("message"));
        }

        [Test]
        public void LogEntry_WithNullMessage_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                var entry = new LogEntry(LogLevel.Info, "Tag", null);
                _ = entry.ToConsoleString();
                _ = entry.ToFileString();
            });
        }

        [Test]
        public void LogEntry_WithVeryLongMessage_FormatsCorrectly()
        {
            string longMsg = new string('a', 10000);
            var entry = new LogEntry(LogLevel.Info, "Tag", longMsg);
            var result = entry.ToConsoleString();
            
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Contains("aaa"));
        }

        [Test]
        public void FileLogWriter_EnqueueMultipleEntries_AllWritten()
        {
            var writer = new FileLogWriter(_tempDir);
            writer.Initialize();
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    var entry = new LogEntry(LogLevel.Info, "Test", $"Message {i}");
                    writer.Enqueue(entry);
                }
                writer.Flush();
                writer.Dispose();

                var file = Directory.GetFiles(_tempDir, $"drush_{DateTime.Now:yyyy-MM-dd}.log").FirstOrDefault();
                Assert.IsNotNull(file);
                var content = File.ReadAllText(file);
                
                for (int i = 0; i < 10; i++)
                {
                    Assert.IsTrue(content.Contains($"Message {i}"));
                }
            }
            finally { writer.Dispose(); }
        }

        [Test]
        public void FileLogWriter_WithInvalidPath_HandlesGracefully()
        {
            var invalidPath = "/nonexistent/path/that/should/not/exist/12345678901234567890";
            var writer = new FileLogWriter(invalidPath);
            
            // Expect a log error from FileLogWriter when it fails to initialize
            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex(@"\[Drush\] \[FileLogWriter\] Failed to initialize.*"));
            
            // Should not throw on initialization — graceful degradation
            Assert.DoesNotThrow(() => writer.Initialize());
        }

        [Test]
        public void FileLogWriter_EnqueueAndFlush_WritesFile()
        {
            var writer = new FileLogWriter(_tempDir);
            writer.Initialize();
            try
            {
                var entry = new LogEntry(LogLevel.Info, "Test", "Test message");
                writer.Enqueue(entry);
                writer.Flush();
                writer.Dispose();

                var file = Directory.GetFiles(_tempDir, $"drush_{DateTime.Now:yyyy-MM-dd}.log").FirstOrDefault();
                Assert.IsNotNull(file);
                var content = File.ReadAllText(file);
                Assert.IsTrue(content.Contains("Test message"));
            }
            finally { writer.Dispose(); }
        }

        [Test]
        public void FileLogWriter_RotateOldFiles_RemovesOldFiles()
        {
            // create 12 old files
            for (int i = 0; i < 12; i++)
            {
                var date = DateTime.Now.AddDays(-(i + 1));
                var path = Path.Combine(_tempDir, $"drush_{date:yyyy-MM-dd}.log");
                File.WriteAllText(path, "old");
                File.SetCreationTime(path, DateTime.Now.AddDays(-(i + 1)));
            }

            var writer = new FileLogWriter(_tempDir);
            writer.Initialize();
            writer.Dispose();

            var files = new DirectoryInfo(_tempDir).GetFiles("drush_*.log");
            Assert.LessOrEqual(files.Length, 10);
        }
    }
}
