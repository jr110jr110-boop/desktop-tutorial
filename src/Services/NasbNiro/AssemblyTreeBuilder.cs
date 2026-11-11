using System;
using System.Collections.Generic;
using System.Linq;

namespace Services.NasbNiro
{
    /// <summary>
    /// Builds a parent/child tree from a flat list in memory (one database query instead of one per node).
    /// A node that is already on the current path is never expanded again; its id is reported.
    /// </summary>
    internal static class AssemblyTreeBuilder
    {
        /// <returns>Ids of nodes that were skipped because they would close a cycle.</returns>
        internal static List<int> AttachChildren<T>(
            IEnumerable<T> roots,
            IEnumerable<T> all,
            Func<T, int> idOf,
            Func<T, int?> parentIdOf,
            Action<T, IEnumerable<T>> setChildren)
        {
            List<int> cycles = new List<int>();
            if (roots == null)
            {
                return cycles;
            }

            ILookup<int, T> byParent = (all ?? Enumerable.Empty<T>())
                .Where(x => parentIdOf(x).HasValue)
                .OrderBy(idOf)
                .ToLookup(x => parentIdOf(x).Value);

            foreach (T root in roots)
            {
                if (root == null)
                {
                    continue;
                }

                Expand(root, new HashSet<int> { idOf(root) });
            }

            return cycles;

            void Expand(T node, HashSet<int> path)
            {
                List<T> children = new List<T>();
                foreach (T child in byParent[idOf(node)])
                {
                    int childId = idOf(child);
                    if (path.Contains(childId))
                    {
                        cycles.Add(childId);
                        continue;
                    }

                    children.Add(child);
                }

                setChildren(node, children.ToArray());

                foreach (T child in children)
                {
                    path.Add(idOf(child));
                    Expand(child, path);
                    path.Remove(idOf(child));
                }
            }
        }
    }
}
