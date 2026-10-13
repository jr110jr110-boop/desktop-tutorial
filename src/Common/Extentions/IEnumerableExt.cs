using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Common.Extentions
{
    public static class IEnumerableExt
    {
        public static void ForEach<T>(this IEnumerable<T> data, Action<T> action)
        {
            foreach (T item in data)
            {
                action(item);
            }
        }

        public static async Task ForEachAsync<T>(this IEnumerable<T> data, Func<T, Task> action)
        {
            IEnumerable<Task> tasks = data.Select(x => action(x));

            foreach (Task item in tasks)
            {
                await item;
            }
        }
    }
}
