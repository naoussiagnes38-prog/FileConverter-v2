"""Validate a complete offline payload and build its NSIS Windows installer."""
import argparse
import os
import subprocess
from pathlib import Path
from tempfile import TemporaryDirectory
from zipfile import ZIP_DEFLATED, ZIP_STORED, ZipFile


def validate(payload):
    required = [
        "FileConverter.exe", "FileConverter.Core.dll", "FileConverter.exe.config",
        "System.ValueTuple.dll", "TranslationRuntime/python.exe",
        "TranslationRuntime/python312.dll", "TranslationRuntime/python312._pth",
        "TranslationRuntime/run_libretranslate.py", "TranslationRuntime/document_worker.py",
        "TranslationRuntime/layout_worker.py", "TranslationRuntime/site-packages/docx/__init__.py",
        "OfficeRuntime/program/soffice.com", "OfficeRuntime/program/soffice.bin",
        "OfficeRuntime/license.txt", "OfficeRuntime/NOTICE",
        "Prerequisites/VC_redist.x64.exe",
    ]
    for component in ("", ".Core", ".Fonts", ".Tokens", ".Tokenization", ".DocumentLayoutAnalysis"):
        required.append("UglyToad.PdfPig" + component + ".dll")
    for source, target in (("en", "fr"), ("fr", "en")):
        model = "TranslationRuntime/models/translate-%s_%s-1_9/" % (source, target)
        required.extend([model + "metadata.json", model + "model/model.bin", model + "minisbd/" + source + ".onnx"])
    missing = [name for name in required if not (payload / name).is_file()]
    if missing:
        raise ValueError("Incomplete installer payload: " + ", ".join(missing))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--makensis", default="makensis")
    parser.add_argument("--nsis-dir", type=Path)
    args = parser.parse_args()
    payload, output = args.payload.resolve(), args.output.resolve()
    validate(payload)
    output.parent.mkdir(parents=True, exist_ok=True)
    files = sorted(path for path in payload.rglob("*") if path.is_file())
    directories = sorted((path for path in payload.rglob("*") if path.is_dir()), key=lambda path: len(path.parts), reverse=True)

    def deployed_path(path):
        return "$INSTDIR\\" + str(path.relative_to(payload)).replace("/", "\\").replace("$", "$$")

    with TemporaryDirectory(prefix="fileconverter-installer-") as temporary:
        archive = Path(temporary) / "payload.zip"
        print("Compressing validated offline payload...", flush=True)
        with ZipFile(archive, "w", ZIP_DEFLATED, compresslevel=1) as package:
            for path in files:
                stored = path.suffix.lower() in (".zip", ".gz", ".png", ".jpg", ".jpeg", ".exe")
                package.write(path, path.relative_to(payload).as_posix(),
                              compress_type=ZIP_STORED if stored else ZIP_DEFLATED)
        print("Building setup executable...", flush=True)
        manifest = Path(temporary) / "uninstall-files.nsh"
        lines = ['Delete "%s"' % deployed_path(path) for path in files]
        lines += ['RMDir "%s"' % deployed_path(path) for path in directories]
        manifest.write_text("\n".join(lines) + "\n", encoding="utf-8-sig")
        environment = os.environ.copy()
        if args.nsis_dir:
            environment["NSISDIR"] = str(args.nsis_dir.resolve())
        option = "/" if os.name == "nt" else "-"
        subprocess.run([
            args.makensis, option + "V2", option + "DPAYLOAD=" + str(payload),
            option + "DMANIFEST=" + str(manifest), option + "DOUTPUT=" + str(output),
            option + "DARCHIVE=" + str(archive),
            str(Path(__file__).with_name("FileConverter.nsi").resolve()),
        ], check=True, env=environment)
    print(output, flush=True)


if __name__ == "__main__":
    main()
