using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace FastDM
{
    public static class ProcessOutput
    {
        const int ReadBufferCharacters = 4096;
        public const int DefaultMaxLineCharacters = 64 * 1024;
        const int MaxLineCharacters = 16 * 1024 * 1024;
        const string TruncatedLineMarker = "… [output line truncated]";

        public static async Task DrainLinesAsync(StreamReader reader, Action<string> callback,
                                                 int maxLineCharacters = DefaultMaxLineCharacters)
        {
            maxLineCharacters = Math.Clamp(maxLineCharacters, 1, MaxLineCharacters);
            var buffer = new char[ReadBufferCharacters];
            var line = new StringBuilder(Math.Min(maxLineCharacters, ReadBufferCharacters));
            bool truncated = false;
            int count;

            while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    char ch = buffer[i];
                    if (ch == '\n')
                    {
                        DeliverLine(callback, line, truncated);
                        line.Clear();
                        truncated = false;
                    }
                    else if (ch != '\r')
                    {
                        if (line.Length < maxLineCharacters) line.Append(ch);
                        else truncated = true;
                    }
                }
            }

            if (line.Length > 0 || truncated) DeliverLine(callback, line, truncated);
        }

        static void DeliverLine(Action<string> callback, StringBuilder line, bool truncated)
        {
            try { callback(truncated ? line.ToString() + TruncatedLineMarker : line.ToString()); }
            catch (Exception ex) { AppLog.Write("Child process output callback failed: " + ex.Message); }
        }

        public static void AppendBoundedTail(StringBuilder output, string line, int maxCharacters)
        {
            if (maxCharacters <= 0) return;
            string text = (line ?? string.Empty) + Environment.NewLine;
            lock (output)
            {
                if (text.Length >= maxCharacters)
                {
                    output.Clear();
                    output.Append(text.AsSpan(text.Length - maxCharacters));
                    return;
                }

                int excess = output.Length + text.Length - maxCharacters;
                if (excess > 0) output.Remove(0, excess);
                output.Append(text);
            }
        }
    }
}
