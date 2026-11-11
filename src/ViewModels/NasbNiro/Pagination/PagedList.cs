using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace ViewModels.NasbNiro.Pagination
{
    public class PagedList<T> : List<T>
    {
        protected PagedList(IEnumerable<T> items, int count, int pageNumber, int pageSize)
        {
            int totalPages = (int)Math.Ceiling(count / (double)pageSize);

            this.CurrentPage = pageNumber;
            this.TotalPages = pageSize == 0 ? 1 : totalPages;
            this.TotalCount = count;
            this.PageSize = pageSize;

            AddRange(items);
        }

        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public int TotalCount { get; set; }
        public int PageSize { get; set; }

        public static async Task<PagedList<T>> CreateAsync(IQueryable<T> source, int pageNumber, int pageSize, CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source), "Source cannot be null.");
            }

            pageNumber = pageNumber <= 0 ? 1 : pageNumber;
            pageSize = pageSize <= 0 ? 0 : pageSize;
            var count = await source.CountAsync();

            IEnumerable<T> items;

            if (pageSize == 0)
            {
                pageSize = count;
                pageNumber = 1;
                items = await source.ToListAsync(cancellationToken);
            }
            else
            {
                items = await EnsureOrdered(source).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
            }
            return new PagedList<T>(items, count, pageNumber, pageSize);
        }

        /// <summary>
        /// FX1: Skip/Take over a query without any ordering gives an unstable page (and EF warns). A query that has no ordering gets one by its Id
        /// (a deterministic key); a query that is already ordered, or a row type without an Id, is returned as it is.
        /// </summary>
        public static IQueryable<T> EnsureOrdered(IQueryable<T> source)
        {
            if (IsOrdered(source.Expression))
            {
                return source;
            }

            System.Reflection.PropertyInfo id = typeof(T).GetProperty("Id");
            if (id == null || !id.CanRead || !typeof(IComparable).IsAssignableFrom(id.PropertyType))
            {
                return source;
            }

            System.Linq.Expressions.ParameterExpression parameter = System.Linq.Expressions.Expression.Parameter(typeof(T), "x");
            System.Linq.Expressions.LambdaExpression key = System.Linq.Expressions.Expression.Lambda(
                System.Linq.Expressions.Expression.Property(parameter, id), parameter);

            System.Linq.Expressions.MethodCallExpression call = System.Linq.Expressions.Expression.Call(
                typeof(Queryable), nameof(Queryable.OrderBy), new[] { typeof(T), id.PropertyType },
                source.Expression, System.Linq.Expressions.Expression.Quote(key));

            return source.Provider.CreateQuery<T>(call);
        }

        private static bool IsOrdered(System.Linq.Expressions.Expression expression)
        {
            while (expression is System.Linq.Expressions.MethodCallExpression call && call.Arguments.Count > 0)
            {
                string name = call.Method.Name;
                if (name.StartsWith("OrderBy", StringComparison.Ordinal) || name.StartsWith("ThenBy", StringComparison.Ordinal))
                {
                    return true;
                }

                expression = call.Arguments[0];
            }

            return false;
        }

        public static async Task<PagedList<T>> CreateAsync(List<T> source, int pageNumber, int pageSize, CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source), "Source cannot be null.");
            }

            pageNumber = pageNumber <= 0 ? 1 : pageNumber;
            pageSize = pageSize <= 0 ? 0 : pageSize;
            var count = source.Count;

            IEnumerable<T> items;

            if (pageSize == 0)
            {
                pageSize = count;
                pageNumber = 1;
                items = source;
            }
            else
            {
                items = source.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
            }
            return new PagedList<T>(items, count, pageNumber, pageSize);
        }
    }
}
