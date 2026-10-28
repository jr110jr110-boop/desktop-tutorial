# -*- coding: utf-8 -*-
"""Appendix «نمونه‌ی محاسبه‌ی دستی»: worked examples with FICTIONAL numbers. Every derived number is computed here, so the text and the tables always agree.
Rounding follows the server: half away from zero (decimal.ROUND_HALF_UP); a short rial text has at most one decimal (the trailing zero is dropped)."""
from decimal import Decimal as D, ROUND_HALF_UP


def rnd(x, d=0):
    return D(x).quantize(D(1).scaleb(-d), rounding=ROUND_HALF_UP)


def grp(n):
    """12,345,678 (Latin; build.py turns it into Persian digits and separators)."""
    return "{:,}".format(int(n))


def short_rial(v):
    """The server's short text of a rial amount: 11.5 میلیارد ریال, 700 میلیون ریال, 2.7 میلیارد ریال."""
    a = abs(D(v)); sign = "-" if v < 0 else ""
    if a < 1000:
        return sign + grp(a) + " ریال"
    scales = [(D(10) ** 3, "هزار ریال"), (D(10) ** 6, "میلیون ریال"), (D(10) ** 9, "میلیارد ریال")]
    i = 0 if a < 10 ** 6 else 1 if a < 10 ** 9 else 2
    s = rnd(a / scales[i][0], 1)
    if s >= 1000 and i < 2:
        i += 1; s = rnd(a / scales[i][0], 1)
    txt = ("%s" % s).rstrip("0").rstrip(".") if "." in "%s" % s else "%s" % s
    return sign + txt + " " + scales[i][1]


def pct(x, d=1):
    return ("%s" % rnd(D(x) * 100, d))


# ------------------------------------------------------------------ 1. cash (نقد و بانک)
CASH_ROWS = [("صندوق مرکزی", "صندوق", 240_000_000, True),
             ("حساب جاری بانک نمونه ۱", "حساب بانکی", 7_810_500_000, True),
             ("حساب جاری بانک نمونه ۲", "حساب بانکی", 3_420_000_000, True),
             ("تنخواه واحد فروش", "تنخواه", 60_000_000, False)]
CASH = sum(v for _, _, v, inc in CASH_ROWS if inc)
CASH_BOXES = sum(v for _, k, v, _ in CASH_ROWS if k == "صندوق")
CASH_BANKS = sum(v for _, k, v, _ in CASH_ROWS if k == "حساب بانکی")
CASH_PETTY = sum(v for _, k, v, _ in CASH_ROWS if k == "تنخواه")
CASH_TOTAL_PAGE = CASH + CASH_PETTY
assert CASH == CASH_BOXES + CASH_BANKS

# ------------------------------------------------------------------ 2. receivables (مطالبات باز) and the 30 percent rule
# aging table «مطالبات به تفکیک مشتری»: current (up to 30 days after the due date, also not yet due), 31-60, 61-90, over 90
RECV_CUST = [("مشتری نمونه الف", 3_300_000_000, 300_000_000, 200_000_000, 0),
             ("مشتری نمونه ب", 1_500_000_000, 400_000_000, 300_000_000, 450_000_000),
             ("مشتری نمونه ج", 1_850_000_000, 0, 0, 0)]
# table «فاکتورهای فروش سررسیدگذشته»: every invoice whose due date has passed (customer, days overdue, open amount)
RECV_INV = [("مشتری نمونه الف", 12, 200_000_000), ("مشتری نمونه الف", 45, 300_000_000), ("مشتری نمونه الف", 75, 200_000_000),
            ("مشتری نمونه ب", 20, 100_000_000), ("مشتری نمونه ب", 40, 400_000_000), ("مشتری نمونه ب", 70, 300_000_000), ("مشتری نمونه ب", 120, 450_000_000),
            ("مشتری نمونه ج", 9, 50_000_000)]
RECV_OPEN = sum(sum(r[1:]) for r in RECV_CUST)
RECV_OVERDUE = sum(a for _, _, a in RECV_INV)
RECV_SHARE = D(RECV_OVERDUE) / D(RECV_OPEN)
RECV_SHARE_TEXT = pct(RECV_SHARE, 0)            # what the tile writes: whole percent
RECV_STATUS = "بحرانی" if RECV_OVERDUE * 100 >= RECV_OPEN * 30 else ("هشدار" if RECV_OVERDUE > 0 else "عادی")
# the tester's check of each customer (open total and overdue part)
RECV_BY_CUSTOMER = []
for name, cur, a, b, c in RECV_CUST:
    tot = cur + a + b + c
    od = sum(x for n, _, x in RECV_INV if n == name)
    RECV_BY_CUSTOMER.append((name, cur, a, b, c, tot, od))
assert RECV_OPEN == sum(r[5] for r in RECV_BY_CUSTOMER) and RECV_OVERDUE == sum(r[6] for r in RECV_BY_CUSTOMER)
# the same overdue amount sits in the buckets: three old buckets plus the part of the first bucket that is 1-30 days overdue
RECV_OLD = sum(a + b + c for _, _, a, b, c in RECV_CUST)
RECV_YOUNG = sum(x for _, d, x in RECV_INV if d <= 30)
assert RECV_OLD + RECV_YOUNG == RECV_OVERDUE
# a limit example: 29.6 percent is shown as 30 percent and is still a warning
LIMIT_OPEN, LIMIT_OVER = 10_000_000_000, 2_960_000_000
LIMIT_TEXT = pct(D(LIMIT_OVER) / D(LIMIT_OPEN), 0)
LIMIT_STATUS = "بحرانی" if LIMIT_OVER * 100 >= LIMIT_OPEN * 30 else "هشدار"
assert LIMIT_TEXT == "30" and LIMIT_STATUS == "هشدار"

# ------------------------------------------------------------------ 3. overdue payables (بدهی سررسیدگذشته) against the cash
# table «بدهی سررسیدگذشته به تأمین‌کنندگان (سن بدهی)»: current, 31-60, 61-90, over 90 days after the document date
PAY_ROWS = [("تأمین‌کننده نمونه ۱", 900_000_000, 600_000_000, 250_000_000, 0),
            ("تأمین‌کننده نمونه ۲", 0, 0, 400_000_000, 1_100_000_000),
            ("تأمین‌کننده نمونه ۳", 200_000_000, 320_000_000, 0, 0)]
PAY = []
for name, cur, a, b, c in PAY_ROWS:
    PAY.append((name, cur, a, b, c, a + b + c, cur + a + b + c))
PAY_OVERDUE = sum(r[5] for r in PAY)
PAY_COUNT = sum(1 for r in PAY if r[5] > 0)
PAY_STATUS = "بحرانی" if PAY_OVERDUE > CASH else ("هشدار" if PAY_OVERDUE > 0 else "عادی")
PAY_LOW_CASH = 2_000_000_000
PAY_STATUS_LOW = "بحرانی" if PAY_OVERDUE > PAY_LOW_CASH else "هشدار"

# ------------------------------------------------------------------ 4. OEE: company figure against the simple mean of the work centres
# columns of the drill: planned hours, downtime hours, run hours (booked), good pieces, scrap pieces, availability, performance, quality, OEE
# standard hours are not a column: performance = standard hours / run hours, so standard hours = performance x run hours
OEE_WC = [dict(code="WC-1", title="مرکز کار نمونه ۱ (کوچک و خوب)", planned=80, down=4, run=60, std=57, good=490, scrap=10),
          dict(code="WC-2", title="مرکز کار نمونه ۲ (بزرگ و ضعیف)", planned=320, down=80, run=200, std=130, good=1140, scrap=60)]
for w in OEE_WC:
    w["A"] = D(w["planned"] - w["down"]) / D(w["planned"])
    w["P"] = D(w["std"]) / D(w["run"])
    w["Q"] = D(w["good"]) / D(w["good"] + w["scrap"])
    w["OEE"] = w["A"] * w["P"] * w["Q"]
    w["A_t"], w["P_t"], w["Q_t"], w["OEE_t"] = pct(w["A"]), pct(w["P"]), pct(w["Q"]), pct(w["OEE"])
    # the tester multiplies the three displayed percents
    w["OEE_check"] = pct(D(w["A_t"]) / 100 * D(w["P_t"]) / 100 * D(w["Q_t"]) / 100)
    assert w["OEE_check"] == w["OEE_t"], (w["OEE_check"], w["OEE_t"])
OEE_MEAN = pct((OEE_WC[0]["OEE"] + OEE_WC[1]["OEE"]) / 2)
OEE_SUM = dict(planned=sum(w["planned"] for w in OEE_WC), down=sum(w["down"] for w in OEE_WC), run=sum(w["run"] for w in OEE_WC),
               std=sum(w["std"] for w in OEE_WC), good=sum(w["good"] for w in OEE_WC), scrap=sum(w["scrap"] for w in OEE_WC))
OEE_A = D(OEE_SUM["planned"] - OEE_SUM["down"]) / D(OEE_SUM["planned"])
OEE_P = D(OEE_SUM["std"]) / D(OEE_SUM["run"])
OEE_Q = D(OEE_SUM["good"]) / D(OEE_SUM["good"] + OEE_SUM["scrap"])
OEE_CO = OEE_A * OEE_P * OEE_Q
OEE_CO_T = pct(OEE_CO)
OEE_A_T, OEE_P_T, OEE_Q_T = pct(OEE_A), pct(OEE_P), pct(OEE_Q)


def oee_status(p):
    p = D(p)
    return "بحرانی" if p < 40 else ("هشدار" if p < 60 else "عادی")


OEE_CO_STATUS, OEE_MEAN_STATUS = oee_status(OEE_CO_T), oee_status(OEE_MEAN)
assert OEE_CO_STATUS != OEE_MEAN_STATUS

# ------------------------------------------------------------------ 5. change against the previous month (the pill)
# (title, today, previous month point, unit, higher is better: True / False / None, count text unit)
TREND = [("نقد و بانک", CASH, CASH - 1_000_000_000, "rial", True),
         ("مطالبات باز", RECV_OPEN, RECV_OPEN - 300_000_000, "rial", False),
         ("گزارش‌های عدم انطباق باز", 7, 9, "count", False),
         ("ارزش موجودی انبار", 14_200_000_000, 14_500_000_000, "rial", None),
         ("درخواست‌های خارج از مهلت (SLA)", 3, 3, "count", False)]
TREND_ROWS = []
for title, today, prev, unit, hib in TREND:
    delta = today - prev
    if delta == 0:
        arrow, text, tone = "■", "بدون تغییر نسبت به ماه قبل", "خاکستری"
    else:
        arrow = "▲" if delta > 0 else "▼"
        amount = short_rial(abs(delta)) if unit == "rial" else "%d مورد" % abs(delta)
        text = amount + (" بیشتر از ماه قبل" if delta > 0 else " کمتر از ماه قبل")
        tone = "خاکستری" if hib is None else ("سبز" if (delta > 0) == hib else "قرمز")
    TREND_ROWS.append((title, today, prev, unit, delta, arrow, text, tone))

# ------------------------------------------------------------------ 6. colours of a few counts (the rules are in the table «شاخص‌ها و آستانه‌های رنگ»)
def by_count(v, w, d):
    return "بحرانی" if v >= d else ("هشدار" if v >= w else "عادی")


COLOUR_ROWS = [("گزارش‌های عدم انطباق باز", 4, "(5, 15)", by_count(4, 5, 15)), ("گزارش‌های عدم انطباق باز", 7, "(5, 15)", by_count(7, 5, 15)),
               ("گزارش‌های عدم انطباق باز", 15, "(5, 15)", by_count(15, 5, 15)),
               ("دستورهای تولید دارای تأخیر", 0, "(1, 5)", by_count(0, 1, 5)), ("دستورهای تولید دارای تأخیر", 4, "(1, 5)", by_count(4, 1, 5)),
               ("دستورهای تولید دارای تأخیر", 5, "(1, 5)", by_count(5, 1, 5))]

if __name__ == "__main__":
    print("cash", CASH, short_rial(CASH), CASH_BOXES, CASH_BANKS, CASH_PETTY, CASH_TOTAL_PAGE)
    print("recv", RECV_OPEN, RECV_OVERDUE, RECV_SHARE_TEXT, RECV_STATUS, RECV_OLD, RECV_YOUNG, short_rial(RECV_OPEN), short_rial(RECV_OVERDUE))
    print("pay", PAY_OVERDUE, PAY_COUNT, PAY_STATUS, short_rial(PAY_OVERDUE), PAY_STATUS_LOW)
    for w in OEE_WC:
        print(w["code"], w["A_t"], w["P_t"], w["Q_t"], w["OEE_t"])
    print("oee mean", OEE_MEAN, OEE_MEAN_STATUS, "company", OEE_A_T, OEE_P_T, OEE_Q_T, OEE_CO_T, OEE_CO_STATUS, OEE_SUM)
    for r in TREND_ROWS:
        print(r)
    print(COLOUR_ROWS)
