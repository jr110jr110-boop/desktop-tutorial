<div dir="rtl">

# ارتقای INNCO API از ASP.NET Core 2.2 به ‎.NET 10

**تاریخ:** ۲۱ مهر ۱۴۰۵ (13 October 2026)

**وضعیت:** پروژه روی ‎.NET 10 بدون خطا Build می‌شود و اجرای آزمایشی آن روی SQL Server موفق بود. Build و آزمایش روی سرورهای GitHub (GitHub Actions) انجام شد، چون SDK دات‌نت و NuGet در محیط Claude در دسترس نیست. نتیجه‌ها در بخش ۱ آمده است. هنوز با داده واقعی شما آزمایش نشده است.

## خلاصه

- هر ۸ پروژه (Common، Entities، ViewModels، DataLayer، Services، IocConfig، WebApi و MsTests) روی `net10.0` و زبان C# 14 هستند.
- نسخه همه بسته‌ها در یک فایل آمده است: `Directory.Packages.props`. بسته‌های مایکروسافت روی 10.0.12 هستند، آخرین نسخه پایدار (۸ سپتامبر ۲۰۲۶).
- بسته‌های منسوخ یا ناسازگار حذف شدند. هر جا لازم بود، با کد محلی جایگزین شدند (بخش ۹).
- رفتار API برای فرانت‌اند ثابت مانده است: آدرس‌ها (همه با `api/v1` شروع می‌شوند)، JSON با camelCase (همان Newtonsoft.Json)، توکن JWT و CORS.
- ایرادهایی که در ‎.NET 10 به خطا می‌رسید رفع شد:
  - پیش از Build: تغییر و بازیابی رمز عبور، داده اولیه نقش‌ها، حلقه بی‌پایان احتمالی در لاگر دیتابیس
  - پس از Build و اجرای واقعی: یک خطای کامپایل در جستجو، از کار افتادن برنامه هنگام شروع به خاطر AutoMapper، ترتیب CORS، و سه کوئری گزارش پیشرفت

## ۱. نتیجه آزمایش

| آزمایش | نتیجه |
|---|---|
| Build روی ویندوز با SDK 10.0.401 | بدون خطا. ۷۰ هشدار که همه قابل انتظار هستند (بخش ۳) |
| تست‌های پروژه MsTests | ۷ از ۷ موفق |
| مقایسه مدل EF Core 10 با snapshot مایگریشن‌ها | مایگریشن آزمایشی `Net10ModelCheck` خالی بود. یعنی EF Core 10 همان ساختار جدول‌ها را می‌بیند |
| اجرای برنامه روی SQL Server 2022 با دیتابیس خالی | هر ۸۴ مایگریشن از صفر اجرا شد، کاربر ادمین ساخته شد، برنامه بالا آمد و Swagger کار کرد |
| ورود، تمدید توکن و خروج | هر سه موفق |
| ۶۲ آدرس GET، یک بار بدون و یک بار با متن جستجو | ۵۷ آدرس بدون خطا. ۲ آدرس پاسخ 400 دادند چون پارامتر الزامی نداشتند، که درست است. ۳ آدرس به خاطر ایرادهای قدیمی کد با پارامتر خالی خطا دادند (بخش ۸، بند ۱۱) |
| کوئری بزرگ گزارش پیشرفت (`AddProgressReportAsync`) | روی SQL Server بدون خطا ترجمه و اجرا شد |

**آزمایش‌نشده:**
- عملیات‌هایی که داده واقعی لازم دارند: ثبت و ویرایش، تغییر وضعیت فرایندها، سفارش خرید و مانند آن
- اتصال به دیتابیس واقعی شما

این‌ها را در بخش ۸ ببینید.

**محل آزمایش:** شاخه‌های `innco-net10-build` (کد بدون رمزهای عبور و فایل آزمایش)، `innco-ci-results` و `innco-ci-smoke` (گزارش‌ها) در مخزن خصوصی desktop-tutorial شما. بعد از پایان کار می‌توان آن‌ها را پاک کرد.

## ۲. پیش‌نیازها

| مورد | توضیح |
|---|---|
| ‎.NET 10 SDK | هر SDK نسخه ۱۰. پیشنهاد: آخرین نسخه (هنگام نوشتن این راهنما 10.0.401). خروجی `dotnet --version` باید با 10 شروع شود. |
| محیط توسعه | Visual Studio 2026 (نسخه ۱۸) که SDK دات‌نت ۱۰ را همراه دارد، یا VS Code با افزونه C# Dev Kit. نسخه‌های قدیمی‌تر Visual Studio ممکن است پروژه `net10.0` را باز نکنند. |
| SQL Server | همان نسخه فعلی. نکته نسخه‌های قدیمی در بخش ۸ آمده است. |
| IIS (روی سرور) | ASP.NET Core 10.0 Hosting Bundle |
| گواهی HTTPS محلی | فقط یک بار اجرا کنید: `dotnet dev-certs https --trust` |

> ⚠️ **قبل از اولین اجرا روی دیتابیس واقعی، از دیتابیس IRNNCO بکاپ کامل بگیرید.** برنامه هنگام شروع، مایگریشن‌های اعمال‌نشده را خودکار روی دیتابیس اجرا می‌کند. این رفتار از قبل در برنامه بوده است.

دستور گرفتن بکاپ و دیدن نسخه سرور در SSMS. فایل بکاپ در پوشه پیش‌فرض بکاپ SQL Server ساخته می‌شود:

</div>

```sql
SELECT @@VERSION;
BACKUP DATABASE [IRNNCO] TO DISK = N'IRNNCO_before_net10.bak' WITH COPY_ONLY, INIT;
```

<div dir="rtl">

## ۳. ساخت (Build)

</div>

```bash
cd <folder-of-the-extracted-zip>
dotnet --version
dotnet restore Base.sln
dotnet build Base.sln
```

<div dir="rtl">

پروژه MsTests در `Base.sln` نیست. برای اجرای تست‌ها: `dotnet test src/MsTests/MsTests.csproj`

خلاصه Build حدود ۷۰ هشدار (warning) نشان می‌دهد. بعضی‌ها چند بار شمرده می‌شوند. همه از نوع‌های زیر هستند و مانع اجرا نمی‌شوند (عددها، تعداد جاهای متفاوت در کد است):

- **AMNF0001** (۳۱): نام متد async بدون پسوند Async. هشدار سبک کدنویسی است و از قبل وجود داشت.
- **CA1416** (۷): System.Drawing در کد بلااستفاده `ImagesGuardExt` فقط روی ویندوز کار می‌کند.
- **NU1903** (۵، یکی برای هر پروژه): آسیب‌پذیری شناخته‌شده AutoMapper 10.1.1 (بخش ۸).
- **ASP0019** (۴): افزودن هدر با `Headers.Add` در `ContentSecurityPolicyMiddleware`.
- **SYSLIB0014** (۲) و **SYSLIB0021** (۱): کلاس‌های قدیمی `WebRequest` و رمزنگاری که هنوز کار می‌کنند.
- **CS0108**، **CS0472** و **CS8321** (۶): هشدارهای قدیمی خود کد.

اگر روی سیستم شما خطایی با شروع `error CS` یا `error NU` دیدید، متن کامل خروجی را برای من بفرستید.

## ۴. هماهنگی مدل با دیتابیس

این بررسی در آزمایش GitHub انجام شد و مایگریشن آزمایشی خالی بود (بخش ۱)، پس لازم نیست شما آن را تکرار کنید. اگر دیتابیس شما تا آخرین مایگریشن کد (۴ مه ۲۰۲۵) به‌روز باشد، برنامه هیچ تغییری در ساختار آن نمی‌دهد. اگر عقب‌تر باشد، برنامه مثل قبل مایگریشن‌های باقی‌مانده را هنگام شروع اجرا می‌کند.

## ۵. اجرا روی سیستم خودتان

</div>

```bash
cd src/WebApi
dotnet run --launch-profile WebApi
```

<div dir="rtl">

- صفحه Swagger: `https://localhost:5001/swagger`. اگر از Visual Studio و پروفایل IIS Express استفاده کنید: `https://localhost:44326/swagger`.
- تست ورود: `POST api/v1/Accounts/login` از طریق Swagger یا فرانت‌اند.
- اتصال فرانت‌اند به API محلی: در فایل `front/.env` این خط را بگذارید. اسلش آخر لازم است:

</div>

```ini
VITE_API_URL=https://localhost:5001/
```

<div dir="rtl">

## ۶. انتشار روی IIS

1. روی سرور ASP.NET Core 10.0 Hosting Bundle را نصب کنید و یک بار `iisreset` بزنید.
2. خروجی انتشار را بسازید (یا در Visual Studio از پروفایل FolderProfile استفاده کنید):

</div>

```bash
dotnet publish src/WebApi/WebApi.csproj -c Release -o C:\publish\innco-api
```

<div dir="rtl">

3. در Application Pool گزینه ‎.NET CLR Version را روی `No Managed Code` بگذارید.
4. در `appsettings.json` روی سرور این‌ها را تنظیم کنید:
   - رشته اتصال سرور واقعی
   - `TrustServerCertificate=True` اگر گواهی SQL Server خودامضا است
   - آدرس فرانت‌اند در `Cors:AllowedOrigins`

## ۷. تنظیمات appsettings.json

| کلید | وضعیت | توضیح |
|---|---|---|
| `ConnectionStrings:SqlServer:ApplicationDbContextConnection` | **تغییر کرد** | رشته اتصال روی سرور محلی (`localhost`) از فایل 1.txt پروژه گذاشته شد. رشته قبلی به سرور راه دور اشاره داشت و رمز کاربر sa در آن آشکار نوشته شده بود. **پیشنهاد امنیتی:** رمز sa آن سرور را عوض کنید و رشته اتصال سرور واقعی را فقط روی خود سرور نگه دارید (مثلاً در `appsettings.Production.json` یا متغیر محیطی)، نه در سورس. |
| `TrustServerCertificate=True` | **اضافه شد** | درایور جدید SQL Server (Microsoft.Data.SqlClient) به‌طور پیش‌فرض اتصال را رمزنگاری می‌کند و گواهی سرور را بررسی می‌کند. بدون این گزینه، اتصال به SQL Server با گواهی خودامضا رد می‌شود. |
| `Cors:AllowedOrigins` | **جدید** | اگر خالی باشد (`[]`)، درخواست از هر دامنه‌ای پذیرفته می‌شود، بدون کوکی. فرانت‌اند توکن را در هدر Authorization می‌فرستد، پس همین کافی است. برای سرور واقعی بهتر است دامنه فرانت‌اند را بنویسید (نمونه زیر جدول). |
| `BearerTokens:Key` | بدون تغییر | کتابخانه جدید JWT کلید کوتاه‌تر از ۳۲ کاراکتر را نمی‌پذیرد. کلید فعلی ۴۵ کاراکتر است و مشکلی ندارد. اگر عوضش کردید، کمتر از ۳۲ کاراکتر نباشد. |

نمونه تنظیم CORS برای سرور واقعی:

</div>

```json
"Cors": {
  "AllowedOrigins": [ "https://your-frontend-domain" ]
}
```

<div dir="rtl">

## ۸. ریسک‌ها و مواردی که باید با داده واقعی تست شوند

1. **عملیات‌های ثبت و تغییر وضعیت.** آزمایش روی دیتابیس خالی همه کوئری‌های فهرست و جستجو را اجرا کرد، ولی مسیرهایی که داده لازم دارند اجرا نشدند. این سناریوها را با داده واقعی تست کنید:
   - ثبت قرارداد و دستور ساخت
   - تغییر وضعیت فرایندها تا تکمیل آخرین فرایند یک BOM (گزارش پیشرفت می‌سازد)
   - سفارش خرید تا رسید انبار

   EF Core 2.2 بخش‌هایی از کوئری را که به SQL تبدیل نمی‌شد بی‌صدا در حافظه اجرا می‌کرد، ولی EF Core 3 به بعد خطای «could not be translated» می‌دهد. اگر چنین خطایی دیدید، متن آن را بفرستید.
2. **Trigger روی جدول‌ها.** EF Core 7 به بعد هنگام درج و ویرایش از دستور OUTPUT استفاده می‌کند. اگر روی جدولی trigger تعریف شده باشد، ذخیره روی آن جدول خطا می‌دهد. در کد trigger‌ای پیدا نشد، ولی ممکن است مستقیم در دیتابیس ساخته شده باشد. اگر خطایی با کلمه OUTPUT یا trigger دیدید، بفرستید. رفعش یک تنظیم ساده برای همان جدول است.
3. **نسخه SQL Server.** آزمایش روی SQL Server 2022 انجام شد. EF Core 10 به‌طور پیش‌فرض SQL را برای SQL Server 2019 به بالا (سطح سازگاری 150) تولید می‌کند. اگر سرور شما قدیمی‌تر است، نسخه را بفرستید (`SELECT @@VERSION`) تا سطح سازگاری تنظیم شود.
4. **آسیب‌پذیری AutoMapper 10.1.1** (CVE-2026-32933). مشکل این است: نگاشت شیئی که به نوع خودش ارجاع دارد و حدود ۲۵٬۰۰۰ سطح تودرتو شده باشد، برنامه را از کار می‌اندازد. در این API عمق JSON ورودی را Newtonsoft.Json 13 به‌طور پیش‌فرض به ۶۴ سطح محدود می‌کند. برای همین خطر عملی پایین است و فقط هشدار NU1903 هنگام Build می‌آید.
   - نسخه‌های اصلاح‌شده (15.1.1 به بعد) لایسنس تجاری دارند. برای شرکت‌هایی با درآمد سالانه کمتر از ۵ میلیون دلار لایسنس رایگان Community هست، ولی سازمان‌های دولتی و شبه‌دولتی شامل آن نمی‌شوند.
   - AutoMapper 10 روی ‎.NET 10 یک ایراد هنگام شروع برنامه هم داشت که با یک تنظیم رفع شد (بخش ۹). نسخه‌های ۱۲ به بعد این ایراد را ندارند.
   - تصمیم برای مرحله بعد است: ارتقای AutoMapper، یا جایگزینی با Mapster یا نگاشت دستی.
5. **ارسال ایمیل.** با System.Net.Mail بازنویسی شد، ولی الان هیچ جای کد ایمیل نمی‌فرستد. ایمیل قالب‌دار (Razor) خطای NotSupportedException می‌دهد، چون در این API هیچ قالبی وجود ندارد.
6. **دسترسی پویا (DynamicPermission / SecurityTrimming).** بررسی «وجود اکشن» که به کتابخانه DNTCommon وابسته بود حذف شد. هیچ کنترلری از این policy استفاده نمی‌کند. خود بررسی مجوز تغییری نکرده است.
7. **بخش‌های مخصوص ویندوز.** System.Drawing (کد بلااستفاده `ImagesGuardExt`) فقط روی ویندوز کار می‌کند. فرهنگ فارسی `fa-IR` روی ویندوز و لینوکس (در آزمایش) کار کرد.
8. **اولین اجرا روی سرور خالی.** اگر دیتابیس هنوز ساخته نشده باشد، تا پیش از اجرای مایگریشن‌ها چند خطای «Cannot open database» در لاگ می‌آید و شروع برنامه کمی بیشتر طول می‌کشد. این فقط یک بار اتفاق می‌افتد و روی دیتابیس موجود شما رخ نمی‌دهد.
9. **هشدارهای EF Core در لاگ.** این سه هشدار از قبل در کد بودند:
   - صفحه‌بندی (Skip/Take) بدون مرتب‌سازی
   - بارگذاری چند مجموعه در یک کوئری
   - ستون‌های decimal بدون دقت مشخص

   خطا نیستند و برای بهینه‌سازی در مرحله بعد مانده‌اند.
10. **بررسی سرویس‌ها در شروع برنامه.** گزینه `ValidateOnBuild` مثل نسخه 2.2 خاموش است. اگر در `Program.cs` روشنش کنید، ثبت نادرست سرویس‌ها همان لحظه شروع معلوم می‌شود.
11. **ایرادهای قدیمی که عمداً دست نخوردند** (برای مرحله اصلاح بعدی):
    - دانلود سند (`api/v1/Documents`) بدون شناسه سند خطا می‌دهد.
    - دریافت تامین‌کننده با شناسه‌ای که وجود ندارد (`api/v1/Provider/providers`) به‌جای «پیدا نشد» خطای NullReference می‌دهد.
    - در `api/v1/Inquiries/get-inquiry` پارامتر از بدنه درخواست خوانده می‌شود که در GET همیشه خالی است. فرانت‌اند از این آدرس استفاده نمی‌کند.
    - خطاهای داخلی سرور با کد HTTP 200 و پیام خطا در بدنه برمی‌گردند (`HttpGlobalExceptionFilter`).
    - در `IdentityDbInitializer.SeedData`، دستوری که نقش جاافتاده را اضافه می‌کند به‌جای خود نقش، نام آن را به EF می‌دهد. چون مایگریشن‌ها همه نقش‌ها را می‌سازند، در عمل اجرا نمی‌شود. در همان کلاس، مقایسه نتیجه با `IdentityResult.Failed` همیشه false است.

## ۹. فهرست کامل تغییرات (برای برنامه‌نویس)

### پروژه و بسته‌ها

- **فریم‌ورک:** از `netstandard2.0` و `netcoreapp2.2` (زبان C# 7.2) به `net10.0` (زبان C# 14).
- **نسخه بسته‌ها:** به‌جای نوشتن نسخه در هر csproj، همه در `Directory.Packages.props` آمده‌اند (Central Package Management). نسخه بسته‌های مایکروسافت با یک متغیر عوض می‌شود: `MicrosoftPackagesVersion`.
- **بسته‌های ASP.NET Core:** به‌جای بسته‌های NuGet نسخه 2.2، از `FrameworkReference Microsoft.AspNetCore.App` استفاده می‌شود. از ‎.NET Core 3 این‌ها بخشی از خود فریم‌ورک هستند.
- **SDK و ابزار:** فایل `global.json` (SDK 10.0.100 به بالا)، ابزار `dotnet-ef` نسخه 10.0.12 و پروفایل‌های انتشار روی net10.0.
- **بسته‌های حذف‌شده:** بسته‌های `Microsoft.AspNet.WebApi` و `System.ServiceModel`، Quartz، CodeGeneration.Design، Razor.Design و ارجاع‌های HintPath. این‌ها مخصوص ‎.NET Framework یا بلااستفاده بودند. ویژگی `ResponseType` به `ProducesResponseType` تبدیل شد.
- **API Versioning:** بسته `Mvc.Versioning` و ویژگی `ApiVersion` حذف شدند. همه مسیرها ثابت و با `api/v1` شروع می‌شوند و نسخه‌بندی استفاده نمی‌شد.
- **DNTCommon.Web.Core 1.3.3:** این نسخه مخصوص ASP.NET Core 2.x است و نسخه‌های جدیدش API متفاوتی دارند. فقط چند قابلیت کوچک آن استفاده می‌شد که به‌صورت محلی بازنویسی شدند: `YeKeModelBinder`، `RunScopedService`، `UploadFileExtensionsAttribute`، `DisableBrowserCache` و `SmtpConfig`. ارسال ایمیل با System.Net.Mail انجام می‌شود.
- **cloudscribe.Web.Pagination:** با کلاس محلی `PaginationSettings` جایگزین شد، چون مخصوص ASP.NET Core 2.x است.

### میزبانی و وب

- **Program.cs:** به‌جای `WebHost.CreateDefaultBuilder` که منسوخ است، Generic Host. فایل `Startup.cs` حفظ شد.
- **مسیریابی:** به‌جای `UseMvc`، Endpoint Routing (`UseRouting`، `UseCors`، `UseAuthentication`، `UseAuthorization` و `UseEndpoints`) با همان مسیرها.
- **JSON:** به‌جای `AddJsonOptions`، `AddNewtonsoftJson` با همان تنظیمات. پیش‌فرض جدید System.Text.Json است، ولی فرانت‌اند به رفتار Newtonsoft وابسته است.
- **JWT:** به‌جای `SecurityTokenValidators` با `InboundClaimTypeMap` خالی، `MapInboundClaims = false`. نام claimها همان قبلی است.
- **CORS:** ترکیب «هر دامنه» با `AllowCredentials` از ASP.NET Core 3 به بعد در شروع برنامه خطا می‌دهد. حالا لیست دامنه‌ها از `Cors:AllowedOrigins` خوانده می‌شود. اگر خالی باشد، هر دامنه بدون credentials پذیرفته می‌شود.
- **محیط میزبان:** به‌جای `IHostingEnvironment`، `IWebHostEnvironment`.
- **مجوز پویا:** مقادیر مسیر به‌جای `AuthorizationFilterContext` از `HttpContext.Request.RouteValues` خوانده می‌شوند. با Endpoint Routing منبع درخواست HttpContext است.

### Identity

- `CustomNormalizer` حالا مستقیم `ILookupNormalizer` را پیاده می‌کند (`NormalizeName` و `NormalizeEmail`) با همان منطق قبلی. برای همین نام‌ها و ایمیل‌های ذخیره‌شده در دیتابیس همچنان مطابقت دارند.
- سازنده‌ها مطابق کلاس‌های پایه جدید عوض شدند:
  - `ApplicationSignInManager` با پارامتر جدید `IUserConfirmation`
  - `ConfirmEmailDataProtectorTokenProvider` با پارامتر جدید `ILogger`
  - `CustomSecurityStampValidator` با `ILoggerFactory` به‌جای `ISystemClock`
- در `IApplicationUserManager`، متد `NormalizeKey` به `NormalizeName` و `NormalizeEmail` تبدیل شد. متد `GetEmailImageAsync` بلااستفاده بود و به DNTCommon وابسته بود، پس حذف شد.
- **رمزهای قبلی کاربر** (`UsedPasswordsService`): EF Core 2.2 بررسی هش را بی‌صدا در حافظه اجرا می‌کرد، ولی EF Core 3 به بعد این کوئری را اجرا نمی‌کند و تغییر یا بازیابی رمز خطا می‌داد. حالا آخرین N هش از دیتابیس خوانده و در حافظه بررسی می‌شود.

### EF Core و دیتابیس

- `ExecuteSqlCommand` به `ExecuteSqlRaw` و `HasName` (روی ایندکس) به `HasDatabaseName` تغییر کرد.
- **حذف آبشاری:** پیش‌فرض EF Core 3 به بعد «فوری» است. با `CascadeTiming.OnSaveChanges` رفتار 2.2 (حذف هنگام SaveChanges) حفظ شد.
- **هشدارها:** تنظیم `IncludeIgnoredWarning` و `QueryClientEvaluationWarning` که دیگر وجود ندارند حذف شد. به‌جای آن `NavigationBaseIncludeIgnored` و `PendingModelChangesWarning` فقط لاگ می‌شوند، چون در EF Core 10 به‌طور پیش‌فرض برنامه را متوقف می‌کنند.
- **داده اولیه نقش‌ها:** مقدار `ConcurrencyStamp` در هر بار ساخت مدل تصادفی بود و EF Core 9 به بعد آن را تغییر مدل حساب می‌کرد. حالا مقدار ثابت از آخرین snapshot است. نتیجه خالی بودن مایگریشن آزمایشی این را تایید کرد.
- `System.Data.SqlClient` (برای `SortOrder`) به `Microsoft.Data.SqlClient` تغییر کرد که درایور جدید EF Core است.
- **DbLogger:** قفلی برای جلوگیری از فراخوانی تودرتو اضافه شد. در EF Core 10، لاگ هنگام ساخت مدل می‌توانست به حلقه بی‌پایان برسد.

### اصلاحاتی که Build و اجرای واقعی نشان داد

- **خطای کامپایل CS9226 در جستجوی `BuyOrderProcessService`:** در C# 13 به بعد، متد `Split` داخل کوئری EF به نسخه‌ای از متد می‌رسد که درخت عبارت (expression tree) نمی‌پذیرد. متن جستجو حالا پیش از ساخت کوئری جدا می‌شود. رفتار همان است.
- **از کار افتادن برنامه هنگام شروع (AutoMapper 10):** AutoMapper 10 همه متدهای کمکی `System.Linq.Enumerable` را بررسی می‌کند، حتی متدهای خصوصی. در ‎.NET 7 به بعد چند متد خصوصی جدید با محدودیت نوع اضافه شده که این بررسی را با خطا متوقف می‌کرد. در `Startup.cs` بررسی به متدهای عمومی محدود شد (`ShouldMapMethod`).
- **CORS:** با Endpoint Routing باید بین `UseRouting` و `UseEndpoints` باشد. وگرنه کنترلرهایی که `EnableCors` دارند (AdminSettings و ApiSettings) خطای ۵۰۰ می‌دادند.
- **گزارش پیشرفت بر اساس قرارداد و دستور ساخت (`ReportsService`):** EF Core 3 به بعد نمی‌تواند داخل نتیجه یک کوئری، کوئری دیگری (IQueryable) برگرداند. سری قیمت‌ها و درصدهای وزنی حالا با یک کوئری جداگانه خوانده می‌شود. خروجی همان است.
- **ساخت گزارش پیشرفت (`BuyOrderService.AddProgressReportAsync`):** یک بخش از همین نوع در کوئری بود که در نتیجه استفاده نمی‌شد و حذف شد.
- **تست‌ها:** ثبت سرویس لاگ، نام دیتابیس حافظه‌ای و ساخت داده اولیه در `CoreTests`.

### تست‌ها و تنظیمات

- MSTest از 1.4 به 3.11 رسید و کلاس `TestWebHostEnvironment` اضافه شد.
- در `appsettings.json` رشته اتصال محلی، `TrustServerCertificate=True` و بخش `Cors` آمده است (بخش ۷).

## ۱۰. برای ادامه کار چه بفرستید

- نسخه SQL Server (`SELECT @@VERSION`)
- نتیجه اجرا روی سیستم شما و متن هر خطا (از کنسول یا جدول AppLogItems)
- نتیجه تست سناریوهای بخش ۸

</div>
