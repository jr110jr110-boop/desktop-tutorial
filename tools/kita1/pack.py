# -*- coding: utf-8 -*-
import os, shutil, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from content import T, QUESTIONS
O = "/home/claude/kita1/out/PMIS_Local_AI_Test/"
def w(path, text, crlf=False, enc="utf-8"):
    text = text.replace("\r\n", "\n")
    if crlf: text = text.replace("\n", "\r\n")
    with open(O + path, "w", encoding=enc, newline="") as f: f.write(text)

w("1-Setup/INNCO_Setup/README.txt", "این پوشه عمداً خالی است.\nبسته نصب PMIS (Install-PMIS-Server.cmd و فایل‌های آن) را ارکستراتور پیش از ارسال به این پوشه اضافه می‌کند.\nPlaceholder: the installer package is added here later.\n")

shutil.copy("/home/claude/backend/.github/ci-tools/mock-llm/mock_llm.py", O + "5-Mock-AI/mock_llm.py")
cmd = r'''@echo off
rem Start-Mock-AI.cmd - starts the two TEST models (not real AI) for the PMIS Local AI test.
rem Port 5091 = mock-primary, port 5092 = mock-fallback. They answer only on 127.0.0.1.
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
w("5-Mock-AI/Start-Mock-AI.cmd", cmd, crlf=True)
w("5-Mock-AI/README-FA.txt", """مدل آزمایشی هوش مصنوعی (اختیاری)

این پوشه یک «مدل آزمایشی» می‌دهد تا بتوان بخش هوش مصنوعی PMIS را بدون مدل واقعی آزمود.
این مدل واقعی نیست. برنامه کوچکی است که بر اساس چند کلیدواژه جمله‌های ثابت می‌دهد. گفتار به متن آن همیشه همان جمله ثابت را می‌نویسد و متن به گفتار آن یک صدای کوتاه ثابت می‌دهد. نتیجه ارزیابی روی آن نشانه کیفیت نیست.

پیش‌نیاز:
1. Python 3 برای ویندوز. روی رایانه‌ای که اینترنت دارد از python.org نصب‌کننده را بگیرید، هنگام نصب تیک «Add to PATH» را بزنید و روی ماشین سرور نصب کنید. (اگر از قبل Python 3 دارید لازم نیست.)

روش کار:
1. فایل Start-Mock-AI.cmd را دوبار کلیک کنید. دو پنجره سیاه باز می‌شود:
   - درگاه 5091 با نام مدل mock-primary
   - درگاه 5092 با نام مدل mock-fallback
   اگر دستور py نبود، خودش python را امتحان می‌کند. هر دو پنجره تا پایان آزمون باز بمانند.
2. در مرورگر همان ماشین http://127.0.0.1:5091/health را باز کنید. باید متن زیر را ببینید:
   {"status": "ok", "model": "mock-primary"}
3. این مدل‌ها در PMIS ثبت می‌شوند (راهنمای آزمون، بخش S2). نشانی را بدون /v1 در پایان بنویسید.

نکته مهم:
- این مدل فقط روی 127.0.0.1 (خود همان رایانه) جواب می‌دهد و از رایانه دیگر در دسترس نیست. پس باید روی همان ماشینی اجرا شود که برنامه PMIS روی آن نصب است. اگر سرور PMIS رایانه دیگری است، همین پوشه را به آن رایانه ببرید و آنجا اجرا کنید.
- هیچ‌چیز از شبکه بیرون نمی‌رود و به اینترنت نیازی ندارد.
- برای بستن، پنجره‌های سیاه را ببندید.
""", crlf=True)

w("README-FA.txt", """بسته آزمون هوش مصنوعی محلی PMIS (انتشار A1)

محتویات:
1-Setup\\INNCO_Setup\\Install-PMIS-Server.cmd
   نصب یا به‌روزرسانی روی سرور. بسته نصب توسط ارکستراتور اضافه می‌شود. رمز مدیر سیستم هنگام نصب تعیین می‌شود و در هیچ جا نوشته نمی‌شود.
2-Guides\\0-Test-Guide-Local-AI-FA.pdf
   راهنمای آزمون: %d آزمون در ۱۱ بخش (S0 تا S9 و تصمیم‌های شرکت در S10)، با مراحل شماره‌دار و نتیجه مورد انتظار؛ و %d پرسش برای شرکت.
3-Data\\eval-questions-list.xlsx و test-data-and-mock-questions.xlsx
   فهرست ۶۶ پرسش ارزیابی و داده‌های آزمون. فقط مرجع‌اند و خودکار وارد برنامه نمی‌شوند.
4-Feedback\\Local-AI-Test-Feedback.xlsx
   فرم ثبت نتیجه (OK / مشکل / انجام نشد)، پاسخ پرسش‌های شرکت و نظرهای کلی.
5-Mock-AI\\
   مدل آزمایشی اختیاری (Start-Mock-AI.cmd و README-FA.txt). مدل واقعی نیست.

نصب:
1. ابتدا کل فایل zip را Extract All کنید (مثلاً در C:\\PMIS). هرگز از داخل zip اجرا نکنید.
2. SQL Server 2022 Express باید قبلاً نصب شده باشد.
3. فایل 1-Setup\\INNCO_Setup\\Install-PMIS-Server.cmd را با Run as administrator اجرا کنید.
4. اگر پرسید «This computer is 'X', not 'PMIS'. Continue anyway?»، روی ماشین آزمایشی yes بنویسید. سایر رایانه‌ها آدرس http://<نام رایانه> را باز می‌کنند.

روش کار:
1. فقط روی پایگاه آزمایشی کار کنید. داده یا نام واقعی وارد نکنید.
2. راهنمای PDF را به ترتیب شماره آزمون‌ها (A-01 تا A-%02d) اجرا کنید. بخش S1 پیش از روشن‌کردن مدل آزمایشی است.
3. نتیجه هر آزمون را در فرم Excel بزنید و برای هر مشکل توضیح و نام فایل تصویر بنویسید.
4. پرسش‌های شرکت را با عنوان شغلی پاسخ‌دهنده پر کنید.
5. فرم پر شده را برای مدیر فنی بفرستید.
""" % (len(T), len(QUESTIONS), len(T)), crlf=True)
print("packed")
