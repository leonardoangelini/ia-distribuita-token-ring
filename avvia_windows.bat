@echo off
REM Compila il progetto e apre 4 finestre del Prompt dei comandi (Windows),
REM una per nodo ATM. Ogni nodo riceve solo la propria porta e il proprio
REM successore. ATM1 (initial holder) viene avviato per ultimo.
REM
REM Uso:  doppio clic su avvia_windows.bat  oppure  avvia_windows.bat
REM Per fermare: Ctrl+C in ciascuna finestra (oppure ferma_nodi_windows.bat).

cd /d "%~dp0"
dotnet build src\TokenRingAtm -c Release -o out --nologo -v q || exit /b 1

start "ATM4" cmd /k dotnet out\TokenRingAtm.dll ATM4 --porta 5004 --successore ATM1:5001
timeout /t 1 /nobreak >nul
start "ATM3" cmd /k dotnet out\TokenRingAtm.dll ATM3 --porta 5003 --successore ATM4:5004
timeout /t 1 /nobreak >nul
start "ATM2" cmd /k dotnet out\TokenRingAtm.dll ATM2 --porta 5002 --successore ATM3:5003
timeout /t 1 /nobreak >nul
start "ATM1" cmd /k dotnet out\TokenRingAtm.dll ATM1 --porta 5001 --successore ATM2:5002 --initial-holder
