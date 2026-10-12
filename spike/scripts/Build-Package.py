#!/usr/bin/env python3
"""Build a self-contained Windows x64 spike ZIP on macOS or Windows."""
import hashlib
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile


def main():
    root = Path(__file__).resolve().parents[1]
    dist = root / "dist"
    dist.mkdir(exist_ok=True)
    subprocess.run(["dotnet", "test", str(root / "SpikeLogic.Tests/SpikeLogic.Tests.csproj"),
                    "-c", "Release", "--verbosity", "minimal"], check=True)
    with tempfile.TemporaryDirectory(prefix="live-streamer-spike-") as tmp:
        package = Path(tmp) / "LiveStreamerSpike"
        subprocess.run(["dotnet", "publish", str(root / "CaptureSpike/CaptureSpike.csproj"),
                        "-c", "Release", "-r", "win-x64", "--self-contained", "true",
                        "-p:PublishSingleFile=false", "-o", str(package),
                        "--verbosity", "minimal"], check=True)
        shutil.copy2(root / "README.md", package / "README.md")
        shutil.copytree(root / "mql5", package / "mql5")
        (package / "scripts").mkdir()
        for script in (root / "scripts").glob("*.ps1"):
            shutil.copy2(script, package / "scripts" / script.name)
        archive = dist / "LiveStreamerSpike.zip"
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as output:
            for file in sorted(package.rglob("*")):
                if file.is_file():
                    output.write(file, file.relative_to(Path(tmp)))
    digest = hashlib.file_digest(archive.open("rb"), "sha256").hexdigest()
    archive.with_suffix(".zip.sha256").write_text(f"{digest}  {archive.name}\n")
    print(f"Package: {archive}\nSHA-256: {digest}")


if __name__ == "__main__":
    main()
