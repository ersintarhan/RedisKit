using System;

namespace RedisKit.Models
{
    /// <summary>
    /// Configuration options for distributed locking
    /// </summary>
    public class DistributedLockOptions
    {
        /// <summary>
        /// Gets or sets whether to enable automatic lock renewal
        /// </summary>
        public bool EnableAutoRenewal { get; set; } = false;

        /// <summary>
        /// Gets or sets the default lock expiry time
        /// </summary>
        public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the default wait time for lock acquisition
        /// </summary>
        public TimeSpan DefaultWaitTime { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or sets the default retry interval
        /// </summary>
        public TimeSpan DefaultRetryInterval { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Gets or sets whether to enable deadlock detection
        /// </summary>
        public bool EnableDeadlockDetection { get; set; } = false;

        /// <summary>
        /// Gets or sets the deadlock detection timeout
        /// </summary>
        public TimeSpan DeadlockDetectionTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets whether to track lock metrics
        /// </summary>
        public bool EnableMetrics { get; set; } = false;

        /// <summary>
        /// Gets or sets the lock key prefix
        /// </summary>
        public string KeyPrefix { get; set; } = "lock";
    }
}