namespace Infrastructure
{
    public class HangfireOptions
    {
        public int WorkerCount { get; set; } = Environment.ProcessorCount * 5;
        public string[] Queues { get; set; } = ["default"];
    }
}
