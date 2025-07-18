namespace Messages;

public class Ping
{
    public int Number { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public class Pong
{
    public int Number { get; set; }
    public DateTime ReceivedPingAt { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public class PingPongStats
{
    public int TotalPings { get; set; }
    public int TotalPongs { get; set; }
    public double AverageRoundTripMs { get; set; }
}