#!/bin/bash
# Compila il progetto e apre 4 finestre di terminale (Linux), una per nodo.
# Usa il primo emulatore di terminale disponibile tra:
#   gnome-terminal, konsole, xfce4-terminal, xterm
# Ogni nodo riceve solo la propria porta e il proprio successore.
# ATM1 (initial holder) viene avviato per ultimo.
#
# Uso:  ./avvia_linux.sh
# Per fermare: Ctrl+C in ciascuna finestra (oppure ./ferma_nodi.sh).

CARTELLA="$(cd "$(dirname "$0")" && pwd)"
cd "$CARTELLA" || exit 1

dotnet build src/TokenRingAtm -c Release -o out --nologo -v q || exit 1

COMANDI=(
  "ATM4|dotnet out/TokenRingAtm.dll ATM4 --porta 5004 --successore ATM1:5001"
  "ATM3|dotnet out/TokenRingAtm.dll ATM3 --porta 5003 --successore ATM4:5004"
  "ATM2|dotnet out/TokenRingAtm.dll ATM2 --porta 5002 --successore ATM3:5003"
  "ATM1|dotnet out/TokenRingAtm.dll ATM1 --porta 5001 --successore ATM2:5002 --initial-holder"
)

apri() {  # $1 = titolo, $2 = comando
  # "exec bash" lascia la finestra aperta dopo la terminazione del nodo
  local cmd="cd '$CARTELLA' && $2; exec bash"
  if command -v gnome-terminal >/dev/null; then
    gnome-terminal --title="$1" -- bash -c "$cmd"
  elif command -v konsole >/dev/null; then
    konsole -p tabtitle="$1" -e bash -c "$cmd" &
  elif command -v xfce4-terminal >/dev/null; then
    xfce4-terminal --title="$1" -e "bash -c \"$cmd\"" &
  elif command -v xterm >/dev/null; then
    xterm -T "$1" -e bash -c "$cmd" &
  else
    echo "Nessun emulatore di terminale trovato: avviare i nodi a mano (vedi README)."
    exit 1
  fi
}

for voce in "${COMANDI[@]}"; do
  apri "${voce%%|*}" "${voce#*|}"
  sleep 0.7
done
