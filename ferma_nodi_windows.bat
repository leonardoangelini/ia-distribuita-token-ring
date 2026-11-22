@echo off
REM Termina i 4 nodi ATM chiudendo le finestre aperte da avvia_windows.bat.
for %%N in (ATM1 ATM2 ATM3 ATM4) do taskkill /FI "WINDOWTITLE eq %%N*" /T /F >nul 2>&1
echo Nodi terminati.
