# Offline Windows release

Build the FileConverter solution in Release and restore its NuGet packages.
Use the x64 CPython 3.12.10 embeddable archive from the official Python release.
Stage the Python packages from `translation-win-x64.lock.txt` in a clean directory.

```sh
uv pip install --only-binary :all: --no-deps --python-version 3.12 \
  --python-platform x86_64-pc-windows-msvc --target staging/site-packages \
  -r packaging/translation-win-x64.lock.txt
```

Put both Argos 1.9 archives and the English and French MiniSBD ONNX files
in `staging/models`. Put the Microsoft x64 Visual C++ redistributable
under `staging/sources/VC_redist.x64.exe`, along with the corresponding
third-party source archives for this distribution.

```sh
python3 packaging/build_offline_release.py \
  --app FileConverter/bin/Release \
  --python-zip staging/python-3.12.10-embed-amd64.zip \
  --site-packages staging/site-packages \
  --models staging/models --sources staging/sources \
  --office-runtime staging/OfficeRuntime \
  --output staging/FileConverter-Offline-Windows-x64.zip
```

Before sharing a release, extract it on Windows x64 with the network
disconnected. Run Offline Translation Test.cmd and then use FileConverter
to translate a document in each direction with no other LibreTranslate
server running. Verify that closing the app stops the server it started.

- [CPython 3.12.10 downloads](https://www.python.org/downloads/release/python-31210/)
- [Argos model index](https://github.com/argosopentech/argospm-index)
- [MiniSBD model release](https://github.com/LibreTranslate/MiniSBD/releases/tag/v0.0.1)
- [Visual C++ runtime downloads](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)

## Windows installer

The NSIS definition is FileConverter.nsi. Extract the portable release to
a staging directory and generate uninstall-files.nsh containing a Delete
instruction for each payload file and an RMDir instruction for each
payload directory in deepest-first order. The uninstall list deliberately
names deployed files so additional user files are preserved.

Compile with NSIS 3, passing PAYLOAD (the extracted FileConverter folder),
MANIFEST (the generated uninstall-files.nsh), and OUTPUT (the setup EXE).
The installer requires Windows x64 and .NET Framework 4.6 or later.
It requests administrator access, installs into Program Files, installs
the included Microsoft Visual C++ runtime, and registers shortcuts and
an uninstaller. Test installation, translation, upgrade, and uninstall
on Windows before distribution. Sign the final EXE when distributing
a trusted production release.

## Included document engines (1.0.1)

Extract the official LibreOffice 25.8.7 Windows x64 MSI into OfficeRuntime
(the directory containing program/soffice.com), preserving license.txt,
LICENSE.html and NOTICE. MSI SHA-256:
ecdb65e76f5e91dc198b8c8dce5b5d6e1eb12fea6023553e52b591afd10b619d.
The bundled engine uses an isolated temporary user profile per conversion.
PyMuPDF handles PDF extraction/rendering and high-fidelity page-image PDF-to-Word output. Page content in those Word documents is not directly editable.
The layout worker preserves DOCX package structure, and replaces selectable PDF text
while retaining page graphics. Text fitting and complex PDF reconstruction can differ.
The local translation launcher serves the app's JSON API directly using
Argos and both installed models. Startup/inference errors are logged under
LOCALAPPDATA/FileConverter/Translation/engine.log. No web UI or model
download step is used. Preserve component licenses when distributing.

Validate a complete staged payload and compile the setup with:

```sh
python3 packaging/build_windows_installer.py \
  --payload staging/FileConverter \
  --output staging/FileConverter-Setup-1.0.1.exe \
  --makensis /path/to/makensis --nsis-dir /path/to/share/nsis
```

The builder checks required office/PDF/translation files and generates an
exact uninstall manifest. The NSIS_DIR options can be omitted with a normal
NSIS installation on Windows (use --makensis to select its executable).
See RELEASE-CHECKS.md for the tested scope and remaining Windows checks.

The installer builder compresses the payload once into a ZIP and embeds it
without recompression. Setup uses a temporary copy of the bundled Python
standard library to unpack it, without requiring Python on the computer.
Office help files and non-English/French interface resources are omitted.

## Version 1.1.0

Stage the pinned python-docx dependency from translation-win-x64.lock.txt. Copy
layout_worker.py alongside document_worker.py and run_libretranslate.py.
The installer validator now requires the layout worker and PDF/DOCX engines.
Build Windows x64 and test against RELEASE-CHECKS.md before distribution.
