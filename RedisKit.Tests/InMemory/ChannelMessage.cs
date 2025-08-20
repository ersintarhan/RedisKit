namespace RedisKit.Tests.InMemory;

public class ChannelMessage<T> where T : class
{
    public string Channel { get; set; } = "";
    public T Data { get; set; } = default!;
}