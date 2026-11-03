@echo off
(echo INSTALL_BAT_RAN & ver) > \\host.lan\Data\result.txt 2>&1
copy /y \\host.lan\Data\run.ps1 C:\run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File C:\run.ps1 > \\host.lan\Data\ps-log.txt 2>&1
echo DONE >> \\host.lan\Data\result.txt
