# FileConverter

FileConverter is a Windows desktop application for converting and translating documents, compressing and extracting ZIP archives, and encrypting or decrypting files. The interface is written in C# with WPF and XAML. Translation uses bundled English/French models in a complete offline release.

This README explains how to recreate the application from the source and how to prepare a distributable installer. It is written for a future developer setting up a new build computer.

## Project contents

- `FileConverter.sln` — Visual Studio solution.
- `FileConverter/` — WPF application, screens, shared services, themes and language resources.
- `FileConverter.Core/` — conversion, compression, encryption, translation and model contracts/services.
- `packaging/` — document workers, offline runtime packaging scripts, NSIS installer definition, tests and detailed build instructions.
- `APPLICATION_GUIDE.md` — application architecture and file-by-file guide.
- `PRESENTATION_GUIDE.md` — presentation-ready explanation of the screens and conversion methods.
- `packaging/INSTALLER_BUILD_GUIDE.md` — first-time Windows installer setup.
- `packaging/REBUILD_INSTALLER.md` — repeated installer rebuild steps after setup is complete.

## Requirements

### To build and run the C# application

- Windows 10 or 11, 64-bit.
- Visual Studio Professional 2015 with the **.NET Framework 4.6 targeting pack** and WPF/.NET desktop development components.
- .NET Framework 4.6 or later on the computer that runs the application.
- NuGet packages listed in `FileConverter/packages.config` (PdfPig 0.1.8 and System.ValueTuple 4.5.0).

### To build the complete offline installer

In addition to Visual Studio, prepare:

- Python for running the packaging scripts. Python 3.15.2 was used for these scripts; the packaged translation runtime is a separate Windows x64 CPython 3.12.10 embeddable runtime.
- `uv` to stage the pinned Python dependencies from `packaging/translation-win-x64.lock.txt`.
- NSIS 3 to create the setup executable.
- LibreOffice 25.8.7 Windows x64 runtime files.
- English-to-French and French-to-English Argos 1.9 models and English/French MiniSBD ONNX files.
- Microsoft Visual C++ x64 redistributable.

Keep third-party licenses and notices with the packaged components. See the first-time [installer setup guide](packaging/INSTALLER_BUILD_GUIDE.md) for the authoritative download locations and exact staging paths.

## Build and run from Visual Studio

1. Install Visual Studio 2015 and the .NET Framework 4.6 targeting pack.
2. Open `FileConverter.sln`.
3. Allow NuGet to restore the packages in `FileConverter/packages.config`. If automatic restore is disabled, use **Tools → NuGet Package Manager → Package Manager Settings** and enable package restore, then restore the solution.
4. Choose the **Debug** configuration and **Any CPU** platform.
5. Select **Build → Build Solution**. Resolve any reported errors before running.
6. Press **F5** to launch the WPF application, or use **Ctrl+F5** to run without the debugger.

The Visual Studio build creates `FileConverter/bin/Debug/FileConverter.exe` or `FileConverter/bin/Release/FileConverter.exe`. A successful source build alone does not include all offline translation models or the bundled LibreOffice runtime. Use the offline package steps below for a self-contained release.

## Create a complete offline Windows installer

The installer is built in two stages: create a complete offline payload, then wrap it in an NSIS setup executable.

### 1. Prepare the staging folders

From PowerShell opened in the folder containing `FileConverter.sln`, run:

```powershell
$stage = Join-Path (Get-Location) 'staging'
New-Item -ItemType Directory -Force -Path `
  "$stage\site-packages", "$stage\models", "$stage\sources", "$stage\OfficeRuntime" | Out-Null
```

Download or extract the following files to these locations:

| Required file | Staging path |
| --- | --- |
| CPython 3.12.10 Windows x64 embeddable ZIP | `staging\python-3.12.10-embed-amd64.zip` |
| Argos English-to-French 1.9 model | `staging\models\translate-en_fr-1_9.argosmodel` |
| Argos French-to-English 1.9 model | `staging\models\translate-fr_en-1_9.argosmodel` |
| English and French MiniSBD ONNX models | `staging\models\en.onnx` and `staging\models\fr.onnx` |
| Microsoft Visual C++ x64 redistributable | `staging\sources\VC_redist.x64.exe` |
| LibreOffice 25.8.7 x64 extracted runtime | `staging\OfficeRuntime` |

Extract the LibreOffice MSI administratively so that `staging\OfficeRuntime\program\soffice.com` exists. Install `uv`, then stage the pinned Windows Python packages:

```powershell
uv pip install --only-binary :all: --no-deps --python-version 3.12 `
  --python-platform x86_64-pc-windows-msvc `
  --target .\staging\site-packages `
  -r .\packaging\translation-win-x64.lock.txt
```

These downloads are setup tasks. Keep the `staging` folder so later rebuilds can reuse the runtimes, models and packages.

### 2. Build the C# Release version

Open `FileConverter.sln` in Visual Studio 2015, choose **Release** and **Any CPU**, then select **Build → Rebuild Solution**. Continue only after Visual Studio reports a successful build.

### 3. Assemble the offline payload

In PowerShell, run:

```powershell
python .\packaging\build_offline_release.py `
  --app .\FileConverter\bin\Release `
  --python-zip .\staging\python-3.12.10-embed-amd64.zip `
  --site-packages .\staging\site-packages `
  --models .\staging\models `
  --sources .\staging\sources `
  --office-runtime .\staging\OfficeRuntime `
  --output .\staging\FileConverter-Offline-Windows-x64.zip
```

Extract it for the installer builder:

```powershell
Expand-Archive `
  -Path .\staging\FileConverter-Offline-Windows-x64.zip `
  -DestinationPath .\staging\extracted `
  -Force
```

The payload folder should be `staging\extracted\FileConverter`, and it must contain `FileConverter.exe`.

### 4. Create the installer

```powershell
python .\packaging\build_windows_installer.py `
  --payload .\staging\extracted\FileConverter `
  --output .\staging\FileConverter-Setup-1.1.5.exe `
  --makensis "C:\Program Files (x86)\NSIS\makensis.exe" `
  --nsis-dir "C:\Program Files (x86)\NSIS"
```

The script validates required application, document engine, translation model and prerequisite files, then creates the installer and its uninstall manifest. It can take several minutes because the offline runtimes and models are large.

## Test a release on Windows

Install the setup on a Windows x64 computer. Verify at least:

1. The application starts and the light/dark theme and English/French UI load.
2. Word, Excel, PowerPoint, image and PDF conversion methods work with representative files.
3. PDF merge keeps the pages and expected page canvas sizes.
4. English-to-French and French-to-English translation work without another LibreTranslate server running. Check PDF, DOCX and TXT outputs.
5. Compression, ZIP extraction, encryption and decryption work and preserve existing files.
6. Per-file progress, cancellation, output paths and History show the expected status.
7. Upgrade and uninstall work. User-created files in the installation folder are not deleted by the uninstaller.

The setup needs administrator access and .NET Framework 4.6 or later on the receiving computer. Sign the installer before distributing it to users who need publisher verification.

## Troubleshooting

- **Visual Studio reports `MSB3644`:** install the .NET Framework 4.6 targeting pack on the build computer.
- **NuGet references are missing:** restore the packages from `packages.config`, then rebuild.
- **`python` is not recognized:** install Python, reopen PowerShell, and confirm `python --version` works.
- **`uv` is not recognized:** finish installing uv, reopen PowerShell, and confirm `uv --version` works.
- **The installer builder reports `Incomplete installer payload`:** follow the missing paths in the error and check the corresponding staging files.
- **`makensis` cannot be found:** install NSIS 3 or correct `--makensis` and `--nsis-dir`.
- **Translation fails in an installed build:** inspect `%LOCALAPPDATA%\FileConverter\Translation\engine.log` and confirm both model directions are present in the payload.

## Technical notes

- The interface is WPF; XAML defines screens, styles, themes and English/French resource dictionaries.
- Document conversions use LibreOffice when the packaged runtime is available. Some fallback paths use installed Microsoft Office automation.
- PDF page handling and rendering use the bundled PyMuPDF worker. DOCX translation edits document XML while retaining unrelated package entries. TXT cannot preserve page layout or images.
- Translation calls the bundled local English/French Argos engine through a loopback LibreTranslate-compatible API. No public translation service is required by the offline release.
- The NSIS definition is `packaging/FileConverter.nsi`; release packaging and validation are implemented by `packaging/build_offline_release.py` and `packaging/build_windows_installer.py`.
