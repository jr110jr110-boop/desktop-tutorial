# -*- coding: utf-8 -*-
"""Reads the 68 detail-table definitions of the CEO dashboard from the backend source AT THE RELEASED COMMIT (git show; the worktree is not touched)
and writes drills.json: key, module, title, description, parameters, columns. Run: python3 drills_gen.py"""
import json, os, re, subprocess, sys

REPO = "/home/claude/backend"
SHA = "1672cba"
BASE = "src/Services/NasbNiro/Ceo/Drills/"
FILES = ["FinanceDrills", "SalesDrills", "ProjectDrills", "ProductionDrills", "QualityDrills", "InventoryDrills", "ProcurementDrills", "ServiceDrills", "OfficeDrills", "AiDrills"]
MODULE = {"FinanceDrills": "finance", "SalesDrills": "sales", "ProjectDrills": "projects", "ProductionDrills": "production", "QualityDrills": "quality",
          "InventoryDrills": "inventory", "ProcurementDrills": "procurement", "ServiceDrills": "service", "OfficeDrills": "office", "AiDrills": "ai"}
CONST = {"MovementKinds": ["receipt", "issue", "all"], "RequestFilters": ["submitted", "approved", "all"], "PriorityNames": ["Critical", "High", "Normal", "Low"],
         "LetterStages": ["approval", "signature", "registered", "all"], "LeaveFilters": ["submitted", "approved", "all"],
         "AiNoteTypes.All": ["QualityNcr", "FinanceJournal", "BankLine", "SupplierIbanChange", "PurchaseRfq", "OfficeLetter", "OfficeMeeting", "PcWeeklyReport", "CeoBrief"]}
STR = r'"((?:[^"\\]|\\.)*)"'


def show(name):
    return subprocess.run(["git", "-C", REPO, "show", "%s:%s%s.cs" % (SHA, BASE, name)], capture_output=True, text=True, check=True).stdout


def unesc(s):
    return s.replace('\\"', '"').replace("\\\\", "\\")


def split_args(text):
    """Top-level comma split of the text between the parentheses of a call."""
    out, depth, cur, i, instr = [], 0, [], 0, False
    while i < len(text):
        c = text[i]
        if instr:
            cur.append(c)
            if c == "\\":
                cur.append(text[i + 1]); i += 1
            elif c == '"':
                instr = False
        elif c == '"':
            instr = True; cur.append(c)
        elif c in "({[":
            depth += 1; cur.append(c)
        elif c in ")}]":
            depth -= 1; cur.append(c)
        elif c == "," and depth == 0:
            out.append("".join(cur).strip()); cur = []
        else:
            cur.append(c)
        i += 1
    if "".join(cur).strip():
        out.append("".join(cur).strip())
    return out


def call_args(text, start):
    """text[start] is the '(' of a call; returns (args text, index after ')')."""
    depth, i, instr = 0, start, False
    while i < len(text):
        c = text[i]
        if instr:
            if c == "\\":
                i += 1
            elif c == '"':
                instr = False
        elif c == '"':
            instr = True
        elif c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return text[start + 1:i], i + 1
        i += 1
    raise ValueError("unbalanced")


def lit(a):
    m = re.fullmatch(STR, a.strip())
    return unesc(m.group(1)) if m else None


def allowed_of(a):
    a = a.strip()
    if a in CONST:
        return CONST[a]
    m = re.fullmatch(r"new\[\]\s*\{(.*)\}", a, re.S)
    if m:
        return [unesc(x) for x in re.findall(STR, m.group(1))]
    return None


def date_pair(kind, what=""):
    if kind == "Last30Days":
        return [dict(name="from", type="date", desc="از تاریخ" + what + "؛ پیش‌فرض ۳۰ روز پیش"), dict(name="to", type="date", desc="تا تاریخ" + what + "؛ پیش‌فرض امروز")]
    if kind == "MonthToDate":
        return [dict(name="from", type="date", desc="از تاریخ" + what + "؛ پیش‌فرض اول ماه شمسی جاری"), dict(name="to", type="date", desc="تا تاریخ" + what + "؛ پیش‌فرض امروز")]
    if kind == "CurrentMonth":
        return [dict(name="from", type="date", desc="از تاریخ" + what + "؛ پیش‌فرض اول ماه شمسی جاری"), dict(name="to", type="date", desc="تا تاریخ" + what + "؛ پیش‌فرض آخر ماه شمسی جاری")]
    return [dict(name="from", type="date", desc="از تاریخ" + what + "؛ پیش‌فرض اول ماه شمسی ۱۱ ماه پیش (۱۲ ماه شمسی اخیر)"), dict(name="to", type="date", desc="تا تاریخ" + what + "؛ پیش‌فرض امروز")]


def parse_params(text):
    params = []
    for m in re.finditer(r"Prm\.(\w+)\(", text):
        kind = m.group(1)
        args_text, _ = call_args(text, m.end() - 1)
        args = split_args(args_text)
        if kind in ("Last30Days", "Last12Months", "MonthToDate", "CurrentMonth"):
            params += date_pair(kind, lit(args[0]) if args and lit(args[0]) is not None else "")
        elif kind == "Days":
            d = int(args[0]); mx = int(args[1])
            params.append(dict(name="days", type="int", desc=lit(args[2]) + "؛ پیش‌فرض %d روز" % d, default=d, min=1, max=mx))
        elif kind == "Int":
            p = dict(name=lit(args[0]), type="int", desc=lit(args[1]))
            if len(args) > 2 and re.fullmatch(r"-?\d+", args[2]): p["default"] = int(args[2])
            if len(args) > 3 and re.fullmatch(r"-?\d+", args[3]): p["min"] = int(args[3])
            if len(args) > 4 and re.fullmatch(r"-?\d+", args[4]): p["max"] = int(args[4])
            params.append(p)
        elif kind == "Str":
            p = dict(name=lit(args[0]), type="string", desc=lit(args[1]))
            if len(args) > 2 and lit(args[2]) is not None: p["default"] = lit(args[2])
            if len(args) > 3 and allowed_of(args[3]): p["allowed"] = allowed_of(args[3])
            params.append(p)
        elif kind == "Bool":
            params.append(dict(name=lit(args[0]), type="bool", desc=lit(args[1])))
        elif kind == "Date":
            params.append(dict(name=lit(args[0]), type="date", desc=lit(args[1])))
    return params


def parse_columns(text):
    cols = []
    for m in re.finditer(r"Col\.(Text|Num|Rial|Pct|Date|Status|Link)\(", text):
        args_text, _ = call_args(text, m.end() - 1)
        args = split_args(args_text)
        if m.group(1) == "Link":
            continue
        cols.append(dict(key=lit(args[0]), title=lit(args[1]), kind=m.group(1).lower()))
    return cols


def parse_notes(body):
    """The fixed texts a drill puts under its title (the note argument of DrillResult.Slice / Paged)."""
    notes = []
    for m in re.finditer(r"DrillResult\.(Slice|Paged)\(", body):
        args_text, _ = call_args(body, m.end() - 1)
        a = split_args(args_text)
        # a note given by a variable or built from several pieces is not a fixed text: left out
        if len(a) > 2:
            t = lit(a[2])
            if t and t not in notes:
                notes.append(t)
    return notes


def parse_file(name):
    src = show(name)
    out = []
    # the monthly ledger helper
    helper = re.search(r"private static DrillDefinition LedgerByMonth", src)
    nxt = re.search(r"private static DrillDefinition \w+\(", src[helper.end():]) if helper else None
    helper_notes = parse_notes(src[helper.start():helper.end() + (nxt.start() if nxt else len(src))]) if helper else []
    for m in re.finditer(r"yield return LedgerByMonth\(", src):
        args_text, _ = call_args(src, m.end() - 1)
        a = split_args(args_text)
        out.append(dict(key=lit(a[0]), module="finance", title=lit(a[1]), description=lit(a[2]), link="/fn/reports/income-statement",
                        params=date_pair("Last12Months"),
                        columns=[dict(key="month", title="ماه", kind="text"), dict(key="amount", title=lit(a[4]), kind="rial"),
                                 dict(key="profit", title="سود خالص ماه", kind="rial"), dict(key="changePercent", title="تغییر نسبت به ماه قبل", kind="percent")],
                        notes=helper_notes))
    idx = [m.start() for m in re.finditer(r"new DrillDefinition\b", src)]
    for n, i in enumerate(idx):
        block = src[i:(idx[n + 1] if n + 1 < len(idx) else len(src))]
        head = block.split("Run =", 1)[0]
        km = re.search(r'Key = ' + STR, head)
        if not km:
            continue  # the helper's own "Key = key"
        tm = re.search(r'Title = ' + STR, head)
        dm = re.search(r'Description = ' + STR, head)
        lm = re.search(r'Link = ' + STR, head)
        pm = re.search(r"Parameters = (.*?)(?=\bColumns =)", head, re.S)
        cm = re.search(r"Columns = (.*)", head, re.S)
        out.append(dict(key=unesc(km.group(1)), module=MODULE[name], title=unesc(tm.group(1)), description=unesc(dm.group(1)) if dm else "",
                        link=unesc(lm.group(1)) if lm else "", params=parse_params(pm.group(1)) if pm else [], columns=parse_columns(cm.group(1)) if cm else [],
                        notes=parse_notes(block.split("Run =", 1)[1] if "Run =" in block else "")))
    return out


def main():
    allv = []
    for f in FILES:
        allv += parse_file(f)
    keys = [d["key"] for d in allv]
    assert len(keys) == len(set(keys)), "duplicate keys"
    assert len(allv) == 68, len(allv)
    for d in allv:
        assert d["title"] and d["columns"], d["key"]
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "drills.json")
    json.dump(allv, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("drills:", len(allv), "with parameters:", sum(1 for d in allv if d["params"]))


if __name__ == "__main__":
    main()
