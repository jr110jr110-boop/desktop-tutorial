# -*- coding: utf-8 -*-
# Content of the Local AI (release A1) test kit (Persian). Digits are Latin here and converted to Persian by build.py.
# Text between the two marks ⟦ and ⟧ is shown as one left-to-right piece with Latin digits (addresses, commands, technical outputs).
import os, re

AD = "مدیر سیستم (Admin)"
CEO = "مدیرعامل"; PMO = "معاونت PMO"; FM = "مدیر مالی"; F1 = "واحد مالی ۱"; F2 = "واحد مالی ۲"
WU = "انباردار آزمایشی"; WS = "سرپرست انبار"; PL = "برنامه‌ریزی و کنترل پروژه"; QC = "کنترل کیفیت"
SE = "دبیرخانه"; SA = "کارشناس فروش"; CO = "واحد بازرگانی"
AI = "هوش مصنوعی ← "
AM = AI + "مدیریت هوش مصنوعی ← "

SECTIONS = [
 ("S0", "آماده‌سازی: نصب، کاربران، منو، انبار و موجودی"),
 ("S1", "بدون هیچ مدل: حالت کاهش‌یافته و صندلی‌هایی که مدل نمی‌خواهند"),
 ("S2", "ثبت سرورهای مدل آزمایشی و بررسی سلامت"),
 ("S3", "گفت‌وگوی متنی: راستی‌آزمایی، دسترسی و حافظه"),
 ("S4", "جابه‌جایی به سرور پشتیبان و قطع کامل مدل"),
 ("S5", "گفت‌وگوی صوتی (فشار و صحبت) و پخش صدا"),
 ("S6", "ممیزی و نسخه‌های دستور سیستم"),
 ("S7", "دستیارهای زمان‌بندی‌شده (صندلی‌ها) و یادداشت‌ها"),
 ("S8", "ارزیابی ۶۶ پرسش"),
 ("S9", "تفکیک وظایف و حریم خصوصی (جمع‌بندی)"),
 ("S10", "تصمیم‌های شرکت که باید تایید شود"),
]

T = []
_KEYS = {}
def t(key, s, title, role, steps, exp):
    i = "A-%02d" % (len(T) + 1)
    _KEYS[key] = i
    T.append(dict(id=i, key=key, sec=s, title=title, role=role, steps=steps, exp=exp))

_here = os.path.dirname(os.path.abspath(__file__))
for _p in ("part_a.py", "part_b.py", "part_c.py"):
    exec(compile(open(os.path.join(_here, _p), encoding="utf-8").read(), _p, "exec"))

def _ref(s):
    return re.sub(r"@(\w+)@", lambda m: _KEYS[m.group(1)], s)
for _x in T:
    _x["steps"] = [_ref(s) for s in _x["steps"]]
    _x["exp"] = _ref(_x["exp"])
    _x["title"] = _ref(_x["title"])
LINKED = {i: _ref(v) for i, v in LINKED.items()}
