using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Drush.Core.Logging
{
    /// <summary>
    /// Writes LogEntry items to disk asynchronously with buffering.
    /// Thread-safe. Automatic daily log file rotation.
    /// </summary>
    public class FileLogWriter : IDisposable
    {
        // ─── Configuration ────────────────────────────────────────────────────
        private const int    MaxArchivedFiles = 10;   // Number of archived log files to keep
        private const int    FlushIntervalMs  = 2000; // Flush every 2 seconds
        private const int    BufferFlushSize  = 50;   // Flush when buffer exceeds 50 entries

        // ─── Internals ────────────────────────────────────────────────────────
        private readonly Queue<LogEntry> _buffer = new();
        private readonly object          _lock   = new();

        private string       _logDirectory;
        private string       _currentFilePath;
        private StreamWriter _writer;
        private Timer        _flushTimer;
        private bool         _disposed;

        // Optional constructor injection for testability: allows using a custom log directory.
        public FileLogWriter() { }

        public FileLogWriter(string logDirectory)
        {
            _logDirectory = logDirectory;
        }

        // ─── Init ──────────────────────────────────────────────────────────────

        public void Initialize()
        {
            try
            {
                if (string.IsNullOrEmpty(_logDirectory))
                    _logDirectory = Path.Combine(Application.persistentDataPath, "Logs");
                
                // The parent directory must exist (we don't create arbitrary deep paths)
                var parentDir = Path.GetDirectoryName(_logDirectory);
                if (string.IsNullOrEmpty(parentDir))
                    throw new ArgumentException($"Invalid log directory path: {_logDirectory}");
                
                if (!Directory.Exists(parentDir))
                    throw new DirectoryNotFoundException($"Parent directory does not exist and will not be created: {parentDir}");
                
                // Now safely create just the log directory
                Directory.CreateDirectory(_logDirectory);
                
                // Validate directory is writable before opening writer
                ValidateDirectoryWritable(_logDirectory);
                
                OpenWriter();

                RotateOldFiles();

                // Periodic flush timer (Unity-safe thread pool)
                _flushTimer = new Timer(_ => Flush(), null, FlushIntervalMs, FlushIntervalMs);
            }
            catch (Exception ex)
            {
                // Log the error and continue with disabled file logging
                Debug.LogError($"[Drush] [FileLogWriter] Failed to initialize: {ex.Message}");
                _logDirectory = null;
                _writer = null;
            }
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Adds an entry to the buffer. Thread-safe.</summary>
        public void Enqueue(LogEntry entry)
        {
            if (_disposed) return;

            lock (_lock)
            {
                _buffer.Enqueue(entry);

                // Early flush when buffer is full
                if (_buffer.Count >= BufferFlushSize)
                    FlushInternal();
            }
        }

        /// <summary>Forces immediate write of the buffer to disk.</summary>
        public void Flush()
        {
            lock (_lock)
                FlushInternal();
        }

        // ─── Internals ────────────────────────────────────────────────────────

        private void FlushInternal()
        {
            if (_buffer.Count == 0 || _writer == null) return;

            // Rotate if day changes
            var expectedPath = BuildFilePath(DateTime.Now);
            if (expectedPath != _currentFilePath)
            {
                CloseWriter();
                OpenWriter();
                RotateOldFiles();
            }

            while (_buffer.Count > 0)
            {
                var entry = _buffer.Dequeue();
                _writer.WriteLine(entry.ToFileString());
            }

            _writer.Flush();
        }

        private void ValidateDirectoryWritable(string directory)
        {
            var testFile = Path.Combine(directory, ".write-test");
            try
            {
                File.WriteAllText(testFile, "");
                File.Delete(testFile);
            }
            catch (Exception ex)
            {
                throw new IOException($"Log directory is not writable: {directory}", ex);
            }
        }

        private void OpenWriter()
        {
            _currentFilePath = BuildFilePath(DateTime.Now);

            // append: true - keep existing logs if the game restarts during the same day
            _writer = new StreamWriter(_currentFilePath, append: true)
            {
                AutoFlush = false
            };

            _writer.WriteLine($"--- Session started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
            _writer.Flush();
        }

        private void CloseWriter()
        {
            _writer?.Flush();
            _writer?.Close();
            _writer?.Dispose();
            _writer = null;
        }

        private string BuildFilePath(DateTime date)
            => Path.Combine(_logDirectory, $"drush_{date:yyyy-MM-dd}.log");

        /// <summary>
        /// Deletes oldest log files if MaxArchivedFiles is exceeded.
        /// </summary>
        private void RotateOldFiles()
        {
            var files = new DirectoryInfo(_logDirectory)
                .GetFiles("drush_*.log");

            Array.Sort(files, (a, b) => a.CreationTime.CompareTo(b.CreationTime));

            while (files.Length > MaxArchivedFiles)
            {
                try { files[0].Delete(); }
                catch { /* Silently ignored */ }

                // Refresh
                files = new DirectoryInfo(_logDirectory).GetFiles("drush_*.log");
                Array.Sort(files, (a, b) => a.CreationTime.CompareTo(b.CreationTime));
            }
        }

        // ─── IDisposable ──────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _flushTimer?.Dispose();

            lock (_lock)
            {
                FlushInternal();
                CloseWriter();
            }
        }
    }
}