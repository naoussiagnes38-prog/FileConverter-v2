Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"

!ifndef PAYLOAD
!error "Pass /DPAYLOAD with the extracted FileConverter folder."
!endif
!ifndef OUTPUT
!define OUTPUT "FileConverter-Setup.exe"
!endif

Name "FileConverter"
OutFile "${OUTPUT}"
InstallDir "$PROGRAMFILES64\FileConverter"
InstallDirRegKey HKLM "Software\FileConverter" "InstallDir"
RequestExecutionLevel admin
SetCompressor zlib
SetDatablockOptimize off
VIProductVersion "1.1.7.0"
VIAddVersionKey "ProductName" "FileConverter"
VIAddVersionKey "FileDescription" "FileConverter offline installer"
VIAddVersionKey "FileVersion" "1.1.7"

!define MUI_ABORTWARNING
!define MUI_ICON "..\FileConverter\AppIcon.ico"
!define MUI_UNICON "..\FileConverter\AppIcon.ico"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "French"

Function .onInit
    ${IfNot} ${AtLeastWin10}
        MessageBox MB_ICONSTOP "FileConverter requires Windows 10 or later (64-bit)."
        Abort
    ${EndIf}
    ${IfNot} ${RunningX64}
        MessageBox MB_ICONSTOP "FileConverter requires 64-bit Windows."
        Abort
    ${EndIf}
    SetRegView 64
    ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
    ${If} $0 < 393295
        MessageBox MB_ICONSTOP "Install .NET Framework 4.6 or later before installing FileConverter."
        Abort
    ${EndIf}
FunctionEnd

Section "FileConverter" Main
    SetRegView 64
    SetShellVarContext all
    !ifdef ARCHIVE
        InitPluginsDir
        SetOutPath "$PLUGINSDIR"
        File /oname=python.exe "${PAYLOAD}\TranslationRuntime\python.exe"
        File /oname=python312.dll "${PAYLOAD}\TranslationRuntime\python312.dll"
        File /oname=python312.zip "${PAYLOAD}\TranslationRuntime\python312.zip"
        File /oname=vcruntime140.dll "${PAYLOAD}\TranslationRuntime\vcruntime140.dll"
        File /oname=vcruntime140_1.dll "${PAYLOAD}\TranslationRuntime\vcruntime140_1.dll"
        FileOpen $1 "$PLUGINSDIR\python312._pth" w
        FileWrite $1 "python312.zip$\r$\n.$\r$\n"
        FileClose $1
        SetCompress off
        File /oname=payload.zip "${ARCHIVE}"
        SetCompress auto
        DetailPrint "Extracting the offline application and document engines..."
        nsExec::ExecToLog '"$PLUGINSDIR\python.exe" -I -S -c "import sys,zipfile;zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])" "$PLUGINSDIR\payload.zip" "$INSTDIR"'
        Pop $0
        ${If} $0 != 0
            MessageBox MB_ICONSTOP "Could not extract FileConverter (error $0). Close FileConverter and check available disk space, then retry."
            Abort
        ${EndIf}
        SetOutPath "$INSTDIR"
    !else
        SetOutPath "$INSTDIR"
        File /r "${PAYLOAD}\*"
    !endif
    DetailPrint "Installing the Microsoft Visual C++ runtime..."
    ExecWait '"$INSTDIR\Prerequisites\VC_redist.x64.exe" /install /quiet /norestart' $0
    ${If} $0 == 3010
        SetRebootFlag true
    ${ElseIf} $0 != 0
    ${AndIf} $0 != 1638
        MessageBox MB_ICONEXCLAMATION "The Visual C++ runtime returned error $0. Run the prerequisite installer in $INSTDIR\Prerequisites before using translation."
    ${EndIf}
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    CreateDirectory "$SMPROGRAMS\FileConverter"
    CreateShortcut "$SMPROGRAMS\FileConverter\FileConverter.lnk" "$INSTDIR\FileConverter.exe"
    CreateShortcut "$SMPROGRAMS\FileConverter\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
    CreateShortcut "$DESKTOP\FileConverter.lnk" "$INSTDIR\FileConverter.exe"
    WriteRegStr HKLM "Software\FileConverter" "InstallDir" "$INSTDIR"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FileConverter" "DisplayName" "FileConverter"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FileConverter" "DisplayVersion" "1.1.7"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FileConverter" "DisplayIcon" "$INSTDIR\FileConverter.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FileConverter" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FileConverter" "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FileConverter" "NoRepair" 1
SectionEnd

Section "Uninstall"
    SetRegView 64
    SetShellVarContext all
    Delete "$DESKTOP\FileConverter.lnk"
    Delete "$SMPROGRAMS\FileConverter\FileConverter.lnk"
    Delete "$SMPROGRAMS\FileConverter\Uninstall.lnk"
    RMDir "$SMPROGRAMS\FileConverter"
    ; Remove only files deployed by this installer.
    !include "${MANIFEST}"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\FileConverter"
    DeleteRegKey HKLM "Software\FileConverter"
SectionEnd
