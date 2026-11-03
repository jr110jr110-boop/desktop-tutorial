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

        // ---- R1a2: the second review (independent approvers, reset requests, expiring temporary passwords, one-use refresh tokens)
        public const string TemporaryPasswordExpired = "مهلت رمز موقت شما تمام شده است؛ از یک مدیر (PMO) بخواهید رمز موقت تازه‌ای بدهد.";
        public const string TemporaryPasswordExpiredCode = "TEMPORARY_PASSWORD_EXPIRED";
        public const string RefreshTokenReused = "این توکن تجدید قبلاً استفاده شده بود؛ برای امنیت همه‌ی نشست‌های این کاربر بسته شد. دوباره وارد شوید.";
        public const string GrantApproverGrantedByRequester = "نقش PMO تأییدکننده به درخواست خود همین درخواست‌کننده داده شده است؛ مدیر (PMO) مستقل دیگری باید تأیید کند.";
        public const string GrantApproverTooNew = "تأییدکننده هنوز به‌اندازه‌ی کافی (چند روز) نقش PMO را نداشته است؛ پس از گذشت این مدت می‌تواند تأیید کند.";
        public const string GrantVoid = "این درخواست دیگر معتبر نیست (کاربر غیرفعال شده یا درخواست‌کننده دیگر PMO نیست) و کنار گذاشته شد.";
        public const string GrantWithdrawnByEdit = "درخواست نقش حساسی که در ویرایش نیامده بود پس گرفته شد: ";
        public const string PmoBootstrapApplied = "نقش PMO بدون تأیید دوم داده شد، چون هنوز دو مدیر (PMO) فعال وجود ندارد؛ این کار در پرونده‌ی ممیزی ثبت و به مدیرعامل اعلام شد. از این پس هر نقش PMO تأیید مدیر دیگری می‌خواهد.";
        public const string ResetNeedsApproval = "رمز این کاربر حساس است؛ درخواست بازنشانی ثبت شد و پس از تأیید یک مدیر (PMO) دیگر انجام می‌شود.";
        public const string ResetAlreadyPending = "برای این کاربر از قبل درخواست بازنشانی رمز عبور ثبت شده و منتظر تأیید است.";
        public const string ResetNoApprover = "بازنشانی رمز یک کاربر حساس دست‌کم یک مدیر (PMO) دیگر لازم دارد، غیر از شما و غیر از خود آن کاربر؛ ابتدا یک PMO سوم تعریف کنید.";
        public const string ResetApproved = "بازنشانی رمز عبور تأیید شد؛ رمز موقت را به کاربر برسانید (فقط همین یک بار نشان داده می‌شود).";
        public const string ResetRequestedInbox = "درخواست بازنشانی رمز عبور حساب شما ثبت شد و منتظر تأیید یک مدیر (PMO) دیگر است. اگر شما چنین چیزی نخواسته‌اید، همین حالا به مدیر امنیت خبر دهید.";
        public const string ResetDoneInbox = "رمز عبور حساب شما با تأیید دو مدیر بازنشانی شد؛ در ورود بعدی باید رمز جدید بگذارید.";
        public const string BootstrapInbox = "نقش PMO بدون تأیید دوم به یک کاربر داده شد (راه‌اندازی مدیر دوم): ";

        // ---- R1a2, review fixes: removing the PMO role / deleting a PMO user is a request (M-1), the receiver of a sensitive role chooses his own password first (M-2)
        public const string RevokePending = "برداشتن نقش PMO پس از تأیید یک مدیر (PMO) دیگر انجام می‌شود: ";
        public const string RevokeAlreadyPending = "برای برداشتن نقش PMO این کاربر از قبل درخواست ثبت شده و منتظر تأیید است.";
        public const string RevokeNoApprover = "برداشتن نقش PMO یا حذف یک مدیر (PMO) دست‌کم یک مدیر (PMO) دیگر لازم دارد، غیر از شما و غیر از خود آن کاربر؛ ابتدا یک PMO دیگر تعریف کنید (یا خود کاربر نقش را از خودش بردارد).";
        public const string RevokeWithdrawnByEdit = "درخواست برداشتن نقش PMO که در ویرایش دیگر نیامده بود پس گرفته شد: ";
        public const string DeleteNeedsApproval = "حذف یک مدیر (PMO) پس از تأیید یک مدیر (PMO) دیگر انجام می‌شود؛ درخواست حذف ثبت شد.";
        public const string DeleteAlreadyPending = "برای حذف این مدیر (PMO) از قبل درخواست ثبت شده و منتظر تأیید است.";
        public const string RevokeApproved = "درخواست تأیید و انجام شد.";
        public const string RevokeRequestedInbox = "برای حساب شما درخواست برداشتن نقش PMO (یا حذف حساب) ثبت شد و منتظر تأیید یک مدیر (PMO) دیگر است. اگر شما چنین چیزی نخواسته‌اید، همین حالا به مدیر امنیت خبر دهید.";
        public const string LastPmoCannotStepDown = "شما آخرین مدیر (PMO) فعال هستید؛ پیش از کنار رفتن باید مدیر (PMO) فعال دیگری وجود داشته باشد.";
        public const string SteppedDown = "نقش PMO از خودتان برداشته شد (در پرونده‌ی ممیزی ثبت شد)؛ از این پس مدیریت کاربران برای شما بسته است و باید دوباره وارد شوید.";
        public const string CannotRejectOwnRequest = "درخواستی که درباره‌ی حساب خودتان است (بازنشانی رمز عبور، برداشتن نقش یا حذف) را خودتان نمی‌توانید رد کنید؛ مدیر (PMO) دیگری باید این کار را بکند.";
        public const string GrantReceiverMustChangeOwnPassword = "گیرنده‌ی نقش هنوز رمز عبور خودش را عوض نکرده است و مدیری که او را ثبت کرده یا رمزش را بازنشانی کرده آن رمز را می‌داند؛ تا گیرنده با «تغییر رمز عبور» (با وارد کردن رمز فعلی) رمز تازه‌ی خودش را نگذارد، این نقش حساس تأیید نمی‌شود.";
        public const string GrantNeedsOwnPasswordInbox = "برای حساب شما یک نقش حساس درخواست شده است؛ تا وقتی رمز عبور خودتان را با «تغییر رمز عبور» (با وارد کردن رمز فعلی) عوض نکنید، این نقش تأیید نمی‌شود. اگر شما چنین چیزی نخواسته‌اید، به مدیر امنیت خبر دهید.";
    }
}
