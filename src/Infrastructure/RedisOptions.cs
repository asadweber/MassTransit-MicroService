namespace Infrastructure
{
    public class RedisOptions
    {
        public string ConnectionString { get; set; } = "localhost:6379";

        // StackExchange.Redis default is 5000ms for both — too tight when Hangfire's
        // storage transactions (job fetch/ack/state-change) contend with the
        // MassTransit.Hangfire scheduler under load, causing intermittent
        // TimeoutException instead of a clean, retryable failure.
        public int ConnectTimeoutMs { get; set; } = 10000;
        public int SyncTimeoutMs { get; set; } = 10000;

        // Fail fast on startup if Redis is unreachable, instead of silently
        // queuing commands against a connection that never came up — surfaces
        // a misconfigured/unreachable Redis immediately rather than as a
        // mysterious stuck-job symptom later.
        public bool AbortOnConnectFail { get; set; } = true;
    }
}
