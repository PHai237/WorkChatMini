using System.Net.Http;

namespace WorkChatMini;

internal static class StoryProviderFailure
{
    internal static string Describe(Exception exception)
    {
        // These errors must be useful to users without exposing raw API bodies,
        // request contents, API keys or machine-specific network details.
        if (exception is TimeoutException || exception.InnerException is TimeoutException)
            return "Gemini timed out. The saved message is ready to retry (↻ Retry AI).";
        if (exception is HttpRequestException)
            return "Cannot reach the AI service. Check network/VPN, then use ↻ Retry AI.";
        string details = exception.Message;
        if (details.Contains("503", StringComparison.Ordinal) ||
            details.Contains("overload", StringComparison.OrdinalIgnoreCase))
            return "Gemini is busy (503). Try ↻ Retry AI in a moment.";
        if (details.Contains("429", StringComparison.Ordinal))
            return "Gemini rate limit (429). Wait briefly, then retry.";
        if (details.Contains("401", StringComparison.Ordinal) || details.Contains("403", StringComparison.Ordinal))
            return "AI access denied. Check provider key and account access in Story Settings.";
        if (details.Contains("404", StringComparison.Ordinal))
            return "AI model not found (404). Check model in Story Settings.";
        return "AI request failed. Open Logs for details; your message is saved and can be retried.";
    }
}
