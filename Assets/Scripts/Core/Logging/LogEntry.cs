using System;

namespace Drush.Core.Logging
{
    /// <summary>
    /// Log severity levels.
    /// </summary>
    public enum LogLevel
    {
        Info    = 0,
        Warning = 1,
        Error   = 2
    }

    /// <summary>
    /// Represents an immutable log entry.
    /// </summary>
    public readonly struct LogEntry
    {
        public readonly DateTime  Timestamp;
        public readonly LogLevel  Level;
        public readonly string    Tag;
        public readonly string    Message;

        public LogEntry(LogLevel level, string tag, string message)
        {
            Timestamp = DateTime.Now;
            Level     = level;
            Tag       = tag ?? "General";
            Message   = message ?? string.Empty;
        }

        /// <summary>Format used for file writing.</summary>
        /// <example>[2026-04-29 14:32:01] [INFO]    [Carrier] The carrier picked up 3 items.</example>
        public string ToFileString()
        {
            var levelStr = Level switch
            {
                LogLevel.Warning => "WARN",
                LogLevel.Error   => "ERROR",
                _                => "INFO",
            };
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{levelStr}] [{Tag}] {Message}";
        }

        /// <summary>Short format for the Unity console.</summary>
        public string ToConsoleString()
            => $"[Drush] [{Tag}] {Message}";
    }
}