using System.ComponentModel.DataAnnotations;

namespace Entities.Model
{
    public enum LogType
    {
        [Display(Name = "نمایندگی ها و شعب")]
        unit = 1,
        [Display(Name = "مراکز درمانی")]
        clinic = 2,
        [Display(Name = "بیمه نامه ها")]
        Insurance = 3,
        [Display(Name = "پرداخت ها")]
        pay = 3,
        [Display(Name = "درخواست ها")]
        request = 4,
        [Display(Name = "پیام ها")]
        message = 5,
        [Display(Name = "محل شعب")]
        UnitLocation = 6,
        [Display(Name = "اطلاعات بیمه")]
        MainInsuranceInfo = 7,
        [Display(Name = "اعلام خسارت")]
        NotificationOfDamages = 8,
    }

    public enum LoginType
    {
        [Display(Name = "با استفاده از پسورد")]
        UsingThePassword = 1,
        [Display(Name = "با استفاده از پیامک متنی")]
        UsingTheSMS = 2
    }

    public enum PasswordScore
    {
        Blank = 0,//جای خالی
        VeryWeak = 1,//خیلی ضعیف
        Weak = 2,//ضعیف
        Medium = 3,//متوسط
        Strong = 4,//قوی
        VeryStrong = 5//بسیار قوی
    }
}

