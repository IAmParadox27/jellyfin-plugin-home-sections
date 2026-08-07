import re
import subprocess
from pathlib import Path

root = Path(r"C:\projects\jellyfin-stuff\jellyfin-plugin-home-sections")
cmd = [
    "dotnet",
    "build",
    "src/Jellyfin.Plugin.HomeScreenSections/Jellyfin.Plugin.HomeScreenSections.csproj",
    "-c",
    "Release",
    "/p:JellyfinVersion=10.11.11",
    "--no-restore",
]
r = subprocess.run(cmd, cwd=root, capture_output=True, text=True)
seen = set()
for line in (r.stdout + r.stderr).splitlines():
    m = re.search(
        r"(src\\Jellyfin\.Plugin\.HomeScreenSections\\[^\(]+)\((\d+),(\d+)\): error (\w+):",
        line,
    )
    if not m:
        continue
    rel, ln, col, rule = m.group(1), int(m.group(2)), int(m.group(3)), m.group(4)
    key = (rel, ln, rule)
    if key in seen:
        continue
    seen.add(key)
    path = root / rel.replace("\\", "/")
    if not path.exists():
        # try find by filename
        matches = list(root.rglob(Path(rel).name))
        path = matches[0] if matches else None
    if path is None or not path.exists():
        print(f"{rule} {rel}:{ln} <missing file>")
        continue
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    code = lines[ln - 1] if 0 < ln <= len(lines) else "<oob>"
    print(f"{rule} {path.name}:{ln}:{col}")
    print(f"  {code.strip()}")
