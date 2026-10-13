using Common.GuardToolkit;
using Microsoft.Extensions.Logging;

namespace Common.Extentions
{
    public static class StringExtensions
    {
        public static string LogInfo<T>(this string info, ILogger<T> logger)
        {
            info.ThrowIfNull(nameof(info));
            logger.ThrowIfNull(nameof(logger));

            logger.LogInformation(info);

            return info;
        }

        public static string EndWithIfNot(this string str, char ch)
        {
            str.ThrowIfNull(nameof(str));

            if (str.EndsWith($"{ch}"))
            {
                return str;
            }

            return str + ch;
        }
    }
}
