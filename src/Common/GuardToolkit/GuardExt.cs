using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Extentions;
using DNTPersianUtils.Core;

namespace Common.GuardToolkit
{
    public static class GuardExt
    {
        public static async Task<(string ErrorMessage, T Result)> RunSafeAsync<T>(
            this Func<Task<T>> operation,
            Action<Exception> exceptionHandler = null,
            CancellationToken cancellationToken = default)
        {
            T result = default;
            string errorMessage = string.Empty;

            try
            {
                result = await operation()
                    .WithCancellationTokenAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                if (exceptionHandler != null)
                {
                    exceptionHandler(ex);
                }

                errorMessage = ex.Message;
            }

            return (errorMessage, result);
        }

        public static T ThrowIfNull<T>(this T data, string name)
        {
            if (data == null)
                throw new ArgumentNullException(name);

            return data;
        }

        public static DateTimeOffset OnlyDateNotHoursUtc(this DateTimeOffset dateTime)
        {
            dateTime.ToUniversalTime();

            return new DateTimeOffset(dateTime.Year, dateTime.Month, dateTime.Day, 0, 0, 0, dateTime.Offset);
        }


        /// <summary>
        /// Checks if the parameter is null.
        /// </summary>
        public static void CheckMandatoryOption(this string s, string name)
        {
            if (string.IsNullOrEmpty(s))
                throw new ArgumentException(name);
        }

        public static bool ContainsNumber(this string inputText)
        {
            return !string.IsNullOrWhiteSpace(inputText) && inputText.ToEnglishNumbers().Any(char.IsDigit);
        }

        public static bool HasConsecutiveChars(this string inputText, int sequenceLength = 4)
        {
            var charEnumerator = StringInfo.GetTextElementEnumerator(inputText);
            var currentElement = string.Empty;
            var count = 1;
            while (charEnumerator.MoveNext())
            {
                if (currentElement == charEnumerator.GetTextElement())
                {
                    if (++count >= sequenceLength)
                    {
                        return true;
                    }
                }
                else
                {
                    count = 1;
                    currentElement = charEnumerator.GetTextElement();
                }
            }
            return false;
        }

        public static bool IsEmailAddress(this string inputText)
        {
            return !string.IsNullOrWhiteSpace(inputText) && new EmailAddressAttribute().IsValid(inputText);
        }

        public static bool IsNumeric(this string inputText)
        {
            if (string.IsNullOrWhiteSpace(inputText)) return false;

            long inputNumber;
            return long.TryParse(inputText.ToEnglishNumbers(), out inputNumber);
        }
    }
}
