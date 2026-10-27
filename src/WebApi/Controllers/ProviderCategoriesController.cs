using Common.Results;
using Microsoft.AspNetCore.Authorization;
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
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   every write: PMO, COMMERCIAL
    [Authorize]
    public class ProviderCategoriesController : BaseApiController
    {
        private readonly IProviderCategoryService _categories;

        public ProviderCategoriesController(IProviderCategoryService categories)
        {
            _categories = categories ?? throw new System.ArgumentNullException(nameof(categories));
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ProviderCategory>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<ProviderCategory>> AddProviderCategoryAsync([FromBody] AddProviderCategoryDto dto, CancellationToken cancellationToken)
        {
            var result = await _categories.AddProviderCategoryAsync(dto.CategoryName, cancellationToken);

            return result;
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
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

        /// <summary>The list page deletes a selection: DELETE ProviderCategories/delete with the ids in the body (the same shape as the other delete-collection routes). A category still used by a supplier is kept.</summary>
        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpDelete("delete")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<ResponseDto<string>>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<IEnumerable<ResponseDto<string>>>> DeleteProviderCategoryCollectionAsync([FromBody] int[] categoryIds, CancellationToken cancellationToken)
        {
            if (categoryIds is null || categoryIds.Length == 0)
                return ResponseDtoHelper.BadRequest<IEnumerable<ResponseDto<string>>>("شناسه‌ای برای حذف داده نشده است.");

            var results = new List<ResponseDto<string>>();
            foreach (int id in categoryIds)
                results.Add(await _categories.DeleteProviderCategoryAsync(id, cancellationToken));
            return ResponseDtoHelper.Ok<IEnumerable<ResponseDto<string>>>(results);
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpDelete("{categoryId:int}")]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<string>> DeleteProviderCategoryAsync([FromRoute] int categoryId, CancellationToken cancellationToken) // the id is in the route, as the template says (it was read from the query and never bound)
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
