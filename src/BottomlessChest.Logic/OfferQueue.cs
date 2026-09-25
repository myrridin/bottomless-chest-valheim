using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BottomlessChest.Logic
{
    /// <summary>
    /// Sends a client's Place Stacks offers one at a time, and never offers an item twice.
    /// </summary>
    /// <remarks>
    /// A server keeps every offered item its chest already holds. Offers to two chests at once
    /// could both keep the same items, and a re-send after a slow reply could be kept twice.
    /// So an offer waits for the previous chest's answer, and every item in flight stays reserved
    /// until its own answer arrives - a timeout lets other chests go ahead but never releases a
    /// reservation, because the chest may have kept those items.
    ///
    /// An offer that is never answered therefore holds its items for good. That needs a reply
    /// lost without a disconnect, since every server path answers and a disconnect clears this
    /// queue with the player it belonged to; <see cref="OverdueCount"/> is how the mod notices
    /// and says so rather than quietly stacking nothing.
    /// </remarks>
    public sealed class OfferQueue<TItem> where TItem : class
    {
        private sealed class Waiting
        {
            internal string StoreId;
            internal bool Message;
        }

        private sealed class InFlight
        {
            internal IReadOnlyList<TItem> Items;
            internal bool Message;
            internal float SentAt;
        }

        private sealed class ByReference : IEqualityComparer<TItem>
        {
            public bool Equals(TItem x, TItem y) => ReferenceEquals(x, y);

            public int GetHashCode(TItem obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private readonly float _timeout;
        private readonly List<Waiting> _waiting = new List<Waiting>();
        private readonly Dictionary<string, InFlight> _inFlight = new Dictionary<string, InFlight>(StringComparer.Ordinal);
        private readonly Dictionary<TItem, int> _reserved = new Dictionary<TItem, int>(new ByReference());

        public OfferQueue(float timeoutSeconds)
        {
            _timeout = timeoutSeconds;
        }

        /// <summary>Asks for an offer to a chest. A chest already waiting is not queued twice.</summary>
        public void Enqueue(string storeId, bool message)
        {
            if (string.IsNullOrEmpty(storeId))
            {
                return;
            }

            foreach (var waiting in _waiting)
            {
                if (waiting.StoreId == storeId)
                {
                    waiting.Message |= message;
                    return;
                }
            }

            _waiting.Add(new Waiting { StoreId = storeId, Message = message });
        }

        /// <summary>
        /// The next chest to offer to, unless an offer still in flight is within its timeout.
        /// </summary>
        /// <remarks>
        /// A chest whose earlier offer is still unanswered is skipped even past the timeout: the
        /// server names kept items by their position in an offer, so a second offer to the same
        /// chest would let the first answer name the wrong items.
        /// </remarks>
        public bool TryNext(float now, out string storeId, out bool message)
        {
            storeId = null;
            message = false;

            foreach (var flight in _inFlight.Values)
            {
                if (now - flight.SentAt < _timeout)
                {
                    return false;
                }
            }

            for (var i = 0; i < _waiting.Count; i++)
            {
                if (_inFlight.ContainsKey(_waiting[i].StoreId))
                {
                    continue;
                }

                storeId = _waiting[i].StoreId;
                message = _waiting[i].Message;
                _waiting.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>Records an offer sent, reserving every item in it.</summary>
        public void Sent(string storeId, IReadOnlyList<TItem> items, bool message, float now)
        {
            var list = items ?? Array.Empty<TItem>();
            _inFlight[storeId] = new InFlight { Items = list, Message = message, SentAt = now };
            foreach (var item in list)
            {
                Reserve(item);
            }
        }

        /// <summary>The chest answered: hands back what was offered and releases it.</summary>
        public bool Complete(string storeId, out IReadOnlyList<TItem> items, out bool message)
        {
            items = Array.Empty<TItem>();
            message = false;

            if (storeId == null || !_inFlight.TryGetValue(storeId, out var flight))
            {
                return false;
            }

            _inFlight.Remove(storeId);
            foreach (var item in flight.Items)
            {
                Release(item);
            }

            items = flight.Items;
            message = flight.Message;
            return true;
        }

        /// <summary>Offers sent longer ago than the timeout and still unanswered.</summary>
        public int OverdueCount(float now)
        {
            var overdue = 0;
            foreach (var flight in _inFlight.Values)
            {
                if (now - flight.SentAt >= _timeout)
                {
                    overdue++;
                }
            }

            return overdue;
        }

        public bool IsReserved(TItem item) => item != null && _reserved.ContainsKey(item);

        /// <summary>Holds an item back from every offer. Counted, so two holders need two releases.</summary>
        public void Reserve(TItem item)
        {
            if (item != null)
            {
                _reserved[item] = _reserved.TryGetValue(item, out var held) ? held + 1 : 1;
            }
        }

        public void Release(TItem item)
        {
            if (item == null || !_reserved.TryGetValue(item, out var held))
            {
                return;
            }

            if (held <= 1)
            {
                _reserved.Remove(item);
            }
            else
            {
                _reserved[item] = held - 1;
            }
        }

        /// <summary>Forgets everything, for when the player it served is gone.</summary>
        public void Clear()
        {
            _waiting.Clear();
            _inFlight.Clear();
            _reserved.Clear();
        }
    }
}
