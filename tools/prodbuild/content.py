# -*- coding: utf-8 -*-
# Content of the production / shop-floor (MES) and maintenance test kit (Persian). Digits are Latin here and converted to Persian by build.py.
import os, re

AD = "مدیر سیستم (Admin)"; PMO = "معاونت PMO"; PM = "مدیر تولید"; SV = "سرپرست کارگاه"; OP = "اپراتور"; MT = "نگهداری و تعمیرات"
QC = "کنترل کیفیت"; EN = "واحد مهندسی"; FM = "مدیر مالی"; FA = "واحد مالی"; PL = "برنامه‌ریزی و کنترل پروژه"; PR = "حقوق و دستمزد"
HR = "اداری و منابع انسانی"; CEO = "مدیرعامل"; WH = "انبار"; CO = "واحد بازرگانی"
M = "تولید و نگهداری ← "

SECTIONS = [
 ("S0", "آماده‌سازی: کاربران، نقش‌ها و داده‌های پیش‌نیاز"),
 ("S1", "داده‌های پایه: مراکز کار، ماشین‌ها، دلایل، نرخ‌ها"),
 ("S2", "استانداردهای عملیات و مسیر ساخت با تایید دو نفره"),
 ("S3", "دستور تولید: پیش‌نویس سیستمی، پنج بررسی صدور، کمبود، کیت مواد"),
 ("S4", "ترمینال تبلت کارگاه"),
 ("S5", "کنترل کیفیت: بازرسی، دوباره‌کاری، عدم انطباق"),
 ("S6", "کار بیرونی (پیمانکار)"),
 ("S7", "تکمیل، رسید تولید و بستن دستور"),
 ("S8", "ثبت زمان توسط سرپرست و مغایرت حضور"),
 ("S9", "زمان‌بند با ظرفیت محدود، بار کارگاه و فهرست توزیع کار"),
 ("S10", "نگهداری و تعمیرات"),
 ("S11", "اثربخشی تجهیزات (OEE)"),
 ("S12", "هزینه‌یابی روزانه تولید"),
 ("S13", "داشبورد تولید و کاشی‌های داشبورد مدیرعامل"),
 ("S14", "تفکیک وظایف و دسترسی‌ها (جمع‌بندی)"),
 ("S15", "تصمیم‌های شرکت که باید تایید شود"),
]

T = []
_KEYS = {}
def t(key, s, title, role, steps, exp):
    i = "M-%02d" % (len(T) + 1)
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
