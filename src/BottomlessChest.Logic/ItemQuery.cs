using System;
using System.Collections.Generic;

namespace BottomlessChest.Logic
{
    /// <summary>
    /// A parsed search box query, reusable across many items.
    /// </summary>
    /// <remarks>
    /// Parsing is separated from matching because the search box re-filters the whole
    /// chest on every keystroke; the query is parsed once and then applied to thousands
    /// of items.
    /// </remarks>
    public sealed class ItemQuery
    {
        private static readonly Dictionary<string, ItemKind> KindTokens =
            new Dictionary<string, ItemKind>(StringComparer.Ordinal)
            {
                { "@material", ItemKind.Material },
                { "@food", ItemKind.Food },
                { "@weapon", ItemKind.Weapon },
                { "@armor", ItemKind.Armor },
                { "@ammo", ItemKind.Ammo },
                { "@tool", ItemKind.Tool },
                { "@trophy", ItemKind.Trophy },
                { "@misc", ItemKind.Misc }
            };

        private readonly List<string> _textTerms;
        private readonly List<ItemKind> _kindTerms;

        private ItemQuery(List<string> textTerms, List<ItemKind> kindTerms)
        {
            _textTerms = textTerms;
            _kindTerms = kindTerms;
        }

        public bool IsEmpty => _textTerms.Count == 0 && _kindTerms.Count == 0;

        public static ItemQuery Parse(string query)
        {
            var textTerms = new List<string>();
            var kindTerms = new List<ItemKind>();

            if (!string.IsNullOrWhiteSpace(query))
            {
                foreach (var raw in query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                {
                    var term = TextKey.Of(raw);
                    if (term.Length == 0)
                    {
                        continue;
                    }

                    // An unrecognised @token is treated as literal text, so a typo finds
                    // nothing instead of silently widening the search.
                    if (KindTokens.TryGetValue(term, out var kind))
                    {
                        kindTerms.Add(kind);
                    }
                    else
                    {
                        textTerms.Add(term);
                    }
                }
            }

            return new ItemQuery(textTerms, kindTerms);
        }

        public bool Matches(IStorableItem item)
        {
            if (IsEmpty)
            {
                return true;
            }

            if (_kindTerms.Count > 0 && !_kindTerms.Contains(item.Kind))
            {
                return false;
            }

            if (_textTerms.Count == 0)
            {
                return true;
            }

            var name = TextKey.Of(item.DisplayName);

            foreach (var term in _textTerms)
            {
                if (name.IndexOf(term, StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }

            return true;
        }

    }
}
