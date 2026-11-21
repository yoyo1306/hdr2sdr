Option Explicit
Dim fso, sh, dir, exe
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
exe = fso.BuildPath(fso.GetParentFolderName(dir), "hdr2sdr.exe")
If Not fso.FileExists(exe) Then exe = sh.ExpandEnvironmentStrings("%ProgramFiles%\hdr2sdr\hdr2sdr.exe")
If Not fso.FileExists(exe) Then exe = "hdr2sdr.exe"
sh.Run """" & exe & """ sdr up", 0, False
