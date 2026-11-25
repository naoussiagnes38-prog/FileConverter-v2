# Build the FileConverter Installer on Windows

This guide is for creating a new installer from the project. It is not needed to install FileConverter. The finished installer is `staging\FileConverter-Setup-1.1.6.exe`.

There are two parts: prepare the build computer and download the release files once; then rebuild the application and create the installer whenever you need a new release.

## Part 1: Set up the build computer once

Use a Windows 10 or 11 64-bit computer. Install:

1. **Visual Studio Professional 2015** with the **.NET Framework 4.6 targeting pack**.
2. **Python**. Open Command Prompt and run `python --version`. It must print a version number. Python 3.15.2 works for these packaging commands.
3. **uv**. In PowerShell, run:

   ```powershell
   powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
   ```

   Close and reopen PowerShell, then run `uv --version`.

4. **NSIS 3**. Install it using its normal Windows installer. The usual folder is `C:\Program Files (x86)\NSIS`.

## Part 2: Prepare the release files once

In File Explorer, open the project folder that contains `FileConverter.sln`. Click the address bar, type `powershell`, and press Enter. Run:

```powershell
$stage = Join-Path (Get-Location) 'staging'
New-Item -ItemType Directory -Force -Path `
  "$stage\site-packages", "$stage\models", "$stage\sources", "$stage\OfficeRuntime" | Out-Null
```

Download or extract these files into the named folders:

| File or folder | Put it here |
| --- | --- |
| Python 3.12.10 **Windows embeddable package (64-bit)** ZIP from [python.org](https://www.python.org/downloads/release/python-31210/) | `staging\python-3.12.10-embed-amd64.zip` |
| [English-to-French Argos model](https://data.argosopentech.com/argospm/v1/translate-en_fr-1_9.argosmodel) | `staging\models\translate-en_fr-1_9.argosmodel` |
| [French-to-English Argos model](https://data.argosopentech.com/argospm/v1/translate-fr_en-1_9.argosmodel) | `staging\models\translate-fr_en-1_9.argosmodel` |
| English and French MiniSBD `.onnx` files from the [MiniSBD release](https://github.com/LibreTranslate/MiniSBD/releases/tag/v0.0.1) | `staging\models\en.onnx` and `staging\models\fr.onnx` |
| x64 Visual C++ Redistributable from [Microsoft](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist/) | `staging\sources\VC_redist.x64.exe` |

Extract the LibreOffice 25.8.7 x64 MSI administratively. Replace the example paths with the MSI path on your computer and this project’s full path:

```powershell
msiexec /a "C:\Users\Elvira\Downloads\LibreOffice_25.8.7_Win_x86-64.msi" `
  TARGETDIR="C:\PATH\TO\FileConverter\staging\OfficeRuntime" /qn
```

The extracted runtime must be directly inside `staging\OfficeRuntime`. Check that this prints `True`:

```powershell
Test-Path .\staging\OfficeRuntime\program\soffice.com
```

Download the pinned Python packages into `staging\site-packages`:

```powershell
uv pip install --only-binary :all: --no-deps --python-version 3.12 `
  --python-platform x86_64-pc-windows-msvc `
  --target .\staging\site-packages `
  -r .\packaging\translation-win-x64.lock.txt
```

This dependency download and file preparation are one-time tasks. Keep the `staging` folder so you do not need to download them again for the next build.

## Part 3: Build a new installer

### 1. Rebuild the C# application

Open `FileConverter.sln` in Visual Studio. Choose **Release** and **Any CPU**, then select **Build → Rebuild Solution**. Continue only when Visual Studio reports a successful build.

### 2. Create the complete offline application package

In the same PowerShell window, run:

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

Wait for the command to finish. It creates `staging\FileConverter-Offline-Windows-x64.zip`.

### 3. Extract the package for the installer builder

```powershell
Expand-Archive `
  -Path .\staging\FileConverter-Offline-Windows-x64.zip `
  -DestinationPath .\staging\extracted `
  -Force
```

The folder passed to the next command must contain `FileConverter.exe`:
`staging\extracted\FileConverter\FileConverter.exe`.

### 4. Create the installer EXE

```powershell
python .\packaging\build_windows_installer.py `
  --payload .\staging\extracted\FileConverter `
  --output .\staging\FileConverter-Setup-1.1.6.exe `
  --makensis "C:\Program Files (x86)\NSIS\makensis.exe" `
  --nsis-dir "C:\Program Files (x86)\NSIS"
```

When it finishes, your installer is:

```text
staging\FileConverter-Setup-1.1.6.exe
```

The setup is large because it includes LibreOffice, Python, and the offline English/French translation models. Test it on a Windows computer before sharing it. The installer needs administrator access and .NET Framework 4.6 or later on the receiving computer.

## If a command fails

- **`python` is not recognized:** install Python, reopen PowerShell, and check `python --version`.
- **`uv` is not recognized:** finish installing uv, reopen PowerShell, and check `uv --version`.
- **`MSB3644`:** install the .NET Framework 4.6 targeting pack on the build computer.
- **`Incomplete installer payload`:** read the missing file paths in the error and check the corresponding files under `staging`.
- **`makensis` is not found:** install NSIS 3 or correct the two NSIS paths in Step 4.

You do not need to create the uninstall manifest yourself; `build_windows_installer.py` creates it automatically.
