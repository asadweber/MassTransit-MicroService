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
    }
}
