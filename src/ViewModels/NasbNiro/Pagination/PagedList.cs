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
                items = await source.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
            }
            return new PagedList<T>(items, count, pageNumber, pageSize);
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
