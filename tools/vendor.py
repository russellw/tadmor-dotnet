#!/usr/bin/env python3
"""Maintain vendor/nuget, the committed NuGet feed the build restores from.

nuget.config restricts restore to vendor/nuget, so this tool is the only
thing that talks to nuget.org (docs/stack.md, "Supply-chain posture"):

  tools/vendor.py sync    Re-resolve every package from nuget.org, online,
                          into an empty package folder: the solution's
                          restore (rewriting the packages.lock.json files)
                          and the self-contained linux-x64 release restore,
                          which adds the runtime packs. Refuse any package
                          published less than 7 days ago, then replace
                          vendor/nuget and write vendor/lock.txt.
  tools/vendor.py check   Verify vendor/nuget against vendor/lock.txt,
                          offline: every locked package present with its
                          SHA-512, nothing else, every package that the
                          packages.lock.json files name locked, and
                          dependencies.json listing exactly those packages.
  tools/vendor.py manifest
                          Write dependencies.json, the manifest tadmor's
                          tools/measure.py reads (tadmor's
                          docs/counterpart-metrics.md): every package with
                          its category and the NuGet owner accounts that
                          can publish it, looked up online. sync runs it.

The packages.lock.json files carry NuGet's own content hashes, which for a
signed package leave out the signature, so they are not the file's SHA-512.
NuGet checks those itself: locked-mode restore refuses a package whose hash
differs from the lock file. vendor/lock.txt adds a plain SHA-512 of each
file, which this tool can verify without NuGet.

sync starts from nothing, so packages the build no longer uses drop out,
and a change is reviewable as a Directory.Packages.props diff plus lock
diffs. If sync fails, vendor/ and the lock files are left as they were.

Standard library only.
"""

import base64
import datetime
import gzip
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FEED = ROOT / "vendor" / "nuget"
LOCK = ROOT / "vendor" / "lock.txt"
SOLUTION = ROOT / "Tadmor.slnx"
SERVER = ROOT / "src" / "Tadmor" / "Tadmor.csproj"
REGISTRATION = "https://api.nuget.org/v3/registration5-gz-semver2/"
SEARCH = "https://azuresearch-usnc.nuget.org/query?q=packageid:{}&prerelease=true&semVerLevel=2.0.0"
MANIFEST = ROOT / "dependencies.json"
SERVER_LOCK = ROOT / "src" / "Tadmor" / "packages.lock.json"
COOLDOWN = datetime.timedelta(days=7)

ONLINE_CONFIG = """<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config><add key="globalPackagesFolder" value="{packages}" /></config>
  <packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
  <auditSources><clear /></auditSources>
</configuration>
"""


def sha512(path: Path) -> str:
    """The hash in NuGet's own form: base64 SHA-512 of the .nupkg."""
    return base64.b64encode(hashlib.sha512(path.read_bytes()).digest()).decode()


def lock_files():
    return sorted(ROOT.glob("src/*/packages.lock.json")) + sorted(ROOT.glob("tests/*/packages.lock.json"))


def dotnet_env():
    env = dict(os.environ)
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="1")
    return env


def restore(config: Path):
    common = ["--configfile", str(config), "--force-evaluate", "-p:RestoreLockedMode=false"]
    subprocess.run(["dotnet", "restore", str(SOLUTION), *common], cwd=ROOT, env=dotnet_env(), check=True)
    # What `make release` restores: the linux-x64 runtime and host packs.
    subprocess.run(["dotnet", "restore", str(SERVER), "-r", "linux-x64", "-p:SelfContained=true", *common],
                   cwd=ROOT, env=dotnet_env(), check=True)


def locked_identities():
    """(id, version) of every package the packages.lock.json files name."""
    found = set()
    for lock_file in lock_files():
        for target in json.loads(lock_file.read_text())["dependencies"].values():
            for package_id, entry in target.items():
                if entry.get("type") != "Project":
                    found.add((package_id.lower(), entry["resolved"].lower()))
    return found


def release_downloads():
    """The runtime and host packs of the last restore of the server, which
    restore() leaves as the linux-x64 self-contained one. NuGet fetches them
    as PackageDownload items, which lock files do not record."""
    assets = json.loads((SERVER.parent / "obj" / "project.assets.json").read_text())
    found = set()
    for framework in assets["project"]["frameworks"].values():
        for item in framework.get("downloadDependencies", []):
            found.add((item["name"].lower(), item["version"].strip("[]").split(",")[0].strip().lower()))
    return found


def published(package_id: str, version: str) -> datetime.datetime:
    url = f"{REGISTRATION}{package_id.lower()}/{version.lower()}.json"
    with urllib.request.urlopen(url, timeout=30) as resp:
        body = resp.read()
    if resp.headers.get("Content-Encoding") == "gzip" or body[:2] == b"\x1f\x8b":
        body = gzip.decompress(body)
    return datetime.datetime.fromisoformat(json.loads(body)["published"])


def nuspec_identity(package_dir: Path):
    """The package's id and version as nuget.org spells them, from the
    .nuspec NuGet extracted (the folder names are lowercased)."""
    nuspec = next(package_dir.glob("*.nuspec"))
    meta = next(el for el in ET.parse(nuspec).getroot() if el.tag.endswith("metadata"))
    field = {el.tag.split("}")[-1]: el.text for el in meta}
    return field["id"], field["version"]


def sync():
    saved = {p: p.read_bytes() for p in lock_files()}
    with tempfile.TemporaryDirectory(prefix="tadmor-vendor-") as tmp:
        tmp = Path(tmp)
        packages = tmp / "packages"
        config = tmp / "nuget.config"
        config.write_text(ONLINE_CONFIG.format(packages=packages))
        try:
            restore(config)
            # NuGet also downloads versions it considers and then passes
            # over, so take only what the lock files and the release name.
            wanted = locked_identities() | release_downloads()
            found = []
            for nupkg in sorted(packages.glob("*/*/*.nupkg")):
                package_id, version = nuspec_identity(nupkg.parent)
                if (package_id.lower(), version.lower()) in wanted:
                    found.append((package_id, version, nupkg))
            missing = wanted - {(i.lower(), v.lower()) for i, v, _ in found}
            if missing:
                raise SystemExit(f"restore did not download {sorted(missing)}")

            now = datetime.datetime.now(datetime.timezone.utc)
            young = []
            for package_id, version, _ in found:
                age = now - published(package_id, version)
                if age < COOLDOWN:
                    young.append(f"{package_id} {version} (published {age.days} days ago)")
            if young:
                raise SystemExit("refusing packages newer than the 7-day cooldown:\n  " + "\n  ".join(young))
        except BaseException:
            for path, data in saved.items():
                path.write_bytes(data)
            raise

        shutil.rmtree(FEED, ignore_errors=True)
        FEED.mkdir(parents=True)
        lines = []
        for package_id, version, nupkg in found:
            name = f"{package_id.lower()}.{version.lower()}.nupkg"
            shutil.copyfile(nupkg, FEED / name)
            lines.append(f"{package_id} {version} {sha512(FEED / name)}")
        LOCK.write_text(
            "# Every package in vendor/nuget: id, version, base64 SHA-512 of the .nupkg.\n"
            "# Written by tools/vendor.py sync; verified by tools/vendor.py check.\n"
            + "\n".join(lines) + "\n")
    print(f"vendored {len(lines)} packages into {FEED.relative_to(ROOT)}")
    manifest()
    check()


def check():
    locked = {}
    for line in LOCK.read_text().splitlines():
        if line and not line.startswith("#"):
            package_id, version, digest = line.split()
            locked[f"{package_id.lower()}.{version.lower()}.nupkg"] = (package_id, version, digest)
    problems = []
    present = {p.name for p in FEED.glob("*") if p.is_file()}
    for name in sorted(present - locked.keys()):
        problems.append(f"vendor/nuget/{name} is not in vendor/lock.txt")
    for name, (_, _, digest) in sorted(locked.items()):
        if name not in present:
            problems.append(f"vendor/nuget/{name} is missing")
        elif sha512(FEED / name) != digest:
            problems.append(f"vendor/nuget/{name} does not match its hash in vendor/lock.txt")

    vendored = {(i.lower(), v.lower()) for i, v, _ in locked.values()}
    for package_id, version in sorted(locked_identities() - vendored):
        problems.append(f"{package_id} {version} is in a packages.lock.json but not vendored")
    check_manifest(problems)
    if problems:
        raise SystemExit("\n".join(problems))
    print(f"vendor/nuget matches vendor/lock.txt ({len(locked)} packages)")


def categorized():
    """(id, version) -> category. The server's packages are runtime; what
    only the tests add is test. The runtime packs (download dependencies)
    are the .NET runtime itself, which the manifest lists as a toolchain."""
    def packages(lock_file):
        found = {}
        for target in json.loads(lock_file.read_text())["dependencies"].values():
            for package_id, entry in target.items():
                if entry.get("type") != "Project":
                    found[(package_id, entry["resolved"])] = True
        return found
    runtime = packages(SERVER_LOCK)
    out = {key: "runtime" for key in runtime}
    for lock_file in lock_files():
        if lock_file != SERVER_LOCK:
            for key in packages(lock_file):
                out.setdefault(key, "test")
    return out


def owners(package_id):
    """The NuGet accounts that can publish the package (owners are per package)."""
    with urllib.request.urlopen(SEARCH.format(package_id.lower()), timeout=30) as resp:
        data = json.load(resp)["data"]
    match = [d for d in data if d["id"].lower() == package_id.lower()]
    return sorted(f"nuget:{o}" for o in match[0]["owners"]) if match else None


def unpacked_bytes(package_id, version):
    with zipfile.ZipFile(FEED / f"{package_id.lower()}.{version.lower()}.nupkg") as z:
        return sum(i.file_size for i in z.infolist())


def manifest():
    """Writes dependencies.json (tadmor's docs/counterpart-metrics.md)."""
    cats = categorized()
    packages = []
    for (package_id, version), category in sorted(cats.items(), key=lambda kv: (kv[1], kv[0][0].lower())):
        packages.append({
            "ecosystem": "nuget", "name": package_id, "version": version, "category": category,
            "identities": owners(package_id), "evidence": SEARCH.format(package_id.lower()),
        })
    sources = []
    for category in ("runtime", "test"):
        keys = [k for k, c in cats.items() if c == category]
        sources.append({"label": f"NuGet packages ({category}, unpacked)",
                        "bytes": sum(unpacked_bytes(*k) for k in keys), "lines": None})
    doc = {
        "format": "tadmor-dependencies/1",
        "generator": "tools/vendor.py manifest (tadmor-dotnet)",
        "platform": "linux/x64",
        "toolchains": [
            "Microsoft .NET SDK 10.0 (Ubuntu's dotnet-sdk-10.0 build)",
            "Microsoft .NET runtime packs from NuGet, bundled into the self-contained release",
        ],
        "packages": packages,
        "sources": sources,
    }
    MANIFEST.write_text(json.dumps(doc, indent=1) + "\n")
    print(f"wrote {MANIFEST.relative_to(ROOT)}: {len(packages)} packages")


def check_manifest(problems):
    if not MANIFEST.exists():
        problems.append("dependencies.json is missing; run tools/vendor.py manifest")
        return
    listed = {(p["name"], p["version"]): p["category"] for p in json.loads(MANIFEST.read_text())["packages"]}
    if listed != categorized():
        problems.append("dependencies.json does not list the locked packages; run tools/vendor.py manifest")


def main():
    commands = {"sync": sync, "check": check, "manifest": manifest}
    if len(sys.argv) != 2 or sys.argv[1] not in commands:
        raise SystemExit("usage: tools/vendor.py sync|check|manifest")
    commands[sys.argv[1]]()


if __name__ == "__main__":
    main()
