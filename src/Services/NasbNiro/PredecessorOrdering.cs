using System;
using System.Collections.Generic;
using System.Linq;

namespace Services.NasbNiro
{
    /// <summary>
    /// Pure helpers for predecessor links (BOM lines and assemblies of a product): topological order,
    /// missing predecessors and cycles. Never throws and always terminates.
    /// </summary>
    internal static class PredecessorOrdering
    {
        internal sealed class Result<T>
        {
            /// <summary>Items in an order where every predecessor comes before its successors.</summary>
            public List<T> Ordered { get; } = new List<T>();

            /// <summary>Items whose predecessor id is not part of the given set (deleted, other product, missing).</summary>
            public List<T> MissingPredecessor { get; } = new List<T>();

            /// <summary>Items that really lie on a cycle (not the ones merely hanging below a cycle).</summary>
            public List<T> OnCycle { get; } = new List<T>();

            public bool IsClean => MissingPredecessor.Count == 0 && OnCycle.Count == 0;
        }

        internal static Result<T> Sort<T>(IEnumerable<T> items, Func<T, int> idOf, Func<T, int?> predOf)
        {
            Result<T> result = new Result<T>();
            List<T> all = (items ?? Enumerable.Empty<T>()).OrderBy(idOf).ToList();
            Dictionary<int, T> byId = new Dictionary<int, T>();
            foreach (T item in all)
            {
                byId[idOf(item)] = item;
            }

            foreach (T item in all)
            {
                int? pred = predOf(item);
                if (pred.HasValue && !byId.ContainsKey(pred.Value))
                {
                    result.MissingPredecessor.Add(item);
                }
            }

            // Kahn: start with the items that have no predecessor, release the successors of every placed item.
            ILookup<int, T> successors = all.Where(x => predOf(x).HasValue).ToLookup(x => predOf(x).Value);
            Queue<T> ready = new Queue<T>(all.Where(x => !predOf(x).HasValue));
            HashSet<int> placed = new HashSet<int>();
            while (ready.Count > 0)
            {
                T current = ready.Dequeue();
                if (!placed.Add(idOf(current)))
                {
                    continue;
                }

                result.Ordered.Add(current);
                foreach (T next in successors[idOf(current)])
                {
                    ready.Enqueue(next);
                }
            }

            foreach (T item in all)
            {
                if (placed.Contains(idOf(item)))
                {
                    continue;
                }

                int? pred = predOf(item);
                if (pred.HasValue && !byId.ContainsKey(pred.Value))
                {
                    continue; // reported as missing predecessor
                }

                // walk up: the item is on a cycle only if the walk returns to it
                int id = idOf(item);
                HashSet<int> seen = new HashSet<int>();
                int? cursor = pred;
                bool onCycle = false;
                while (cursor.HasValue && byId.ContainsKey(cursor.Value) && seen.Add(cursor.Value))
                {
                    if (cursor.Value == id)
                    {
                        onCycle = true;
                        break;
                    }

                    cursor = predOf(byId[cursor.Value]);
                }

                if (onCycle)
                {
                    result.OnCycle.Add(item);
                }
            }

            return result;
        }

        /// <summary>
        /// True when giving <paramref name="id"/> the predecessor <paramref name="newPredecessorId"/> would close a loop
        /// (also true when the stored chain above the new predecessor already loops).
        /// </summary>
        internal static bool WouldCloseLoop(int? id, int newPredecessorId, Func<int, int?> predecessorOf)
        {
            if (id.HasValue && newPredecessorId == id.Value)
            {
                return true;
            }

            HashSet<int> seen = new HashSet<int>();
            int? cursor = newPredecessorId;
            while (cursor.HasValue)
            {
                if (id.HasValue && cursor.Value == id.Value)
                {
                    return true;
                }

                if (!seen.Add(cursor.Value))
                {
                    return true;
                }

                cursor = predecessorOf(cursor.Value);
            }

            return false;
        }
    }
}
