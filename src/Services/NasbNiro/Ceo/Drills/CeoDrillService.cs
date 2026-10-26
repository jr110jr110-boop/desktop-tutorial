using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.CeoDtos;

namespace Services.NasbNiro.Ceo.Drills
{
    public interface ICeoDrillService
    {
        /// <summary>The catalogue (CEO and PMO).</summary>
        Task<ResponseDto<List<DrillDescriptorDto>>> GetCatalogueAsync(CancellationToken ct);

        /// <summary>
        /// One drill. <paramref name="top"/> and <paramref name="page"/> are the raw texts (default 50 and 1; top at most 200); <paramref name="parameters"/> are the raw texts of the
        /// parameters of the drill (names without case). Wrong values are a Persian message, not an exception.
        /// </summary>
        Task<ResponseDto<DrillTableDto>> DrillAsync(string key, string top, string page, IReadOnlyDictionary<string, string> parameters, CancellationToken ct, int defaultTop = DrillBinder.DefaultTop);
    }

    /// <summary>
    /// Runs the drills of the catalogue as the signed-in user. The drills call the existing module services in this request scope, so the role checks and the row filters
    /// of the modules apply; the service itself only lets CEO and PMO in (some module services, such as the treasury, have no role check of their own).
    /// </summary>
    public class CeoDrillService : ICeoDrillService
    {
        private readonly IServiceProvider _services;
        private readonly IInventoryUserContext _user;

        public CeoDrillService(IServiceProvider services, IInventoryUserContext user)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);

        public Task<ResponseDto<List<DrillDescriptorDto>>> GetCatalogueAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(CeoDrillRoles.Readers)) return Task.FromResult(Bad<List<DrillDescriptorDto>>(CeoDrillText.NotAllowed));
            return Task.FromResult(ResponseDtoHelper.Ok(CeoDrillCatalog.All.Select(CeoDrillCatalog.Describe).ToList()));
        }

        public async Task<ResponseDto<DrillTableDto>> DrillAsync(string key, string top, string page, IReadOnlyDictionary<string, string> parameters, CancellationToken ct, int defaultTop = DrillBinder.DefaultTop)
        {
            if (!_user.IsInRole(CeoDrillRoles.Readers)) return Bad<DrillTableDto>(CeoDrillText.NotAllowed);
            if (DrillText.Clean(key) == null) return Bad<DrillTableDto>(CeoDrillText.KeyRequired);
            DrillDefinition definition = CeoDrillCatalog.Find(key);
            if (definition == null) return ResponseDtoHelper.NotFound<DrillTableDto>(CeoDrillText.UnknownKey);

            string error = DrillBinder.BindPaging(top, page, defaultTop, out int pageSize, out int pageNumber);
            if (error != null) return Bad<DrillTableDto>(error);
            DateTime today = DateTime.Today;
            error = DrillBinder.Bind(definition.Parameters, parameters, today, out DrillArgs args);
            if (error != null) return Bad<DrillTableDto>(error);

            DrillResult result;
            try
            {
                result = await definition.Run(new DrillContext(_services, _user, args, pageSize, pageNumber, today), ct);
            }
            catch (DrillFailureException ex)
            {
                return Bad<DrillTableDto>(ex.Message);
            }

            return ResponseDtoHelper.Ok(new DrillTableDto
            {
                Key = definition.Key,
                Title = definition.Title,
                AsOfText = DrillDates.Text(today),
                Columns = definition.Columns.Select(c => new DrillColumnDto { Key = c.Key, Title = c.Title, Kind = c.Kind }).ToList(),
                Rows = result.Rows ?? new List<Dictionary<string, object>>(),
                TotalCount = result.TotalCount,
                Page = pageNumber,
                PageSize = pageSize,
                Link = result.Link ?? definition.Link,
                Note = result.Note,
                Hidden = result.Hidden,
                Totals = result.Totals,
            });
        }
    }
}
