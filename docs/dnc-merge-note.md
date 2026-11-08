زبان این سند فارسی است؛ شناسه‌ها و دستورها انگلیسی.

# یادداشت ادغام DNC (تولید P2 بخش A) برای هماهنگ‌کننده اصلی

تاریخ: 1405/08/17. نوشته‌شده در جلسه‌ای جدا از MAIN SESSION، به خواست کارفرما. هیچ push به `innco-net10-build`، `innco-front` یا `innco-handoff` انجام نشده است.

## شاخه‌ها
- بک‌اند: `innco-dnc` (پایه: `innco-net10-build` b4172ff).
- فرانت: `innco-front-dnc` (پایه: `innco-front` 5bf4dd7).
- طرح فنی: `docs/dnc-backend-brief.md` روی همین شاخه (ماشین‌های واقعی شرکت از پلاک و پنل کنترل).

## راستی‌آزمایی انجام‌شده
- `dotnet build Base.sln`: ۰ خطا، بدون هشدار تازه در فایل‌های DNC.
- آزمون واحد `DncTests`: ۲۵ قبول، شامل مبدل سریال جعلی روی loopback (فاصله‌گذاری باود و XOFF/XON)، سرور FTP جعلی، پوشه شبکه و کل جریان روی پایگاه داده درون‌حافظه‌ای (بارگذاری، تأیید دونفره، ارسال، تحویل، تأیید اپراتور، گیت شروع، ثبت اجرا، دریافت از ماشین با تفاوت). `ControllerAuthorizationTests` سبز (مسیرهای گیت‌وی در فهرست ناشناس عمدی).
- آزمون دود: بخش «dnc:» درست پیش از «PB2 fix round» افزوده شد (`bash -n` و `shellcheck` بدون هشدار تازه؛ همه عبارت‌های jq کامپایل شدند). روی SQL Server اجرا نشده است، چون در sandbox در دسترس نیست. **اجرای CI لازم است.**

## گام‌های ادغام (طبق HANDOFF بخش 3)
1. پس از پایان اجرای جاری CI: `git merge origin/innco-dnc` در `innco-net10-build`.
2. فایل‌های مشترکی که تغییر کوچک گرفته‌اند (احتمال تعارض متنی):
   - `InventoryConfigurations.cs`: یک خط `builder.AddDncConfigurations()`.
   - `FinanceAuditTrail.cs`: چهار ورودی.
   - `AddCustomServicesExtensions.cs`: سه ثبت سرویس.
   - `MfShopFloorService.cs`: دو درج در `StartAsync`، یکی گیت و یکی ثبت `MfNcRun`.
   - `ControllerAuthorizationTests.cs`: چهار نام در `AnonymousByDesign`.
   - `inventory-smoke.sh`: یک بلوک پیش از PB2.
3. مایگریشن محلی روی شاخه ساخت با نام پیشنهادی `ProductionDnc`. فقط افزودنی است: ۶ جدول `MfMachineConnections`، `MfNcPrograms`، `MfNcRevisions`، `MfNcContents`، `MfNcTransfers` و `MfNcRuns`. هیچ ستونی به جدول موجود اضافه نمی‌شود.
4. push و انتظار برای CI سبز (هر دو SQL، نصب ×۳).
5. فرانت: `git merge origin/innco-front-dnc` در `innco-front` و سپس ساخت بسته در `.github/ci-frontend-dist`. فایل‌های مشترک: `router/index.ts` (سه مسیر)، `KsSidebar.vue` (یک پیوند)، `Terminal.vue` (پنل DNC در گفت‌وگوی شروع).

## پیکربندی سرور
- کار پس‌زمینه انتقال هر ۱۰ ثانیه اجرا می‌شود و با همان کلید `Production:Jobs` خاموش می‌شود (در CI خاموش است و آزمون دود صف را با `POST production/dnc/transfers/run-pending` اجرا می‌کند). فاصله: `Dnc:PollSeconds`.
- رمز FTP کنترلرها فقط در `appsettings` سرور زیر `Dnc:Credentials:<key>:User|Password` قرار می‌گیرد، هرگز در پایگاه داده.
- برای پیوند پوشه شبکه، هویت IIS App Pool باید روی پوشه اشتراکی دسترسی نوشتن داشته باشد.

## تصمیم‌های فرض‌شده (★، برای دفتر بازخورد)
بخش «پرسش‌های باز شرکت» در `docs/dnc-backend-brief.md` را ببینید: نقش‌های نویسنده و تأییدکننده، الزام تأیید دریافت پیش از شروع (پیش‌فرض بله)، سقف ۴ مگابایت، کنترلر تراش CMZ، خرید مبدل‌های سریال و ماژول‌های ورودی دیجیتال، و **تعمیر صفحه‌نمایش شکسته کنترلر Deckel Maho**.
