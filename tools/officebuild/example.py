# -*- coding: utf-8 -*-
"""Worked attendance example (Mehr 1405). Single source for the sample file and the guide table."""
def hm(m): return "%d:%02d" % (m // 60, m % 60)
def mins(t): h, m = t.split(":"); return int(h) * 60 + int(m)

GRACE, BREAK = 10, 30
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

def calc(shift, ev, overtime_approved_min=0):
    """returns dict of minutes."""
    pts = [d * 1440 + mins(t) for d, t in ev]
    if len(pts) < 2:
        return dict(status="ناقص (تک‌تردد)")
    first, last = pts[0], pts[-1]
    span = last - first
    if shift is None:
        return dict(status="حاضر", worked=span, delay=0, early=0, ot=0, night=0, hol=0)
    s, e = SHIFT[shift]
    worked = span - BREAK
    delay = max(0, first - s); delay = 0 if delay <= GRACE else delay
    early = max(0, e - last); early = 0 if early <= GRACE else early
    raw = max(0, last - e)
    cap = min(overtime_approved_min, 120)
    ot = min(raw, cap); ot = ot // 15 * 15
    # night window 22:00-06:00 (day offsets -1..+1)
    night = 0
    for k in (-1, 0, 1):
        a, b = 1440 * k + 22 * 60, 1440 * k + 1440 + 6 * 60
        night += max(0, min(last, b) - max(first, a))
    return dict(status="حاضر", worked=worked, delay=delay, early=early, ot=ot, night=night, hol=0, raw=raw)

def rows(approved=False):
    out = []
    for code, d, lab, sh, ev in DAYS:
        r = calc(sh, ev, 180 if (approved and code == "ATT01" and d.endswith("07/06")) else 0)
        if d.endswith("07/10"):
            r["hol"] = r["worked"]; r["delay"] = 0; r["early"] = 0
        out.append((code, d, lab, sh, ev, r))
    return out

if __name__ == "__main__":
    for code, d, lab, sh, ev, r in rows(True):
        print(code, d, r)
