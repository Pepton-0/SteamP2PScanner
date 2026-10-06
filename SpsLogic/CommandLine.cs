using System.Text;

namespace SpsLogic
{
    public static class CommandLine
    {
        /// <summary>Quotes an argument so that the Windows command line parser restores it exactly.</summary>
        public static string Quote(string value)
        {
            var quoted = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in value ?? string.Empty)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                // Backslashes are literal unless they precede a quote.
                quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
                quoted.Append(c);
                backslashes = 0;
            }

            quoted.Append('\\', backslashes * 2);
            return quoted.Append('"').ToString();
        }
    }
}
