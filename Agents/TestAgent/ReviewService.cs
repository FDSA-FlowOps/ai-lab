namespace TestAgent;

public sealed class ReviewService
{
    public string BuildStatusMessage()
    {
        return IsReady() ? "ready-for-review" : "not-ready";
    }

    public bool IsReady()
    {
        return true;
    }
}
