# Rebuild the FileConverter Installer

Use these steps when the one-time setup is already complete and the project's `staging` folder still contains its runtime files. You do not need to download the models or runtimes again.

If this is the first build on this computer, or the `staging` folder is missing, follow [INSTALLER_BUILD_GUIDE.md](INSTALLER_BUILD_GUIDE.md) first.

## Step 1: Rebuild the application

1. Open `FileConverter.sln` in Visual Studio 2015.
2. Choose **Release** and **Any CPU** in the toolbar.
3. Select **Build → Rebuild Solution**.
4. Continue only after Visual Studio reports **Build succeeded**.

## Step 2: Open PowerShell in the project folder

In File Explorer, open the folder containing `FileConverter.sln`. Click the address bar, type `powershell`, and press Enter.

The PowerShell prompt should now be in the project folder. Check by running:

```powershell
Test-Path .\FileConverter.sln
```

It must print `True`. If it prints `False`, open PowerShell from the correct folder.

## Step 3: Create the offline package

Copy and run this command in PowerShell:

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

Wait until it finishes. It creates:

```text
staging\FileConverter-Offline-Windows-x64.zip
```

## Step 4: Extract the offline package

Delete the old extracted copy first so files from an earlier build are not included:

```powershell
if (Test-Path .\staging\extracted) {
  Remove-Item .\staging\extracted -Recurse -Force
}
```

Then run:

```powershell
Expand-Archive `
  -Path .\staging\FileConverter-Offline-Windows-x64.zip `
  -DestinationPath .\staging\extracted `
  -Force
```

The extracted app must be here:

```text
staging\extracted\FileConverter\FileConverter.exe
```

## Step 5: Build the installer

Run:

```powershell
python .\packaging\build_windows_installer.py `
  --payload .\staging\extracted\FileConverter `
  --output .\staging\FileConverter-Setup-1.1.6.exe `
  --makensis "C:\Program Files (x86)\NSIS\makensis.exe" `
  --nsis-dir "C:\Program Files (x86)\NSIS"
```

The installer is created here:

```text
staging\FileConverter-Setup-1.1.6.exe
```

The command checks the package contents and creates the uninstall list automatically. It can take several minutes because the installer contains the offline document and translation engines.

## If the rebuild stops

- **`Test-Path` prints `False`:** reopen PowerShell from the folder containing `FileConverter.sln`.
- **Visual Studio build fails:** resolve the reported build error before running the packaging commands.
- **`python` is not recognized:** install Python, reopen PowerShell, then check `python --version`.
- **A file is missing under `staging`:** use [INSTALLER_BUILD_GUIDE.md](INSTALLER_BUILD_GUIDE.md) to prepare the one-time files.
- **`makensis` is not found:** install NSIS 3 or correct the NSIS paths in Step 5.
