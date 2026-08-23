using System.Globalization;
using System.Text;

namespace BottomlessChest.Logic
{
    /// <summary>
    /// Turns display text into a comparison key: lower-cased and stripped of diacritics.
    /// </summary>
    /// <remarks>
    /// Shared by search and sorting so both agree on what counts as the same text. Without
    /// it, "angbat" would not find "Ångbåt", and "Ångbåt" would sort after "Zebra" because
    /// its first character is above 'Z' in ordinal terms.
    /// </remarks>
    public static class TextKey
    {
        public static string Of(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var decomposed = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);

            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }
    }
}
