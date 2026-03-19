namespace TestAgent;

public sealed class AgentRunner
{
    public string Name => "test-agent";

    public string Run(AgentRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return $"{Name}:disabled";
        }

        var mode = string.IsNullOrWhiteSpace(options.Mode) ? "standard" : options.Mode.Trim().ToLowerInvariant();
        return $"{Name}:{mode}:ok";
    }
}
