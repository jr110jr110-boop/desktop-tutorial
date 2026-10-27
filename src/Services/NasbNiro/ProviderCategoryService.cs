using System;
using System.Collections.Generic;
using System.Linq.Dynamic.Core;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.GuardToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Services.NasbNiro.Contracts;

namespace Services.NasbNiro
{
    public class ProviderCategoryService : IProviderCategoryService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly DbSet<ProviderCategory> _categories;

        public ProviderCategoryService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork.ThrowIfNull(nameof(unitOfWork));
            _categories = unitOfWork.Set<ProviderCategory>().ThrowIfNull(nameof(_categories));
        }

        public async ValueTask<ResponseDto<ProviderCategory>> AddProviderCategoryAsync(string categoryName, CancellationToken cancellationToken)
        {
            EntityEntry<ProviderCategory> entityEntry = await _categories
                 .AddAsync(new ProviderCategory()
                 {
                     Category = categoryName,
                 });

            if (await SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(entityEntry.Entity);
            }

            return ResponseDtoHelper.BadRequest<ProviderCategory>("دسته بندی اضافه نشد.");
        }

        public async ValueTask<ResponseDto<string>> DeleteProviderCategoryAsync(int categoryId, CancellationToken cancellationToken)
        {
            ProviderCategory categoryToDelete = await _categories.FindAsync(categoryId);

            if (categoryToDelete is null)
            {
                return ResponseDtoHelper.BadRequest<string>("دسته بندی یافت نشد.");
            }

            // a category that suppliers still carry is not removed (the suppliers would lose their grouping; move them first)
            if (await _unitOfWork.Set<Provider>().AnyAsync(p => !p.Deleted && p.CategoryId == categoryId, cancellationToken))
                return ResponseDtoHelper.BadRequest<string>("این دسته‌بندی هنوز به تأمین‌کننده‌هایی اختصاص دارد و حذف نمی‌شود؛ ابتدا دسته‌بندی آن‌ها را تغییر دهید.");

            _categories.Remove(categoryToDelete);


            if (await SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok("دسته بندی حذف شد.");
            }

            return ResponseDtoHelper.BadRequest<string>("دسته بندی حذف نشد.");
        }

        public async ValueTask<ResponseDto<IEnumerable<ProviderCategory>>> GetAllProviderCategoriesAsync(CancellationToken cancellationToken)
        {
            IEnumerable<ProviderCategory> result = await _categories.ToListAsync(cancellationToken);

            return ResponseDtoHelper.Ok(result);
        }

        public async ValueTask<ResponseDto<ProviderCategory>> GetProviderCategoryAsync(int categoryId, CancellationToken cancellationToken)
        {
            ProviderCategory dbCategory = await _categories.FindAsync(categoryId);

            if (dbCategory is null)
            {
                return ResponseDtoHelper.BadRequest<ProviderCategory>("دسته بندی یافت نشد.");
            }
            else
            {
                return ResponseDtoHelper.Ok(dbCategory);

            }
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }

        public async ValueTask<ResponseDto<ProviderCategory>> UpdateProviderCategoryAsync(ProviderCategory providerCategory, CancellationToken cancellationToken)
        {
            ProviderCategory categoryToUpdate = await _categories.FindAsync(providerCategory.Id);

            if (categoryToUpdate is null)
            {
                return ResponseDtoHelper.BadRequest<ProviderCategory>("دسته بندی یافت نشد.");
            }

            if (categoryToUpdate.Category != providerCategory.Category)
            {
                categoryToUpdate.Category = providerCategory.Category;
            }

            if (await SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(categoryToUpdate);
            }

            return ResponseDtoHelper.BadRequest<ProviderCategory>("دسته بندی به روز نشد.");
        }
    }
}
