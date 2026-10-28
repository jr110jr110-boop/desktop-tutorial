using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Newtonsoft.Json.Serialization;
using ViewModels.NasbNiro.BuyOrderDtos;
using ViewModels.NasbNiro.ContractDtos;
using ViewModels.NasbNiro.InquiryDtos;
using ViewModels.NasbNiro.ProviderDtos;
using ViewModels.NasbNiro.ProvisionalStatementDtos;
using WebApi.Controllers;

namespace WebApi.Extensions
{
    /// <summary>
    /// R1a2 (M8): the sensitive fields of the legacy reads. The legacy controllers keep their reads open to every signed-in user (the card board, the
    /// dashboard and the lists need the records), but two kinds of value are only for the roles that work with them:
    /// <list type="bullet">
    /// <item><description>the bank accounts of a provider (account number, SHABA, card number): <see cref="LegacyRoles.ProviderBankReaders"/>; everybody else gets the number masked to its last four digits (<c>****1234</c>);</description></item>
    /// <item><description>the prices of the contract (<c>price</c>), of a provisional statement (<c>price</c>, <c>acceptedPrice</c>), of an inquiry (<c>price</c>) and the finance price of a buy order (<c>financePrice</c>): <see cref="LegacyRoles.PriceReaders"/>; everybody else gets <c>null</c>.</description></item>
    /// </list>
    /// The rule is applied where the answer is written (<see cref="LegacyFieldContractResolver"/>, part of the JSON settings of MVC), so it holds for every
    /// action that returns one of these DTOs, also when it sits inside another one (an inquiry carries its provider with the accounts, the dashboard carries buy orders),
    /// and the DTO objects themselves are not changed. Writers of these records are in the lists, so the answer to their own call is complete.
    /// </summary>
    public static class LegacyFieldAccess
    {
        public static bool InAny(ClaimsPrincipal user, string roles) =>
            user != null && roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(user.IsInRole);

        public static bool CanSeeBankAccounts(ClaimsPrincipal user) => InAny(user, LegacyRoles.ProviderBankReaders);

        public static bool CanSeePrices(ClaimsPrincipal user) => InAny(user, LegacyRoles.PriceReaders);

        /// <summary>The last four characters behind four stars; a value of four characters or fewer is hidden completely; nothing stays nothing.</summary>
        public static string Mask(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= 4 ? "****" : "****" + value.Substring(value.Length - 4);
        }

        /// <summary>The members of the legacy DTOs that are hidden from the roles outside the two lists (the unit test checks every entry against the DTO types).</summary>
        public static readonly IReadOnlyList<(Type Type, string[] Members, bool Bank)> Hidden = new List<(Type, string[], bool)>
        {
            (typeof(ResponseProviderAccountDto), new[] { nameof(ResponseProviderAccountDto.No), nameof(ResponseProviderAccountDto.SHABA), nameof(ResponseProviderAccountDto.Card) }, true),
            (typeof(ResponseContractDto), new[] { nameof(ResponseContractDto.Price) }, false),
            (typeof(ResponseProStatDto), new[] { nameof(ResponseProStatDto.Price), nameof(ResponseProStatDto.AcceptedPrice) }, false),
            (typeof(ResponseInquiryDto), new[] { nameof(ResponseInquiryDto.Price) }, false),
            (typeof(ResponseBuyOrderDto), new[] { nameof(ResponseBuyOrderDto.FinancePrice) }, false),
            (typeof(ResponseBuyOrderCardBoardDto), new[] { nameof(ResponseBuyOrderCardBoardDto.FinancePrice) }, false),
        };
    }

    /// <summary>
    /// The JSON contract of MVC with the rule of <see cref="LegacyFieldAccess"/> built in: a hidden member is read through a value provider that asks for the
    /// user of the current request every time, so the cached contract can be shared by all requests. With no user at all (no HTTP request) the members are hidden.
    /// </summary>
    public sealed class LegacyFieldContractResolver : DefaultContractResolver
    {
        private readonly Func<ClaimsPrincipal> _currentUser;

        public LegacyFieldContractResolver(Func<ClaimsPrincipal> currentUser)
        {
            _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
            NamingStrategy = new CamelCaseNamingStrategy(true, true, true);
        }

        protected override IList<JsonProperty> CreateProperties(Type type, Newtonsoft.Json.MemberSerialization memberSerialization)
        {
            IList<JsonProperty> properties = base.CreateProperties(type, memberSerialization);
            foreach (JsonProperty property in properties)
            {
                foreach ((Type hiddenType, string[] members, bool bank) in LegacyFieldAccess.Hidden)
                {
                    if (hiddenType.IsAssignableFrom(type) && members.Contains(property.UnderlyingName, StringComparer.Ordinal))
                    {
                        property.ValueProvider = new HidingValueProvider(property.ValueProvider, bank, _currentUser);
                        break;
                    }
                }
            }
            return properties;
        }

        private sealed class HidingValueProvider : IValueProvider
        {
            private readonly IValueProvider _inner;
            private readonly bool _bank;
            private readonly Func<ClaimsPrincipal> _currentUser;

            public HidingValueProvider(IValueProvider inner, bool bank, Func<ClaimsPrincipal> currentUser)
            {
                _inner = inner;
                _bank = bank;
                _currentUser = currentUser;
            }

            public object GetValue(object target)
            {
                object value = _inner.GetValue(target);
                if (value == null) return null;
                ClaimsPrincipal user = _currentUser();
                if (_bank ? LegacyFieldAccess.CanSeeBankAccounts(user) : LegacyFieldAccess.CanSeePrices(user)) return value;
                return _bank ? LegacyFieldAccess.Mask(value as string) : null;
            }

            public void SetValue(object target, object value) => _inner.SetValue(target, value);
        }
    }
}
