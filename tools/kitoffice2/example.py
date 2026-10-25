# -*- coding: utf-8 -*-
"""Worked attendance example (Mehr 1405), R2 policy of the company. Single source for the sample file and the guide table.

Policy used (the company answers, 1405/08): grace 15 minutes, a delay beyond the grace is deducted as delay x 3 (whole delay),
overtime needs no pre-approval, no daily maximum, no rounding, monthly cap 22 hours (working-day overtime only). Early leave is not deducted.
"""
def hm(m): return "%d:%02d" % (m // 60, m % 60)
def mins(t): h, m = t.split(":"); return int(h) * 60 + int(m)

GRACE, BREAK, FACTOR = 15, 30, 3
# (code, persian date, day label, shift, [(day_offset, "HH:MM"), ...])
DAYS = [
 ("ATT01", "1405/07/04", "شنبه", "day", [(0, "08:05"), (0, "16:30")]),
 ("ATT01", "1405/07/05", "یکشنبه", "day", [(0, "08:25"), (0, "16:10")]),
 ("ATT01", "1405/07/06", "دوشنبه", "day", [(0, "07:55"), (0, "18:50")]),
 ("ATT01", "1405/07/07", "سه‌شنبه", "day", [(0, "08:00")]),
 ("ATT01", "1405/07/10", "جمعه", "day", [(0, "09:00"), (0, "13:30")]),
 ("ATT02", "1405/07/04", "شنبه", "night", [(0, "21:55"), (1, "06:10")]),
 ("ATT02", "1405/07/05", "یکشنبه", "night", [(0, "22:20"), (1, "06:00")]),
 ("ATT03", "1405/07/04", "شنبه", None, [(0, "08:00"), (0, "16:00")]),
 ("ATT03", "1405/07/05", "یکشنبه", None, [(0, "08:00")]),
]
SHIFT = {"day": (mins("08:00"), mins("16:30")), "night": (mins("22:00"), mins("06:00") + 1440)}
# scheduled minutes of the shift = end - start - break (the base of the lateness deduction in days)
SCHEDULED = {"day": 480, "night": 450}

def calc(shift, ev):
    """returns dict of minutes."""
    pts = [d * 1440 + mins(t) for d, t in ev]
    if len(pts) < 2:
        return dict(status="ناقص (تک‌تردد)")
    first, last = pts[0], pts[-1]
    span = last - first
    if shift is None:
        return dict(status="حاضر", worked=span, delay=0, ded=0, early=0, ot=0, night=0, hol=0)
    s, e = SHIFT[shift]
    worked = span - BREAK
    delay = max(0, first - s); delay = 0 if delay <= GRACE else delay
    ded = delay * FACTOR
    early = max(0, e - last); early = 0 if early <= GRACE else early
    ot = max(0, last - e)          # no pre-approval, no daily maximum, no rounding
    # night window 22:00-06:00 (day offsets -1..+1)
    night = 0
    for k in (-1, 0, 1):
        a, b = 1440 * k + 22 * 60, 1440 * k + 1440 + 6 * 60
        night += max(0, min(last, b) - max(first, a))
    return dict(status="حاضر", worked=worked, delay=delay, ded=ded, early=early, ot=ot, night=night, hol=0)

def rows():
    out = []
    for code, d, lab, sh, ev in DAYS:
        r = calc(sh, ev)
        if d.endswith("07/10"):
            r["hol"] = r["worked"]; r["delay"] = 0; r["ded"] = 0; r["early"] = 0; r["ot"] = 0; r["night"] = 0
        out.append((code, d, lab, sh, ev, r))
    return out

def month_sheet():
    """Expected monthly sheet values after the S11 tests (leave 07/08, sick 07/11, forgotten exit 07/07, mission of ATT03)."""
    res = {}
    for code, d, lab, sh, ev, r in rows():
        x = res.setdefault(code, dict(ot=0, night=0, hol=0, ded=0, sched=SCHEDULED.get(sh) or 0))
        if "worked" in r:
            x["ot"] += r["ot"]; x["night"] += r["night"]; x["hol"] += r["hol"]; x["ded"] += r["ded"]
    return res

if __name__ == "__main__":
    for code, d, lab, sh, ev, r in rows():
        print(code, d, r)
    print(month_sheet())
