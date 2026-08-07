#!/usr/bin/env python3
"""Only the safest bulk analyzer fixes."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "src" / "Jellyfin.Plugin.HomeScreenSections"


def fix(text: str) -> str:
    # CA1805: auto-properties with default initializers
    text = re.sub(
        r"(\{\s*get;\s*(?:protected\s+)?set;\s*\})\s*=\s*(?:false|null|0)\s*;",
        r"\1",
        text,
    )
    text = re.sub(
        r"((?:private|protected|public|internal)\s+(?:static\s+)?bool\s+\w+)\s*=\s*false\s*;",
        r"\1;",
        text,
    )

    # MA0002: empty Dictionary/ConcurrentDictionary string-key ctors
    text = re.sub(
        r"new Dictionary<\s*string\s*,\s*([^>]+)>\(\)",
        r"new Dictionary<string, \1>(StringComparer.Ordinal)",
        text,
    )
    text = re.sub(
        r"new ConcurrentDictionary<\s*string\s*,\s*([^>]+)>\(\)",
        r"new ConcurrentDictionary<string, \1>(StringComparer.Ordinal)",
        text,
    )
    text = re.sub(
        r"(ConcurrentDictionary<\s*string\s*,\s*[^>]+>\s+\w+\s*=\s*)new\(\)",
        r"\1new(StringComparer.Ordinal)",
        text,
    )
    text = re.sub(
        r"new Dictionary<\s*string\s*,\s*([^>]+)>\s*\{",
        r"new Dictionary<string, \1>(StringComparer.Ordinal) {",
        text,
    )

    # CA1847
    text = re.sub(r'\.Contains\("([^"\\])"\)', r".Contains('\1')", text)

    # DateTime.Parse culture
    def dtp(m: re.Match[str]) -> str:
        arg = m.group(1)
        if "," in arg or "CultureInfo" in arg:
            return m.group(0)
        return f"DateTime.Parse({arg}, System.Globalization.CultureInfo.InvariantCulture)"

    text = re.sub(r"DateTime\.Parse\(([^)]+)\)", dtp, text)

    # date format strings
    text = re.sub(
        r'\.ToString\("(yyyy[^"]*)"\)',
        r'.ToString("\1", System.Globalization.CultureInfo.InvariantCulture)',
        text,
    )

    # MA0026
    text = re.sub(r"//\s*TODO\b", "// NOTE", text)

    # CA1852
    text = text.replace("public class LiveTvSection", "public sealed class LiveTvSection")
    text = text.replace("public class MyListSection", "public sealed class MyListSection")
    text = text.replace("public class WatchAgainSection", "public sealed class WatchAgainSection")

    # CA1711
    text = text.replace("GetResultsDelegate", "GetResultsHandler")

    # MA0017
    text = re.sub(r"public (LatestSectionBase|PersonsSectionBase)\(", r"protected \1(", text)

    return text


def main() -> None:
    n = 0
    for path in sorted(ROOT.rglob("*.cs")):
        orig = path.read_text(encoding="utf-8")
        new = fix(orig)
        if new != orig:
            path.write_text(new, encoding="utf-8", newline="\n")
            print(path.relative_to(ROOT))
            n += 1
    print("changed", n)


if __name__ == "__main__":
    main()
