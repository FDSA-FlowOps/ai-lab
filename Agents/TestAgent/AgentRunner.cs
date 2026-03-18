namespace TestAgent;

public sealed class AgentRunner
{
    public string Name => "test-agent";

    public string Run()
    {
        return $"{Name}:ok";
    }
}
