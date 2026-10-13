using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Providers;

namespace Services.NasbNiro.Contracts
{
    public interface IProviderCategoryService:ISaveChangesAsync
    {
        ValueTask<ResponseDto<ProviderCategory>> AddProviderCategoryAsync(string categoryName,CancellationToken cancellationToken);
        ValueTask<ResponseDto<ProviderCategory>> UpdateProviderCategoryAsync(ProviderCategory providerCategory, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ProviderCategory>> GetProviderCategoryAsync(int categoryId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<ProviderCategory>>> GetAllProviderCategoriesAsync(CancellationToken cancellationToken);
        ValueTask<ResponseDto<string>> DeleteProviderCategoryAsync(int categoryId, CancellationToken cancellationToken);
    }
}
