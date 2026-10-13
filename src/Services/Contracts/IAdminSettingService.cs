using System.Threading.Tasks;
using ViewModels;
using ViewModels.Dto;

namespace Services.Contracts
{
    public interface IAdminSettingService
    {
        Task<AdminSettingListDto> GetAdminSettingListAsync(DataTableAjaxPostModel dataTable);
        Task<int> AddAdminSettingAsync(AdminSettingVM model);
        Task UpdateAdminSettingAsync(AdminSettingVM model);
        Task<AdminSettingVM> GetAdminSettingAsync();
    }
}
