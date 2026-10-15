using System.Security.Claims;
using System.Security.Principal;
using DataLayer.Context;
using Entities.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.Contracts;
using Services.Contracts.Identity;
using Services.Identity;
using Services.Identity.AccountServices;
using Services.Identity.Logger;
using Services.NasbNiro;
using Services.NasbNiro.Contracts;
using Services.NasbNiro.DateTimeService;
using Services.NasbNiro.ExternalServices;
using Services.NasbNiro.Holidays;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Reports;
using Services.NasbNiro.WorkOrders;
using Services.Services;
using Services.Token;

namespace IocConfig
{
    public static class AddCustomServicesExtensions
    {
        public static IServiceCollection AddCustomServices(this IServiceCollection services)
        {
            #region Identity
            services.AddScoped<IAccountService, AccountService>();
            #endregion

            #region NasbNiro
            services.AddTransient<ITenderService, TenderService>();
            services.AddTransient<IContractService, ContractService>();
            services.AddTransient<IEquipmentService, EquipmentService>();
            services.AddTransient<IEquipmentAssemblyService, EquipmentAssemblyService>();
            services.AddTransient<IContractEquipmentService, ContractEquipmentService>();
            services.AddTransient<IEquipmentBomService, EquipmentBomService>();
            services.AddTransient<IEquipmentProcessService, EquipmentProcessService>();
            services.AddTransient<IWorkOrderService, WorkOrderService>();
            services.AddTransient<IAssemblyService, AssemblyService>();
            services.AddTransient<IBomService, BomService>();
            services.AddTransient<IProcessService, ProcessService>();
            services.AddTransient<IProviderService, ProviderService>();
            services.AddTransient<IBuyOrderService, BuyOrderService>();
            services.AddTransient<IBuyOrderProcessService, BuyOrderProcessService>();
            services.AddTransient<IInquiryService, InquiryService>();
            services.AddTransient<ICardBoardService, CardBoardService>();
            services.AddTransient<IHolidaysService, HolidaysService>();
            services.AddTransient<IDateTimeService, TransientDateTimeService>();
            services.AddTransient<IBuyOrderOutgoingService, BuyOrderOutgoingService>();
            services.AddTransient<IEquipmentEstimationReports, ReportsService>();
            services.AddTransient<IProgressReports, ReportsService>();
            services.AddTransient<IMaterialBalanceReports, ReportsService>();
            services.AddTransient<IProStatService, ProStatService>();
            services.AddTransient<IProviderCategoryService, ProviderCategoryService>();
            services.AddTransient<StoreService>();
            #endregion

            services.AddScoped<IUnitOfWork, ApplicationDbContext>();

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<IPrincipal>(provider =>
                provider.GetService<IHttpContextAccessor>()?.HttpContext?.User ?? ClaimsPrincipal.Current);

            // CustomNormalizer now implements ILookupNormalizer directly (UpperInvariantLookupNormalizer has no virtual members since ASP.NET Core 3.0).
            services.AddScoped<ILookupNormalizer, CustomNormalizer>();

            services.AddScoped<ISecurityStampValidator, CustomSecurityStampValidator>();
            services.AddScoped<SecurityStampValidator<User>, CustomSecurityStampValidator>();

            services.AddScoped<IPasswordValidator<User>, CustomPasswordValidator>();
            services.AddScoped<PasswordValidator<User>, CustomPasswordValidator>();

            services.AddScoped<IUserValidator<User>, CustomUserValidator>();
            services.AddScoped<UserValidator<User>, CustomUserValidator>();

            services.AddScoped<IUserClaimsPrincipalFactory<User>, ApplicationClaimsPrincipalFactory>();
            services.AddScoped<UserClaimsPrincipalFactory<User, Role>, ApplicationClaimsPrincipalFactory>();

            services.AddScoped<IdentityErrorDescriber, CustomIdentityErrorDescriber>();

            services.AddScoped<IApplicationUserStore, ApplicationUserStore>();
            services.AddScoped<UserStore<User, Role, ApplicationDbContext, int, UserClaim, UserRole, UserLogin, UserToken, RoleClaim>, ApplicationUserStore>();

            services.AddScoped<IApplicationUserManager, ApplicationUserManager>();
            services.AddScoped<UserManager<User>, ApplicationUserManager>();

            services.AddScoped<IApplicationRoleManager, ApplicationRoleManager>();
            services.AddScoped<RoleManager<Role>, ApplicationRoleManager>();

            services.AddScoped<IApplicationSignInManager, ApplicationSignInManager>();
            services.AddScoped<SignInManager<User>, ApplicationSignInManager>();

            services.AddScoped<IApplicationRoleStore, ApplicationRoleStore>();
            services.AddScoped<RoleStore<Role, ApplicationDbContext, int, UserRole, RoleClaim>, ApplicationRoleStore>();

            services.AddScoped<IEmailSender, AuthMessageSender>();
            services.AddScoped<ISmsSender, AuthMessageSender>();

            services.AddScoped<IIdentityDbInitializer, IdentityDbInitializer>();
            services.AddScoped<IUsedPasswordsService, UsedPasswordsService>();
            services.AddScoped<ISiteStatService, SiteStatService>();
            services.AddScoped<IUsersPhotoService, UsersPhotoService>();
            services.AddScoped<ISecurityTrimmingService, SecurityTrimmingService>();
            services.AddScoped<IAppLogItemsService, AppLogItemsService>();
            services.AddScoped<ITokenStoreService, TokenStoreService>();
            services.AddScoped<ITokenFactoryService, TokenFactoryService>();
            services.AddScoped<ISiteInfoService, SiteInfoService>();

            services.AddSingleton<ISecurityService, SecurityService>();
            services.AddScoped<IDocumentService, DocumentService>();

            services.AddScoped<IAdminSettingService, AdminSettingService>();
            services.AddScoped<IWorkflowGroupService, WorkflowGroupService>();
            services.AddScoped<IWorkflowStationService, WorkflowStationService>();
            services.AddScoped<IMachineService, MachineService>();

            // Warehouse, part traceability and Sepidar link
            services.AddScoped<IInventoryUserContext, InventoryUserContext>();
            services.AddScoped<IInventoryMasterService, InventoryMasterService>();
            services.AddScoped<IInventoryDocumentService, InventoryDocumentService>();
            services.AddScoped<IInventoryStockService, InventoryStockService>();
            services.AddScoped<ITraceabilityService, TraceabilityService>();
            services.AddScoped<ISepidarLinkService, SepidarLinkService>();
            return services;
        }
    }
}