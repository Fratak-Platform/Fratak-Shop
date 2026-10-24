#!/usr/bin/env python3
"""Prints the failed tests of one or more .trx files as markdown (name, message, first stack lines)."""
import glob
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
total = passed = failed = 0
out = []
for path in sys.argv[1:] or glob.glob("**/*.trx", recursive=True):
    root = ET.parse(path).getroot()
    counters = root.find("t:ResultSummary/t:Counters", NS)
    if counters is not None:
        total += int(counters.get("total", 0))
        passed += int(counters.get("passed", 0))
        failed += int(counters.get("failed", 0))
    for result in root.iterfind(".//t:UnitTestResult", NS):
        if result.get("outcome") != "Failed":
            continue
        message = (result.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS) or "").strip()
        stack = (result.findtext("t:Output/t:ErrorInfo/t:StackTrace", default="", namespaces=NS) or "").strip().splitlines()
        out.append(f"- **{result.get('testName')}**\n```\n{message[:1500]}\n" + "\n".join(stack[:6]) + "\n```")
print(f"### Tests: {passed}/{total} passed, {failed} failed\n")
print("\n".join(out[:60]))
