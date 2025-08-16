using System;
using System.Collections.Generic;

namespace RedisKit.Models
{
    /// <summary>
    /// Contains health information about a Redis Stream
    /// </summary>
    public class StreamHealthInfo
    {
        /// <summary>
        /// Total number of messages in the stream
        /// </summary>
        public long Length { get; set; }

        /// <summary>
        /// Number of consumer groups
        /// </summary>
        public int ConsumerGroupCount { get; set; }

        /// <summary>
        /// Total number of pending messages across all consumer groups
        /// </summary>
        public long TotalPendingMessages { get; set; }

        /// <summary>
        /// Age of the oldest pending message
        /// </summary>
        public TimeSpan OldestPendingAge { get; set; }

        /// <summary>
        /// Indicates if the stream is healthy based on thresholds
        /// </summary>
        public bool IsHealthy { get; set; }

        /// <summary>
        /// Health check message
        /// </summary>
        public string HealthMessage { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp of the health check
        /// </summary>
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    }
}