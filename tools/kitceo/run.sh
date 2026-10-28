#!/bin/bash
# build the guide: html -> pdf -> page numbers of the contents list -> html -> pdf (twice so that the numbers settle)
set -e
cd /home/claude/kitceo
export NODE_PATH=$(npm root -g)
PDF=/home/claude/kitceo/out/PMIS_CEO_Dashboard_Test/2-Guides/0-Test-Guide-CEO-Dashboard-FA.pdf
mkdir -p $(dirname $PDF)
for i in 1 2 3; do
  python3 build.py
  node render.js /home/claude/kitceo/guide.html $PDF
  if python3 tocpages.py --check >/dev/null 2>&1; then echo "settled after pass $i"; break; fi
  python3 tocpages.py >/dev/null
done
python3 tocpages.py --check
pdfinfo $PDF | egrep "Pages|File size"
