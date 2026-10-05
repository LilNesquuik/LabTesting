"""Parse templates and check local reusable workflow input contracts (requires PyYAML)."""
from pathlib import Path
import yaml

class Loader(yaml.SafeLoader):
    pass

# GitHub uses YAML 1.2: 'on' is a key, not the YAML 1.1 boolean.
Loader.yaml_implicit_resolvers = {
    key: [(tag, pattern) for tag, pattern in entries if tag != "tag:yaml.org,2002:bool"]
    for key, entries in Loader.yaml_implicit_resolvers.items()
}
paths = list(Path(".github/workflows").glob("*.yml")) + list(Path("templates/github").glob("*.yml"))
for path in paths:
    document = yaml.load(path.read_text(), Loader=Loader)
    assert "on" in document and "jobs" in document, path
    for job in document["jobs"].values():
        target = job.get("uses", "")
        if target.startswith("./"):
            called = yaml.load(Path(target).read_text(), Loader=Loader)["on"]["workflow_call"] or {}
            inputs = called.get("inputs", {})
            supplied = job.get("with", {})
            assert set(supplied) <= set(inputs), (path, "unknown input")
            assert all(key in supplied for key, value in inputs.items() if str(value.get("required")).lower() == "true"), (path, "missing input")
    print("OK", path)
