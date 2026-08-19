namespace Infrastructure
{
    public class RabbitMqOptions
    {
        public string Host { get; set; } = "localhost";
        public string VirtualHost { get; set; } = "/";
        public string Username { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public ushort PrefetchCount { get; set; } = 32;
        public int ConcurrentMessageLimit { get; set; } = 16;
        public int RateLimit { get; set; } = 1000;
        public int QueryMessageLimit { get; set; }
        public double QueryDelaySeconds { get; set; }
        public int MessageDeliveryLimit { get; set; }
        public double MessageDeliveryTimeoutSeconds { get; set; }

        // A dead/starved connection otherwise blocks Publish/Send indefinitely
        // (no default timeout) — a Hangfire-scheduled job's worker thread stays
        // stuck "Processing" forever instead of throwing and letting Hangfire's
        // AutomaticRetry recover it. Heartbeat detects the dead connection and
        // tears it down so pending operations fail fast instead of hanging.
        public double HeartbeatSeconds { get; set; } = 10;
        public double RequestedConnectionTimeoutSeconds { get; set; } = 15;
        public double QueryTimeoutSeconds { get; set; } = 30;
    }
}
