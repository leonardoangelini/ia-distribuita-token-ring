#!/bin/bash
# Registra il video dimostrativo (macOS), senza tagli:
#   1. avvia la registrazione dello schermo (screencapture)
#   2. apre i 4 terminali con i nodi ATM (avvia_macos.sh)
#   3. il primo giro esegue le 3 transazioni della traccia (saldo 1000 -> 400)
#   4. digita due transazioni aggiuntive nei terminali di ATM3 e ATM1
#   5. ferma i nodi e mostra la risorsa condivisa (conto.txt e registro)
#   6. ferma la registrazione
#   7. converte il video in video/demo_token_ring.mp4 (ffmpeg)
#
# Uso:  ./registra_demo.sh
# Durante la registrazione (~70 s) non usare il Mac.

set -e
CARTELLA="$(cd "$(dirname "$0")" && pwd)"
cd "$CARTELLA"

DURATA=70
GREZZO="$(mktemp -d)/registrazione.mov"
USCITA="video/demo_token_ring.mp4"
mkdir -p video

# Restituisce l'id della finestra di Terminal il cui titolo e' il nodo.
# Il titolo e' "... — ATMx — dotnet ...": si cerca "— ATMx —" perche' il nome
# del successore compare anche negli argomenti del comando. L'id va letto
# mentre il nodo e' attivo: dopo l'arresto la shell cambia il titolo.
id_finestra() {
    osascript -e "tell application \"Terminal\" to get id of first window whose name contains \"— $1 —\""
}

# Scrive un comando (seguito da Invio) nella finestra con l'id indicato.
scrivi_in() {
    osascript -e "tell application \"Terminal\" to do script \"$2\" in selected tab of window id $1"
}

echo "Compilo il progetto..."
dotnet build src/TokenRingAtm -c Release -o out --nologo -v q

echo "Fermo eventuali nodi gia' attivi..."
./ferma_nodi.sh >/dev/null || true
sleep 1

echo "La registrazione parte tra 5 secondi: non usare il Mac per circa $DURATA secondi."
sleep 5

screencapture -v -x -V "$DURATA" "$GREZZO" &
PID_REC=$!
sleep 2

# t ~ 2 s: apertura dei 4 terminali (ATM1 per ultimo, crea il token)
SALTA_BUILD=1 ./avvia_macos.sh
# Primo giro: ATM2 prelievo 200, ATM3 deposito 100, ATM4 prelievo 500 -> saldo 400

# t ~ 24 s: il token e' al secondo giro. Richieste aggiuntive digitate a mano:
sleep 3
FIN_ATM1=$(id_finestra ATM1 || true)
FIN_ATM3=$(id_finestra ATM3 || true)
sleep 14
# verra' RIFIUTATA (saldo 400 insufficiente)
scrivi_in "$FIN_ATM3" "prelievo 1000" || true
sleep 1
# verra' eseguita al giro successivo -> saldo 650
scrivi_in "$FIN_ATM1" "deposito 250" || true

# Lascia completare almeno un altro giro completo
sleep 30

echo "Fermo i nodi..."
./ferma_nodi.sh >/dev/null || true

# Mostra nel terminale di ATM1 il contenuto finale della risorsa condivisa
sleep 2
scrivi_in "$FIN_ATM1" "clear; echo '--- conto.txt'; cat conto.txt; echo '--- registro_operazioni.txt'; cat registro_operazioni.txt" || true

echo "Attendo la fine della registrazione..."
wait "$PID_REC"

echo "Converto il video..."
ffmpeg -y -loglevel error -i "$GREZZO" -vf "scale=-2:1080" -c:v libx264 -crf 26 \
       -preset medium -pix_fmt yuv420p -an "$USCITA"

echo "Video salvato in $USCITA ($(du -h "$USCITA" | cut -f1))"

# Avviso di fine (dopo la registrazione, quindi non compare nel video)
afplay /System/Library/Sounds/Glass.aiff 2>/dev/null &
osascript -e 'display notification "Video salvato in video/demo_token_ring.mp4" with title "Registrazione completata"' 2>/dev/null || true
