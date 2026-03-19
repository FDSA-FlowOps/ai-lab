namespace TestAgent;

public sealed class ReviewService
{
    public bool CanReview(AgentRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Enabled && IsReady();
    }

    public string BuildStatusMessage()
    {
        return IsReady() ? "ready-for-review" : "not-ready";
    }

    public string BuildSummary(AgentRunOptions options)
    {
        return CanReview(options)
            ? $"review-enabled:{NormalizeMode(options.Mode)}"
            : "review-blocked";
    }

    public bool IsReady()
    {
        return true;
    }

    private static string NormalizeMode(string? mode)
    {
        return string.IsNullOrWhiteSpace(mode)
            ? "standard"
            : mode.Trim().ToLowerInvariant();
    }
}
