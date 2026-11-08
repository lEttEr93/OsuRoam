' Double-click launcher for the dev build (no console window):
' build if something changed, then start Roam. On failure the build log opens
' in Notepad so the reason is visible.
Option Explicit

Dim shell, fso, root, exitCode, logPath
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

root = fso.GetParentFolderName(WScript.ScriptFullName)
logPath = root & "\bin\run-build.log"

exitCode = shell.Run("cmd /c """ & root & "\run.cmd""", 0, True)

If exitCode <> 0 Then
    If fso.FileExists(logPath) Then
        shell.Run "notepad.exe """ & logPath & """", 1, False
    Else
        MsgBox "Run failed (exit " & exitCode & "). See " & logPath, 16, "osu! Roam (dev)"
    End If
End If
