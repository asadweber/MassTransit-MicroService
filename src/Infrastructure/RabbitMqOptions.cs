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
    }
}
