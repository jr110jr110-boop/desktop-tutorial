using System;
using System.Threading.Tasks;
using DNTPersianUtils.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Common.WebToolkit
{
    /// <summary>
    /// Persian Ye/Ke model binder: replaces Arabic ي/ك with Persian ی/ک in bound string values.
    /// Local copy of the binder that used to come from DNTCommon.Web.Core, with the same behaviour
    /// (including binding simple-type parameters from route/query values).
    /// </summary>
    public class YeKeModelBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null)
            {
                throw new ArgumentNullException(nameof(bindingContext));
            }

            var loggerFactory = bindingContext.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
            var fallbackBinder = new SimpleTypeModelBinder(bindingContext.ModelType, loggerFactory);

            var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
            if (valueProviderResult == ValueProviderResult.None)
            {
                return fallbackBinder.BindModelAsync(bindingContext);
            }

            bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueProviderResult);

            var valueAsString = valueProviderResult.FirstValue;
            if (string.IsNullOrWhiteSpace(valueAsString))
            {
                return fallbackBinder.BindModelAsync(bindingContext);
            }

            var model = valueAsString.ApplyCorrectYeKe();
            bindingContext.Result = ModelBindingResult.Success(model);
            return Task.CompletedTask;
        }
    }

    public class YeKeModelBinderProvider : IModelBinderProvider
    {
        public IModelBinder GetBinder(ModelBinderProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.Metadata.IsComplexType)
            {
                return null;
            }

            if (context.Metadata.ModelType == typeof(string))
            {
                return new YeKeModelBinder();
            }

            var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
            return new SimpleTypeModelBinder(context.Metadata.ModelType, loggerFactory);
        }
    }

    public static class YeKeModelBinderExtensions
    {
        /// <summary>
        /// Inserts YeKeModelBinderProvider at the top of the MvcOptions.ModelBinderProviders list.
        /// </summary>
        public static MvcOptions UseYeKeModelBinder(this MvcOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.ModelBinderProviders.Insert(0, new YeKeModelBinderProvider());
            return options;
        }
    }
}
