using System;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("Tests.Runtime")]

namespace Drush.Core.Logging
{
    /// <summary>
    /// Single entry point for DRUSH logging system.
    /// Persistent singleton across scenes, intercepts native Unity logs
    /// and redirects them to the console and the FileLogWriter.
    /// </summary>
    public class LogManager : MonoBehaviour
    {
        // Factory used to create the FileLogWriter. Can be swapped in tests.
        public static Func<FileLogWriter> WriterFactory = () => new FileLogWriter();

        // ─── Singleton ────────────────────────────────────────────────────────
        private static LogManager _instance;
        public static LogManager Instance
        {
            get
            {
                if (_instance == null)
                    Debug.LogWarning("[Drush] [LogManager] Instance not found. Make sure it is present in the boot scene.");
                return _instance;
            }
        }

        // ─── Configuration ────────────────────────────────────────────────────
        [Header("Configuration")]
        [Tooltip("Minimum level required for a log to be written to disk.")]
        [SerializeField] private LogLevel minimumFileLogLevel = LogLevel.Info;

        [Tooltip("Automatically intercept native Unity Debug.Log() calls.")]
        [SerializeField] private bool interceptUnityLogs = true;

        [Tooltip("Display logs in the Unity console during development.")]
        [SerializeField] private bool logToConsole = true;

        // ─── Dependencies ─────────────────────────────────────────────────────
        private FileLogWriter _fileWriter;

        // ─── Lifecycle ────────────────────────────────────────────────────────
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            _fileWriter = WriterFactory?.Invoke();
            _fileWriter?.Initialize();

            if (interceptUnityLogs)
                Application.logMessageReceived += OnUnityLogReceived;

            Log(LogLevel.Info, "System", "LogManager initialized.");
        }

        private void OnDestroy()
        {
            if (interceptUnityLogs)
                Application.logMessageReceived -= OnUnityLogReceived;

            Log(LogLevel.Info, "System", "LogManager stopped - final flush.");
            _fileWriter?.Flush();
            _fileWriter?.Dispose();
            
            // Clear singleton reference so a new LogManager can be created
            // This is critical for tests and scene transitions
            if (_instance == this)
                _instance = null;
        }

        private void OnApplicationQuit()
        {
            _fileWriter?.Flush();
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Info level log.</summary>
        public static void LogInfo(string tag, string message)
            => Instance?.Log(LogLevel.Info, tag, message);

        /// <summary>Warning level log.</summary>
        public static void LogWarning(string tag, string message)
            => Instance?.Log(LogLevel.Warning, tag, message);

        /// <summary>Error level log.</summary>
        public static void LogError(string tag, string message)
            => Instance?.Log(LogLevel.Error, tag, message);

        /// <summary>Error level log with exception.</summary>
        public static void LogException(string tag, Exception ex)
            => Instance?.Log(LogLevel.Error, tag, $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

        // ─── Testing Support ──────────────────────────────────────────────────

        /// <summary>
        /// FOR TESTING ONLY: Provides access to the current FileLogWriter.
        /// This eliminates the need for reflection-based access in tests.
        /// Should never be used in production code.
        /// </summary>
        internal static FileLogWriter GetCurrentWriter()
        {
            return Instance?._fileWriter;
        }

        // ─── Internals ────────────────────────────────────────────────────────

        private void Log(LogLevel level, string tag, string message)
        {
            var entry = new LogEntry(level, tag, message);

            if (logToConsole)
                PrintToConsole(entry);

            if (level >= minimumFileLogLevel)
                _fileWriter?.Enqueue(entry);
        }

        /// <summary>
        /// Unity callback - intercepts native Debug.Log / LogWarning / LogError.
        /// </summary>
        private void OnUnityLogReceived(string condition, string stackTrace, LogType type)
        {
            // Prevent infinite recursion
            if (condition.StartsWith("[Drush]")) return;

            var level = type switch
            {
                LogType.Warning  => LogLevel.Warning,
                LogType.Error    => LogLevel.Error,
                LogType.Exception => LogLevel.Error,
                LogType.Assert   => LogLevel.Error,
                _                => LogLevel.Info
            };

            var entry = new LogEntry(level, "Unity", condition);
            _fileWriter?.Enqueue(entry);
        }

        private static void PrintToConsole(LogEntry entry)
        {
            var formatted = entry.ToConsoleString();
            switch (entry.Level)
            {
                case LogLevel.Warning: Debug.LogWarning(formatted); break;
                case LogLevel.Error:   Debug.LogError(formatted);   break;
                default:               Debug.Log(formatted);        break;
            }
        }
    }
}