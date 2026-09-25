Set objArgs = WScript.Arguments
If objArgs.Count > 0 Then
    installDir = objArgs(0)
Else
    installDir = "C:\Program Files\SoftLab_ReplayBridge"
End If

Set sh = CreateObject("WScript.Shell")
Set lnk = sh.CreateShortcut(sh.SpecialFolders("Desktop") & "\Replay Editor Bridge.lnk")
lnk.TargetPath = installDir & "\ReplayBridge.exe"
lnk.WorkingDirectory = installDir
lnk.Save