namespace Infrastructure
{
    public class HangfireOptions
    {
        public int WorkerCount { get; set; } = Environment.ProcessorCount * 5;
        public string[] Queues { get; set; } = ["default"];

        // How long a server can go without a heartbeat before Hangfire considers
        // it dead and requeues its jobs. Shorter than the 30 min default so a
        // stopped/replaced process (e.g. after a redeploy) stops holding its
        // "Processing" jobs hostage for half an hour.
        public TimeSpan ServerTimeout { get; set; } = TimeSpan.FromMinutes(2);

        // How often each server checks storage for other servers that have
        // gone silent past ServerTimeout. Kept well below ServerTimeout so
        // detection isn't delayed by the poll interval itself.
        public TimeSpan ServerCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

        // How long a fetched job stays invisible to other workers before
        // Hangfire assumes the fetching worker died and puts it back on the
        // queue. Default is 30 min — far too long for a job stuck on a hung
        // RabbitMQ publish; shortened so a dead worker's job is reclaimed fast.
        public TimeSpan InvisibilityTimeout { get; set; } = TimeSpan.FromMinutes(5);

        // How long a job stays in storage after reaching a final state
        // (Succeeded/Deleted) before Hangfire's expiration manager sweeps it.
        // Default is 7 days so completed job history is available for a
        // reasonable window without growing Redis storage unbounded.
        public TimeSpan JobExpirationTimeout { get; set; } = TimeSpan.FromDays(1);
    }
}
