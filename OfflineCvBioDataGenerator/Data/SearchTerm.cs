using System;
using System.Text;

namespace OfflineCvBioDataGenerator.Data
{
    /// <summary>
    /// Helpers for turning user input into a safe SQL LIKE pattern.
    ///
    /// Without escaping, a search for "A_" or "100%" matches far more rows than the user
    /// expects, because '_' and '%' are LIKE wildcards. A backslash is used as the escape
    /// character (and the SQL must declare ESCAPE '\').
    /// </summary>
    public static class SearchTerm
    {
        private const char EscapeChar = '\\';

        /// <summary>Escapes LIKE wildcards inside the user's text.</summary>
        public static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var sb = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                if (c == EscapeChar || c == '%' || c == '_')
                    sb.Append(EscapeChar);
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Builds a "contains" pattern (%value%) with wildcards escaped.</summary>
        public static string Contains(string? value)
        {
            string escaped = Escape(value);
            return escaped.Length == 0 ? "%" : $"%{escaped}%";
        }
    }
}
