!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "FileAssociation.nsh"

Name "Glimpse"
OutFile "InstallGlimpse-${VERSION}.exe"
Unicode true
RequestExecutionLevel admin

InstallDir "$PROGRAMFILES64\Glimpse"
InstallDirRegKey HKLM "SOFTWARE\Glimpse" "InstallDir"

!define MUI_WELCOMEPAGE_TEXT "Install or upgrade Glimpse ${VERSION} to your system."
!define MUI_FINISHPAGE_TEXT "Glimpse has been successfully installed!"
!define MUI_FINISHPAGE_RUN "Glimpse.exe"
!define MUI_ABORTWARNING
!define MUI_ICON "GlimpseInstaller.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\..\LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

# ensure glimpse is not running before installing
Function .onInit
	System::Call 'kernel32::WaitNamedPipe(t "\\.\pipe\GlimpsePlayer")i.R0'
	IntCmp $R0 0 notRunning
		System::Call 'kernel32::CloseHandle(p $R0)'
		MessageBox MB_OK|MB_ICONEXCLAMATION "Glimpse is running. Please close it, then re-run the installer." /SD IDOK
		Abort
	notRunning:
FunctionEnd

# ensure glimpse is not running before uninstalling
Function un.onInit
	System::Call 'kernel32::WaitNamedPipe(t "\\.\pipe\GlimpsePlayer")i.R0'
	IntCmp $R0 0 notRunning
		System::Call 'kernel32::CloseHandle(p $R0)'
		MessageBox MB_OK|MB_ICONEXCLAMATION "Glimpse is running. Please close it, then re-run the uninstaller." /SD IDOK
		Abort
	notRunning:
FunctionEnd

Section "Install Glimpse"

    SectionIn RO
    SetOutPath $INSTDIR
	
	Delete "$INSTDIR\*.*"
    File /r "${PUBLISHDIR}\*.*"
	ExecWait "$INSTDIR\vc_redist.x64.exe /install /quiet /norestart"
	Delete "$INSTDIR\vc_redist.x64.exe"

    SetRegView 64
    WriteRegStr HKLM "SOFTWARE\Glimpse" "InstallDir" "$INSTDIR"

    WriteRegStr HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Glimpse" "DisplayName" "Glimpse"
    WriteRegStr HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Glimpse" "DisplayIcon" "$INSTDIR\Glimpse.exe"
    WriteRegStr HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Glimpse" "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Glimpse" "UninstallString" '"$INSTDIR\uninstall.exe"'
    WriteRegDWORD HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Glimpse" "NoModify" 1
    WriteRegDWORD HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Glimpse" "NoRepair" 1
    WriteRegStr HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Glimpse" "URLInfoAbout" "https://glimpseplayer.com"

    WriteUninstaller "$INSTDIR\uninstall.exe"

SectionEnd

Section "Start Menu Shortcut"

	# delete the old start menu folders from previous installs, if present
	Delete "$SMPROGRAMS\Glimpse\*.*"
    RMDir "$SMPROGRAMS\Glimpse"
    CreateShortcut "$SMPROGRAMS\Glimpse.lnk" "$INSTDIR\Glimpse.exe"

SectionEnd

Section "Make File Associations"

	${RegisterExtension} "$INSTDIR\Glimpse.exe" ".mp3" "GlimpsePlayer.Media.mp3"
	${RegisterExtension} "$INSTDIR\Glimpse.exe" ".flac" "GlimpsePlayer.Media.flac"
	${RegisterExtension} "$INSTDIR\Glimpse.exe" ".wav" "GlimpsePlayer.Media.wav"
	${RegisterExtension} "$INSTDIR\Glimpse.exe" ".ogg" "GlimpsePlayer.Media.ogg"

SectionEnd

Section "Uninstall"

    SetRegView 64
    DeleteRegKey HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Glimpse"
    DeleteRegKey HKLM "SOFTWARE\Glimpse"

    Delete "$INSTDIR\*.*"
	RMDir "$INSTDIR"
	
    Delete "$SMPROGRAMS\Glimpse.lnk"
	
	${UnRegisterExtension} ".mp3" "GlimpsePlayer.Media.mp3"
	${UnRegisterExtension} ".flac" "GlimpsePlayer.Media.flac"
	${UnRegisterExtension} ".wav" "GlimpsePlayer.Media.wav"
	${UnRegisterExtension} ".ogg" "GlimpsePlayer.Media.ogg"

SectionEnd
