<div dir="rtl">

# ارتقای INNCO API از ASP.NET Core 2.2 به ‎.NET 10

**تاریخ:** ۲۱ مهر ۱۴۰۵ (13 October 2026)

**وضعیت:** تغییرات کد کامل شده، ولی پروژه هنوز **کامپایل نشده** است. در محیطی که ارتقا انجام شد، ‎.NET SDK و NuGet در دسترس نبود. برای همین اولین Build و اجرا باید روی سیستم شما انجام شود (بخش‌های ۳ تا ۵).

## خلاصه

- هر ۸ پروژه (Common، Entities، ViewModels، DataLayer، Services، IocConfig، WebApi و MsTests) روی `net10.0` و زبان C# 14 هستند.
- نسخه همه بسته‌ها در یک فایل آمده است: `Directory.Packages.props`. بسته‌های مایکروسافت روی 10.0.12 هستند، آخرین نسخه پایدار (۸ سپتامبر ۲۰۲۶).
- بسته‌های منسوخ یا ناسازگار حذف شدند. هر جا لازم بود، با کد محلی جایگزین شدند (بخش ۹).
- رفتار API برای فرانت‌اند ثابت مانده است: آدرس‌ها (همه با `api/v1` شروع می‌شوند)، JSON با camelCase (همان Newtonsoft.Json)، توکن JWT و CORS.
- چند ایراد که در ‎.NET 10 به خطا می‌رسید از قبل رفع شد:
  - تغییر و بازیابی رمز عبور
  - داده اولیه نقش‌ها
  - حلقه بی‌پایان احتمالی در لاگر دیتابیس

## ۱. آنچه بررسی شد و آنچه نشد

بررسی شده:

- نحو (syntax) هر ۸۱۴ فایل C#‎ پروژه: بدون خطا.
- سازنده‌ها و متدهایی که کد از ASP.NET Core و EF Core صدا می‌زند، با سورس رسمی نسخه 10.0.12 مقایسه شدند.
- نام کلاس‌های پروژه با ‎.NET 10، ASP.NET Core، EF Core، DNTPersianUtils.Core و System.Linq.Dynamic.Core تداخلی ندارد.
- فایل‌های پروژه (csproj) و نسخه بسته‌ها با هم هماهنگ هستند.

نیاز به سیستم شما:

- کامپایل واقعی (`dotnet build`)، اجرای برنامه، اتصال به SQL Server و تست صفحات.

## ۲. پیش‌نیازها

| مورد | توضیح |
|---|---|
| ‎.NET 10 SDK | هر SDK نسخه ۱۰. پیشنهاد: آخرین نسخه (هنگام نوشتن این راهنما 10.0.401). خروجی `dotnet --version` باید با 10 شروع شود. |
| محیط توسعه | Visual Studio 2026 (نسخه ۱۸) که SDK دات‌نت ۱۰ را همراه دارد، یا VS Code با افزونه C# Dev Kit. نسخه‌های قدیمی‌تر Visual Studio ممکن است پروژه `net10.0` را باز نکنند. |
| SQL Server | همان نسخه فعلی. نکته نسخه‌های قدیمی در بخش ۸ آمده است. |
| IIS (روی سرور) | ASP.NET Core 10.0 Hosting Bundle |
| گواهی HTTPS محلی | فقط یک بار اجرا کنید: `dotnet dev-certs https --trust` |

> ⚠️ **قبل از اولین اجرا از دیتابیس IRNNCO بکاپ کامل بگیرید.** برنامه هنگام شروع، مایگریشن‌های اعمال‌نشده را خودکار روی دیتابیس اجرا می‌کند. این رفتار از قبل در برنامه بوده است.

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

این هشدارها (warning) قابل انتظار هستند و مانع اجرا نمی‌شوند:

- **CS0618** برای `HasName` در فایل‌های مایگریشن قدیمی. EF Core این متد را عمداً برای مایگریشن‌های قدیمی نگه داشته است.
- **SYSLIB0014** برای `WebRequest` در ارسال نوتیفیکیشن (AuthMessageSender).
- **CA1416** برای System.Drawing در کد بلااستفاده `ImagesGuardExt`.
- **NU1903** برای AutoMapper 10.1.1 (توضیح در بخش ۸).

هر خطا با شروع `error CS` یا `error NU` را با متن کامل خروجی برای من بفرستید.

## ۴. بررسی هماهنگی مدل با دیتابیس (قبل از اجرای برنامه)

آخرین snapshot مایگریشن‌ها با EF Core 2.2.6 ساخته شده است. با این مرحله معلوم می‌شود که EF Core 10 همان ساختار جدول‌ها را می‌بیند یا تفاوتی پیدا می‌کند.

</div>

```bash
cd src/WebApi
dotnet tool restore
dotnet ef migrations add Net10ModelCheck --project ../DataLayer/DataLayer.csproj --startup-project WebApi.csproj --context ApplicationDbContext
```

<div dir="rtl">

در پوشه `src/DataLayer/Migrations` فایل جدیدی ساخته می‌شود که نامش با تاریخ و ساعت شروع می‌شود و به `Net10ModelCheck.cs` ختم می‌شود. آن را باز کنید.

- **اگر متدهای `Up` و `Down` خالی بودند:** مدل با دیتابیس یکی است. فایل را نگه دارید، چون فقط snapshot را به قالب EF Core 10 به‌روز می‌کند. سپس به بخش ۵ بروید.
- **اگر خالی نبودند:** برنامه را اجرا نکنید، چون در شروع، این تغییرات را خودکار روی دیتابیس اعمال می‌کند. مایگریشن را حذف کنید و محتوای فایل را برای من بفرستید.

</div>

```bash
dotnet ef migrations remove --project ../DataLayer/DataLayer.csproj --startup-project WebApi.csproj --context ApplicationDbContext
```

<div dir="rtl">

ممکن است این دستور به دیتابیس دسترسی نداشته باشد و خطا بدهد. در آن صورت، دو فایلی را که نامشان به `Net10ModelCheck.cs` و `Net10ModelCheck.Designer.cs` ختم می‌شود پاک کنید و snapshot را به حالت قبل برگردانید (پوشه مخفی git داخل zip هست):

</div>

```bash
git checkout -- ../DataLayer/Migrations/ApplicationDbContextModelSnapshot.cs
```

<div dir="rtl">

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

## ۸. ریسک‌ها و مواردی که باید در اجرا تست شوند

1. **تکمیل آخرین فرایند یک BOM** (`BuyOrderService.AddProgressReportAsync`). این متد یک کوئری LINQ بزرگ دارد. EF Core 2.2 بخش‌هایی را که به SQL تبدیل نمی‌شد بی‌صدا در حافظه اجرا می‌کرد، ولی EF Core 3 به بعد خطای «could not be translated» می‌دهد. این عملیات را تست کنید و اگر خطا داد، متن آن را بفرستید. چند کوئری مشابه هم در کدهای بلااستفاده هست (`AppLogItemsService.GetLogItemsAsync` و `GetChartDataAsync`).
2. **Trigger روی جدول‌ها.** EF Core 7 به بعد هنگام درج و ویرایش از دستور OUTPUT استفاده می‌کند. اگر روی جدولی trigger تعریف شده باشد، ذخیره روی آن جدول خطا می‌دهد. در کد trigger‌ای پیدا نشد، ولی ممکن است مستقیم در دیتابیس ساخته شده باشد. اگر خطایی با کلمه OUTPUT یا trigger دیدید، بفرستید. رفعش یک تنظیم ساده برای همان جدول است.
3. **نسخه SQL Server.** EF Core 10 به‌طور پیش‌فرض SQL را برای SQL Server 2019 به بالا (سطح سازگاری 150) تولید می‌کند. اگر سرور قدیمی‌تر است و خطای نحوی SQL دیدید، نسخه را بفرستید (`SELECT @@VERSION`) تا سطح سازگاری تنظیم شود.
4. **تفاوت مدل و دیتابیس.** بخش ۴ را ببینید. EF Core 10 در حالت پیش‌فرض با دو هشدار برنامه را متوقف می‌کند: `PendingModelChangesWarning` و `NavigationBaseIncludeIgnored`. این دو طوری تنظیم شدند که فقط در لاگ ثبت شوند. اگر `PendingModelChangesWarning` را در جدول لاگ (AppLogItems) دیدید، یعنی بخش ۴ لازم است.
5. **آسیب‌پذیری AutoMapper 10.1.1** (CVE-2026-32933). مشکل این است: نگاشت شیئی که به نوع خودش ارجاع دارد و حدود ۲۵٬۰۰۰ سطح تودرتو شده باشد، برنامه را از کار می‌اندازد. در این API عمق JSON ورودی را Newtonsoft.Json 13 به‌طور پیش‌فرض به ۶۴ سطح محدود می‌کند. برای همین خطر عملی پایین است و فقط هشدار NU1903 هنگام Build می‌آید. نسخه‌های اصلاح‌شده (15.1.1 به بعد) لایسنس تجاری دارند. برای شرکت‌هایی با درآمد سالانه کمتر از ۵ میلیون دلار لایسنس رایگان Community هست، ولی سازمان‌های دولتی و شبه‌دولتی شامل آن نمی‌شوند. ارتقا به نسخه ۱۵ چند تغییر کد هم لازم دارد. تصمیمش برای مرحله بعد است: ارتقای AutoMapper، یا جایگزینی با Mapster یا نگاشت دستی.
6. **ارسال ایمیل.** با System.Net.Mail بازنویسی شد، ولی الان هیچ جای کد ایمیل نمی‌فرستد. ایمیل قالب‌دار (Razor) خطای NotSupportedException می‌دهد، چون در این API هیچ قالبی وجود ندارد.
7. **دسترسی پویا (DynamicPermission / SecurityTrimming).** بررسی «وجود اکشن» که به کتابخانه DNTCommon وابسته بود حذف شد. هیچ کنترلری از این policy استفاده نمی‌کند. خود بررسی مجوز تغییری نکرده است.
8. **بخش‌های مخصوص ویندوز.** System.Drawing (کد بلااستفاده `ImagesGuardExt`) فقط روی ویندوز کار می‌کند. فرهنگ فارسی `fa-IR` روی ویندوز مشکلی ندارد. روی لینوکس به کتابخانه ICU نیاز دارد که معمولاً نصب است.
9. **بررسی سرویس‌ها در شروع برنامه.** گزینه `ValidateOnBuild` مثل نسخه 2.2 خاموش است. اگر در `Program.cs` روشنش کنید، ثبت نادرست سرویس‌ها همان لحظه شروع معلوم می‌شود.
10. **ایرادهای قدیمی که عمداً دست نخوردند:**
    - در `IdentityDbInitializer.SeedData`، دستوری که نقش جاافتاده را اضافه می‌کند به‌جای خود نقش، نام آن را به EF می‌دهد و اگر نقشی در دیتابیس نباشد خطا می‌دهد. چون مایگریشن‌ها همه نقش‌ها را می‌سازند، در عمل اجرا نمی‌شود.
    - در همان کلاس، مقایسه نتیجه با `IdentityResult.Failed` همیشه false است، پس شکست ساخت کاربر ادمین فقط در لاگ دیده می‌شود.

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
- **مسیریابی:** به‌جای `UseMvc`، Endpoint Routing (`UseRouting`، `UseAuthentication`، `UseAuthorization` و `UseEndpoints`) با همان مسیرها.
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
- **داده اولیه نقش‌ها:** مقدار `ConcurrencyStamp` در هر بار ساخت مدل تصادفی بود و EF Core 9 به بعد آن را تغییر مدل حساب می‌کرد. حالا مقدار ثابت از آخرین snapshot است.
- `System.Data.SqlClient` (برای `SortOrder`) به `Microsoft.Data.SqlClient` تغییر کرد که درایور جدید EF Core است.
- **DbLogger:** قفلی برای جلوگیری از فراخوانی تودرتو اضافه شد. در EF Core 10، لاگ هنگام ساخت مدل می‌توانست به حلقه بی‌پایان برسد.

### تست‌ها و تنظیمات

- MSTest از 1.4 به 3.11 رسید و کلاس `TestWebHostEnvironment` اضافه شد.
- در `appsettings.json` رشته اتصال محلی، `TrustServerCertificate=True` و بخش `Cors` آمده است (بخش ۷).

## ۱۰. برای ادامه کار چه بفرستید

- خروجی کامل `dotnet build` اگر خطا داشت
- محتوای فایل مایگریشن `Net10ModelCheck` اگر خالی نبود
- متن هر خطای زمان اجرا (از کنسول یا جدول AppLogItems)
- نسخه SQL Server (`SELECT @@VERSION`)

</div>
