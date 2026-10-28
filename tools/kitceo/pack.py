# -*- coding: utf-8 -*-
"""Writes the README files and the mock-AI folder of the CEO executive dashboard (release D1) test kit and zips the kit.
Run after build.py/render (run.sh) and feedback.py: python3 pack.py"""
import os, re, subprocess, sys, time, zipfile
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import content as C
import guide_text as G
from content import T, SECTIONS, QUESTIONS, STAR
import pypdf

SHA = "1672cba"
BASE = "/home/claude/kitceo/out"
O = BASE + "/PMIS_CEO_Dashboard_Test/"
PDF = O + "2-Guides/0-Test-Guide-CEO-Dashboard-FA.pdf"
XLSX = O + "4-Feedback/CEO-Dashboard-Test-Feedback.xlsx"
INSTALL = O + "1-Setup/INNCO_Setup/"
BACKEND = "/home/claude/backend"


def w(path, text, crlf=True):
    text = text.replace("\r\n", "\n")
    if crlf:
        text = text.replace("\n", "\r\n")
    os.makedirs(os.path.dirname(O + path), exist_ok=True)
    with open(O + path, "w", encoding="utf-8", newline="") as f:
        f.write(text)


def clean(s):
    """Text of the guide for a plain file: references to the scenarios resolved, the left-to-right marks removed."""
    return C._ref(s).replace("⟦", "").replace("⟧", "")


# ---------------------------------------------------------------- inputs that must be right before anything is packed
for p in (PDF, XLSX):
    assert os.path.exists(p) and time.time() - os.path.getmtime(p) < 3600, "missing or stale: " + p
info = open(INSTALL + "BUILD-INFO.txt", encoding="utf-8").read()
m = re.search(r"^source_sha=([0-9a-f]{40})\s*$", info, re.M)
assert m and m.group(1).startswith(SHA), "the package is not build " + SHA
assert "SET-BY-INSTALL-ps1" in open(INSTALL + "app/appsettings.json", encoding="utf-8").read(), "appsettings placeholder missing"
assert not os.path.exists(INSTALL + "publish.log"), "publish.log must not be packed"
pages = len(pypdf.PdfReader(PDF).pages)
n_tests, n_star, n_q, n_sec = len(T), len(STAR), len(QUESTIONS), len(SECTIONS)
last = "T-%02d" % n_tests
K = C._KEYS
t_mock, t_ep, t_users, t_base = K["mockstart"], K["endpoints"], K["users"], K["base"]

# ---------------------------------------------------------------- root README
w("README-FA.txt", """بسته آزمون «داشبورد مدیرعامل» PMIS (انتشار D1)

این بسته داشبورد اجرایی مدیرعامل را می‌آزماید: هشت شاخص اصلی، نُه بخش با نمودار، هشدارها، گزارش روزانه، 68 جدول جزئیات، «تحلیل هوشمند» هر بخش و کل شرکت، کشوی «دستیار هوشمند»، حالت صوتی (شنیدن، گفتن و انجام کار)، حالت تیره، چاپ، روندها و نمای معاونت PMO با پنهان‌سازی مبلغ‌های حقوق.

محتویات:
1-Setup\\INNCO_Setup\\
   بسته نصب PMIS (Install-PMIS-Server.cmd و فایل‌های آن). نسخه‌ی بسته در فایل BUILD-INFO.txt کنار آن است: خط source_sha باید با %(sha)s شروع شود. رمز مدیر سیستم هنگام نصب تعیین می‌شود و در هیچ جا نوشته نمی‌شود. فایل‌های نصب‌کننده را دست نزنید.
2-Guides\\0-Test-Guide-CEO-Dashboard-FA.pdf
   راهنمای آزمون (%(pages)d صفحه): %(tests)d آزمون (T-01 تا %(last)s) در %(secs)d بخش (S0 تا S%(smax)d)، با مراحل شماره‌دار و نتیجه مورد انتظار؛ %(star)d مفروضه‌ی ★ برای تایید شرکت؛ %(q)d پرسش بی‌پاسخ برای شرکت؛ و پیوست‌ها (شاخص‌ها و آستانه‌ها، نمودارها، هشدارها، 68 جدول جزئیات، نمونه محاسبه دستی، دستورهای ویژه مدیر فنی).
3-Data\\README-FA.txt
   فقط یک یادداشت. این بسته داده‌ی آماده ندارد: داشبورد داده‌ی پایگاه آزمایشی شما را نشان می‌دهد (در پایگاه خالی همه‌ی عددها صفر است). یادداشت می‌گوید هر بخش از کدام ماژول یا بسته‌ی آزمون قبلی پر می‌شود.
4-Feedback\\CEO-Dashboard-Test-Feedback.xlsx
   فرم ثبت نتیجه: برگه «نتایج تست» (برای هر آزمون OK، مشکل یا انجام نشد)، برگه «مفروضات ★»، برگه «پرسش‌های شرکت»، برگه «نظرهای کلی» و برگه «راهنما».
5-Mock-AI\\
   مدل آزمایشی هوش مصنوعی (Start-Mock-AI.cmd، mock_llm.py و README-FA.txt). مدل واقعی نیست؛ برای آزمودن «تحلیل هوشمند»، دستیار و حالت صوتی بدون مدل واقعی لازم است.

نصب:
1. ابتدا کل فایل zip را Extract All کنید (مثلاً در C:\\PMIS). هرگز از داخل zip اجرا نکنید.
2. SQL Server 2022 Express باید قبلاً روی همان ماشین نصب شده باشد. برای مدل آزمایشی روی همان ماشین Python 3 هم لازم است (از python.org؛ هنگام نصب «Add to PATH» را بزنید). مرورگر: Chrome یا Edge.
3. فایل 1-Setup\\INNCO_Setup\\BUILD-INFO.txt را باز کنید: خط source_sha باید با %(sha)s شروع شود. اگر چیز دیگری بود آزمون را شروع نکنید و به مدیر فنی خبر بدهید.
4. فایل 1-Setup\\INNCO_Setup\\Install-PMIS-Server.cmd را با Run as administrator اجرا کنید.
5. اگر پرسید «This computer is 'X', not 'PMIS'. Continue anyway?»، روی ماشین آزمایشی yes بنویسید. در پایان پیام «INSTALLATION FINISHED» و نشانی‌ها دیده می‌شود. اگر پایگاه تازه ساخته شده باشد، نصب‌کننده زیر «First sign-in (new database)» نام کاربری و رمز Admin را فقط یک بار می‌نویسد؛ همان لحظه یادداشت کنید و پس از نخستین ورود تغییر دهید.
6. روی همان ماشین سرور در مرورگر http://localhost را باز کنید. (میکروفون و حالت صوتی فقط با http://localhost یا نشانی https کار می‌کند؛ از رایانه دیگر با http://نام-رایانه فقط گفت‌وگوی متنی کار می‌کند.)

روش کار:
1. فقط روی پایگاه آزمایشی کار کنید. داده یا نام واقعی وارد نکنید.
2. راهنمای PDF را به ترتیب شماره آزمون‌ها (T-01 تا %(last)s) اجرا کنید. بخش S0 آماده‌سازی است (کاربران، مدل آزمایشی، عددهای پایه) و پیش از بقیه انجام می‌شود: کاربران آزمون را بسازید (آزمون %(users)s)، مدل آزمایشی را با 5-Mock-AI\\Start-Mock-AI.cmd راه بیندازید (آزمون %(mock)s)، سرورهای آن را در PMIS ثبت کنید (آزمون %(ep)s) و عددهای پایه را یادداشت کنید (آزمون %(base)s). جزئیات در 5-Mock-AI\\README-FA.txt است.
3. بیشتر آزمون‌ها عددهای داشبورد را با صفحه‌ی ماژول‌ها می‌سنجند؛ در پایگاه خالی همه‌ی عددها صفرند (3-Data\\README-FA.txt را ببینید).
4. نتیجه هر آزمون را در فرم Excel بزنید و برای هر مشکل توضیح و نام فایل تصویر بنویسید. مفروضات ★ و پرسش‌های شرکت را با عنوان شغلی پاسخ‌دهنده پر کنید.
5. فرم پر شده را با نام CEO-Dashboard-Test-Feedback-PMO.xlsx (به‌جای PMO عنوان شغلی خودتان را با حروف لاتین بنویسید) برای مدیر فنی بفرستید.
""" % dict(sha=SHA, pages=pages, tests=n_tests, last=last, secs=n_sec, smax=n_sec - 1, star=n_star, q=n_q,
           users=t_users, mock=t_mock, ep=t_ep, base=t_base))

# ---------------------------------------------------------------- 3-Data README (no data files)
rows = []
for sec, src, empty in G.DATA_ROWS:
    rows.append("%s\n   از کجا پر می‌شود: %s\n   در پایگاه خالی: %s" % (clean(sec), clean(src), clean(empty)))
w("3-Data/README-FA.txt", """داده‌ی آزمون داشبورد مدیرعامل PMIS (انتشار D1)

این پوشه عمداً فایل داده ندارد.

داشبورد هیچ داده‌ای از خودش نمی‌سازد. همه‌ی عددها، نمودارها، هشدارها و جدول‌های آن از ماژول‌های PMIS در همان پایگاه آزمایشی خوانده می‌شود. پس هر چه آزمون‌های بسته‌های قبلی (فروش و خدمات پس از فروش، تولید و نگهداری، اتوماسیون اداری، هوش مصنوعی محلی) و آزمون‌های ماژول‌ها در پایگاه شما ساخته باشند، همان را می‌بینید. اگر پایگاه شما از قبل پر است، عددها واقعی‌اند و بیشتر آزمون‌ها بدون کار اضافه انجام می‌شوند.

اگر پایگاه خالی است:
- همه‌ی کاشی‌ها صفر و وضعیت‌ها «عادی» است (این خودش یک آزمون است) و نمودارها پیام «برای این نمودار هنوز داده‌ای ثبت نشده است.» می‌نویسند.
- جدول جزئیات بدون ردیف پیام «ردیفی برای نمایش نیست؛ هیچ رکوردی در این جدول با شرایط امروز جور نیست.» می‌دهد.
- برای آزمون‌های وابسته به داده (در راهنما با «(شرطی)» نشان خورده‌اند) یا پیش از آزمون در ماژول مربوط چند رکورد آزمایشی بسازید، یا آزمون را «انجام نشد» بزنید و علت را بنویسید.

هر بخش داشبورد از کجا پر می‌شود:

%(rows)s

روندها: خط کوچک زیر کاشی‌های اصلی و قرص «نسبت به ماه قبل» از «عکس شبانه‌ی شاخص‌ها» می‌آید. در پایگاه تازه عکس ماه‌های قبل نیست؛ فقط چهار شاخص (نقد و بانک، مطالبات باز، گزارش‌های عدم انطباق باز، درخواست‌های خارج از مهلت SLA) از سندهای ماه‌های گذشته تاریخچه می‌گیرند و تنها اگر چنین سندی در پایگاه هست؛ بقیه «بدون مقایسه با ماه قبل» می‌نویسند. آزمون‌های %(t75)s تا %(t77)s این را می‌آزمایند؛ ساخت نمونه‌ی ماه قبل با دستورهای پیوست 6 راهنما کار مدیر فنی است.

نکته‌ها:
1. عددهای روز نخست را در آزمون %(base)s یادداشت کنید؛ بیشتر آزمون‌ها همان‌ها را با صفحه‌ی ماژول‌ها می‌سنجند.
2. پس از هر تغییر داده «بازخوانی» را بزنید: حافظه‌ی موقت سرور تا 2 دقیقه عدد قبلی را نگه می‌دارد (آزمون %(refresh)s).
3. فقط داده‌ی آزمایشی و ساختگی وارد کنید؛ نام و عدد واقعی شرکت یا مشتری وارد نشود. عکس‌های راهنما هم با داده‌ی ساختگی گرفته شده‌اند و عددهای شما با آن‌ها فرق می‌کند.
""" % dict(rows="\n\n".join(rows), base=t_base, refresh=K["refresh"], t75=K["trend"], t77=K["trend_job"]))

# ---------------------------------------------------------------- 5-Mock-AI: the mock server of the build, the start script, the README
mock = subprocess.run(["git", "-C", BACKEND, "show", SHA + ":.github/ci-tools/mock-llm/mock_llm.py"], check=True, capture_output=True).stdout
assert b"--port" in mock and b"/v1/audio/speech" in mock and "طبق نتایج سامانه".encode("utf-8") in mock
os.makedirs(O + "5-Mock-AI", exist_ok=True)
with open(O + "5-Mock-AI/mock_llm.py", "wb") as f:
    f.write(mock)

cmd = r'''@echo off
rem Start-Mock-AI.cmd - starts the two TEST models (not real AI) for the PMIS CEO dashboard test.
rem Port 5091 = mock-primary, port 5092 = mock-fallback. They answer only on 127.0.0.1 (this computer).
cd /d "%~dp0"
set PY=
py -3 --version >nul 2>&1
if not errorlevel 1 set PY=py -3
if "%PY%"=="" (
  python --version >nul 2>&1
  if not errorlevel 1 set PY=python
)
if "%PY%"=="" (
  echo Python 3 was not found. Install Python 3 from python.org and tick "Add to PATH", then run this file again.
  pause
  exit /b 1
)
echo Using: %PY%
start "mock-primary 5091" cmd /k "%PY% mock_llm.py --port 5091 --model mock-primary"
start "mock-fallback 5092" cmd /k "%PY% mock_llm.py --port 5092 --model mock-fallback"
echo Two windows were opened. Keep both open during the test.
echo Check: http://127.0.0.1:5091/health and http://127.0.0.1:5092/health
pause
'''
w("5-Mock-AI/Start-Mock-AI.cmd", cmd)

servers = "\n".join("   %d. %s: نوع «%s»، نشانی %s، مدل %s، اولویت %s" % (i, a, b, c, d, e) for i, (a, b, c, d, e) in enumerate(G.MOCK_SERVERS, 1))
phrases = "\n".join("- %s\n  مدل چه می‌کند: %s\n  پاسخ یا اثر: %s\n  آزمون: %s" % (clean(a), clean(b), clean(c), clean(d)) for a, b, c, d in G.MOCK_ROWS)
w("5-Mock-AI/README-FA.txt", """مدل آزمایشی هوش مصنوعی (برای آزمون داشبورد مدیرعامل)

این پوشه یک «مدل آزمایشی» می‌دهد تا «تحلیل هوشمند»، «دستیار هوشمند» و حالت صوتی داشبورد مدیرعامل PMIS را بدون مدل واقعی بتوان آزمود.
این مدل واقعی نیست. برنامه‌ی کوچکی (mock_llm.py، فقط Python) است که بر پایه‌ی چند واژه یک ابزار را صدا می‌زند و با جمله‌ی ثابت یا «طبق نتایج سامانه: …» و سه عدد اول نتیجه‌ی ابزار جواب می‌دهد؛ گفتار را همیشه به یک جمله‌ی ثابت تبدیل می‌کند؛ متن را به صدای 0.1 ثانیه‌ای سکوت (WAV) تبدیل می‌کند؛ و برای بردارسازی برداری ثابت می‌دهد. نتیجه‌ی ارزیابی روی آن نشانه‌ی کیفیت نیست؛ فقط می‌سنجد که PMIS درست با یک مدل حرف می‌زند، عددها را راستی‌آزمایی می‌کند و هر کار را فقط با دسترسی خود کاربر انجام می‌دهد.
فایل mock_llm.py همان فایل ساخت %(sha)s است؛ ویرایشش نکنید.

پیش‌نیاز:
1. Python 3 برای ویندوز. روی رایانه‌ای که اینترنت دارد از python.org نصب‌کننده را بگیرید، هنگام نصب تیک «Add to PATH» را بزنید و روی ماشین سرور PMIS نصب کنید. (اگر از قبل Python 3 دارید لازم نیست.)

روش کار:
1. فایل Start-Mock-AI.cmd را دوبار کلیک کنید. دو پنجره‌ی سیاه باز می‌شود:
   - درگاه 5091 با نام مدل mock-primary
   - درگاه 5092 با نام مدل mock-fallback
   اگر دستور py نبود، خودش python را امتحان می‌کند. هر دو پنجره تا پایان آزمون باز بمانند.
2. در مرورگر همان ماشین http://127.0.0.1:5091/health را باز کنید. باید متن زیر را ببینید:
   {"status": "ok", "model": "mock-primary"}
   و برای http://127.0.0.1:5092/health همان متن با mock-fallback.
3. سرورها را در خود PMIS ثبت کنید (با کاربر مدیر سیستم یا معاونت PMO؛ آزمون %(ep)s راهنما):
   هوش مصنوعی ← مدیریت هوش مصنوعی ← سرورهای هوش مصنوعی ← سرور جدید
   نشانی را بدون /v1 در پایان بنویسید؛ مهلت 20؛ فعال. پنج سرور:
%(servers)s
   گفت‌وگوی متنی، گفتار به متن و متن به گفتار همه روی درگاه 5091 جواب می‌دهند؛ درگاه 5092 فقط جانشین گفت‌وگوست (با بستن پنجره‌ی 5091 پاسخ‌های متنی از mock-fallback می‌آیند؛ گفتار و صدا قطع می‌شود؛ آزمون‌های %(aidown)s و %(speechdown)s).
4. هیچ تنظیمی در فایل app\\appsettings.json لازم نیست. نشانی مدل‌ها در آن فایل نیست؛ بخش Ai آن فقط مهلت‌ها و چند عدد دیگر دارد (ChatTimeoutSeconds، VoiceTimeoutSeconds، BreakerSeconds و ...) و برای آزمون همان مقدارهای نصب‌شده بماند.

مدل آزمایشی به این واژه‌ها چه جوابی می‌دهد:
ترتیب فهرست ترتیب بررسی است: نخستین مورد که با پرسش جور شود برنده است. «سامانه» یعنی PMIS: ابزارها فقط با دسترسی خود کاربر کار می‌کنند و عدد هر پاسخ با نتیجه‌ی ابزار سنجیده می‌شود. «واژه‌ی کامل» یعنی کلمه باید جدا باشد («ریز» در «برنامه‌ریزی» یا «واریز» نمی‌آید).
%(phrases)s
- «تکرار» (از بسته‌ی هوش مصنوعی محلی؛ پیش از همه‌ی واژه‌های بالا بررسی می‌شود)
  مدل چه می‌کند: ابزاری صدا نمی‌زند و سه عدد اول آخرین نتیجه‌ی ابزار در همان گفت‌وگو را دوباره می‌گوید.
  پاسخ یا اثر: «طبق نتایج سامانه: …» (یا همان جمله‌ی سلام اگر در گفت‌وگو هنوز نتیجه‌ی ابزاری نیست).
  آزمون: در این راهنما آزمونی برای آن نیست.

میکروفون و صدا:
- مرورگر میکروفون را فقط به صفحه‌ی «امن» می‌دهد: نشانی https یا localhost. روی ماشین سرور با http://localhost کار می‌کند؛ از رایانه‌ی دیگر با http://نام-سرور (بدون https) دکمه‌ی میکروفون خاکستری است و فقط گفت‌وگوی متنی کار می‌کند. آزمون‌های صدا را روی ماشین سرور انجام دهید؛ نخستین بار مرورگر (Chrome یا Edge) اجازه می‌پرسد: Allow.
- گفتار به متن: مدل آزمایشی صدا را نمی‌شنود؛ متن فقط به نام فایل بستگی دارد: همیشه «خلاصه داشبورد مدیرعامل»، و اگر نام فایل با ui-open شروع شود «باز کن سفارش‌های تولید». مرورگر فایلی با این نام نمی‌فرستد؛ این حالت را مدیر فنی با PowerShell می‌آزماید (پیوست 6 راهنما).
- متن به گفتار: یک صدای 0.1 ثانیه‌ای سکوت؛ صدای شنیدنی نیست. آنچه دیده می‌شود کارت «در حال گفتن…» و متن پاسخ است.

با مدل آزمایشی دیده نمی‌شود:
- «تحلیل کن بخش فروش» به‌عنوان تحلیل بخش، «نشانم بده پروژه‌های قرمز» (فیلتر جدول)، باز کردن جدول با فیلتر، پیمایش به بخش، خواندن یک بخش یا خلاصه یا گزارش با فرمان صوتی، فهم گفتار فارسی و صدای شنیدنی. این رفتارها را فقط یک مدل واقعی نشان می‌دهد؛ در راهنما با «با یک مدل واقعی» نوشته شده‌اند و پذیرش مدل واقعی پرسش 2 شرکت است.

نکته‌ی مهم:
- این مدل‌ها فقط روی 127.0.0.1 (خود همان رایانه) جواب می‌دهند و از رایانه‌ی دیگر در دسترس نیستند. پس باید روی همان ماشینی اجرا شوند که برنامه‌ی PMIS روی آن نصب است. اگر سرور PMIS رایانه‌ی دیگری است، همین پوشه را به آن رایانه ببرید و آنجا اجرا کنید.
- هیچ‌چیز از شبکه بیرون نمی‌رود و به اینترنت نیازی ندارد.
- برای بستن، پنجره‌های سیاه را ببندید.
""" % dict(sha=SHA, ep=t_ep, servers=servers, aidown=K["aidown"], speechdown=K["speechdown"], phrases=phrases))

# ---------------------------------------------------------------- the tree must be exactly what the brief asks for
want_top = {"1-Setup", "2-Guides", "3-Data", "4-Feedback", "5-Mock-AI", "README-FA.txt"}
assert set(os.listdir(O)) == want_top, os.listdir(O)
assert sorted(os.listdir(O + "2-Guides")) == ["0-Test-Guide-CEO-Dashboard-FA.pdf"]
assert sorted(os.listdir(O + "3-Data")) == ["README-FA.txt"]
assert sorted(os.listdir(O + "4-Feedback")) == ["CEO-Dashboard-Test-Feedback.xlsx"]
assert sorted(os.listdir(O + "5-Mock-AI")) == ["README-FA.txt", "Start-Mock-AI.cmd", "mock_llm.py"]
assert os.listdir(O + "1-Setup") == ["INNCO_Setup"]

# ---------------------------------------------------------------- zip
zp = BASE + "/PMIS_CEO_Dashboard_Test.zip"
if os.path.exists(zp):
    os.remove(zp)
with zipfile.ZipFile(zp, "w", zipfile.ZIP_DEFLATED) as z:
    for root, dirs, files in os.walk(O):
        dirs.sort()
        for f in sorted(files):
            full = os.path.join(root, f)
            z.write(full, "PMIS_CEO_Dashboard_Test/" + os.path.relpath(full, O).replace(os.sep, "/"))
with zipfile.ZipFile(zp) as z:
    assert z.testzip() is None
    names = z.namelist()
print("packed", zp, os.path.getsize(zp), "bytes,", len(names), "files; guide", pages, "pages; package", m.group(1))
