# -*- coding: utf-8 -*-
# The commands of the technical manager (SQL and PowerShell) printed in the guide, section «دستورهای SQL و PowerShell مدیر فنی».
# The code is Latin (left to right, monospace, never converted to Persian digits); the Persian text around it uses the marks ⟦ ⟧ for pieces that stay Latin.
# Labels used by the scenarios: SQL-1, SQL-2, SQL-3, SQL-3b, SQL-4, SQL-5, PS-1, PS-2. The other labels (API-1, API-2, VOICE-1, VOICE-2) are named by the section only.
# Nothing here has been run against a real server by the author of the kit (no Windows, IIS or SQL Server on the build machine): the technical manager reads each command before running it.

MARK = "2000-01-01T00:00:00+00:00"

# (group, [snippets]); a snippet is a dict(id, title, lang, intro, code, note)
GROUPS = []

GROUPS.append(("دسترسی به رابط با PowerShell", [
 dict(id="API-1", lang="powershell", title="ورود با کاربر آزمایشی، گرفتن توکن و دو تابع کمکی",
  intro="روی ماشین سرور PowerShell را باز کنید (Windows PowerShell یا PowerShell 7؛ نیازی به Run as administrator نیست). نخست آدرس برنامه را بنویسید و با Get-Credential نام کاربری و رمز همان کاربر آزمایشی را بدهید (پنجره‌ی ورود می‌آید؛ رمز در هیچ فایلی ذخیره نمی‌شود). توکن را در ⟦$token⟧ نگه می‌دارد و تا بستن پنجره‌ی PowerShell همان می‌ماند. ورود با رابط، نشست مرورگر همان کاربر را می‌بندد (هر کاربر یک نشست دارد)؛ پس برای کاربری که هم‌زمان در مرورگر کار می‌کنید ورود را پس از پایان کار مرورگر بزنید.",
  code='''# API-1  (once per user; repeat the "sign in" part to change the user)
$base = "http://localhost"            # the address of the PMIS site

# sign in: a dialog asks for the user name and password (nothing is saved)
$cred  = Get-Credential -Message "PMIS test user"
$pw    = $cred.GetNetworkCredential().Password
$login = @{ UserName = $cred.UserName; Password = $pw } | ConvertTo-Json
$uri   = "$base/api/v1/Accounts/login"
$ct    = "application/json"
$r     = Invoke-RestMethod -Method Post -Uri $uri -Body $login -ContentType $ct
$token = $r.data.accessToken
$h     = @{ Authorization = "Bearer $token" }

# Get-Pmis returns the answer as an object (use it to read single values).
# Call-Pmis prints the whole answer as JSON, also when the server refuses (HTTP error).
function Get-Pmis([string]$Path, [string]$Method = "Get") {
    Invoke-RestMethod -Method $Method -Uri "$base$Path" -Headers $h -ContentType $ct
}
function Call-Pmis([string]$Path, [string]$Method = "Get") {
    try   { Get-Pmis $Path $Method | ConvertTo-Json -Depth 8 }
    catch { $m = $_.ErrorDetails.Message; if ($m) { $m } else { $_.Exception.Message } }
}''',
  note="اگر ورود پیام خطا داد، نام کاربری و رمز را در صفحه‌ی ورود مرورگر هم امتحان کنید. اگر پس از مدتی پاسخ‌ها با خطای 401 برگشتند، توکن تمام شده است؛ بخش «ورود» را دوباره بزنید."),
 dict(id="API-2", lang="powershell", title="نمونه‌ی درخواست‌ها (پس از API-1)",
  intro="هر خط یکی از درخواست‌هایی است که آزمون‌ها می‌گویند. پاسخ همه‌ی درخواست‌ها شامل ⟦success⟧، ⟦message⟧ و ⟦data⟧ است؛ رد شدن به‌خاطر نقش یا ورودی نادرست معمولاً با ⟦success=false⟧ و پیام فارسی برمی‌گردد (نه خطای سرور). متن فارسی در پنجره‌ی PowerShell ممکن است قطعه‌قطعه دیده شود؛ برای خواندن راحت‌تر پاسخ را در فایل بنویسید و با Notepad باز کنید (خط آخر).",
  code='''# API-2  (after API-1)
# the whole dashboard (cached for up to 120 seconds), and a fresh read
Call-Pmis "/api/v1/ceo/overview"
Call-Pmis "/api/v1/ceo/overview?refresh=true"
# one section (finance sales projects production quality inventory procurement service office)
Call-Pmis "/api/v1/ceo/section/finance"
# one drill with its parameters
Call-Pmis "/api/v1/ceo/drill?key=pc.portfolio&status=danger"
# the spoken text (what = headline | section | alerts | brief)
Call-Pmis "/api/v1/ceo/speech?what=alerts"
# store today's trend points (CEO or PMO only)
Call-Pmis "/api/v1/ceo/snapshot" "Post"

# count the rows of a drill, or show them as a table
$d = Get-Pmis "/api/v1/ceo/drill?key=qc.ncr_open&onlyOverdue=true"
$d.data.totalCount
$d.data.rows | Format-Table

# a long answer in a UTF-8 file (open it with Notepad)
Call-Pmis "/api/v1/ceo/overview" | Out-File "$PWD\\overview.json" -Encoding utf8''',
  note="بدون توکن یا با توکن نقش دیگر: ⟦success=false⟧ با پیام «شما مجوز این کار را ندارید.». درخواست بدون ⟦key⟧ یا با کلید ناشناخته پیام فارسی جداگانه دارد (آزمون @apirefusal@)."),
]))

GROUPS.append(("درخواست صوتی با PowerShell", [
 dict(id="VOICE-1", lang="powershell", title="ساختن فایل صوتی آزمایشی ui-open.wav",
  intro="مدل آزمایشی گفتار را نمی‌فهمد: «متنی» که برمی‌گرداند فقط به نام فایل بستگی دارد. فایلی که نامش با ⟦ui-open⟧ شروع شود «باز کن سفارش‌های تولید» شنیده می‌شود و هر فایل دیگر «خلاصه داشبورد مدیرعامل». خود صدا سکوت است. این دستور یک فایل WAV کوتاه (۰٫۱ ثانیه، ۱۶ کیلوهرتز، تک‌کاناله) در پوشه‌ی جاری می‌سازد.",
  code='''# VOICE-1  (creates ui-open.wav and speech.wav in the current folder)
# 0.1 second of silence, 16 kHz, mono, 16 bit
function New-Wav([string]$Path) {
    $data  = New-Object byte[] 3200
    $ms    = New-Object System.IO.MemoryStream
    $bw    = New-Object System.IO.BinaryWriter($ms)
    $ascii = [System.Text.Encoding]::ASCII
    $bw.Write($ascii.GetBytes("RIFF")); $bw.Write([int](36 + $data.Length))
    $bw.Write($ascii.GetBytes("WAVEfmt ")); $bw.Write([int]16)
    $bw.Write([int16]1); $bw.Write([int16]1)          # PCM, mono
    $bw.Write([int]16000); $bw.Write([int]32000)      # 16 kHz, bytes per second
    $bw.Write([int16]2); $bw.Write([int16]16)         # block size, 16 bit
    $bw.Write($ascii.GetBytes("data")); $bw.Write([int]$data.Length); $bw.Write($data)
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($Path, $ms.ToArray())
}
# the mock "hears" open-production-orders from this one
New-Wav "$PWD\\ui-open.wav"
# and the dashboard-summary sentence from this one
New-Wav "$PWD\\speech.wav"''',
  note="فایل را با هر نام دیگری که با ⟦ui-open⟧ شروع نمی‌شود بسازید تا گفتار «خلاصه داشبورد مدیرعامل» شنیده شود."),
 dict(id="VOICE-2", lang="powershell", title="فرستادن فایل صوتی با و بدون صفحه‌ی کاربر (پس از API-1 و VOICE-1)",
  intro="درخواست صوتی یک فرم چندبخشی (multipart) است. فیلدهای ⟦uiContext.*⟧ می‌گویند کاربر در کدام صفحه است؛ ابزارهای «کار روی صفحه» (باز کردن صفحه، خواندن هشدارها) فقط وقتی به مدل پیشنهاد می‌شوند که این فیلدها فرستاده شده باشند. curl.exe در Windows 10 و Windows Server 2019 و بعد از آن هست.",
  code='''# VOICE-2  (after API-1 and VOICE-1)
$u  = "$base/api/v1/ai/voice"
$au = "Authorization: Bearer $token"
$f1 = "file=@$PWD\\ui-open.wav;type=audio/wav"
$f2 = "file=@$PWD\\speech.wav;type=audio/wav"
# with the page of the user (page tools are offered), without it, and the summary sentence
curl.exe -s -X POST $u -H $au -F $f1 -F "uiContext.page=/ceo" -o "$PWD\\voice-with.json"
curl.exe -s -X POST $u -H $au -F $f1 -o "$PWD\\voice-without.json"
curl.exe -s -X POST $u -H $au -F $f2 -o "$PWD\\voice-summary.json"

$with = Get-Content "$PWD\\voice-with.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$with.success; $with.data.verified; $with.data.degraded
$with.data.toolCalls | Format-List     # name = ui_navigate, allowed = True
$with.data.actions   | Format-List     # type = navigate, link = /production/orders
$with.data.audioBase64.Length          # 4328 with the mock (a 0.1 s WAV)

$without = Get-Content "$PWD\\voice-without.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$without.data.actions.Count            # 0
$without.data.answer                   # Persian text: use Out-File if the console shows boxes''',
  note="متن‌های فارسی ⟦transcript⟧ و ⟦answer⟧ را برای مقایسه‌ی دقیق با فایل ⟦voice-with.json⟧ در Notepad (کدگذاری UTF-8) ببینید. ⟦audioBase64⟧ با مدل آزمایشی ۴۳۲۸ نویسه است؛ اگر سرور «متن به گفتار» خاموش باشد خالی است و ⟦degraded⟧ برابر True می‌شود (آزمون @speechdown@)."),
]))

GROUPS.append(("پایگاه داده و کار شبانه (SQL و PowerShell)", [
 dict(id="PS-1", lang="powershell", title="تاریخ «دو روز پیش از ماه شمسی جاری» (برای ساختن نقطه‌ی ماه قبل)",
  intro="نقطه‌ی «ماه قبل» باید تاریخی در ماه شمسی گذشته داشته باشد. این دستور اول ماه شمسی جاری را با همان تقویم برنامه حساب می‌کند و دو روز عقب می‌رود (پیش‌گیری از برخورد با عکسی که کار شبانه برای آخرین روز ماه گذشته نوشته است) و تاریخ را به صورت میلادی (سال-ماه-روز) می‌نویسد؛ همان را در ⟦SQL-3⟧ می‌گذارید.",
  code='''# PS-1
$pc    = New-Object System.Globalization.PersianCalendar
$today = (Get-Date).Date
# first day of the current Persian month
$first = $pc.ToDateTime($pc.GetYear($today), $pc.GetMonth($today), 1, 0, 0, 0, 0)
$ci    = [System.Globalization.CultureInfo]::InvariantCulture
$first.AddDays(-2).ToString("yyyy-MM-dd", $ci)''',
  note="خروجی مثلاً ⟦2026-10-21⟧ است (تاریخ میلادی؛ فقط نمونه). اگر امروز هنوز روز اول ماه شمسی است باز هم درست است."),
 dict(id="SQL-1", lang="sql", title="نقطه‌های امروز (انتظار: دقیقاً ۱۲ ردیف)",
  intro="دستورهای SQL را در SQL Server Management Studio اجرا کنید یا با sqlcmd: ⟦sqlcmd -S .\\SQLEXPRESS -E -d IRNNCO -i C:\\PMIS\\sql-1.sql⟧ (نام نمونه‌ی SQL Server همان است که نصب‌کننده در خط «Instance:» نوشت؛ دستور را در فایل ⟦sql-1.sql⟧ با Notepad بنویسید). همه‌ی آن‌ها فقط روی پایگاه آزمایشی اجرا شوند.",
  code='''-- SQL-1
USE IRNNCO;
SELECT [Key], [Value], [CreatedAt]
FROM dbo.CeoKpiSnapshots
WHERE [Date] = CAST(GETDATE() AS date)
ORDER BY [Key];''',
  note="ستون ⟦Value⟧ مبلغ ریالی کامل یا شمار است. ⟦CreatedAt⟧ لحظه‌ی آخرین نوشتن همان ردیف است."),
 dict(id="SQL-2", lang="sql", title="شمار ردیف‌های دیروز و امروز و ساعت نوشتن",
  intro="برای آزمون کار شبانه و جبران هنگام راه‌اندازی. ستون ⟦Rows⟧ شمار ردیف‌های هر روز است (کامل: ۱۲).",
  code='''-- SQL-2
USE IRNNCO;
SELECT [Date], COUNT(*) AS [Rows], MIN([CreatedAt]) AS FirstWrite, MAX([CreatedAt]) AS LastWrite
FROM dbo.CeoKpiSnapshots
WHERE [Date] >= DATEADD(day, -1, CAST(GETDATE() AS date))
GROUP BY [Date]
ORDER BY [Date];''',
  note="اگر هیچ ردیفی برای آن روزها نیست، خروجی خالی است."),
 dict(id="SQL-3", lang="sql", title="ساختن نقطه‌ی «ماه قبل» برای چهار شاخص (از ردیف‌های امروز)",
  intro="پیش از اجرا ⟦@d⟧ را با تاریخ خروجی ⟦PS-1⟧ عوض کنید. نقطه‌ی هر شاخص برابر مقدار امروز به‌علاوه‌ی یک عدد ثابت است (پس ماه قبل بیشتر و امروز کمتر است). ردیف‌های ساخته‌شده با این دستور ⟦CreatedAt⟧ برابر ⟦2000-01-01⟧ دارند تا ⟦SQL-4⟧ فقط همان‌ها را پاک کند. اگر برای آن روز و شاخص ردیف واقعی هست، چیزی افزوده نمی‌شود. «ثبت نقطه‌ی امروز روندها» باید پیش‌تر زده شده باشد (ردیف‌های امروز لازم است).",
  code='''-- SQL-3
USE IRNNCO;
DECLARE @d     date = '2026-10-21';   -- replace with the date printed by PS-1
DECLARE @today date = CAST(GETDATE() AS date);

INSERT INTO dbo.CeoKpiSnapshots ([Date], [Key], [Value], [CreatedAt])
SELECT @d, s.[Key],
       s.[Value] + CASE s.[Key]
                     WHEN 'cash'              THEN 1000000000   -- 1 billion rials more
                     WHEN 'openNcrs'          THEN 2            -- 2 more open NCRs
                     WHEN 'itemsBelowMinimum' THEN 3            -- 3 more items below minimum
                     WHEN 'stockValue'        THEN 500000000    -- 500 million rials more
                   END,
       '%s'
FROM dbo.CeoKpiSnapshots AS s
WHERE s.[Date] = @today
  AND s.[Key] IN ('cash', 'openNcrs', 'itemsBelowMinimum', 'stockValue')
  AND NOT EXISTS (SELECT 1 FROM dbo.CeoKpiSnapshots AS x
                  WHERE x.[Date] = @d AND x.[Key] = s.[Key]);

SELECT [Key], [Value], [CreatedAt] FROM dbo.CeoKpiSnapshots
WHERE [Date] = @d ORDER BY [Key];''' % MARK,
  note="خروجی نهایی باید چهار ردیف باشد (اگر پیش‌تر ساخته بودید همان‌ها). بعد از آن در داشبورد «بازخوانی» بزنید."),
 dict(id="SQL-3b", lang="sql", title="تغییر نقطه‌ی ماه قبل: «نقد و بانک» کمتر از امروز، «گزارش‌های عدم انطباق باز» برابر امروز",
  intro="همان ⟦@d⟧ ⟦SQL-3⟧. دو ردیف ساخته‌شده با ⟦SQL-3⟧ را عوض می‌کند: نقطه‌ی ماه قبل «نقد و بانک» یک میلیارد ریال زیر عدد امروز و نقطه‌ی «گزارش‌های عدم انطباق باز» برابر عدد امروز می‌شود. دو ردیف دیگر (⟦itemsBelowMinimum⟧ و ⟦stockValue⟧) همان‌طور می‌مانند.",
  code='''-- SQL-3b
USE IRNNCO;
DECLARE @d     date = '2026-10-21';   -- the same date as in SQL-3
DECLARE @today date = CAST(GETDATE() AS date);

UPDATE p
SET    p.[Value] = CASE p.[Key] WHEN 'cash' THEN t.[Value] - 1000000000
                                WHEN 'openNcrs' THEN t.[Value] END
FROM   dbo.CeoKpiSnapshots AS p
JOIN   dbo.CeoKpiSnapshots AS t ON t.[Key] = p.[Key] AND t.[Date] = @today
WHERE  p.[Date] = @d AND p.[Key] IN ('cash', 'openNcrs')
  AND  p.[CreatedAt] = '%s';

SELECT [Key], [Value], [CreatedAt] FROM dbo.CeoKpiSnapshots
WHERE [Date] = @d ORDER BY [Key];''' % MARK,
  note="پس از اجرا «بازخوانی» را در داشبورد بزنید."),
 dict(id="SQL-4", lang="sql", title="پاک کردن ردیف‌هایی که ⟦SQL-3⟧ ساخته است",
  intro="فقط ردیف‌هایی را پاک می‌کند که ⟦CreatedAt⟧ برابر ⟦2000-01-01⟧ دارند؛ ردیف‌های واقعی (کار شبانه و دکمه‌ی ثبت) دست نمی‌خورند.",
  code='''-- SQL-4
USE IRNNCO;
DELETE FROM dbo.CeoKpiSnapshots WHERE [CreatedAt] = '%s';
SELECT @@ROWCOUNT AS DeletedRows;''' % MARK,
  note="در پایان آزمون روندها همین را بزنید و در داشبورد «بازخوانی» کنید."),
 dict(id="SQL-5", lang="sql", title="پاک کردن ردیف‌های دیروز (برای آزمون جبران هنگام راه‌اندازی)",
  intro="پس از آن در ⟦SQL-2⟧ برای دیروز ردیفی نمی‌ماند. این دستور هر ردیف دیروز را پاک می‌کند، از جمله ردیف واقعی کار شبانه؛ فقط روی پایگاه آزمایشی بزنید.",
  code='''-- SQL-5
USE IRNNCO;
DELETE FROM dbo.CeoKpiSnapshots WHERE [Date] = DATEADD(day, -1, CAST(GETDATE() AS date));
SELECT @@ROWCOUNT AS DeletedRows;''',
  note=""),
 dict(id="PS-2", lang="powershell", title="بازیافت (Recycle) استخر برنامه‌ی INNCO",
  intro="با Run as administrator اجرا کنید. استخر ⟦INNCO⟧ بسته و دوباره برپا می‌شود؛ برنامه حدود ۳۰ ثانیه پس از برخاستن، «جبران» عکس دیروز را بررسی می‌کند. چند ثانیه‌ی اول پس از بازیافت برنامه در مرورگر آماده نیست و ورود ممکن است پس از اندکی صبر کار کند.",
  code='''# PS-2  (PowerShell as administrator)
$ac = "$env:windir\\System32\\inetsrv\\appcmd.exe"
& $ac recycle apppool /apppool.name:"INNCO"
# then open http://localhost once in the browser
# so that the application is certainly started''',
  note="اگر ⟦appcmd.exe⟧ پیدا نشد، در «IIS Manager ← Application Pools ← INNCO ← Recycle» همین کار را بکنید."),
]))
