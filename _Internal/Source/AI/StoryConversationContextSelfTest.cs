namespace WorkChatMini;

internal static class StoryConversationContextSelfTest
{
    internal static void Verify()
    {
        var messages = new List<StoryMessage>
        {
            new() { Role = "user", Content = "Keep the lighthouse secret." },
            new() { Role = "assistant", Content = "We can reveal it later.", Action = "Chat" }
        };
        // Long history should not cause the next writing turn to retain any
        // rejected draft or its original user prompt.
        for (int i = 0; i < 2000; i++)
        {
            messages.Add(new StoryMessage { Role = "user", Content = $"rejected instruction {i}" });
            messages.Add(new StoryMessage
            {
                Role = "assistant", Action = "ContinueStory", Content = $"rejected draft {i}",
                IsNarrativeDraft = true, ManuscriptCommitted = false
            });
        }
        messages.Add(new StoryMessage { Role = "user", Content = "Latest question" });
        string context = StoryConversationContextBuilder.Build(messages, "Continue the story");
        if (!context.Contains("lighthouse secret", StringComparison.Ordinal) ||
            !context.Contains("Latest question", StringComparison.Ordinal) ||
            context.Contains("rejected instruction", StringComparison.Ordinal) ||
            context.Contains("rejected draft", StringComparison.Ordinal))
            throw new InvalidOperationException("Uncommitted manuscript polluted the bounded writing context.");

        messages.Add(new StoryMessage { Role = "user", Content = "Continue the story" });
        string current = StoryConversationContextBuilder.Build(messages, "Continue the story");
        if (current.Contains("Creator: Continue the story", StringComparison.Ordinal))
            throw new InvalidOperationException("The current direction was inserted twice in writing context.");

        messages.Add(new StoryMessage { Role = "assistant", Action = "ContinueStory",
            IsNarrativeDraft = true, ManuscriptCommitted = true, Content = "The lighthouse was finally found." });
        string committed = StoryConversationContextBuilder.Build(messages, "new instruction");
        if (!committed.Contains("lighthouse was finally found", StringComparison.Ordinal))
            throw new InvalidOperationException("An approved chapter was excluded from writing context.");

        var chat = new List<StoryMessage>
        {
            new() { Role = "user", Content = "What if the harbor is flooded?" },
            new() { Role = "assistant", Action = "Chat", Content = "Incomplete response",
                IsInterruptedResponse = true },
            new() { Role = "user", Content = "What if the lighthouse is abandoned?" }
        };
        string chatContext = StoryConversationContextBuilder.Build(chat, "next question");
        if (!chatContext.Contains("harbor is flooded", StringComparison.Ordinal) ||
            chatContext.Contains("Incomplete response", StringComparison.Ordinal) ||
            !chatContext.Contains("lighthouse is abandoned", StringComparison.Ordinal))
            throw new InvalidOperationException("Interrupted Chat wrongly removed the creator's question.");
    }
}
