# -*- coding: utf-8 -*-
"""Reads the page of every heading of the guide PDF (named destinations of the contents list) and writes pages.json.
python3 tocpages.py          writes pages.json
python3 tocpages.py --check  compares the PDF with pages.json (exit code 1 when a page moved)"""
import sys, os, json
import pypdf

HERE = os.path.dirname(os.path.abspath(__file__))
PDF = "/home/claude/kitceo/out/PMIS_CEO_Dashboard_Test/2-Guides/0-Test-Guide-CEO-Dashboard-FA.pdf"
PAGES = os.path.join(HERE, "pages.json")

r = pypdf.PdfReader(PDF)
pages = {k.lstrip("/"): r.get_destination_page_number(v) + 1 for k, v in r.named_destinations.items()}
want = [k for k in pages if k[0] in "sa" and (k[1:].isdigit() or k.startswith("sec-"))]
pages = {k: pages[k] for k in sorted(want)}
if "--check" in sys.argv:
    old = json.load(open(PAGES, encoding="utf-8"))
    moved = {k: (old.get(k), v) for k, v in pages.items() if old.get(k) != v}
    print("pages in PDF:", len(r.pages), "headings:", len(pages), "moved:", moved)
    sys.exit(1 if moved else 0)
json.dump(pages, open(PAGES, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("pages in PDF:", len(r.pages), "headings:", len(pages))
for k, v in pages.items():
    print(" ", k, v)
