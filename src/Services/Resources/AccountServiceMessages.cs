namespace Services.Resources
{
    public static class AccountServiceMessages
    {
        public const string NullDataReceived = "داده نامعتبر است";
        public const string PasswordAndConfirmPasswordNotMatched = "عدم تطابق رمزعبور و تایید رمزعبور";
        public const string UserNameIsUsedBefore = "نام کاربری قبلا استفاده شده است.";
        public const string InvalidRoleDetected = "نقش نامعتبر";
        public const string UserNotFound = "کاربری یافت نشد.";
        public const string AccountIsNotActive = "این حساب کاربری غیر فعال است.";
        public const string AccountIsDeleted = "این حساب کاربری حذف شده است.";
        public const string InvalidLoginData = "نام کاربری یا رمزعبور معتبر نیست";
        public const string AccountIsLockedOut = "این حساب کاربری تعلیق شده است.";
        public const string LogedInSuccessfully = "با موفقیت وارد شدید.";
        public const string LogedOutSuccessfully = "با موفقیت خارج شدید.";
        public const string RegisteredSuccessfully = "با موفقیت خارج شدید.";
        public const string InvalidRefreshToken = "توکن تجدید نامعتبر است.";
        public const string FailedToRegisterUser = "ثبت اکانت جدید شکست خورد.";
        public const string FailedToAssignRole = "تخصیص نقش شکست خورد";
        public const string WeakPasswordDetected = "رمز واردشده ضعیف است";
        public const string ChangePasswordFailed = "تغییر رمزعبور شکست خورد";
        public const string RolesChangedSuccessfully = "نقش ها با موفقیت تغییر کرد";

        // ---- R1a: user administration (PMO only), passwords, two-person approval of sensitive roles
        public const string OnlyPmo = "مدیریت کاربران فقط برای مدیریت (PMO) مجاز است.";
        public const string NotSignedIn = "ابتدا وارد سامانه شوید.";
        public const string CannotChangeOwnRoles = "تغییر نقش‌های خودتان مجاز نیست؛ مدیر دیگری باید این کار را انجام دهد.";
        public const string CannotDeleteSelf = "حذف حساب کاربری خودتان مجاز نیست.";
        public const string OnlyOwnProfile = "فقط اطلاعات و عکس خودتان را می‌توانید ببینید یا تغییر دهید.";
        public const string OnlyOwnPassword = "فقط رمز عبور خودتان را می‌توانید تغییر دهید؛ برای کاربران دیگر از «بازنشانی رمز عبور توسط مدیر» استفاده کنید.";
        public const string CurrentPasswordRequired = "رمز عبور فعلی را وارد کنید.";
        public const string CurrentPasswordWrong = "رمز عبور فعلی درست نیست.";
        public const string NewPasswordSameAsCurrent = "رمز عبور جدید باید با رمز فعلی فرق داشته باشد.";
        public const string PasswordChangedSuccessfully = "رمز عبور تغییر کرد.";
        public const string CannotResetOwnPassword = "رمز عبور خودتان را با «تغییر رمز عبور» (با وارد کردن رمز فعلی) عوض کنید.";
        public const string PasswordResetSuccessfully = "رمز موقت تنظیم شد؛ کاربر باید در ورود بعدی رمز جدید بگذارد.";
        public const string MustChangePassword = "رمز عبور شما موقت است و باید تغییر کند. رمز جدید را در صفحه‌ی «تغییر رمز موقت» وارد کنید.";
        public const string MustChangePasswordCode = "MUST_CHANGE_PASSWORD";
        public const string NoTemporaryPassword = "برای این حساب رمز موقتی تعریف نشده است؛ از «تغییر رمز عبور» استفاده کنید.";
        public const string SensitiveRolePending = "نقش‌های حساس پس از تأیید یک مدیر (PMO) دیگر به کاربر داده می‌شود: ";
        public const string SensitiveRoleAlreadyPending = "برای این نقش از قبل درخواست تأیید ثبت شده است: ";
        public const string GrantNotFound = "درخواست نقش پیدا نشد (ممکن است قبلاً تأیید، رد یا منقضی شده باشد).";
        public const string GrantSameApprover = "درخواست‌کننده نمی‌تواند خودش آن را تأیید کند؛ مدیر (PMO) دیگری باید تأیید کند.";
        public const string GrantApproverIsTarget = "کاربری که نقش به او داده می‌شود نمی‌تواند خودش آن را تأیید کند.";
        public const string GrantExpired = "مهلت تأیید این درخواست گذشته است؛ درخواست جدید ثبت کنید.";
        public const string GrantApproved = "نقش تأیید و به کاربر داده شد.";
        public const string GrantRejected = "درخواست نقش رد شد.";
        public const string AccountRefreshForbidden = "این حساب کاربری اجازه‌ی تمدید ورود ندارد.";
    }
}
