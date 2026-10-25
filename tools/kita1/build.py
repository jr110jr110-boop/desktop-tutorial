# -*- coding: utf-8 -*-
import re, html, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import content
from content import *

OUT = "/home/claude/kita1/out/PMIS_Local_AI_Test"
HERE = os.path.dirname(os.path.abspath(__file__))
FA = str.maketrans("0123456789", "۰۱۲۳۴۵۶۷۸۹")
TOKEN = re.compile(r"This computer is 'X', not 'PMIS'\. Continue anyway\?|SQL Server 2022 Express|[A-Za-z0-9._-]+@[A-Za-z0-9.-]+\.[A-Za-z]+|https?://[A-Za-z0-9._:/-]*|[A-Za-z]:\\[A-Za-z0-9_.\\-]+|\d{4}/\d{2}/\d{2}|\d{1,2}:\d{2}|(?<![\d/])\d{2}/\d{2}(?![\d/])|\d-[A-Za-z][A-Za-z0-9\\._-]*|[A-Za-z][\w-]*\.(?:xlsx|csv|pdf|cmd|py|txt|wav)|\d+(?:\.\d+){2,}|(?<![\w])[A-Z]{1,4}-\d+|(?<![\w])[A-Za-z][A-Za-z0-9]*(?:-[A-Za-z0-9]+)+|(?<![\w])(?=[A-Za-z0-9]*\d)[A-Za-z][A-Za-z0-9]*")
MARK = re.compile(r"⟦(.*?)⟧", re.S)
def BDI_(t):
    return '<bdi class="l%s" dir="ltr">%s</bdi>' % ('' if ' ' in t else ' nw', t.replace('\\', '\\<wbr>'))

def fa(text, wrap=None):
    text = text.replace("%", "٪")
    out, pos = [], 0
    for m in TOKEN.finditer(text):
        out.append(text[pos:m.start()].translate(FA))
        tok = m.group(0)
        if re.fullmatch(r"[\d:/]+", tok): tok = tok.translate(FA)
        out.append(wrap(tok) if wrap else tok)
        pos = m.end()
    out.append(text[pos:].translate(FA))
    return "".join(out)

def h(text):
    e = html.escape(text.replace("<b>", "\x01").replace("</b>", "\x02"), quote=False)
    out, pos = [], 0
    for m in MARK.finditer(e):
        out.append(fa(e[pos:m.start()], wrap=lambda t: BDI_(t)))
        out.append(BDI_(m.group(1)))
        pos = m.end()
    out.append(fa(e[pos:], wrap=lambda t: BDI_(t)))
    return "".join(out).replace("\x01", "<b>").replace("\x02", "</b>")

def ltr(s):
    return re.sub(r"(?<![>\w])(A-\d\d)(?![\w<])", r'<bdi class="l nw" dir="ltr">\1</bdi>', s)

def plain(text):
    return fa(MARK.sub(lambda m: m.group(1), text))

SEC = dict(SECTIONS)

FOLDERS = [
 ("README-FA.txt", "راهنمای کوتاه پوشه"),
 ("1-Setup\\INNCO_Setup\\Install-PMIS-Server.cmd", "نصب روی سرور با دوبار کلیک (Run as administrator). در این نسخه پوشه فقط یک یادداشت دارد؛ بسته نصب را ارکستراتور بعداً اضافه می‌کند."),
 ("2-Guides\\0-Test-Guide-Local-AI-FA.pdf", "همین راهنما"),
 ("3-Data\\eval-questions-list.xlsx", "فهرست ۶۶ پرسش ارزیابی از کد برنامه (فقط مرجع؛ خودکار وارد نمی‌شود)"),
 ("3-Data\\test-data-and-mock-questions.xlsx", "داده‌های آزمون (کالاها، اسناد)، پرسش‌های ثابت این راهنما و کلیدواژه‌های مدل آزمایشی، فهرست هفت دستیار (فقط مرجع)"),
 ("4-Feedback\\Local-AI-Test-Feedback.xlsx", "فرم ثبت نتیجه (OK / مشکل / انجام نشد)، پاسخ پرسش‌های شرکت و نظرهای کلی"),
 ("5-Mock-AI\\Start-Mock-AI.cmd", "مدل آزمایشی اختیاری روی ویندوز (دو پنجره، درگاه 5091 و 5092). مدل واقعی نیست؛ پاسخ‌های ثابت می‌دهد. راهنمای فارسی کنار آن است."),
]
ROLES = [
 ("مدیر سیستم", "نقش «مدیر سیستم» (Admin)", "ساخت کاربران؛ رمز هنگام نصب تعیین می‌شود و در هیچ‌جا نوشته نمی‌شود."),
 ("معاونت PMO", "نقش «معاونت PMO»", "مدیریت کامل هوش مصنوعی: ثبت سرورها، نسخه‌های دستور، کلید و اجرای دستیارها، ارزیابی، ممیزی، بازسازی نمایه."),
 ("مدیرعامل", "نقش «مدیرعامل»", "داشبورد با دستیار و کارت گزارش روزانه (فقط او)، خواندن صفحه‌های مدیریت بدون تغییر، دیدن پاسخ‌های ارزیابی."),
 ("مدیر مالی، واحد مالی ۱ و ۲", "نقش‌های «مدیر مالی» و «مالی»", "ساخت و کنترل و قطعی‌کردن سند برای بازبینی ناهنجاری؛ دیدن و تصمیم درباره یادداشت‌های مالی. دو نفر مالی لازم است چون سازنده سند خودش کنترل نمی‌کند."),
 ("انباردار آزمایشی، سرپرست انبار، برنامه‌ریزی و کنترل پروژه", "نقش‌های «انبار»، «سرپرست انبار» و «برنامه‌ریزی و کنترل پروژه»", "موجودی اول دوره با دو تایید؛ ارسال پیش‌نویس انبار (برنامه‌ریز) و تایید آن (کاربر دیگر)."),
 ("کنترل کیفیت", "نقش «کنترل کیفیت»", "گزارش عدم انطباق و تصمیم درباره یادداشت دستیار کیفیت."),
 ("دبیرخانه، بازرگانی، فروش", "نقش‌های همنام", "فقط آزمون‌های شرطی و آزمون‌هایی که دسترسی کاربر عادی را می‌سنجند."),
]
DATES = [
 ("امروز", "روز اجرای هر آزمون. همه تاریخ‌ها نسبت به امروز نوشته شده‌اند؛ تاریخ شمسی امروز را از تقویم برنامه بخوانید. تاریخ ثابت و تاریخ میلادی در هیچ جای این آزمون نیست."),
 ("امروز منهای ۲ روز", "تاریخ سند دوم تکراری در آزمون @finmake@ (باید با سند اول در هفت روز باشد)."),
 ("آخرین جمعه گذشته", "تاریخ سند روز تعطیل در آزمون @finmake@ (حداکثر ۶۰ روز پیش، چون دستیار مالی ۶۰ روز گذشته را می‌بیند)."),
 ("فردا", "فقط برای دیدن رد شدن اجرای دستیار برای روز آینده در آزمون @futureday@."),
]
CSS = """
@font-face{font-family:BZar;src:url(BZar.ttf)}
@page{size:A4;margin:14mm 13mm}
body{font-family:BZar;font-size:12.5pt;line-height:1.7;color:#111}
h1{font-size:20pt;text-align:center;margin:0 0 3mm}
.sub{text-align:center;color:#444;margin-bottom:5mm}
h2{font-size:14.5pt;border-bottom:1.5px solid #333;padding-bottom:1mm;margin-top:6mm;margin-bottom:2mm;page-break-after:avoid}
table{width:100%;border-collapse:collapse;margin:2mm 0}
th,td{border:1px solid #555;padding:1.2mm 1.8mm;vertical-align:top;font-size:11pt;line-height:1.6}
tr{page-break-inside:avoid}
thead{display:table-header-group}
th{background:#e8e8e8}
.note{border:1px solid #888;background:#f5f5f5;padding:2mm 3mm;margin:3mm 0;page-break-inside:avoid}
.warn{border:1.5px solid #8a1c1c;background:#fbeeee;padding:2mm 3mm;margin:3mm 0;page-break-inside:avoid}
ol,ul{margin:1mm 0 2mm;padding-right:7mm}
td ol{margin:0;padding-right:5mm}
td li{margin:0 0 .6mm}
.id{font-weight:bold;white-space:nowrap;text-align:center}
.role{color:#333;font-size:10pt}
.nw{white-space:nowrap}
.l{unicode-bidi:isolate;direction:ltr;font-family:Tahoma,Arial,sans-serif;font-size:.85em}
.sec{break-after:avoid;page-break-after:avoid}.sec td{background:#d9d9d9;font-weight:bold;font-size:11.5pt}
.q td{height:22mm}
.pb{page-break-before:always}
"""

def R(s):
    return re.sub(r"@(\w+)@", lambda m: content._KEYS[m.group(1)], s)

def steps_html(steps):
    if len(steps) == 1:
        return "<div>%s</div>" % h(steps[0])
    return "<ol>%s</ol>" % "".join("<li>%s</li>" % h(s) for s in steps)

def build_html():
    n_tests = len(T)
    o = []
    o.append('<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><style>%s</style></head><body>' % CSS)
    o.append("<h1>راهنمای آزمون بسته هوش مصنوعی محلی (انتشار A1)</h1>")
    o.append('<div class="sub">نرم‌افزار جامع PMIS — دستیار هوشمند متنی و صوتی، سرورهای مدل داخلی، راستی‌آزمایی عددها، جابه‌جایی به سرور پشتیبان، ممیزی، نسخه‌های دستور، هفت دستیار زمان‌بندی‌شده و ارزیابی ۶۶ پرسش — آبان ۱۴۰۵</div>')

    o.append("<h2>۱. این بسته برای چیست</h2>")
    o.append("<p>"+h("این راهنما نخستین انتشار هوش مصنوعی محلی PMIS را آزمایش می‌کند. دستیار هوشمند به پرسش‌های مدیر و کارکنان با ابزارهای خود سامانه جواب می‌دهد و هر عددی را که می‌گوید با داده‌های سامانه تطبیق می‌دهد؛ اگر عدد قابل تطبیق نباشد، پاسخ مدل نشان داده نمی‌شود و نتیجه مستقیم سامانه می‌آید. بدون هیچ مدل هم داشبورد و بقیه برنامه کار می‌کند. مدل‌ها فقط روی شبکه داخلی شرکت‌اند و هیچ‌چیز از شبکه بیرون نمی‌رود. هفت دستیار زمان‌بندی‌شده در پس‌زمینه فقط «یادداشت» یا «پیش‌نویس» می‌سازند و تصمیم و تایید همیشه با انسان است.")+"</p>")
    o.append('<p>%s</p>' % h('<b>در این نسخه:</b> نام اشخاص نوشته نشده و فقط عنوان شغلی می‌آید. همه تاریخ‌ها شمسی و مبلغ‌ها ریال‌اند. برای آزمون به مدل واقعی نیازی نیست: پوشه 5-Mock-AI یک مدل آزمایشی می‌دهد. پایان راهنما پرسش‌هایی است که شرکت باید پاسخ دهد.'))
    o.append('<div class="warn">%s</div>' % h('<b>فقط روی پایگاه آزمایشی.</b> هیچ داده واقعی در این آزمون وارد نشود. همه نام‌ها، کدها و عددها آزمایشی‌اند. رمز و نام کاربری در این برگه‌ها و در فرم نوشته نمی‌شود.'))
    o.append('<div class="warn">%s</div>' % h('<b>مدل آزمایشی مدل واقعی نیست.</b> برنامه کوچکی است که بر اساس چند کلیدواژه جمله‌های ثابت می‌دهد تا بتوان مسیرهای برنامه را دید (فهرست در 3-Data\\test-data-and-mock-questions.xlsx). فقط روی 127.0.0.1 (خود همان رایانه) جواب می‌دهد. پس باید روی همان ماشین سرور PMIS اجرا شود. شمار «قبول» ارزیابی روی آن فقط بررسی فنی است و نشانه کیفیت نیست.'))
    o.append('<div class="note">%s</div>' % h('<b>ترتیب اجرا مهم است.</b> هر بخش به داده یا تنظیم بخش قبل تکیه دارد. ترتیب شماره آزمون‌ها (A-01 تا A-%s) همان ترتیب اجراست. بخش S1 عمداً پیش از روشن‌کردن مدل آزمایشی است. اگر آزمونی انجام نشد، وابسته‌های آن را «انجام نشد» بزنید و علت را بنویسید. آزمون‌هایی که با «(شرطی)» شروع می‌شوند فقط وقتی شرط‌شان برقرار است اجرا می‌شوند.' % ('%02d' % n_tests)))
    o.append('<div class="note">%s</div>' % h('<b>زمان‌بندی خودکار:</b> سرور به‌طور پیش‌فرض دستیارها را در ساعت خودشان خودکار هم اجرا می‌کند. اگر اجرای خودکار پیش از اجرای دستی شما کار را انجام داده باشد، خروجی دستی «کاری نبود» می‌شود؛ در آزمون‌ها نسخه جایگزین نتیجه نوشته شده است.'))

    o.append("<h2>۲. محتویات پوشه</h2>")
    o.append("<table><thead><tr><th style='width:44%'>پوشه / فایل</th><th>کاربرد</th></tr></thead><tbody>")
    for a, b in FOLDERS:
        o.append("<tr><td>%s</td><td>%s</td></tr>" % (h(a), h(b)))
    o.append("</tbody></table>")

    o.append("<h2>۳. نصب یا به‌روزرسانی</h2>")
    o.append("<ol>")
    for s in [
        "ابتدا کل فایل zip را استخراج کنید (راست‌کلیک ← Extract All، مثلاً در C:\\PMIS). هرگز برنامه را از داخل خود zip اجرا نکنید.",
        "SQL Server 2022 Express باید پیش از نصب روی همان ماشین نصب شده باشد.",
        "در پوشه استخراج‌شده فایل 1-Setup\\INNCO_Setup\\Install-PMIS-Server.cmd را با «Run as administrator» اجرا کنید (راست‌کلیک ← Run as administrator).",
        "اگر نصب‌کننده پرسید «This computer is 'X', not 'PMIS'. Continue anyway?» روی ماشین آزمایشی yes بنویسید. از رایانه‌های دیگر شبکه آدرس http://نام-رایانه را در مرورگر باز کنید.",
        "در پایان پیام سبز و آدرس‌ها را می‌بینید. اگر خطا دیدید پیام صفحه و فایل‌های پوشه logs را بفرستید.",
        "رمز مدیر سیستم (Admin) را نصب‌کننده تعیین می‌کند. آن را نزد مدیر شبکه نگه دارید؛ این رمز در هیچ برگه یا فرمی نوشته نمی‌شود.",
    ]:
        o.append("<li>%s</li>" % h(s))
    o.append("</ol>")

    o.append("<h2>۴. مدل آزمایشی (پوشه 5-Mock-AI)</h2>")
    o.append("<ol>")
    for s in [
        "Python 3 برای ویندوز لازم است. روی رایانه‌ای که اینترنت دارد از python.org نصب‌کننده را بگیرید و هنگام نصب تیک «Add to PATH» را بزنید؛ سپس نصب‌کننده را با حافظه قابل‌حمل به ماشین سرور ببرید. اگر Python از قبل هست، لازم نیست.",
        "روی ماشین سرور PMIS، فایل 5-Mock-AI\\Start-Mock-AI.cmd را دوبار کلیک کنید. دو پنجره سیاه باز می‌شود (درگاه 5091 با مدل mock-primary و درگاه 5092 با mock-fallback). تا پایان آزمون‌ها باز بمانند.",
        "اگر دستور py پیدا نشد، خود فایل از python استفاده می‌کند. اگر هیچ‌کدام نبود، پیام می‌دهد که Python را نصب کنید.",
        "آدرس‌های مدل در PMIS (آزمون @regchat@) بدون /v1 در پایان نوشته می‌شود: ⟦http://127.0.0.1:5091⟧. اگر /v1 اضافه شود برنامه خودش دوباره اضافه می‌کند و بررسی سلامت خطا می‌دهد.",
        "برای مدل واقعی همین مراحل ثبت سرور (آزمون @regchat@) با آدرس سرور مدل داخلی شرکت تکرار می‌شود؛ آدرس بیرون از شبکه داخلی پذیرفته نمی‌شود.",
    ]:
        o.append("<li>%s</li>" % h(R(s)))
    o.append("</ol>")

    o.append("<h2>۵. کاربران و نقش‌ها</h2>")
    o.append("<p>%s</p>" % h(R("کاربران را مدیر سیستم می‌سازد (آزمون @users@): «داده ها ← کاربران ← کاربر جدید»، یک کاربر برای هر عنوان شغلی. نام کاربری و رمز را مدیر سیستم تعیین و به صاحب آن تحویل می‌دهد.")))
    o.append("<table><thead><tr><th style='width:22%'>عنوان شغلی</th><th style='width:22%'>نقش در برنامه</th><th>کار در آزمون</th></tr></thead><tbody>")
    for a, b, c in ROLES:
        o.append("<tr><td>%s</td><td>%s</td><td>%s</td></tr>" % (h(a), h(b), h(c)))
    o.append("</tbody></table>")

    o.append("<h2>۶. قراردادهای آزمون</h2>")
    o.append("<ul>")
    for s in [
        "نام دکمه‌ها، تب‌ها و پیام‌ها همان است که در برنامه می‌بینید؛ پیام‌های «مورد انتظار» را عیناً با پیام صفحه مقایسه کنید. جایی که نوشته «پیامی با عبارت …» یعنی فقط همان عبارت در پیام باید باشد.",
        "«هوش مصنوعی ← مدیریت هوش مصنوعی ← …» یعنی در منوی کناری گروه «هوش مصنوعی» را باز کنید، «مدیریت هوش مصنوعی» را بزنید و زبانه نام‌برده را انتخاب کنید.",
        "پرسش‌هایی که در گیومه برای دستیار نوشته شده‌اند را عیناً بنویسید؛ مدل آزمایشی بر اساس واژه‌های کلیدی همین جمله‌ها جواب می‌دهد.",
        "هر پیام خطا یا رفتار عجیب را با یک تصویر ثبت کنید و نام فایل تصویر را در ستون «نام فایل تصویر» بنویسید.",
        "داده‌های آزمون را پاک نکنید؛ آزمون‌های بعد به آن‌ها تکیه دارند.",
    ]:
        o.append("<li>%s</li>" % h(s))
    o.append("</ul>")
    o.append("<table><thead><tr><th style='width:26%'>تاریخ</th><th>کاربرد</th></tr></thead><tbody>")
    for a, b in DATES:
        o.append("<tr><td>%s</td><td>%s</td></tr>" % (h(a), h(R(b))))
    o.append("</tbody></table>")

    o.append("<h2>۷. داده‌های آزمون و اعداد کلیدی</h2>")
    o.append("<ul>")
    for s in [
        "انبار ⟦WH-AI1⟧. کالا ۱ «قطعه آزمون هوش مصنوعی ۱» کد ⟦88.10.00.001.1⟧، موجودی اول دوره 20 عدد با فی 1,000,000، نقطه سفارش 50: پس پیش‌نویس انبار مقدار 30 می‌سازد. کالا ۲ کد ⟦88.10.00.002.1⟧، 100 عدد با فی 500,000، نقطه سفارش 10.",
        "اعداد کل (ارزش موجودی و تعداد زیر حداقل) را در آزمون @base@ از خود داشبورد بخوانید؛ اگر پایگاه شما داده دیگری هم دارد، با این راهنما برابر نیست.",
        "مشتری مالی «مشتری آزمون هوش مصنوعی». سندهای تکراری 123,456,000؛ سند رُند 2,000,000,000؛ سند غیررند 2,050,000,000؛ سند جمعه 77,000,000. حساب‌ها 10301 (با تفصیلی مشتری) و 60102.",
        "پرسش‌های ثابت دستیار: «%s»، «%s»، «%s»، «%s»، «%s»، «%s»، «%s»." % (Q_DASH, Q_FORGE, Q_CASH, Q_FORCE, Q_REPEAT, Q_HI, Q_STOCK),
    ]:
        o.append("<li>%s</li>" % h(R(s)))
    o.append("</ul>")

    o.append('<h2 class="pb">۸. سناریوها (%s آزمون)</h2>' % plain(str(n_tests)))
    o.append("<p>هر ردیف یک آزمون است. کد (مثل A-26) همان کد در فرم Excel است.</p>")
    cur = None
    o.append("<table><thead><tr><th style='width:7%'>کد</th><th style='width:16%'>عنوان / نقش</th><th style='width:40%'>مراحل</th><th>نتیجه مورد انتظار</th></tr></thead><tbody>")
    for x in T:
        if x["sec"] != cur:
            cur = x["sec"]
            o.append('<tr class="sec"><td colspan="4">%s</td></tr>' % h(SEC[cur]))
        o.append('<tr><td class="id">%s</td><td><b>%s</b><div class="role">%s</div></td><td>%s</td><td>%s</td></tr>' % (
            h(x["id"]), h(x["title"]), h(x["role"]), steps_html(x["steps"]), h(x["exp"])))
    o.append("</tbody></table>")

    o.append('<h2 class="pb">۹. تصمیم‌های شرکت که باید تایید شود</h2>')
    o.append("<p>این %s مورد تصمیم‌هایی است که شرکت باید بگیرد. پاسخ را در برگه «پرسش‌های شرکت» فایل Excel بنویسید (پاسخ‌دهنده با عنوان شغلی، نه نام)." % plain(str(len(QUESTIONS))) + "</p>")
    o.append("<table><thead><tr><th style='width:5%'>ردیف</th><th style='width:50%'>پرسش</th><th style='width:12%'>آزمون</th><th>پاسخ و تصمیم</th></tr></thead><tbody>")
    for i, q in enumerate(QUESTIONS, 1):
        o.append('<tr class="q"><td class="id">%s</td><td>%s</td><td>%s</td><td></td></tr>' % (plain(str(i)), h(q), h(LINKED[i]).replace('، ', '<br>')))
    o.append("</tbody></table>")
    o.append("</body></html>")
    return ltr("\n".join(o))

if __name__ == "__main__":
    s = build_html()
    open(os.path.join(HERE, "guide.html"), "w", encoding="utf-8").write(s)
    print(len(T), "tests", len(s))
