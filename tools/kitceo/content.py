# -*- coding: utf-8 -*-
# Content of the CEO dashboard (release D1) test kit (Persian). Digits are Latin here and are converted to Persian digits by build.py.
# Text between the two marks ⟦ and ⟧ is shown as one left-to-right piece with Latin digits (addresses, commands, keys, technical outputs).
import os, re

AD = "مدیر سیستم (Admin)"
CEO = "مدیرعامل"; PMO = "معاونت PMO"; FM = "مدیر مالی"; WU = "انباردار آزمایشی"
DASH = "مالی و بهای تمام‌شده ← داشبورد مدیرعامل"
AIM = "هوش مصنوعی ← مدیریت هوش مصنوعی ← "

SECTIONS = [
 ("S0", "آماده‌سازی: نصب، کاربران، مدل آزمایشی و عددهای پایه"),
 ("S1", "دسترسی و نقش‌ها"),
 ("S2", "سرصفحه، خط صبح، چراغ‌های وضعیت و شاخص‌های اصلی"),
 ("S3", "تطبیق شاخص‌های اصلی با صفحه‌های ماژول‌ها"),
 ("S4", "نُه بخش شرکت: شاخص‌ها، نمودارها و آستانه‌های رنگ"),
 ("S5", "جدول‌های جزئیات (۶۸ جدول)"),
 ("S6", "تحلیل هوشمند"),
 ("S7", "دستیار هوشمند (کشوی گفت‌وگو)"),
 ("S8", "حالت صوتی: شنیدن، گفتن و انجام کار"),
 ("S9", "روندها و عکس شبانه‌ی شاخص‌ها"),
 ("S10", "تازه‌سازی، حافظه‌ی موقت و خطاها"),
 ("S11", "حالت تیره، چاپ و صفحه‌ی کوچک"),
 ("S12", "نقش معاونت PMO و پنهان‌سازی مبلغ‌های حقوق"),
]

T = []
_KEYS = {}
def t(key, s, title, role, steps, exp):
    i = "T-%02d" % (len(T) + 1)
    _KEYS[key] = i
    T.append(dict(id=i, key=key, sec=s, title=title, role=role, steps=steps, exp=exp))

_here = os.path.dirname(os.path.abspath(__file__))
for _p in ("part_a.py", "part_b.py", "part_c.py", "part_d.py", "part_e.py", "part_f.py"):
    exec(compile(open(os.path.join(_here, _p), encoding="utf-8").read(), _p, "exec"))

def _ref(s):
    return re.sub(r"@(\w+)@", lambda m: _KEYS[m.group(1)], s)
for _x in T:
    _x["steps"] = [_ref(s) for s in _x["steps"]]
    _x["exp"] = _ref(_x["exp"])
    _x["title"] = _ref(_x["title"])
LINKED = {i: _ref(v) for i, v in LINKED.items()}
STAR_LINKED = {i: _ref(v) for i, v in STAR_LINKED.items()}
