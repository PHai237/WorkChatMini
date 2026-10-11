using System.Text.Json;

namespace WorkChatMini;

public static class StoryJson
{
    public static T? TryDeserialize<T>(string value)
    {
        string json = ExtractObject(value);
        if (json.Length == 0)
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return default;
        }
    }

    private static string ExtractObject(string value)
    {
        string text = value.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            int firstNewLine = text.IndexOf('\n');
            int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
                text = text[(firstNewLine + 1)..lastFence].Trim();
        }
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : string.Empty;
    }
}
