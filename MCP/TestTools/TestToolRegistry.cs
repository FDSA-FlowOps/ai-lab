namespace TestTools;

public static class TestToolRegistry
{
    public static string[] GetTools()
    {
        return ["search", "review", "summarize"];
    }

    public static bool Supports(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        return GetTools().Contains(toolName, StringComparer.OrdinalIgnoreCase);
    }
}
