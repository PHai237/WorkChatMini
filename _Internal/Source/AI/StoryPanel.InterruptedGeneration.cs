using System.Windows.Controls;

namespace WorkChatMini;

public partial class StoryPanel
{
    // Executed only on the UI dispatcher after the provider stops. Never use
    // the cancellation token that terminated the provider to save the partial.
    private async Task<bool?> PreserveInterruptedReplyAsync(
        StoryProject project, StoryMessage reply, bool stopped)
    {
        // A completed reply can already have been persisted before a later
        // bookkeeping/research operation throws. Do not create another message.
        if (_messages.Contains(reply)) return false;
        if (!StoryInterruptedGeneration.TryPrepare(reply, _streamAccumulated.ToString(), stopped))
            return false;

        _messages.Add(reply);
        try
        {
            // Durability must not depend on whether a WPF bubble can redraw.
            await _store.SaveMessagesAsync(project.Id, _messages);
        }
        catch (Exception ex)
        {
            // Keep the unsaved partial in memory for a later save/recovery;
            // never claim it is durably saved or silently swallow this error.
            AppLogger.Error("STORY", "Could not persist interrupted AI reply", ex);
            return null;
        }
        try
        {
            if (_streamingTextBlock?.Parent is StackPanel content)
            {
                // During streaming only the tail was visible. Restore the full
                // saved text as a bounded reader, not a giant native TextBlock.
                AttachLongMessageReader(content, _streamingTextBlock, reply);
                if (content.Children.Count > 0 && content.Children[0] is Border badge &&
                    badge.Child is TextBlock label)
                    label.Text = "STORY COMPANION · INCOMPLETE";
            }
        }
        catch (Exception ex)
        {
            // The conversation is already safely on disk; a visual failure
            // must not turn a recovered partial into an unhandled UI crash.
            AppLogger.Error("STORY", "Could not display saved interrupted AI reply", ex);
        }
        return true;
    }
}
