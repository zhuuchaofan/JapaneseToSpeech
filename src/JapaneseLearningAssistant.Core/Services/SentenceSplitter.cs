namespace JapaneseLearningAssistant.Core.Services;

public static class SentenceSplitter
{
    private static readonly HashSet<char> SentenceEndMarks = ['\u3002', '\uff01', '\uff1f', '!', '?'];
    private static readonly HashSet<char> ClosingMarks = ['\u300d', '\u300f', '\uff09', ')', '\u3011', '\u300b', '\u3009', '\uff3d', ']', '"', '\''];

    public static IReadOnlyList<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var sentences = new List<string>();
        var start = 0;
        var i = 0;

        while (i < text.Length)
        {
            var current = text[i];
            if (current == '\r' || current == '\n')
            {
                AddSentence(sentences, text[start..i]);
                i = SkipLineBreaks(text, i);
                start = i;
                continue;
            }

            if (SentenceEndMarks.Contains(current))
            {
                i++;
                while (i < text.Length && ClosingMarks.Contains(text[i]))
                {
                    i++;
                }

                AddSentence(sentences, text[start..i]);
                start = i;
                continue;
            }

            i++;
        }

        if (start < text.Length)
        {
            AddSentence(sentences, text[start..]);
        }

        return sentences;
    }

    private static int SkipLineBreaks(string text, int index)
    {
        while (index < text.Length && (text[index] == '\r' || text[index] == '\n'))
        {
            index++;
        }

        return index;
    }

    private static void AddSentence(ICollection<string> sentences, string sentence)
    {
        sentence = sentence.Trim();
        if (sentence.Length > 0)
        {
            sentences.Add(sentence);
        }
    }
}
