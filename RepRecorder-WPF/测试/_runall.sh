#!/bin/bash
# NOTE: 700s per script because WPF-18 grew (it runs the low-integrity update test); the others finish in seconds.
cd "$(dirname "$0")"
: > _allresults.txt
for f in 验证WPF-*.ps1; do
  out="_out-${f%.ps1}.txt"
  timeout 700 powershell -NoProfile -ExecutionPolicy Bypass -File "$f" > "$out" 2>&1
  rc=$?
  if [ $rc -eq 0 ]; then verdict=PASS; else verdict="FAIL(rc=$rc)"; fi
  echo "$f -> $verdict" >> _allresults.txt
done
echo "ALL DONE" >> _allresults.txt
