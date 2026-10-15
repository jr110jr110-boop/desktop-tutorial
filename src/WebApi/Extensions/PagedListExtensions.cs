using Common.Results;
using ViewModels.NasbNiro.Pagination;

namespace WebApi.Extensions
{
    public static class PagedListExtensions
    {
        public static ResponseDto<PaginatedResult<T>>
            ToPaginatedResult<T>(this ResponseDto<PagedList<T>> responseContract)
        {
            PagedList<T> pg = responseContract.Data;

            PaginatedResult<T> data = new PaginatedResult<T>()
            {
                Data = pg,
                CurrentPage = pg.CurrentPage,
                ItemsPerPage = pg.PageSize,
                TotalItems = pg.TotalCount,
                TotalPages = pg.TotalPages
            };

            return new ResponseDto<PaginatedResult<T>>
                (responseContract.Success, responseContract.Message, data);
        }

        /// <summary>Like <see cref="ToPaginatedResult{T}"/>, but keeps the error message when the list could not be made.</summary>
        public static ResponseDto<PaginatedResult<T>>
            ToPaginatedResultSafe<T>(this ResponseDto<PagedList<T>> responseContract)
        {
            if (responseContract?.Data == null)
            {
                return new ResponseDto<PaginatedResult<T>>(false, responseContract?.Message, null);
            }
            return responseContract.ToPaginatedResult();
        }
    }
}
