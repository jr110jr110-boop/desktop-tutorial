# -*- coding: utf-8 -*-
"""Builds guide.html of the CEO executive dashboard (release D1) test kit.
Run: python3 build.py   (then render.js turns guide.html into the PDF; tocpages.py fills the page numbers of the contents list)."""
import re, html, sys, os, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import content as C
from content import T, SECTIONS, QUESTIONS, LINKED, STAR, STAR_LINKED
import kpis as K
import example as EX
import snippets as SN
import guide_text as G

OUT = "/home/claude/kitceo/out/PMIS_CEO_Dashboard_Test"
SEC = dict(SECTIONS)
FA = str.maketrans("0123456789", "۰۱۲۳۴۵۶۷۸۹")
DRILLS = json.load(open(os.path.join(HERE, "drills.json"), encoding="utf-8"))
DK = {d["key"]: d for d in DRILLS}
MODTITLE = C._MODTITLE
PAGES_FILE = os.path.join(HERE, "pages.json")
PAGES = json.load(open(PAGES_FILE, encoding="utf-8")) if os.path.exists(PAGES_FILE) else {}

# ------------------------------------------------------------------ Persian digits and left-to-right pieces
TOKEN = re.compile("|".join([
    r"https?://[A-Za-z0-9._~:/?#@!$&*+=%-]+",
    r"[A-Za-z]:\\[\w\\.\-]+",
    r"\d{4}/\d{2}/\d{2}",
    r"(?<![\w:])\d{1,2}:\d{2}(?::\d{2})?(?![\w:])",
    r"(?<![\w/])\d[\w-]*\.(?:pdf|xlsx|csv|txt|png|cmd|py)",
    r"\d-[A-Za-z][A-Za-z0-9\\._-]*",
    r"(?<![\w])[A-Za-z][A-Za-z0-9]*(?:[-\\.][A-Za-z0-9]+)*\d[A-Za-z0-9-]*(?:\.[A-Za-z]{2,4})?",
    r"(?<![\w])[A-Za-z][A-Za-z+]*(?: [A-Za-z][A-Za-z+]*)* \d+(?:\.\d+)*(?: [A-Za-z][A-Za-z+]*)*(?![\w/])",
    r"(?<![\w])[A-Za-z][A-Za-z0-9]*(?:[-.+][A-Za-z0-9]+)+(?![\w])",
]))
SINGLE = re.compile(r"(?<![\w])([A-Za-z])(?![\w])")
MARKS = re.compile(r"(⟦.*?⟧)", re.S)


def _num(s):
    s = re.sub(r"(?<=\d)\.(?=\d)", "٫", s)
    s = re.sub(r"(?<=\d),(?=\d{3}(?!\d))", "٬", s)
    return s.translate(FA)


def fa(text, wrap=None, wrap1=None):
    """Persian digits and separators; Latin identifiers (letters with digits, paths, dates, times) stay as they are."""
    text = text.replace("%", "٪")
    out, pos = [], 0

    def chunk(c):
        c = _num(c)
        return SINGLE.sub(lambda m: wrap1(m.group(1)), c) if wrap1 else c

    for m in TOKEN.finditer(text):
        out.append(chunk(text[pos:m.start()]))
        tok = m.group(0)
        if re.fullmatch(r"[\d:/]+", tok):
            tok = tok.translate(FA)
        out.append(wrap(tok) if wrap else tok)
        pos = m.end()
    out.append(chunk(text[pos:]))
    return "".join(out)


def _wrap_tok(t):
    return '<bdi class="%s" dir="ltr">%s</bdi>' % ("l nw" if len(t) <= 16 else "l", t)


def _wrap_one(t):
    return '<bdi dir="ltr">%s</bdi>' % t


def _is_mark(part):
    return len(part) >= 2 and part[0] == "⟦" and part[-1] == "⟧"


def h(text):
    text = C._ref(text)
    out = []
    for part in MARKS.split(text):
        if _is_mark(part):
            inner = html.escape(part[1:-1], quote=False)
            out.append('<bdi class="%s" dir="ltr">%s</bdi>' % ("l nw" if (len(inner) <= 16 and " " not in inner) else "l", inner))
        else:
            out.append(fa(html.escape(part, quote=False), _wrap_tok, _wrap_one))
    return "".join(out)


def plain(text):
    """The same text for Excel cells and file names: Persian digits, no marks."""
    text = C._ref(text)
    out = []
    for part in MARKS.split(text):
        out.append(part[1:-1] if _is_mark(part) else fa(part))
    return "".join(out)


def lt(s):
    return '<bdi class="l" dir="ltr">%s</bdi>' % html.escape(s, quote=False)


def num(n):
    return h(EX.grp(n))


def rial(n):
    return h(EX.short_rial(n))


# ------------------------------------------------------------------ style
CSS = """
@font-face{font-family:BZar;src:url(BZar.ttf)}
@font-face{font-family:BZarDigits;src:url(BZar.ttf);unicode-range:U+06F0-06F9}
@font-face{font-family:LatinSans;src:local("Liberation Sans"),local("Arial"),local("Helvetica");unicode-range:U+0030-0039,U+0041-005A,U+0061-007A;size-adjust:88%}
@font-face{font-family:LatinAll;src:local("Liberation Sans"),local("Arial"),local("Helvetica");unicode-range:U+0021-007E;size-adjust:88%}
@page{size:A4;margin:14mm 13mm 18mm;
 @bottom-center{content:counter(page, persian);font-family:BZar;font-size:10pt;color:#444}
 @bottom-right{content:"راهنمای آزمون داشبورد مدیرعامل — D1";font-family:BZar;font-size:9pt;color:#777}
 @bottom-left{content:"PMIS · 1672cba";font-family:Arial,sans-serif;font-size:8pt;color:#777}}
html{direction:rtl}
body{font-family:LatinSans,BZar;font-size:12pt;line-height:1.7;color:#111}
h1{font-size:21pt;text-align:center;margin:0 0 2mm}
.sub{text-align:center;color:#444;margin-bottom:2mm}
.meta{text-align:center;color:#333;font-size:10.5pt;margin-bottom:4mm}
h2{font-size:15pt;border-bottom:1.5px solid #333;padding-bottom:1mm;margin:7mm 0 2mm;page-break-after:avoid;break-after:avoid}
h3{font-size:12.5pt;margin:4mm 0 1.5mm;page-break-after:avoid;break-after:avoid}
h4{font-size:11.5pt;margin:3mm 0 1mm;page-break-after:avoid;break-after:avoid}
p{margin:1.2mm 0}
table{width:100%;border-collapse:collapse;margin:2mm 0}
th,td{border:1px solid #555;padding:1.1mm 1.7mm;vertical-align:top;font-size:10.5pt;line-height:1.55}
tr{break-inside:avoid;page-break-inside:avoid}
thead{display:table-header-group}
th{background:#e8e8e8}
table.ref th,table.ref td{font-size:9.5pt;line-height:1.5;padding:.9mm 1.4mm}
.note{border:1px solid #888;background:#f5f5f5;padding:2mm 3mm;margin:3mm 0;page-break-inside:avoid;break-inside:avoid}
.warn{border:1.5px solid #8a1c1c;background:#fbeeee;padding:2mm 3mm;margin:3mm 0;page-break-inside:avoid;break-inside:avoid}
ol,ul{margin:1mm 0 2mm;padding-right:7mm}
ol{list-style-type:persian}
td ol{margin:0;padding-right:5mm}
td li,li{margin:0 0 .8mm}
.id{font-weight:bold;white-space:nowrap}
.nw{white-space:nowrap}
.role{color:#333;font-size:9.5pt;font-weight:normal}
.sm{color:#444;font-size:8.5pt;line-height:1.4}
.k{color:#666;font-size:8pt}
.tag{border:1px solid #666;border-radius:1mm;padding:0 1mm;font-size:8pt;color:#333;white-space:nowrap}
.l{unicode-bidi:isolate;direction:ltr;font-family:BZarDigits,LatinAll,BZar,sans-serif;overflow-wrap:anywhere}
.sec{break-after:avoid;page-break-after:avoid}.sec td{background:#d9d9d9;font-weight:bold;font-size:11.5pt}
.sec .cnt{font-weight:normal;font-size:9.5pt;color:#333}
.pb{page-break-before:always;break-before:page}
.q td{height:20mm}
.blank{background:#fffff2}
table.scn th,table.scn td{font-size:10.5pt;line-height:1.55}
tr.hd td{background:#efefef;padding:1.3mm 2mm;break-after:avoid;page-break-after:avoid}
tr.hd .id{font-size:11.5pt}
tr.hd b{font-size:11pt}
tr.keep{break-inside:avoid;page-break-inside:avoid}
tr.brk{break-inside:auto;page-break-inside:auto}
.ep{margin:0 0 1.2mm}
.it{margin:0 0 .6mm;padding-right:3.5mm}
.ep:last-child{margin-bottom:0}
.toc td{border:0;border-bottom:1px dotted #999;padding:.2mm 1mm;font-size:10.5pt;line-height:1.45}
.toc tr{break-inside:avoid}
.toc .tn{width:9mm;font-weight:bold}.toc .tp{width:11mm;text-align:left}
.toc tr.l2 td{font-size:9pt;line-height:1.35;color:#333;border-bottom-style:dotted}
.toc tr.l2 .tt{padding-right:7mm}
.toc a{color:inherit;text-decoration:none}
figure{margin:2mm 0 3.5mm;break-inside:avoid;page-break-inside:avoid}
figure img{border:1px solid #888;display:block}
figure.w img{width:94%;margin:0 auto}
figure.m img{width:118mm;margin:0 auto}
figure.p,figure.ph{display:flex;gap:6mm;align-items:flex-start}
figure.p img{width:58mm;flex:none}
figure.ph img{width:52mm;flex:none}
figure.m figcaption{margin-top:1.5mm}
figure.w figcaption{margin-top:1.5mm}
figcaption{font-size:10.5pt;line-height:1.6}
figcaption .ref{color:#444;font-size:9.5pt}
pre{direction:ltr;text-align:left;unicode-bidi:isolate;font-family:"DejaVu Sans Mono","Liberation Mono",Consolas,monospace;font-size:8.2pt;line-height:1.35;background:#f3f3f3;border:1px solid #aaa;padding:2mm 2.5mm;margin:1.5mm 0;white-space:pre-wrap;overflow-wrap:anywhere;break-inside:avoid;page-break-inside:avoid}
.sn{margin:0 0 4mm}
.sn h4{margin-bottom:.5mm}
.sn p{margin:.8mm 0}
.pm{margin:0 0 .6mm}
.dc{font-size:9.5pt;line-height:1.5}
.dc div{margin:0 0 .9mm}
.dc b{font-weight:bold}
.grid td{height:7mm}
.center{text-align:center}
.num{direction:ltr;text-align:left;white-space:nowrap}
"""


def esc(s):
    return html.escape(s, quote=False)


def td(c, cls="", span=0):
    return "<td%s%s>%s</td>" % (' class="%s"' % cls if cls else "", ' colspan="%d"' % span if span else "", c)


def tbl(head, rows, widths=None, cls=""):
    """head: list of texts; rows: list of lists of cell html (already built with h()); a row given as ('sec', text) is a group row."""
    ncols = len(head) if head else len(rows[0])
    o = ['<table%s>' % (' class="%s"' % cls if cls else "")]
    if widths:
        o.append("<colgroup>%s</colgroup>" % "".join('<col style="width:%s">' % w if w else "<col>" for w in widths))
    if head:
        o.append("<thead><tr>%s</tr></thead>" % "".join("<th>%s</th>" % h(c) for c in head))
    o.append("<tbody>")
    for r in rows:
        if isinstance(r, tuple) and r[0] == "sec":
            o.append('<tr class="sec"><td colspan="%d">%s</td></tr>' % (ncols, r[1]))
        else:
            o.append("<tr>%s</tr>" % "".join(c if c.startswith("<td") else td(c) for c in r))
    o.append("</tbody></table>")
    return "".join(o)


def para(text, cls=""):
    return "<p%s>%s</p>" % (' class="%s"' % cls if cls else "", h(text))


def ul(items):
    return "<ul>%s</ul>" % "".join("<li>%s</li>" % h(i) for i in items)


def ol(items):
    return "<ol>%s</ol>" % "".join("<li>%s</li>" % h(i) for i in items)


def box(kind, head, text):
    return '<div class="%s"><b>%s</b> %s</div>' % (kind, h(head), h(text))


HEADS = []


def H2(hid, label, title, pb=False):
    HEADS.append((hid, label, title))
    return '<h2 id="%s"%s>%s</h2>' % (hid, ' class="pb"' if pb else "", h(label + title))


# ------------------------------------------------------------------ the parts
def part_purpose():
    o = [H2("s1", "1. ", "این بسته برای چیست", pb=True)]
    for p in G.PURPOSE:
        o.append(para(p))
    o.append("<h3>%s</h3>" % h(G.RELEASE_TITLE))
    o.append(ul(G.RELEASE))
    o.append(box("warn", *G.WARN_TEST_DB))
    o.append(box("note", *G.NOTE_MOCK))
    o.append(box("note", *G.NOTE_SEP))
    n_sec = len(SECTIONS)
    o.append(para(G.CONTENT_COUNTS % (len(T), len(T), n_sec, n_sec - 1, len(STAR), len(QUESTIONS))))
    return "".join(o)


def part_folders():
    o = [H2("s2", "2. ", "محتویات پوشه")]
    o.append(tbl(["پوشه / فایل", "کاربرد"], [[h(a), h(b)] for a, b in G.FOLDERS], ["37%", None]))
    return "".join(o)


def part_install():
    o = [H2("s3", "3. ", "نصب و پیش‌نیازها")]
    o.append(ol(G.INSTALL))
    o.append(box("note", "یادآوری:", G.INSTALL_NOTE))
    o.append(para("نسخه‌ی کامل برنامه‌ی آزمون‌شده: ⟦%s⟧ (شناسه‌ی کامل در فایل ⟦BUILD-INFO.txt⟧ پوشه‌ی نصب)؛ پیش از هر آزمون، شروع شدن آن با ⟦1672cba⟧ را در آزمون @install@ بسنجید." % G.SHA))
    return "".join(o)


def part_roles():
    o = [H2("s4", "4. ", "کاربران و نقش‌ها")]
    o.append(para("کاربران را مدیر سیستم می‌سازد (آزمون @users@): «داده ها ← کاربران ← کاربر جدید»، یک کاربر برای هر عنوان شغلی. جدول زیر کاربرهای آزمون و کارشان را می‌گوید. در همه‌ی برگه‌ها فقط عنوان شغلی نوشته می‌شود، نه نام شخص."))
    o.append(tbl(["عنوان شغلی", "نقش در برنامه", "کار در آزمون"], [[h(a), h(b), h(c)] for a, b, c in G.ROLES], ["20%", "21%", None]))
    o.append(box("note", "نشست و رمز:", G.ROLES_NOTE))
    return "".join(o)


def part_mock():
    o = [H2("s5", "5. ", "مدل آزمایشی")]
    for p in G.MOCK_INTRO:
        o.append(para(p))
    o.append("<h3>%s</h3>" % h("راه‌اندازی و ثبت در PMIS"))
    items = "".join("<li>%s</li>" % h(s) for s in G.MOCK_START)
    o.append("<ol>%s</ol>" % items)
    o.append(tbl(["نام", "نوع", "نشانی", "مدل", "اولویت"],
                 [[h(n), h(k), lt(a), lt(m), h(p)] for n, k, a, m, p in G.MOCK_SERVERS], ["22%", "16%", "26%", "22%", None]))
    o.append(para(G.MOCK_AFTER))
    o.append("<h3>%s</h3>" % h("جدول پاسخ‌های مدل آزمایشی"))
    o.append(para(G.MOCK_PHRASE_INTRO))
    o.append(tbl(["واژه‌ی پرسش", "مدل چه می‌کند", "پاسخ یا اثر", "آزمون"],
                 [[h(a), h(b), h(c), h(d)] for a, b, c, d in G.MOCK_ROWS], ["19%", "28%", "35%", None], cls="ref"))
    o.append("<h3>%s</h3>" % h("آنچه با مدل آزمایشی دیده نمی‌شود"))
    o.append(ul(G.MOCK_LIMITS))
    return "".join(o)


def part_mic():
    o = [H2("s6", "6. ", "میکروفون و نشانی امن")]
    o.append(tbl(["موضوع", "توضیح"], [[h(a), h(b)] for a, b in G.MIC], ["17%", None]))
    return "".join(o)


def part_data():
    o = [H2("s7", "7. ", "داده‌های آزمون")]
    o.append(para(G.DATA_INTRO))
    o.append(tbl(["بخش", "پر شدن از کجا", "پایگاه خالی"], [[h(a), h(b), h(c)] for a, b, c in G.DATA_ROWS], ["13%", None, "22%"], cls="ref"))
    o.append(box("note", "عددهای روز نخست:", G.DATA_NOTE))
    return "".join(o)


def part_conv():
    o = [H2("s8", "8. ", "قراردادهای آزمون")]
    conv = list(G.CONV)
    conv[1] = conv[1] % len(T)
    o.append(ul(conv))
    o.append("<h3>%s</h3>" % h("ثبت نتیجه در فرم Excel"))
    o.append(tbl(["نتیجه", "چه زمانی"], [
        [h("OK"), h("هر چه در «نتیجه‌ی مورد انتظار» آمده دیده شد (پیام‌ها، عددها و رنگ‌ها).")],
        [h("مشکل"), h("نتیجه فرق داشت یا خطا آمد. در «توضیح» بنویسید چه دیدید (متن پیام، عدد دیده‌شده در برابر عدد مورد انتظار) و نام فایل تصویر را بنویسید. مشکل «بحرانی» است اگر عدد داشبورد با صفحه‌ی ماژول فرق دارد، اطلاعاتی به نقش بی‌دسترسی می‌رسد (مبلغ حقوق، صفحه‌ی ممنوع)، دستیار کاری مثل تایید، پرداخت یا حذف انجام می‌دهد، یا داشبورد باز نمی‌شود.")],
        [h("انجام نشد"), h("آزمون اجرا نشد (داده‌ی لازم یا میکروفون یا مدل واقعی نبود)؛ علت را بنویسید.")],
    ], ["15%", None]))
    return "".join(o)


def part_figures():
    o = [H2("s9", "9. ", "شکل‌ها", pb=True)]
    o.append(para(G.FIG_INTRO))
    for i, (fn, cls, cap, refs) in enumerate(G.FIGURES, 1):
        capt = '<figcaption><b>%s</b> %s <span class="ref">%s</span></figcaption>' % (h("شکل %d —" % i), h(cap), h("(آزمون‌ها: %s)" % refs))
        img = '<img src="shots/%s" alt="">' % fn
        o.append('<figure class="%s">%s%s</figure>' % (cls, img, capt))
    return "".join(o)


SPLIT = re.compile(r"(?<=[.)»])\s+(?=(?:گام\s+\d|پاسخ\s+(?:اول|دوم|سوم|چهارم)\s*:))")
LABEL = re.compile(r"^((?:گام|پاسخ)[^:]{0,34}:)")
ITEM = re.compile(r"؛ (?=\(\d+\) )")


def _sentences(p):
    """Splits at '. ' outside the marks (a full stop followed by a quote or a bracket does not end a sentence here)."""
    marks = MARKS.findall(p)
    parts = re.split(r"(?<=[.؟!])\s+(?=\S)", MARKS.sub("\x00", p))
    it = iter(marks)
    return [re.sub("\x00", lambda m: next(it), part) for part in parts]


def _sentence_html(sn):
    """A sentence that lists numbered items '(1) ... ؛ (2) ...' gets one line per item."""
    m = re.search(r"\(1\) «", sn)
    if m and len(re.findall(r"\(\d+\) «", sn)) >= 3:
        items = ITEM.split(sn[m.start():])
        return '<div class="ep">%s</div>%s' % (h(sn[:m.start()].rstrip()), "".join('<div class="it">%s</div>' % h(i) for i in items))
    return '<div class="ep">%s</div>' % h(sn)


def exp_html(exp):
    out = []
    for p in SPLIT.split(exp):
        lab = LABEL.match(p)
        body = p[lab.end():] if lab else p
        sents = _sentences(body) if len(body) > 450 else [body]
        first = True
        for sn in sents:
            if first and lab:
                out.append('<div class="ep"><b>%s</b>%s</div>' % (h(lab.group(1)), h(sn)) if not re.search(r"\(1\) «", sn) else
                           '<div class="ep"><b>%s</b></div>%s' % (h(lab.group(1)), _sentence_html(sn)))
            else:
                out.append(_sentence_html(sn))
            first = False
    return "".join(out)


def steps_html(steps):
    if len(steps) == 1:
        return "<div>%s</div>" % h(steps[0])
    return "<ol>%s</ol>" % "".join("<li>%s</li>" % h(s) for s in steps)


def part_scenarios():
    o = [H2("s10", "10. ", "سناریوها (%d آزمون)" % len(T), pb=True)]
    o.append(para("هر آزمون یک نوار با شناسه (مثل T-26)، عنوان و نقش دارد و زیر آن «مراحل» (شماره‌دار) و «نتیجه‌ی مورد انتظار». شناسه همان شناسه‌ی فرم Excel است. مسیر منو همان عنوان منوی برنامه است («الف ← ب» یعنی منوی الف و سپس ب). N و X و Y جای عددهای روزند. متن میان ⟦⟧ در راهنما با حروف لاتین و چپ‌به‌راست نوشته می‌شود (نشانی، فرمان، کلید)."))
    cur = None
    rows = []
    by_sec = {}
    for x in T:
        by_sec.setdefault(x["sec"], []).append(x)
    o.append('<table class="scn"><colgroup><col style="width:46%"><col></colgroup><thead><tr><th>مراحل</th><th>نتیجه‌ی مورد انتظار</th></tr></thead><tbody>')
    for x in T:
        if x["sec"] != cur:
            cur = x["sec"]
            lst = by_sec[cur]
            o.append('<tr class="sec" id="sec-%s"><td colspan="2">%s — %s <span class="cnt">%s</span></td></tr>' % (
                cur, h(cur), h(SEC[cur]), h("(%d آزمون: %s تا %s)" % (len(lst), lst[0]["id"], lst[-1]["id"]))))
        o.append('<tr class="hd" id="%s"><td colspan="2"><span class="id">%s</span> <b>%s</b> <span class="role">— %s</span></td></tr>' % (
            x["id"], h(x["id"]), h(x["title"]), h(x["role"])))
        big = max(sum(len(s) for s in x["steps"]) + 40 * len(x["steps"]), len(x["exp"]))
        o.append('<tr class="%s"><td>%s</td><td>%s</td></tr>' % ("keep" if big <= 1050 else "brk", steps_html(x["steps"]), exp_html(x["exp"])))
    o.append("</tbody></table>")
    return "".join(o)


def part_star():
    o = [H2("s11", "11. ", "مفروضات ★ برای تایید شرکت", pb=True)]
    o.append(para(G.STAR_INTRO))
    rows = []
    for i, s in enumerate(STAR, 1):
        rows.append([td(h(str(i)), "id center"), td(h(s)), td(h(STAR_LINKED[i]).replace("، ", "<br>"), "nw"), td("", "blank")])
    o.append(tbl(["ردیف", "مفروضه", "آزمون", "تایید یا مقدار دیگر"], rows, ["6%", "52%", "13%", None], cls="ref q"))
    return "".join(o)


def part_questions():
    o = [H2("s12", "12. ", "پرسش‌های شرکت", pb=True)]
    o.append(para(G.Q_INTRO % len(QUESTIONS)))
    rows = []
    for i, q in enumerate(QUESTIONS, 1):
        rows.append([td(h(str(i)), "id center"), td(h(q)), td(h(LINKED[i]).replace("، ", "<br>"), "nw"), td("", "blank")])
    o.append(tbl(["ردیف", "پرسش", "آزمون", "پاسخ و تصمیم"], rows, ["6%", "50%", "13%", None], cls="q"))
    return "".join(o)


# ------------------------------------------------------------------ appendices
def app_kpis():
    o = [H2("a1", "پیوست 1. ", "شاخص‌ها و آستانه‌های رنگ", pb=True)]
    o.append(para(G.APP1_INTRO))
    for sk, st in K.SECTION_KEYS:
        ks = [k for k in K.KPIS if k[0] == sk]
        o.append("<h3>%s</h3>" % h("بخش «%s» — %d شاخص" % (st, len(ks))))
        rows = []
        for sec, key, title, unit, rule, dkey, recount, head in ks:
            c1 = '<b>%s</b>%s<div class="sm">%s · %s</div>' % (h(title), (' <span class="tag">%s</span>' % h("اصلی")) if head else "", lt(key), h(unit))
            c4 = h("جدول «%s» ⟦%s⟧: %s" % (DK[dkey]["title"], dkey, recount))
            rows.append([c1, h(rule), h(K.NOTES[key]), c4])
        o.append(tbl(["شاخص", "قاعده‌ی رنگ", "جمله‌ی زیر عدد", "شمارش دوباره"], rows, ["21%", "25%", "24%", None], cls="ref"))
    return "".join(o)


def app_charts():
    o = [H2("a2", "پیوست 2. ", "نمودارها", pb=True)]
    o.append(para(G.APP2_INTRO))
    rows = []
    for sk, st in K.SECTION_KEYS:
        cs = [c for c in K.CHARTS if c[0] == sk]
        if not cs:
            continue
        rows.append(("sec", h("بخش «%s» — %d نمودار" % (st, len(cs)))))
        for sec, key, title, kind, series, dkey, note in cs:
            rows.append(['<b>%s</b><div class="sm">%s</div>' % (h(title), lt(key)), h(kind), h(series),
                         '%s<div class="sm">%s</div>' % (h(DK[dkey]["title"]), lt(dkey)), h(note)])
    o.append(tbl(["نمودار", "نوع", "سری‌ها", "جدول جزئیات", "یادداشت زیر نمودار"], rows, ["19%", "11%", "17%", "17%", None], cls="ref"))
    return "".join(o)


def app_alerts():
    o = [H2("a3", "پیوست 3. ", "هشدارهای ممکن", pb=True)]
    o.append(para(G.APP3_INTRO))
    rows = []
    n = 0
    for sev, name in (("danger", "بحرانی"), ("warning", "هشدار"), ("info", "اطلاع")):
        al = [a for a in K.ALERTS if a[0] == sev]
        rows.append(("sec", h("گروه «%s» — %d هشدار" % (name, len(al)))))
        for _, text, eq, route in al:
            n += 1
            rows.append([td(h(str(n)), "center"), h(text), h(eq) if eq != "-" else "—", td(lt(route), "nw")])
    o.append(tbl(["ردیف", "جمله‌ی هشدار", "برابر با (شاخص صفحه)", "صفحه‌ای که باز می‌شود"], rows, ["6%", "49%", "24%", None], cls="ref"))
    return "".join(o)


KIND_TAG = {"rial": "ریال", "status": "وضعیت", "pct": "٪", "percent": "٪"}
TYPE_FA = {"date": "تاریخ", "string": "متن", "int": "عدد صحیح", "bool": "بلی/خیر"}


def drill_cell(d):
    parts = ["<div>%s</div>" % h(d["description"])]
    if d["params"]:
        ps = []
        for p in d["params"]:
            extra = []
            if "allowed" in p:
                extra.append("مقدار مجاز: " + "، ".join(p["allowed"]))
            if "min" in p:
                extra.append("از %s تا %s" % (p["min"], p["max"]))
            ps.append('<div class="pm">%s <span class="k">(%s)</span> — %s%s</div>' % (
                lt(p["name"]), h(TYPE_FA[p["type"]]), h(p["desc"]), (" " + h("[" + "؛ ".join(extra) + "]")) if extra else ""))
        parts.append("<div><b>%s</b>%s</div>" % (h("پارامترها (فقط رابط یا دستیار می‌فرستد):"), "".join(ps)))
    cols = " · ".join("%s%s" % (h(c["title"]), (' <span class="k">(%s)</span>' % h(KIND_TAG[c["kind"]])) if c["kind"] in KIND_TAG else "") for c in d["columns"])
    parts.append("<div><b>%s</b> %s</div>" % (h("ستون‌ها:"), cols))
    for nt in d["notes"]:
        parts.append("<div><b>%s</b> %s</div>" % (h("زیر عنوان:"), h(nt)))
    return '<div class="dc">%s</div>' % "".join(parts)


def app_drills():
    o = [H2("a4", "پیوست 4. ", "جدول‌های جزئیات", pb=True)]
    o.append(para(G.APP4_INTRO))
    o.append(para("نشان‌ها: «ریال» ستون مبلغ (عدد کامل با جداکننده‌ی هزارگان)، «وضعیت» ستون با نشان رنگی، «٪» ستون درصد."))
    total = 0
    for mod in MODTITLE:
        ds = [d for d in DRILLS if d["module"] == mod]
        total += len(ds)
        o.append("<h3>%s</h3>" % h("گروه «%s» — %d جدول" % (MODTITLE[mod], len(ds))))
        rows = []
        for d in ds:
            left = '<b>%s</b><div class="sm">%s</div><div class="sm">%s %s</div>%s' % (
                h(d["title"]), lt(d["key"]), h("صفحه:"), lt(d["link"]), ('<div class="sm">%s</div>' % h("دارای پارامتر")) if d["params"] else "")
            rows.append([left, drill_cell(d)])
        o.append(tbl(["جدول", "توضیح، پارامترها و ستون‌ها"], rows, ["24%", None], cls="ref"))
    assert total == len(DRILLS) == 68
    return "".join(o)


def app_example():
    o = [H2("a5", "پیوست 5. ", "نمونه‌ی محاسبه‌ی دستی", pb=True)]
    o.append(para(G.APP5_INTRO))
    # --- rial short text
    o.append("<h3>%s</h3>" % h("مبلغ ریالی کوتاه و کامل"))
    o.append(para("کاشی‌ها و نمودارها مبلغ را کوتاه می‌نویسند: یک رقم اعشار، صفر آخر حذف می‌شود، واحد «هزار»، «میلیون» یا «میلیارد» ریال است؛ اشاره‌گر روی عدد مبلغ کامل را با جداکننده‌ی هزارگان می‌دهد. گرد کردن رو به بالا (نیم به بالا) است."))
    ex = [48_234_567_890, 11_470_500_000, 2_700_000_000, 300_000_000, 950_000]
    o.append(tbl(["مبلغ کامل (ریال)", "نوشته‌ی کاشی"], [[td(num(v), "num"), h(EX.short_rial(v))] for v in ex], ["40%", None]))
    # --- cash
    o.append("<h3>%s</h3>" % h("نمونه 1 — «نقد و بانک» (آزمون @x_cash@)"))
    rows = [[h(n), h(k), td(num(v), "num"), h("بلی" if inc else "خیر (تنخواه جدا)")] for n, k, v, inc in EX.CASH_ROWS]
    rows.append([td(h("جمع صندوق‌ها و حساب‌های بانکی"), span=2), td(num(EX.CASH), "num"), h("کاشی: %s" % EX.short_rial(EX.CASH))])
    o.append(tbl(["نام حساب", "نوع", "موجودی (ریال)", "در «نقد و بانک»؟"], rows, ["32%", "16%", "26%", None]))
    o.append(para("«صندوق‌ها» (%s) + «بانک‌ها» (%s) = %s ریال؛ این همان الف = ب = پ آزمون است. «تنخواه‌ها» %s ریال در شاخص نیست و «جمع نقدینگی» صفحه‌ی خزانه با تنخواه %s ریال می‌شود؛ این فرق خطا نیست." % (
        EX.grp(EX.CASH_BOXES), EX.grp(EX.CASH_BANKS), EX.grp(EX.CASH), EX.grp(EX.CASH_PETTY), EX.grp(EX.CASH_TOTAL_PAGE))))
    # --- receivables
    o.append("<h3>%s</h3>" % h("نمونه 2 — «مطالبات باز» و قاعده‌ی ۳۰٪ (آزمون @x_recv@)"))
    o.append(para("جدول «مطالبات به تفکیک مشتری» (دسته‌ها از سررسید فاکتور؛ دسته‌ی «جاری» تا ۳۰ روز پس از سررسید را هم دارد) و جدول «فاکتورهای فروش سررسیدگذشته»:"))
    rows = [[h(n), td(num(c0), "num"), td(num(a), "num"), td(num(b), "num"), td(num(c), "num"), td(num(t), "num"), td(num(od), "num")] for n, c0, a, b, c, t, od in EX.RECV_BY_CUSTOMER]
    tot = [sum(r[i] for r in EX.RECV_BY_CUSTOMER) for i in range(1, 7)]
    rows.append([h("جمع")] + [td(num(v), "num") for v in tot])
    o.append(tbl(["مشتری", "جاری تا ۳۰ روز", "۳۱ تا ۶۰", "۶۱ تا ۹۰", "بیش از ۹۰", "جمع مانده", "سررسیدگذشته"], rows, ["19%", "15%", "13%", "13%", "13%", "15%", None], cls="ref"))
    rows = [[h(n), td(h(str(d)), "center"), td(num(x), "num")] for n, d, x in EX.RECV_INV]
    rows.append([h("جمع «مانده‌ی باز»"), "", td(num(EX.RECV_OVERDUE), "num")])
    o.append(tbl(["مشتری", "روز گذشته از سررسید", "مانده‌ی باز (ریال)"], rows, ["40%", "26%", None], cls="ref"))
    o.append(para("الف = جمع مانده = %s ریال (کاشی %s). ت = جمع «مانده‌ی باز» فاکتورهای سررسیدگذشته = %s ریال (%s). N٪ = %s ÷ %s = %s٪ ← کاشی زیر عدد می‌نویسد «سررسیدگذشته: %s (%s٪ از مطالبات باز)» و چون سررسیدگذشته بیشتر از صفر ولی کمتر از ۳۰٪ است وضعیت «%s» است." % (
        EX.grp(EX.RECV_OPEN), EX.short_rial(EX.RECV_OPEN), EX.grp(EX.RECV_OVERDUE), EX.short_rial(EX.RECV_OVERDUE), EX.grp(EX.RECV_OVERDUE), EX.grp(EX.RECV_OPEN), EX.RECV_SHARE_TEXT,
        EX.short_rial(EX.RECV_OVERDUE), EX.RECV_SHARE_TEXT, EX.RECV_STATUS)))
    o.append(para("تطبیق با نمودار «سن مطالبات»: سه دسته‌ی ۳۱ تا ۶۰، ۶۱ تا ۹۰ و بیش از ۹۰ روز با هم %s ریال‌اند و فاکتورهای ۱ تا ۳۰ روز گذشته از سررسید (در دسته‌ی «جاری») %s ریال؛ جمع آن دو %s ریال، یعنی همان سررسیدگذشته‌ی ت." % (
        EX.grp(EX.RECV_OLD), EX.grp(EX.RECV_YOUNG), EX.grp(EX.RECV_OLD + EX.RECV_YOUNG))))
    o.append(para("مرز قاعده: اگر مطالبات باز %s ریال و سررسیدگذشته %s ریال باشد، نسبت %s%% است؛ کاشی «%s٪ از مطالبات باز» می‌نویسد ولی وضعیت «%s» می‌ماند، چون رنگ از نسبت دقیق می‌آید (۳۰٪ یا بیشتر بحرانی است) و رقم نوشته‌شده به عدد صحیح گرد شده است." % (
        EX.grp(EX.LIMIT_OPEN), EX.grp(EX.LIMIT_OVER), EX.pct(EX.D(EX.LIMIT_OVER) / EX.D(EX.LIMIT_OPEN), 1), EX.LIMIT_TEXT, EX.LIMIT_STATUS)))
    # --- payables
    o.append("<h3>%s</h3>" % h("نمونه 3 — «بدهی سررسیدگذشته به تأمین‌کنندگان» در برابر «نقد و بانک» (آزمون @x_pay@)"))
    rows = [[h(n), td(num(c0), "num"), td(num(a), "num"), td(num(b), "num"), td(num(c), "num"), td(num(od), "num"), td(num(tot), "num")] for n, c0, a, b, c, od, tot in EX.PAY]
    rows.append([h("جمع"), "", "", "", "", td(num(EX.PAY_OVERDUE), "num"), ""])
    o.append(tbl(["تأمین‌کننده", "جاری", "۳۱ تا ۶۰", "۶۱ تا ۹۰", "بیش از ۹۰", "سررسیدگذشته", "جمع بدهی"], rows, ["20%", "13%", "13%", "13%", "13%", "15%", None], cls="ref"))
    o.append(para("ب = %s ریال (کاشی %s) و ن = %d تأمین‌کننده با سررسیدگذشته بیشتر از صفر. با «نقد و بانک» %s ریال وضعیت «%s» است (بدهی از نقد بیشتر نیست)؛ اگر نقد و بانک فقط %s ریال بود، همان بدهی از نقد بیشتر می‌شد و وضعیت «%s» می‌شد." % (
        EX.grp(EX.PAY_OVERDUE), EX.short_rial(EX.PAY_OVERDUE), EX.PAY_COUNT, EX.grp(EX.CASH), EX.PAY_STATUS, EX.grp(EX.PAY_LOW_CASH), EX.PAY_STATUS_LOW)))
    # --- OEE
    o.append("<h3>%s</h3>" % h("نمونه 4 — OEE شرکت در برابر میانگین ساده‌ی مراکز (آزمون @x_production@)"))
    o.append(para("ستون‌های جدول «اثربخشی تجهیزات به تفکیک مرکز کار»: ساعت برنامه‌ای، ساعت توقف، ساعت کارکرد، تعداد سالم، تعداد ضایعات و سه درصد. ساعت استاندارد ستون جدول نیست: عملکرد = ساعت استاندارد ÷ ساعت کارکرد، پس ساعت استاندارد = عملکرد × ساعت کارکرد. دسترس‌پذیری = (برنامه‌ای − توقف) ÷ برنامه‌ای؛ کیفیت = سالم ÷ (سالم + ضایعات)؛ OEE = حاصل‌ضرب سه درصد."))
    rows = []
    for w in EX.OEE_WC:
        rows.append([h("%s — %s" % (w["code"], w["title"])), td(h(str(w["planned"])), "center"), td(h(str(w["down"])), "center"), td(h(str(w["run"])), "center"), td(h(str(w["std"])), "center"),
                     td(h(str(w["good"])), "center"), td(h(str(w["scrap"])), "center"), td(h(w["A_t"] + "%"), "center"), td(h(w["P_t"] + "%"), "center"), td(h(w["Q_t"] + "%"), "center"), td(h(w["OEE_t"] + "%"), "center")])
    S = EX.OEE_SUM
    rows.append([h("جمع شرکت"), td(h(str(S["planned"])), "center"), td(h(str(S["down"])), "center"), td(h(str(S["run"])), "center"), td(h(str(S["std"])), "center"),
                 td(h(str(S["good"])), "center"), td(h(str(S["scrap"])), "center"), td(h(EX.OEE_A_T + "%"), "center"), td(h(EX.OEE_P_T + "%"), "center"), td(h(EX.OEE_Q_T + "%"), "center"), td(h(EX.OEE_CO_T + "%"), "center")])
    o.append(tbl(["مرکز کار", "برنامه‌ای", "توقف", "کارکرد", "استاندارد", "سالم", "ضایعات", "دسترس", "عملکرد", "کیفیت", "OEE"], rows,
                 ["25%", "7%", "6%", "7%", "8%", "7%", "7%", "8%", "8%", "8%", None], cls="ref"))
    o.append(para("کاشی «اثربخشی کلی تجهیزات (OEE)» داشبورد مدیرعامل از جمع ساعت‌ها و قطعه‌ها حساب می‌شود: %s%% × %s%% × %s%% = %s%%، یعنی «%s». میانگین ساده‌ی OEE دو مرکز (صفحه‌ی OEE) %s%% است و وضعیتش «%s» می‌شد؛ پس تفاوت میان داشبورد و میانگین ساده‌ی مراکز طبیعی است و خطا نیست (با یک مرکز تنها دقیقاً برابرند)." % (
        EX.OEE_A_T, EX.OEE_P_T, EX.OEE_Q_T, EX.OEE_CO_T, EX.OEE_CO_STATUS, EX.OEE_MEAN, EX.OEE_MEAN_STATUS)))
    # --- trend pills
    o.append("<h3>%s</h3>" % h("نمونه 5 — قرص «نسبت به ماه قبل» (آزمون @trend_lines@)"))
    o.append(para("تفاوت امروز با نقطه‌ی ماه شمسی قبل، به دقت واحد گرد می‌شود: مبلغ با همان قاعده‌ی عدد کوتاه، شمارنده با «N مورد». پیکان ▲ بیشتر، ▼ کمتر و ■ بدون تغییر است. رنگ از «جهت مطلوب» می‌آید: برای شاخصی که بالاتر بهتر است افزایش سبز و کاهش قرمز است؛ برای شاخصی که کمتر بهتر است برعکس؛ شاخص بدون جهت (ارزش موجودی) و تغییر صفر خاکستری است."))
    rows = []
    for title, today, prev, unit, delta, arrow, text, tone in EX.TREND_ROWS:
        fmt = (lambda v: num(v)) if unit == "rial" else (lambda v: h(str(v)))
        rows.append([h(title), td(fmt(today), "num"), td(fmt(prev), "num"), td(fmt(delta), "num"), h("%s %s" % (arrow, text)), h(tone)])
    o.append(tbl(["شاخص", "امروز", "ماه قبل", "تفاوت", "قرص", "رنگ"], rows, ["22%", "17%", "17%", "13%", "21%", None], cls="ref"))
    # --- colours of counts
    o.append("<h3>%s</h3>" % h("نمونه 6 — رنگ چند شمارنده"))
    o.append(para("قاعده‌ی «شمارنده»: کمتر از آستانه‌ی هشدار «عادی»؛ از آستانه‌ی هشدار تا یکی کمتر از آستانه‌ی بحرانی «هشدار»؛ از آستانه‌ی بحرانی به بالا «بحرانی». آستانه‌ی هر شاخص در جدول «شاخص‌ها و آستانه‌های رنگ» پیوست ۱ است."))
    o.append(tbl(["شاخص", "عدد", "آستانه‌ها (هشدار، بحرانی)", "رنگ کاشی"], [[h(a), td(h(str(b)), "center"), td(h(c), "center"), h(d)] for a, b, c, d in EX.COLOUR_ROWS], ["38%", "12%", "27%", None], cls="ref"))
    return "".join(o)


def app_snippets():
    o = [H2("a6", "پیوست 6. ", "دستورهای SQL و PowerShell مدیر فنی", pb=True)]
    o.append(para(G.APP6_INTRO))
    for gname, items in SN.GROUPS:
        o.append("<h3>%s</h3>" % h(gname))
        for it in items:
            o.append('<div class="sn"><h4>%s — %s</h4><p>%s</p><pre dir="ltr">%s</pre>%s</div>' % (
                h(it["id"]), h(it["title"]), h(it["intro"]), esc(it["code"].strip("\n")), ("<p>%s</p>" % h(it["note"])) if it["note"] else ""))
    return "".join(o)


# ------------------------------------------------------------------ contents list
def toc_html():
    rows = []
    for hid, label, title in HEADS:
        pg = PAGES.get(hid, 0)
        rows.append('<tr class="l1"><td class="tn">%s</td><td class="tt"><a href="#%s">%s</a></td><td class="tp">%s</td></tr>' % (
            h(label.strip().rstrip(".").replace("پیوست ", "پ")), hid, h(title), fa(str(pg))))
        if hid == "s10":
            for s, name in SECTIONS:
                rows.append('<tr class="l2"><td></td><td class="tt"><a href="#sec-%s">%s — %s</a></td><td class="tp">%s</td></tr>' % (
                    s, h(s), h(name), fa(str(PAGES.get("sec-" + s, 0)))))
    return '<table class="toc"><tbody>%s</tbody></table>' % "".join(rows)


def build_html():
    HEADS.clear()
    body = [part_purpose(), part_folders(), part_install(), part_roles(), part_mock(), part_mic(), part_data(), part_conv(),
            part_figures(), part_scenarios(), part_star(), part_questions(),
            app_kpis(), app_charts(), app_alerts(), app_drills(), app_example(), app_snippets()]
    head = ['<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><title>%s</title><style>%s</style></head><body>' % (esc(G.TITLE), CSS)]
    head.append("<h1>%s</h1>" % h(G.TITLE))
    head.append('<div class="sub">%s</div>' % h(G.SUB))
    head.append('<div class="meta">%s</div>' % h("نسخه‌ی برنامه: ⟦source_sha=1672cba⟧ — %d آزمون در %d بخش — %d مفروضه‌ی ★ — %d پرسش شرکت" % (len(T), len(SECTIONS), len(STAR), len(QUESTIONS))))
    head.append("<h3>%s</h3>" % h("فهرست"))
    head.append(toc_html())
    return "\n".join(head + body + ["</body></html>"])


def check(s):
    bad = []
    for pat, what in ((r"⟦|⟧", "mark left"), (r"@\w+@", "unresolved reference"), (r"None", "None"), (r"\{\w+\}", "format brace")):
        for m in re.finditer(pat, s):
            bad.append((what, s[max(0, m.start() - 40):m.end() + 40]))
    return bad


if __name__ == "__main__":
    s = build_html()
    bad = [b for b in check(re.sub(r"<style>.*?</style>", "", s, flags=re.S)) if b[0] != "format brace"]
    if bad:
        for b in bad[:20]:
            print("WARN", b)
    open(os.path.join(HERE, "guide.html"), "w", encoding="utf-8").write(s)
    print(len(T), "tests", len(s), "bytes", "pages known:", len(PAGES))
