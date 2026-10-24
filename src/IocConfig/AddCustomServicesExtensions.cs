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
            services.AddScoped<IOpeningStockService, OpeningStockService>();
            services.AddScoped<Services.NasbNiro.Finance.ICostingService, Services.NasbNiro.Finance.CostingService>();
            services.AddScoped<Services.NasbNiro.Finance.IFinanceLinkService, Services.NasbNiro.Finance.FinanceLinkService>();
            services.AddScoped<Services.NasbNiro.Finance.ICeoDashboardService, Services.NasbNiro.Finance.CeoDashboardService>();
            services.AddScoped<Services.NasbNiro.Finance.IEventPostingService, Services.NasbNiro.Finance.EventPostingService>();
            services.AddScoped<Services.NasbNiro.Finance.ILedgerReportService, Services.NasbNiro.Finance.LedgerReportService>();
            services.AddScoped<Services.NasbNiro.Finance.IGlService, Services.NasbNiro.Finance.GlService>();
            services.AddScoped<Services.NasbNiro.Finance.IGlControlsService>(sp => (Services.NasbNiro.Finance.GlService)sp.GetRequiredService<Services.NasbNiro.Finance.IGlService>());
            services.AddScoped<Services.NasbNiro.Finance.ILegalEntityService>(sp => (Services.NasbNiro.Finance.GlService)sp.GetRequiredService<Services.NasbNiro.Finance.IGlService>());
            services.AddScoped<Services.NasbNiro.Finance.IMigrationService>(sp => (Services.NasbNiro.Finance.GlService)sp.GetRequiredService<Services.NasbNiro.Finance.IGlService>());
            services.AddScoped<Services.NasbNiro.Finance.IFxService>(sp => (Services.NasbNiro.Finance.GlService)sp.GetRequiredService<Services.NasbNiro.Finance.IGlService>());
            services.AddScoped<Services.NasbNiro.Finance.IBudgetService>(sp => (Services.NasbNiro.Finance.GlService)sp.GetRequiredService<Services.NasbNiro.Finance.IGlService>());
            services.AddScoped<Services.NasbNiro.Finance.ICostingCompletenessService>(sp => (Services.NasbNiro.Finance.GlService)sp.GetRequiredService<Services.NasbNiro.Finance.IGlService>());
            services.AddScoped<Services.NasbNiro.Finance.IDeferralService>(sp => (Services.NasbNiro.Finance.GlService)sp.GetRequiredService<Services.NasbNiro.Finance.IGlService>());
            services.AddScoped<Services.NasbNiro.Finance.ILoanService>(sp => (Services.NasbNiro.Finance.GlService)sp.GetRequiredService<Services.NasbNiro.Finance.IGlService>());
            services.AddScoped<Services.NasbNiro.Finance.IStatementsService>(sp => (Services.NasbNiro.Finance.GlService)sp.GetRequiredService<Services.NasbNiro.Finance.IGlService>());
            services.AddScoped<Services.NasbNiro.Finance.ITaxCompletenessService>(sp => (Services.NasbNiro.Finance.GlService)sp.GetRequiredService<Services.NasbNiro.Finance.IGlService>());
            services.AddScoped<Services.NasbNiro.Finance.ICollectionsService>(sp => (Services.NasbNiro.Finance.GlService)sp.GetRequiredService<Services.NasbNiro.Finance.IGlService>());
            services.AddScoped<Services.NasbNiro.Finance.IFinanceCompletenessService>(sp => (Services.NasbNiro.Finance.GlService)sp.GetRequiredService<Services.NasbNiro.Finance.IGlService>());
            services.AddScoped<Services.NasbNiro.Finance.ISepidarFullExportService, Services.NasbNiro.Finance.SepidarFullExportService>();
            services.AddScoped<Services.NasbNiro.Finance.ITreasuryService, Services.NasbNiro.Finance.TreasuryService>();
            services.AddScoped<Services.NasbNiro.Finance.IReceivablesService, Services.NasbNiro.Finance.ReceivablesService>();
            services.AddScoped<Services.NasbNiro.Finance.ITaxService, Services.NasbNiro.Finance.TaxService>();
            services.AddScoped<Services.NasbNiro.Finance.IFinanceDashboardService, Services.NasbNiro.Finance.FinanceDashboardService>();
            services.AddHostedService<Services.NasbNiro.Finance.FinanceEventPostHostedService>();
            // release F2: fixed assets, payroll and insurance, Sepidar outbox
            services.AddScoped<Services.NasbNiro.Finance.IAssetService, Services.NasbNiro.Finance.AssetService>();
            services.AddScoped<Services.NasbNiro.Finance.IPayrollMasterService, Services.NasbNiro.Finance.PayrollMasterService>();
            services.AddScoped<Services.NasbNiro.Finance.IPayrollRunService, Services.NasbNiro.Finance.PayrollRunService>();
            services.AddScoped<Services.NasbNiro.Finance.ISepidarOutboxService, Services.NasbNiro.Finance.SepidarOutboxService>();
            services.AddSingleton<Services.NasbNiro.Finance.ISepidarClient, Services.NasbNiro.Finance.FileSepidarClient>();
            services.AddHostedService<Services.NasbNiro.Finance.FinanceF2HostedService>();
            services.AddScoped<Services.NasbNiro.Procurement.IProcurementService, Services.NasbNiro.Procurement.ProcurementService>();
            services.AddScoped<Services.NasbNiro.Procurement.IPayablesService, Services.NasbNiro.Procurement.PayablesService>();

            // quality module (QC + QA)
            services.AddScoped<Services.NasbNiro.Quality.IItpService, Services.NasbNiro.Quality.ItpService>();
            services.AddScoped<Services.NasbNiro.Quality.IInspectionService, Services.NasbNiro.Quality.InspectionService>();
            services.AddScoped<Services.NasbNiro.Quality.ICalibrationService, Services.NasbNiro.Quality.CalibrationService>();
            services.AddScoped<Services.NasbNiro.Quality.IPersonnelService, Services.NasbNiro.Quality.PersonnelService>();
            services.AddScoped<Services.NasbNiro.Quality.IMaterialService, Services.NasbNiro.Quality.MaterialService>();
            services.AddScoped<Services.NasbNiro.Quality.ICapaService, Services.NasbNiro.Quality.CapaService>();
            services.AddScoped<Services.NasbNiro.Quality.IReleaseService, Services.NasbNiro.Quality.ReleaseService>();
            services.AddScoped<Services.NasbNiro.Quality.IDocumentControlService, Services.NasbNiro.Quality.DocumentControlService>();
            services.AddScoped<Services.NasbNiro.Quality.IAuditReviewService, Services.NasbNiro.Quality.AuditReviewService>();
            services.AddScoped<Services.NasbNiro.Quality.IQualityDashboardService, Services.NasbNiro.Quality.QualityDashboardService>();
            services.AddScoped<IInventoryCodingService, InventoryCodingService>();
            services.AddScoped<Services.NasbNiro.ProjectControl.IPcProjectService, Services.NasbNiro.ProjectControl.PcProjectService>();
            services.AddScoped<Services.NasbNiro.ProjectControl.IPcProgressService, Services.NasbNiro.ProjectControl.PcProgressService>();
            services.AddScoped<Services.NasbNiro.ProjectControl.IPcCommercialService, Services.NasbNiro.ProjectControl.PcCommercialService>();
            services.AddScoped<Services.NasbNiro.ProjectControl.IPcMdrService, Services.NasbNiro.ProjectControl.PcMdrService>();
            services.AddScoped<Services.NasbNiro.ProjectControl.IPcScheduleService, Services.NasbNiro.ProjectControl.PcScheduleService>();
            services.AddScoped<Services.NasbNiro.ProjectControl.IPcBaselineService, Services.NasbNiro.ProjectControl.PcBaselineService>();
            services.AddScoped<Services.NasbNiro.ProjectControl.IPcReportService, Services.NasbNiro.ProjectControl.PcReportService>();
            services.AddHostedService<Services.NasbNiro.ProjectControl.PcSnapshotHostedService>();

            services.AddScoped<IInventoryQualityService, InventoryQualityService>();
            services.AddScoped<IInventoryWorkflowService, InventoryWorkflowService>();
            return services;
        }
    }
}