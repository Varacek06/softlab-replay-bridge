Set sh = CreateObject("WScript.Shell")
Set lnk = sh.CreateShortcut(sh.SpecialFolders("Desktop") & "\Replay Editor Bridge.lnk")
lnk.TargetPath = "C:\SoftLab_ReplayBridge\ReplayBridge.exe"
lnk.WorkingDirectory = "C:\SoftLab_ReplayBridge"
lnk.Save