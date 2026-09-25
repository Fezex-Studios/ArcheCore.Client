using System.Text.RegularExpressions;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Makes player-typed text safe to put in a TextMeshPro label (audit H5).
    ///
    /// Our own labels use rich text (colours, sizes), so it can't just be
    /// switched off. Instead anything a PLAYER wrote - chat, names, mail
    /// subjects - goes inside &lt;noparse&gt;, where TMP shows tags as plain
    /// text. A "&lt;size=999&gt;" in chat is then just those characters, not a
    /// screen-filling message. The only way out of noparse is a closing
    /// noparse tag, so those are removed from the text first.
    /// </summary>
    public static class RichText
    {
        private static readonly Regex NoParseTag = new Regex(@"<\s*/?\s*noparse\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Player text, shown literally. Use for anything a player could have typed.</summary>
        public static string Safe(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // No '<' means no tag - skip the wrapping (and the allocation).
            if (text.IndexOf('<') < 0)
                return text;

            return "<noparse>" + NoParseTag.Replace(text, "") + "</noparse>";
        }
    }
}
