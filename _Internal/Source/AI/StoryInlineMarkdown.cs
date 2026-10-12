using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WorkChatMini;

/// <summary>Lightweight, bounded Markdown presentation. The persisted message remains untouched.</summary>
internal static class StoryInlineMarkdown
{
    private const int MaxCharacters = 7200;
    private const int MaxInlines = 180;
    private static readonly FontFamily CodeFont = new("Consolas");
    private static readonly Brush CodeBackground = new SolidColorBrush(Color.FromRgb(40, 37, 32));

    internal static void Render(TextBlock text, string? source)
    {
        string value = source ?? string.Empty;
        // Protect the UI: the long-message reader chunks first, and pathological
        // formatting never creates thousands of WPF elements.
        if (value.Length > MaxCharacters)
        {
            text.Text = value;
            return;
        }

        var fragments = new List<(string Value, int Format)>();
        var plain = new StringBuilder();
        void FlushPlain()
        {
            if (plain.Length == 0) return;
            fragments.Add((plain.ToString(), 0));
            plain.Clear();
        }

        for (int i = 0; i < value.Length;)
        {
            // Use bullet glyphs for common Markdown lists, without rewriting canon.
            if (i == 0 || value[i - 1] == '\n')
            {
                if (i + 1 < value.Length && (value[i] == '-' || value[i] == '*' || value[i] == '+') && value[i + 1] == ' ')
                {
                    plain.Append("• ");
                    i += 2;
                    continue;
                }
            }
            if (value[i] == '\\' && i + 1 < value.Length && "*_`\\".Contains(value[i + 1]))
            {
                plain.Append(value[i + 1]);
                i += 2;
                continue;
            }

            int format = 0;
            int markerLength = 0;
            char c = value[i];
            if (c == '`') { format = 3; markerLength = 1; }
            else if ((c == '*' || c == '_') && i + 1 < value.Length && value[i + 1] == c)
            { format = 1; markerLength = 2; }
            else if (c == '*' || c == '_') { format = 2; markerLength = 1; }

            if (format != 0)
            {
                string marker = value.Substring(i, markerLength);
                int end = value.IndexOf(marker, i + markerLength, StringComparison.Ordinal);
                if (end > i + markerLength && value.AsSpan(i + markerLength, end - i - markerLength).IndexOf('\n') < 0)
                {
                    FlushPlain();
                    fragments.Add((value.Substring(i + markerLength, end - i - markerLength), format));
                    i = end + markerLength;
                    if (fragments.Count > MaxInlines) { text.Text = value; return; }
                    continue;
                }
            }
            plain.Append(c);
            i++;
        }
        FlushPlain();
        if (fragments.Count > MaxInlines) { text.Text = value; return; }
        text.Inlines.Clear();
        foreach ((string segment, int format) in fragments)
        {
            var run = new Run(segment);
            if (format == 1) run.FontWeight = FontWeights.Bold;
            else if (format == 2) run.FontStyle = FontStyles.Italic;
            else if (format == 3)
            {
                run.FontFamily = CodeFont;
                run.Background = CodeBackground;
            }
            text.Inlines.Add(run);
        }
    }
}
