using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Common.GuardToolkit;
using Common.PersianToolkit;
using DataLayer.Configurations.EF;
using DataLayer.Configurations.EF.Identity;
using DataLayer.Configurations.EF.NasbNiro;
using DataLayer.Mappings;
using Entities.AuditableEntity;
using Entities.Identity;
using Entities.Model;
using Entities.Model.NasbNiro.ContractEquipments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.Reports;
using Entities.Model.NasbNiro.Tenders;
using Entities.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ViewModels.Identity.Settings;

namespace DataLayer.Context
{
    public class ApplicationDbContext :
        IdentityDbContext<User, Role, int, UserClaim, UserRole, UserLogin, RoleClaim, UserToken>,
        IUnitOfWork
    {

        // we can't use constructor injection anymore, because we are using the `AddDbContextPool<>`
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
            // EF Core 3+ applies cascade deletes immediately when an entity is marked as Deleted.
            // Keep the EF Core 2.2 behaviour (cascade at SaveChanges) that this code was written for.
            ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
            ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
        }

        public virtual DbSet<HolidaysDayOfWeek> HolidaysByEnum { get; set; }
        public virtual DbSet<HolidaysByDate> HolidaysByDate { get; set; }

        #region NasbNiro
        public virtual DbSet<Tender> Tenders { get; set; }
        public virtual DbSet<TenderFile> TenderFiles { get; set; }
        public virtual DbSet<TenderGuaranty> TenderGuaranties { get; set; }
        public virtual DbSet<Provider> Providers { get; set; }
        public virtual DbSet<ProviderContact> ProviderContacts { get; set; }
        public virtual DbSet<ProviderAccount> ProviderAccounts { get; set; }
        public virtual DbSet<ContractEntity> Contracts { get; set; }
        public virtual DbSet<ContractGuaranty> ContractGuaranties { get; set; }
        public virtual DbSet<ProvisionalStatement> ProvisionalStatements { get; set; }
        public virtual DbSet<ContractFile> ContractFiles { get; set; }
        public virtual DbSet<ContractEquipment> ContractEquipments { get; set; }
        public virtual DbSet<Equipment> Equipments { get; set; }
        public virtual DbSet<EquipmentAssembly> EquipmentAssembly { get; set; }
        public virtual DbSet<EquipmentBom> EquipmentBoms { get; set; }
        public virtual DbSet<EquipmentProcess> EquipmentProcesses { get; set; }
        public virtual DbSet<EquipmentProcessFile> EquipmentProcessFiles { get; set; }
        public virtual DbSet<AssemblyEntity> Assemblies { get; set; }
        public virtual DbSet<Bom> Boms { get; set; }
        public virtual DbSet<ProcessEntity> Processes { get; set; }
        public virtual DbSet<ProcessFile> ProcessFiles { get; set; }
        public virtual DbSet<BuyOrder> BuyOrders { get; set; }
        public virtual DbSet<BuyOrderFile> BuyOrderFiles { get; set; }
        public virtual DbSet<BuyOrderProcesses> BuyOrderProcesses { get; set; }
        public virtual DbSet<Inquiry> Inquiries { get; set; }
        public virtual DbSet<WorkOrder> WorkOrders { get; set; }
        public virtual DbSet<WorkOrderHistory> WorkOrderHistories { get; set; }
        public virtual DbSet<ProgressReport> ProgressReports { get; set; }
        public virtual DbSet<ProviderCategory> Categories { get; set; }
        #endregion

        #region BaseClass

        public virtual DbSet<AppLogItem> AppLogItems { get; set; }
        public virtual DbSet<AppSqlCache> AppSqlCache { get; set; }
        public virtual DbSet<AppDataProtectionKey> AppDataProtectionKeys { get; set; }
        public virtual DbSet<Document> Documents { get; set; }

        public virtual DbSet<UserProfile> UserProfiles { get; set; }
        public virtual DbSet<Address> Addresses { get; set; }
        public virtual DbSet<City> Cities { get; set; }
        public virtual DbSet<Country> Countries { get; set; }
        public virtual DbSet<Province> Provinces { get; set; }
        public virtual DbSet<Section> Sections { get; set; }
        public virtual DbSet<AdminSetting> AdminSettings { get; set; }

        public void AddRange<TEntity>(IEnumerable<TEntity> entities) where TEntity : class
        {
            Set<TEntity>().AddRange(entities);
        }

        // EF Core 3+ renamed Database.ExecuteSqlCommand to ExecuteSqlRaw (same {0}, {1} parameter placeholders).
        public void ExecuteSqlCommand(string query)
        {
            Database.ExecuteSqlRaw(query);
        }

        public void ExecuteSqlCommand(string query, params object[] parameters)
        {
            Database.ExecuteSqlRaw(query, parameters);
        }

        public T GetShadowPropertyValue<T>(object entity, string propertyName) where T : IConvertible
        {
            var value = this.Entry(entity).Property(propertyName).CurrentValue;
            return value != null
                ? (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture)
                : default(T);
        }

        public object GetShadowPropertyValue(object entity, string propertyName)
        {
            return this.Entry(entity).Property(propertyName).CurrentValue;
        }

        public void MarkAsChanged<TEntity>(TEntity entity) where TEntity : class
        {
            Update(entity);
        }

        public void RemoveRange<TEntity>(IEnumerable<TEntity> entities) where TEntity : class
        {
            Set<TEntity>().RemoveRange(entities);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ChangeTracker.DetectChanges();

            beforeSaveTriggers();

            ChangeTracker.AutoDetectChangesEnabled = false; // for performance reasons, to avoid calling DetectChanges() again.
            var result = base.SaveChanges(acceptAllChangesOnSuccess);
            ChangeTracker.AutoDetectChangesEnabled = true;
            return result;
        }

        public override int SaveChanges()
        {
            ChangeTracker.DetectChanges();

            beforeSaveTriggers();

            ChangeTracker.AutoDetectChangesEnabled = false; // for performance reasons, to avoid calling DetectChanges() again.
            var result = base.SaveChanges();
            ChangeTracker.AutoDetectChangesEnabled = true;
            return result;
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            ChangeTracker.DetectChanges();

            beforeSaveTriggers();

            ChangeTracker.AutoDetectChangesEnabled = false; // for performance reasons, to avoid calling DetectChanges() again.
            var result = base.SaveChangesAsync(cancellationToken);
            ChangeTracker.AutoDetectChangesEnabled = true;
            return result;
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = new CancellationToken())
        {
            ChangeTracker.DetectChanges();

            beforeSaveTriggers();

            ChangeTracker.AutoDetectChangesEnabled = false; // for performance reasons, to avoid calling DetectChanges() again.
            var result = base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            ChangeTracker.AutoDetectChangesEnabled = true;
            return result;
        }

        private void beforeSaveTriggers()
        {
            validateEntities();
            setShadowProperties();
            this.ApplyCorrectYeKe();
        }

        private void setShadowProperties()
        {
            // we can't use constructor injection anymore, because we are using the `AddDbContextPool<>`
            var httpContextAccessor = this.GetService<IHttpContextAccessor>();
            httpContextAccessor.ThrowIfNull(nameof(httpContextAccessor));
            ChangeTracker.SetAuditableEntityPropertyValues(httpContextAccessor);
        }

        private void validateEntities()
        {
            var errors = this.GetValidationErrors();
            if (!string.IsNullOrWhiteSpace(errors))
            {
                // we can't use constructor injection anymore, because we are using the `AddDbContextPool<>`
                var loggerFactory = this.GetService<ILoggerFactory>();
                loggerFactory.ThrowIfNull(nameof(loggerFactory));
                var logger = loggerFactory.CreateLogger<ApplicationDbContext>();
                logger.LogError(errors);
                throw new InvalidOperationException(errors);
            }
        }

        #endregion

        #region Scalar Functions
        [DbFunction("CalculateAssemblyProgressLevel", "dbo")]
        public static decimal? CalculateAssemblyProgressLevel(int assemblyId)
        {
            throw new NotImplementedException();
        }

        [DbFunction("GetAssemblyDoneBomsCount", "dbo")]
        public static int? GetAssemblyDoneBomsCount(int assemblyId)
        {
            throw new NotImplementedException();
        }

        [DbFunction("GetAssemblyBomsCount", "dbo")]
        public static int? GetAssemblyBomsCount(int assemblyId)
        {
            throw new NotImplementedException();
        }

        [DbFunction("CalculateBomProgressLevel", "dbo")]
        public static decimal? CalculateBomProgressLevel(int bomId)
        {
            throw new NotImplementedException();
        }

        [DbFunction("GetBomDoneProcessesCount", "dbo")]
        public static int? GetBomDoneProcessesCount(int bomId)
        {
            throw new NotImplementedException();
        }

        [DbFunction("GetBomProcessesCount", "dbo")]
        public static int? GetBomProcessesCount(int bomId)
        {
            throw new NotImplementedException();
        }

        [DbFunction("CalculateWorkOrderProgressLevel", "dbo")]
        public static decimal? CalculateWorkOrderProgressLevel(int workOrderId)
        {
            throw new NotImplementedException();
        }

        [DbFunction("GetWorkOrderDoneAssembliesCount", "dbo")]
        public static int? GetWorkOrderDoneAssembliesCount(int workOrderId)
        {
            throw new NotImplementedException();
        }

        [DbFunction("GetWorkOrderAssembliesCount", "dbo")]
        public static int? GetWorkOrderAssembliesCount(int workOrderId)
        {
            throw new NotImplementedException();
        }
        #endregion

        protected override void OnModelCreating(ModelBuilder builder)
        {
            // it should be placed here, otherwise it will rewrite the following settings!
            base.OnModelCreating(builder);
            // we can't use constructor injection anymore, because we are using the `AddDbContextPool<>`
            var siteSettings = this.GetService<IOptionsSnapshot<SiteSettings>>();
            siteSettings.ThrowIfNull(nameof(siteSettings));
            siteSettings.Value.ThrowIfNull(nameof(siteSettings.Value));
            // Adds all of the ASP.NET Core Identity related mappings at once.

            builder.AddCustomIdentityMappings(siteSettings.Value);

            builder.ApplyConfiguration(new SetHolidaysByDateConfigurations());
            builder.ApplyConfiguration(new SetHolidaysByEnumConfigurations());

            builder.ApplyConfiguration(new SetAppRoleClaimConfigurations());
            builder.ApplyConfiguration(new SetUserRoleConfigurations());

            builder.ApplyConfiguration(new SetTenderConfigurations());
            builder.ApplyConfiguration(new SetTenderFileConfigurations());
            builder.ApplyConfiguration(new SetTenderGuarantyConfigurations());
            builder.ApplyConfiguration(new SetProviderConfigurations());
            builder.ApplyConfiguration(new SetProviderContactConfigurations());
            builder.ApplyConfiguration(new SetProviderAccountConfigurations());

            builder.ApplyConfiguration(new SetContractConfigurations());
            builder.ApplyConfiguration(new SetContractGuarantyConfigurations());
            builder.ApplyConfiguration(new SetContractFileConfigurations());
            builder.ApplyConfiguration(new SetEquipmentAssemblyConfigurations());
            builder.ApplyConfiguration(new SetEquipmentConfigurations());
            builder.ApplyConfiguration(new SetContractEquipmentConfigurations());
            builder.ApplyConfiguration(new SetEquipmentBomConfigurations());
            builder.ApplyConfiguration(new SetEquipmentProcessConfigurations());
            builder.ApplyConfiguration(new SetAssemblyConfigurations());
            builder.ApplyConfiguration(new SetBomConfigurations());

            builder.ApplyConfiguration(new SetProcessConfigurations());
            builder.ApplyConfiguration(new SetProcessFileConfigurations());

            builder.ApplyConfiguration(new SetBuyOrderConfigurations());
            builder.ApplyConfiguration(new SetBuyOrderFileConfigurations());
            builder.ApplyConfiguration(new SetBuyOrderProcessesConfigurations());

            builder.ApplyConfiguration(new SetInquiryConfigurations());
            builder.ApplyConfiguration(new SetEquipmentProcessFileConfigurations());

            builder.ApplyConfiguration(new SetWorkOrderHistoryConfigurations());
            builder.ApplyConfiguration(new SetProgressReportsConfigurations());
            builder.ApplyConfiguration(new SetProviderCategoryConfigurations());

            // This should be placed here, at the end.
            builder.AddAuditableShadowProperties();
        }
    }
}