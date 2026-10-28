using System.Globalization;
using System.Text.Json;

namespace WorkChatMini;

// Metadata-only reader position helpers. Scripts run on the currently opened Web tab;
// they never collect or store the chapter's text, images, cookies or credentials.
internal static class ReadingScrollScripts
{
    internal const string Capture = """
        (() => {
          const root = document.scrollingElement || document.documentElement;
          if (!root) return null;
          let chosen = root;
          let score = (root.scrollTop > 1 ? 100000000 : 0) + Math.max(0, root.scrollHeight - innerHeight);
          const candidates = document.querySelectorAll('main, article, [role="main"], [id*="read"], [class*="reader"], [class*="scroll"]');
          for (let i = 0; i < Math.min(candidates.length, 120); i++) {
            const node = candidates[i];
            if (node === root || node.clientHeight < 100) continue;
            const max = node.scrollHeight - node.clientHeight;
            if (max < 80 || !/^(auto|scroll|overlay)$/.test(getComputedStyle(node).overflowY)) continue;
            const candidateScore = (node.scrollTop > 1 ? 100000000 : 0) + max;
            if (candidateScore > score) { chosen = node; score = candidateScore; }
          }
          const isDocument = chosen === root;
          const max = Math.max(0, chosen.scrollHeight - (isDocument ? innerHeight : chosen.clientHeight));
          const y = Math.max(0, isDocument ? (scrollY || root.scrollTop) : chosen.scrollTop);
          const target = isDocument ? 'document' : (chosen.id && chosen.id.length <= 80 ? 'id:' + chosen.id : 'auto');
          return JSON.stringify({url: location.href, fraction: max > 0 ? Math.min(1, y / max) : 0, target});
        })()
        """;

    internal static string NormalizeTarget(string? target) =>
        target is "auto" or "document" ? target :
        target is { Length: > 3 and <= 83 } && target.StartsWith("id:", StringComparison.Ordinal) &&
            !target.Contains('\n') && !target.Contains('\r') ? target : "document";

    internal static string Restore(double fraction, string? target)
    {
        string amount = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1)
            .ToString("R", CultureInfo.InvariantCulture);
        string preferred = JsonSerializer.Serialize(NormalizeTarget(target));
        return """
            (() => {
              const root = document.scrollingElement || document.documentElement;
              if (!root) return 'not-scrollable';
              const preferred = TARGET_VALUE;
              let chosen = root;
              if (preferred.startsWith('id:')) {
                const match = document.getElementById(preferred.slice(3));
                if (match && match.scrollHeight - match.clientHeight > 80) chosen = match;
              }
              if (chosen === root && preferred !== 'document') {
                let score = Math.max(0, root.scrollHeight - innerHeight);
                const candidates = document.querySelectorAll('main, article, [role="main"], [id*="read"], [class*="reader"], [class*="scroll"]');
                for (let i = 0; i < Math.min(candidates.length, 120); i++) {
                  const node = candidates[i];
                  if (node === root || node.clientHeight < 100) continue;
                  const max = node.scrollHeight - node.clientHeight;
                  if (max > score && /^(auto|scroll|overlay)$/.test(getComputedStyle(node).overflowY)) {
                    chosen = node; score = max;
                  }
                }
              }
              const isDocument = chosen === root;
              const max = Math.max(0, chosen.scrollHeight - (isDocument ? innerHeight : chosen.clientHeight));
              if (max <= 0) return 'not-scrollable';
              const position = max * FRACTION_VALUE;
              if (isDocument) window.scrollTo(0, position);
              else chosen.scrollTop = position;
              return 'restored';
            })()
            """.Replace("TARGET_VALUE", preferred, StringComparison.Ordinal)
                  .Replace("FRACTION_VALUE", amount, StringComparison.Ordinal);
    }
}
