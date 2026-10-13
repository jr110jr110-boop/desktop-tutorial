using Common.Results;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro;
using System.Net;
using System.Threading;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.ProviderDtos;
using Entities.Model.NasbNiro.Providers;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WebApi.Controllers
{
    public class ProviderCategoriesController : BaseApiController
    {
        private readonly IProviderCategoryService _categories;

        public ProviderCategoriesController(IProviderCategoryService categories)
        {
            _categories = categories ?? throw new System.ArgumentNullException(nameof(categories));
        }

        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ProviderCategory>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<ProviderCategory>> AddProviderCategoryAsync([FromBody] AddProviderCategoryDto dto, CancellationToken cancellationToken)
        {
            var result = await _categories.AddProviderCategoryAsync(dto.CategoryName, cancellationToken);

            return result;
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ProviderCategory>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<ProviderCategory>> UpdateProviderCategoriesAsync(
            [FromBody] ProviderCategory providerCategory,
            CancellationToken cancellationToken)
        {
            var result = await _categories.UpdateProviderCategoryAsync(providerCategory, cancellationToken);

            return result;
        }

        [HttpGet("categories/{categoryId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ProviderCategory>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<ProviderCategory>> GetProviderCategoryAsync([FromQuery] int categoryId, CancellationToken cancellationToken)
        {
            var result = await _categories.GetProviderCategoryAsync(categoryId, cancellationToken);

            return result;
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<ProviderCategory>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<IEnumerable<ProviderCategory>>> GetAllProviderCategoriesAsync(CancellationToken cancellationToken)
        {
            var result = await _categories.GetAllProviderCategoriesAsync(cancellationToken);

            return result;
        }

        [HttpDelete("{categoryId:int}")]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<string>> DeleteProviderCategoryAsync([FromQuery] int categoryId, CancellationToken cancellationToken)
        {
            var result = await _categories.DeleteProviderCategoryAsync(categoryId, cancellationToken);

            return result;
        }
    }

    public class AddProviderCategoryDto
    {
        [Required]
        public string CategoryName { get; set; }
    }
}
