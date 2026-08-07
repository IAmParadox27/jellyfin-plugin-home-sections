import re
import subprocess
from collections import Counter
from pathlib import Path

root = Path(r"C:\projects\jellyfin-stuff\jellyfin-plugin-home-sections")
cmd = [
    "dotnet",
    "build",
    "src/Jellyfin.Plugin.HomeScreenSections/Jellyfin.Plugin.HomeScreenSections.csproj",
    "-c",
    "Release",
    "/p:JellyfinVersion=10.11.11",
    "/t:Rebuild",
]
r = subprocess.run(cmd, cwd=root, capture_output=True, text=True)
errs = []
for line in (r.stdout + r.stderr).splitlines():
    m = re.search(r"\\([^\\]+\.cs)\((\d+),(\d+)\): error (\w+):", line)
    if m:
        errs.append((m.group(4), m.group(1), int(m.group(2)), int(m.group(3))))

print(f"total errors: {len(errs)}")
print(Counter(e[0] for e in errs))
for e in sorted(set(errs)):
    print(f"{e[0]} {e[1]}:{e[2]}:{e[3]}")
