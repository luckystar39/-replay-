#!/bin/bash
cd "d:/【完整包】非想天则整合包2026-06-15/RepRecorder-WPF"
: > _allresults.txt
for f in 验证WPF-*.ps1; do
  out="_out-${f%.ps1}.txt"
  timeout 300 powershell -NoProfile -ExecutionPolicy Bypass -File "$f" > "$out" 2>&1
  rc=$?
  if [ $rc -eq 0 ]; then verdict=PASS; else verdict="FAIL(rc=$rc)"; fi
  echo "$f -> $verdict" >> _allresults.txt
done
echo "ALL DONE" >> _allresults.txt
