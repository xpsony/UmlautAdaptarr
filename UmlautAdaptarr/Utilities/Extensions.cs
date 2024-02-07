﻿using System.Globalization;
using System.Text;

namespace UmlautAdaptarr.Utilities
{
    public static class Extensions
    {
        public static string GetQuery(this HttpContext context, string key)
        {
            return context.Request.Query[key].FirstOrDefault() ?? string.Empty;
        }
        public static string RemoveAccent(this string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);

                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }


        public static string RemoveAccentButKeepGermanUmlauts(this string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);

                if (unicodeCategory != UnicodeCategory.NonSpacingMark || c == '\u0308')
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        public static string ReplaceGermanUmlautsWithLatinEquivalents(this string text)
        {
            return text
                .Replace("Ö", "Oe")
                .Replace("Ä", "Ae")
                .Replace("Ü", "Ue")
                .Replace("ö", "oe")
                .Replace("ä", "ae")
                .Replace("ü", "ue")
                .Replace("ß", "ss");
        }

        public static string RemoveGermanUmlautDots(this string text)
        {
            return text
                .Replace("ö", "o")
                .Replace("ü", "u")
                .Replace("ä", "a")
                .Replace("Ö", "O")
                .Replace("Ü", "U")
                .Replace("Ä", "A")
                .Replace("ß", "ss");
        }

        public static bool HasGermanUmlauts(this string text)
        {
            if (text == null) return false;
            var umlauts = new[] { 'ö', 'ä', 'ü', 'Ä', 'Ü', 'Ö', 'ß' };
            return umlauts.Any(text.Contains);
        }
    }
}
