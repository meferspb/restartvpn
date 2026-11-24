using System.Text;

namespace RestartVpn.Core.Settings
{
    /// <summary>
    /// Minimal pretty-printer for JSON produced by JavaScriptSerializer. It only changes
    /// whitespace outside string literals, so it cannot corrupt the payload.
    /// </summary>
    public static class JsonText
    {
        public static string Prettify(string json)
        {
            if (string.IsNullOrEmpty(json)) return json ?? string.Empty;

            var sb = new StringBuilder(json.Length + json.Length / 4 + 64);
            int depth = 0;
            bool inString = false;
            bool escape = false;
            char lastSignificant = '\0';

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    sb.Append(c);
                    if (escape) { escape = false; }
                    else if (c == '\\') { escape = true; }
                    else if (c == '"') { inString = false; lastSignificant = c; }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        sb.Append(c);
                        break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        depth++;
                        lastSignificant = c;
                        sb.Append('\n');
                        sb.Append(' ', depth * 2);
                        break;
                    case '}':
                    case ']':
                        if (lastSignificant == '{' || lastSignificant == '[')
                        {
                            depth--;
                            sb.Append(c);
                        }
                        else
                        {
                            depth--;
                            sb.Append('\n');
                            sb.Append(' ', depth * 2);
                            sb.Append(c);
                        }
                        lastSignificant = c;
                        break;
                    case ',':
                        sb.Append(c);
                        sb.Append('\n');
                        sb.Append(' ', depth * 2);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    case ' ':
                    case '\t':
                    case '\r':
                    case '\n':
                        break;
                    default:
                        sb.Append(c);
                        lastSignificant = c;
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
