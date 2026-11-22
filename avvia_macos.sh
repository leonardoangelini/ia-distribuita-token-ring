#!/bin/bash
# Compila il progetto e apre 4 finestre di Terminal.app (macOS), una per nodo,
# disposte a griglia 2x2. Ogni nodo riceve solo la propria porta e il proprio
# successore. ATM1 (initial holder) viene avviato per ultimo.
#
# Uso:  ./avvia_macos.sh
# Per fermare: Ctrl+C in ciascuna finestra (oppure ./ferma_nodi.sh).

CARTELLA="$(cd "$(dirname "$0")" && pwd)"
cd "$CARTELLA" || exit 1

if [ -z "$SALTA_BUILD" ]; then
  dotnet build src/TokenRingAtm -c Release -o out --nologo -v q || exit 1
fi

CMD1="dotnet out/TokenRingAtm.dll ATM1 --porta 5001 --successore ATM2:5002 --initial-holder"
CMD2="dotnet out/TokenRingAtm.dll ATM2 --porta 5002 --successore ATM3:5003"
CMD3="dotnet out/TokenRingAtm.dll ATM3 --porta 5003 --successore ATM4:5004"
CMD4="dotnet out/TokenRingAtm.dll ATM4 --porta 5004 --successore ATM1:5001"

osascript <<EOF
tell application "Finder" to set schermo to bounds of window of desktop
set larghezza to item 3 of schermo
set altezza to item 4 of schermo
set meta_x to larghezza div 2
set meta_y to (altezza - 25) div 2 + 25

-- posizioni: ATM1 alto-sx, ATM2 alto-dx, ATM3 basso-dx, ATM4 basso-sx
-- (l'ordine segue l'anello in senso orario)
set riquadri to {{0, 25, meta_x, meta_y}, {meta_x, 25, larghezza, meta_y}, ¬
    {meta_x, meta_y, larghezza, altezza}, {0, meta_y, meta_x, altezza}}
set comandi to {"$CMD1", "$CMD2", "$CMD3", "$CMD4"}

tell application "Terminal"
    activate
    repeat with n in {4, 3, 2, 1}
        set comando to "cd " & quoted form of "$CARTELLA" & " && clear && " & item n of comandi
        do script comando
        set bounds of front window to item n of riquadri
        delay 0.7
    end repeat
end tell
EOF
