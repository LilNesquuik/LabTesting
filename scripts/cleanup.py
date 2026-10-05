"""Recovery after runner interruption. Only owned labtest children of the supplied parent."""
import os
from pathlib import Path
import shutil
import signal
import sys

parent = Path(sys.argv[1]).resolve()
if not parent.is_dir():
    sys.exit(0)
owned = []
for path in parent.iterdir():
    if path.is_symlink() or not path.is_dir() or not path.name.startswith("labtest-"):
        continue
    marker = path / ".labtesting-owned"
    if marker.is_file() and marker.read_text().strip() == path.name[len("labtest-"):]:
        owned.append(path.resolve())
# /proc snapshot: find processes executing inside owned copies, then their descendants.
processes = {}
selected = set()
if Path("/proc").is_dir():
    for entry in Path("/proc").iterdir():
        if not entry.name.isdigit():
            continue
        try:
            pid = int(entry.name)
            executable = (entry / "exe").resolve(strict=True)
            status = (entry / "status").read_text()
            ppid = int(next(x.split()[1] for x in status.splitlines() if x.startswith("PPid:")))
            processes[pid] = ppid
            if any(root in executable.parents for root in owned):
                selected.add(pid)
        except (OSError, ValueError, StopIteration):
            continue
    while True:
        descendants = {pid for pid, ppid in processes.items() if ppid in selected}
        if descendants <= selected:
            break
        selected |= descendants
    for pid in selected:
        try:
            os.kill(pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
for root in owned:
    if root.parent != parent:
        raise RuntimeError("Ownership boundary changed")
    shutil.rmtree(root)
