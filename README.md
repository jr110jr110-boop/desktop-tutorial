<div dir="rtl">

سفارشی سازی ASP.NET Core Identity 2.2.102
=======



![dnt-identity-02](/src/AdakBase/wwwroot/images/dnti02.png)



![dnt-identity-01](/src/AdakBase/wwwroot/images/dnti01.png)




دارای قسمت‌های
------
ثبت نام به همراه تائید ایمیل، لاگین، برگه‌ی اطلاعات کاربری، تنظیمات کاربری، تغییر کلمه‌ی عبور، بازیابی کلمه‌ی عبور، اعتبارسنجی دو مرحله‌ای توسط ایمیل، مدیریت کاربران و نقش‌های ثابت. مدیریت سطوح دسترسی پویای به صفحات و مشاهده‌ی لاگ خطاهای برنامه.



با امکانات
----------
* اضافه کردن Remote validationها به قسمت‌های ثبت نام، تغییر کلمه‌ی عبور، بازیابی کلمه‌ی عبور و تنظیمات کاربری
* پیاده سازی امکان ویرایش تنظیمات کاربری به همراه آپلود تصویر شخص و فیلدهای سفارشی مانند تاریخ تولد، مکان و امثال آن
* پیاده سازی کامل قسمت‌های ارسال ایمیل و همچنین پشتیبانی از تهیه‌ی قالب‌های ایمیل توسط Razor Views
* پیاده سازی مدیریت نقش‌های ثابت سیستم
* پیاده سازی جزئیات مدیریت کاربران به همراه جستجوی آن‌ها
* پیاده سازی سطوح دسترسی پویای به صفحات مختلف سایت به کمک ویژگی جدید پالیسی در ASP.NET Core
* پیاده سازی مفهوم Security Trimming جهت عدم نمایش لینک‌هایی که کاربر جاری به آن‌ها دسترسی پویا ندارد
* پیاده سازی ویجت کاربران آنلاین
* پیاده سازی ویجت تولدهای امروز با سفارشی سازی موجودیت و همچنین سرویس مدیریت کاربران سایت



سفارشی سازی تنظیمات کلاینت
--------
* استفاده از بوت استرپ 3 راست به چپ
* استفاده از قلم فارسی مناسب
* تنظیم Unobtrusive jQuery Ajax & Validation
* تنظیم Bundling & minification اسکریپت‌ها و شیوه‌نامه‌ها



سفارشی سازی لایه‌های برنامه
------
* جدا سازی کامل لایه‌ها به همراه تزریق وابستگی‌ها و پیاده سازی الگوی واحد کار
* انتقال تمام مباحث مدیریت کاربران به یک Area جدید به نام Identity جهت سهولت استفاده‌ی از آن در سایر برنامه‌ها



سفارشی سازی لایه‌ها‌ی دسترسی به داده‌ها و موجودیت‌ها
--------
* سفارشی سازی موجودیت‌های Identity جهت افزودن خواصی بیشتر و همچنین تغییر نوع کلید اصلی جداول به int
* DbContext مورد استفاده، به لایه‌ی مخصوص خود انتقال یافته و همچنین تنظیمات Migrations نیز از برنامه‌ی اصلی وب خارج و به این لایه منتقل شده‌اند.
* سفارشی سازی کامل DbContext برنامه جهت افزودن امکان استفاده‌ی از بانک‌های اطلاعاتی مختلف، سفارشی سازی نام جداول Identity، افزودن Shadow properties ویژه‌ی ثبت جزئیات کاربر ثبت کننده و ویرایش کننده‌ی رکوردها، به همراه IP‌ و زمان تغییرات.
* یک دست سازی ی و ک ثبت شده‌ی توسط EF Core با سفارشی سازی Change Tracker آن



سفارشی سازی لایه‌ی سرویس‌های Identity
--------
* پیاده سازی متد Seed جهت افزودن کاربر ادمین و همچنین نقش آن به سیستم در اولین بار اجرای برنامه
* سفارشی سازی تمام سرویس‌های توکار Identity مانند مدیریت کاربران و نقش‌ها
* سفارشی سازی مدیریت Claims جهت افزودن مسیر تصویر شخص جهت کاهش رفت و برگشت به بانک اطلاعاتی
* سفارشی سازی نرمال ساز ایمیل‌ها و همچنین نام کاربری جهت اعمال یک دست سازی به ایمیل‌هایی یکسان با چند نوع قابل قبول مانند ایمیل‌های جی‌میل
* پیاده سازی یک Ticket Store مبتنی بر بانک اطلاعاتی جهت کاهش اندازه‌ی کوکی‌های حاصل از تعریف سطوح دسترسی پویای به صفحات مختلف برنامه
* سفارشی سازی اعتبارسنجی کلمات عبور برنامه جهت عدم قبول مواردی ساده و یا قابل حدس
* تدارک یک لاگر پیاده سازی شده‌ی با EF Core جهت ثبت خطاهای برنامه به همراه صفحه‌ای مدیریتی مخصوص جهت مشاهده‌ی این خطاها
* پیاده سازی سرویس ذخیره سازی سابقه‌ی کلمات عبور کاربران و ارائه‌ی اخطارهای لازم به آن‌ها
* سفارشی سازی طول عمر توکن صادر شده‌ی جهت تائید ایمیل شخص در حین ثبت نام که به صورت پیش فرض تنها یک روز است
* انتقال تمام تنظیمات Identity و برنامه به یک فایل json، جهت سهولت تغییر آن‌ها بدون نیازی به تغییری در برنامه
* تهیه‌ی یک رجیستری جهت سهولت افزودن تمام تنظیمات تزریق وابستگی‌های سفارشی سازی شده‌ی برنامه توسط یک متد در کلاس آغازین آن



بومی سازی
-------
* فارسی بودن تمام قسمت‌های برنامه به همراه کلیه‌ی خطاها و اخطارهای توکار Identity



آزمون‌های واحد
------
* تهیه و تنظیم کامل زیر ساخت نوشتن آزمون‌های واحد درون حافظه‌ای EF Core به کمک فریم ورک MS Test



پیش از مشارکت
------
[« آشنایی با ساختار یک Pull Request خوب »](http://www.dotnettips.info/post/2033)


------------------------------------------------------------------------------------------------------


جزئیات و توضیحات بیشتر این موارد را می‌توانید در گروه [ASP.NET Core Identity](http://www.dotnettips.info/search/label/asp.net%20core%20identity) پیگیری نمائید.

Sepidar transfer (sender configuration)
------
Finance work is done inside Orchestra. Only the kinds of documents that the owner switches on (page «انتقال به سپیدار», kinds all OFF by default) are queued as items; a person approves each item; then the configured sender hands it over. The sender is chosen by configuration, never in code:

| Setting | Meaning |
|---|---|
| `Sepidar:Sender` | `None` (default: approved items wait in the queue, nothing is sent, no error), `File` or `Api` |
| `Sepidar:FileDropFolder` | for `File`: the folder where each approved item is written as one JSON file (`sepidar-<itemId>-<hash16>.json`); a path with `..` is refused; a repeat of the same item writes the same file |
| `Sepidar:MaxAttempts` | automatic send attempts per item (default 5, 1 to 20); waits between failures: 5 minutes, 30 minutes, 2 hours, 12 hours; after the last one the item stays failed until a person retries or cancels it |
| `Sepidar:Api:BaseUrl` and credentials | reserved for the real API client (user secrets or environment variables only; never in `appsettings.json`, never logged) |

`Api` is a placeholder (`ApiSepidarSender`, `IsConfigured` is false): the real client is written there when the owner delivers the Sepidar API. Use the key `item id + ":" + payload hash` (`SepidarEnvelope.IdempotencyKey`) as the idempotency key of the call.

**Drop-folder permissions (`Sepidar:Sender = File`).** The folder in `Sepidar:FileDropFolder` must exist and be writable by the account that runs the API process (and only by it plus the account that picks the files up for Sepidar); the sender never creates parents, never follows a path with `..`, and a missing or unwritable folder is a refused attempt with a short Persian text (the exception text, which may hold a path, is never shown). Files hold the transferred data of finance documents: keep the folder off shared drives. The two keys that matter in operation are `Sepidar:Sender` and `Sepidar:MaxAttempts`; `Sepidar:FileDropFolder` is needed only for `File`.

**Who does what.** Reading the page: finance manager, CEO, auditor. Approving, rejecting, retrying and cancelling an item: finance manager or CEO, never the maker or editor of the source document and never an AI seat. Switching a kind on or off: finance manager or CEO. A kind is switched on with a first day (`FromDate`): only documents finalised on or after that day are queued. A restricted item (a payroll total, or a voucher with a protected labour line or a line on an employee detail account) is visible only to the readers of payroll (payroll, finance manager, CEO) in the list, the detail, the summary and the task texts.

**Old monthly packages and the new per-document transfer (page «بسته‌های ماهانهٔ سپیدار», api `finance/sepidar`).** The same data must not reach Sepidar twice: a kind cannot be switched on while an old package of the kind in the table below exists (not superseded, not rejected) whose period ends on or after the first day of the new kind. Choose a first day after the last monthly package, or retire the monthly packages of that period first.

| Per-document kind | Old monthly package kinds that carry the same data |
|---|---|
| `SALES_INVOICE` | sales invoices for the taxpayers' system, VAT return |
| `PURCHASE_INVOICE` | VAT return |
| `PAYROLL_SUMMARY` | payroll insurance list, payroll tax list |
| `VOUCHER`, `RECEIPT`, `PAYMENT` | none |

Not done on purpose: the monthly background job is not switched off by the new transfer (the check above is the guard); the fixed-asset voucher and other auto vouchers are ordinary vouchers and follow the `VOUCHER` kind.
