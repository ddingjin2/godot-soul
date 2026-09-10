using System.Text;

namespace UnityTestAgent.Replay
{
    internal static class JsonText
    {
        public static string Escape(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public static string Unescape(string value)
        {
            if (value == null)
            {
                return "";
            }

            var builder = new StringBuilder();
            var escaping = false;
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (escaping)
                {
                    builder.Append(current);
                    escaping = false;
                }
                else if (current == '\\')
                {
                    escaping = true;
                }
                else
                {
                    builder.Append(current);
                }
            }

            return builder.ToString();
        }
    }
}
