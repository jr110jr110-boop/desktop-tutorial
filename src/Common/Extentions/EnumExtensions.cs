using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Base
{
    /// <summary>
    /// برای مدیریت اینام ها
    /// </summary>
    public static class EnumExtensions
    {
        #region GetDisplayName
        /// <summary>
        /// در یافت نام فارسی اینام
        /// </summary>
        /// <param name="enu"></param>
        /// <returns></returns>
        public static string GetDisplayName(this Enum enu)
        {
            var attr = GetDisplayAttribute(enu);
            return attr != null ? attr.Name : enu.ToString();
        }
        #endregion
        public static string GetEnumDescription(Enum value)
        {
            return value.GetDisplayName();
        }
        #region persiannumber to en
        /// <summary>
        /// تبدیل تاریخ شمسی با فرمت 97/11/11 به میلادی  
        /// </summary>
        /// <param name="num"></param>
        /// <returns></returns>
        public static DateTime ToEnNumber(this string num)
        {
            var attr = num.Replace("۰", "0").Replace("۱", "1").Replace("۲", "2").Replace("۳", "3").Replace("۴", "4").Replace("۵", "5").Replace("۶", "6").Replace("۷", "7").Replace("۸", "8").Replace("۹", "9"); ;
            PersianDateTime persianDate = PersianDateTime.Parse(attr);
            DateTime miladiDate = persianDate.ToDateTime();
            return miladiDate;
        }

        #endregion
        /// <summary>
        /// تبدیل تاریخ میلادی به شمسی
        /// </summary>
        /// <param name="dt">تاریخ میلادی</param>
        /// <returns> 1396/06/12</returns>
        public static string ToPersianDigitalDateString(this DateTime dt)
        {
            var pd = new PersianDateTime(dt);
            return pd.ToString("yyyy/MM/dd");
        }

        #region GetDescription
        /// <summary>
        /// دریافت توضیح فارسی اینام
        /// </summary>
        /// <param name="enu"></param>
        /// <returns></returns>
        public static string GetDescription(this Enum enu)
        {
            var attr = GetDisplayAttribute(enu);
            return attr != null ? attr.Description : enu.ToString();
        }
        #endregion

        #region GetDisplayAttribute
        /// <summary>
        /// متد دریافت اتربیوت اینام
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static DisplayAttribute GetDisplayAttribute(object value)
        {
            Type type = value.GetType();
            if (!type.IsEnum)
            {
                throw new ArgumentException(string.Format("Type {0} is not an enum", type));
            }

            // Get the enum field.
            var field = type.GetField(value.ToString());
            //return field == null ? null : field.GetCustomAttributes(typeof(DisplayAttribute), true);
            if (field == null)
            {
                return null;
            }
            else
            {
                Object[] displayAttributes = field.GetCustomAttributes(typeof(DisplayAttribute), true);
                if (displayAttributes != null && displayAttributes.Length == 1)
                {
                    return ((DisplayAttribute)displayAttributes[0]);
                }

                return null;
            }
        }
        #endregion

        public static List<SelectListItem> ToSelectList<TEnum>() where TEnum : struct, IConvertible
        {
            return Enum.GetValues(typeof(TEnum)).Cast<TEnum>()
                .Select(@enum => new
                {
                    enumValue = GetValue(@enum),
                    enumText = GetDisplayName(@enum)
                })
                .Select(x => new SelectListItem
                {
                    Text = x.enumText,
                    Value = x.enumValue
                }
            ).ToList();
        }
        private static string GetValue<TEnum>(TEnum @enum) where TEnum : struct, IConvertible
        {
            return ((int)Enum.Parse(typeof(TEnum), @enum.ToString())).ToString();
        }
        private static string GetDisplayName<TEnum>(TEnum @enum) where TEnum : struct, IConvertible
        {
            return @enum.GetType().GetMember(@enum.ToString()).FirstOrDefault()?
                        .GetCustomAttribute<DisplayAttribute>(false)?.Name ?? @enum.ToString();
        }

        public static string EmptyIfNull(this object value)
        {
            if (value == null)
            {
                return "";
            }

            return value.ToString();
        }

    }
}