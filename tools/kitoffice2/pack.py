# -*- coding: utf-8 -*-
import os, sys, zipfile, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from content import T, QUESTIONS
BASE = "/home/claude/kitoffice2/out"
O = BASE + "/PMIS_Office_R2_Test/"
def w(path, text, crlf=False):
    text = text.replace("\r\n", "\n")
    if crlf: text = text.replace("\n", "\r\n")
    os.makedirs(os.path.dirname(O + path), exist_ok=True)
    with open(O + path, "w", encoding="utf-8", newline="") as f: f.write(text)

w("1-Setup/INNCO_Setup/README.txt", "این پوشه عمداً خالی است.\nبسته نصب PMIS (Install-PMIS-Server.cmd و فایل‌های آن) را ارکستراتور پیش از ارسال به این پوشه اضافه می‌کند.\nPlaceholder: the installer package is added here later.\n", crlf=True)
w("README-FA.txt", """بسته آزمون اتوماسیون اداری PMIS (نسخه R2: پاسخ‌های شرکت)

محتویات:
1-Setup\\INNCO_Setup\\
   فعلاً فقط یادداشت؛ بسته نصب (Install-PMIS-Server.cmd و فایل‌های آن) را ارکستراتور اضافه می‌کند. رمز مدیر سیستم هنگام نصب تعیین می‌شود و در هیچ جا نوشته نمی‌شود.
2-Guides\\0-Test-Guide-Office-R2-FA.pdf
   راهنمای آزمون: %d آزمون (O-01 تا O-%d) در بخش‌های S0 تا S13 و بخش تازه R2 (O-107 تا O-%d)، با مراحل شماره‌دار و نتیجه مورد انتظار؛ و %d پرسش بی‌پاسخ برای شرکت.
3-Data\\
   clock-import-sample.xlsx و legacy-letters-sample.csv (مانند نسخه قبل)، pw-kara-sample.xlsx و pw-kara-ambiguous-dates.xlsx (ورود تردد از اکسل PW Kara)، و پوشه archive-pdf (پنج PDF آزمایشی برای پیوست به نامه‌های آرشیو).
4-Feedback\\Office-R2-Test-Feedback.xlsx
   فرم ثبت نتیجه (OK / مشکل / انجام نشد)، پاسخ پرسش‌های شرکت و نظرهای کلی.

نصب:
1. ابتدا کل فایل zip را Extract All کنید (مثلاً در C:\\PMIS). هرگز از داخل zip اجرا نکنید.
2. SQL Server 2022 Express باید قبلاً نصب شده باشد.
3. فایل 1-Setup\\INNCO_Setup\\Install-PMIS-Server.cmd را با Run as administrator اجرا کنید.
4. اگر پرسید «This computer is 'X', not 'PMIS'. Continue anyway?»، روی ماشین آزمایشی yes بنویسید. سایر رایانه‌ها آدرس http://<نام رایانه> را باز می‌کنند.

روش کار:
1. فقط روی پایگاه آزمایشی کار کنید. داده یا نام واقعی وارد نکنید.
2. راهنمای PDF را به ترتیب شماره آزمون‌ها اجرا کنید. آزمون‌های R2 پس از آزمون‌های S0 تا S13 هستند.
3. نتیجه هر آزمون را در فرم Excel بزنید و برای هر مشکل توضیح و نام فایل تصویر بنویسید.
4. پرسش‌های شرکت را با عنوان شغلی پاسخ‌دهنده پر کنید.
5. فرم پر شده را برای مدیر فنی بفرستید.
""" % (len(T), len(T), len(T), len(QUESTIONS)), crlf=True)

# the guide must be freshly generated
pdf = O + "2-Guides/0-Test-Guide-Office-R2-FA.pdf"
assert os.path.exists(pdf) and time.time() - os.path.getmtime(pdf) < 3600, "guide PDF is missing or stale"
zp = BASE + "/PMIS_Office_R2_Test.zip"
if os.path.exists(zp): os.remove(zp)
with zipfile.ZipFile(zp, "w", zipfile.ZIP_DEFLATED) as z:
    for root, dirs, files in os.walk(O):
        dirs.sort()
        for f in sorted(files):
            full = os.path.join(root, f)
            arc = "PMIS_Office_R2_Test/" + os.path.relpath(full, O).replace(os.sep, "/")
            z.write(full, arc)
print("packed", zp, os.path.getsize(zp))
