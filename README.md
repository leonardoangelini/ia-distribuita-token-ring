# Transazioni bancarie distribuite con mutua esclusione Token Ring

Progetto finale di **Intelligenza Artificiale Distribuita** (0322509INGINF05I), Università Telematica San Raffaele Roma, LM-32.
Studente: Leonardo Angelini, matricola 0322500161.

Quattro ATM indipendenti (processi separati, ognuno nel proprio terminale) operano su un unico conto bancario condiviso, salvato nel file `conto.txt`. L'accesso al conto è regolato esclusivamente dall'algoritmo **Token Ring**: solo il nodo che possiede il token può leggere e modificare il saldo.

```text
ATM1 ──▶ ATM2 ──▶ ATM3 ──▶ ATM4
  ▲                          │
  └──────────────────────────┘
```

## Video dimostrativo

📹 [`video/demo_token_ring.mp4`](video/demo_token_ring.mp4)

## Requisiti

- **.NET SDK 10** (linguaggio C#). Sviluppato e testato su macOS con .NET 10.0.100.
- Il programma usa solo la libreria standard di .NET (`System.Net.Sockets`, `System.Text.Json`, `System.Threading`, `System.IO`). Non servono pacchetti esterni.
- I test usano xUnit, che `dotnet test` scarica automaticamente da NuGet.

## Compilazione

```bash
dotnet build src/TokenRingAtm -c Release -o out
```

## Avvio

Aprire **4 terminali**, posizionarsi nella cartella del progetto e lanciare un nodo per terminale. Ogni nodo riceve solo la propria porta e il proprio successore:

```bash
# Terminale 1
dotnet out/TokenRingAtm.dll ATM1 --porta 5001 --successore ATM2:5002 --initial-holder
# Terminale 2
dotnet out/TokenRingAtm.dll ATM2 --porta 5002 --successore ATM3:5003
# Terminale 3
dotnet out/TokenRingAtm.dll ATM3 --porta 5003 --successore ATM4:5004
# Terminale 4
dotnet out/TokenRingAtm.dll ATM4 --porta 5004 --successore ATM1:5001
```

`--initial-holder` indica il nodo che crea l'unico token e inizializza `conto.txt` a 1000. L'ordine di avvio è libero. Consigliato: ATM2, ATM3, ATM4 e per ultimo ATM1. Se il successore non è ancora in ascolto, il nodo ritenta l'inoltro ogni secondo.

### Avvio automatico

Gli script compilano il progetto e aprono le 4 finestre:

| Sistema | Avvio | Arresto |
| --- | --- | --- |
| macOS | `./avvia_macos.sh` (finestre a griglia) | `Ctrl+C` in ogni finestra, oppure `./ferma_nodi.sh` |
| Linux | `./avvia_linux.sh` (gnome-terminal, konsole, xfce4-terminal o xterm) | `Ctrl+C` in ogni finestra, oppure `./ferma_nodi.sh` |
| Windows | `avvia_windows.bat` | `Ctrl+C` in ogni finestra, oppure `ferma_nodi_windows.bat` |

### Riconoscere i nodi

- All'avvio ogni terminale mostra un'intestazione con nome del nodo, porta, successore e risorsa condivisa.
- Il titolo della finestra diventa il nome del nodo.
- Ogni riga di log inizia con `[ATMx hh:mm:ss.mmm]` e ogni nodo ha un colore diverso.

### Come leggere il terminale

| Elemento | Significato |
| --- | --- |
| `● TOKEN` su sfondo giallo | il nodo ha ricevuto il token; a destra la vista locale `predecessore ──▶ [nodo] ──▶ successore` |
| riquadro rosso `■ SEZIONE CRITICA` | transazione in corso: i 5 passi (lettura, validazione, aggiornamento, scrittura, registrazione) |
| `SALDO AGGIORNATO` su sfondo verde | uscita dalla sezione critica con il nuovo saldo (`SALDO INVARIATO` se rifiutata) |
| riga grigia | nessuna richiesta: il nodo non entra in sezione critica e passa il token |
| `──▶ token inoltrato` | il token parte verso il successore |
| `+ richiesta in coda` in azzurro | una nuova richiesta attende l'arrivo del token |

Nei file `logs/ATMx.log` gli stessi eventi sono registrati come testo semplice.

### Nuove transazioni da tastiera (opzionale)

In qualsiasi terminale si può digitare `deposito 50` oppure `prelievo 30` e premere Invio. La richiesta resta in attesa finché il token non arriva a quel nodo. A ogni passaggio del token un nodo esegue una sola transazione: le altre attendono il giro successivo.

### Debug in Visual Studio Code

La cartella `.vscode/` contiene le configurazioni per l'estensione C#:

- **Anello completo (4 nodi)**: compila e avvia i 4 nodi in debug, ognuno nel proprio terminale integrato. Si possono mettere breakpoint in `NodoAtm.cs` (per esempio in `GestisciToken` o `SezioneCritica`) e seguire il token passo per passo.
- **ATM1 … ATM4**: avviano un singolo nodo in debug (prima compilare con `Cmd/Ctrl+Shift+B`). Gli altri nodi si possono avviare da terminale.
- **Task**: `build`, `test` e `pulisci dati`, che elimina `conto.txt`, il registro e i log.

## Scenario dimostrato

È l'esempio della traccia, definito in [`Configurazione.cs`](src/TokenRingAtm/Configurazione.cs):

| Nodo | Transazione | Saldo in `conto.txt` |
| --- | --- | --- |
| ATM1 (initial holder) | nessuna, inoltra il token | 1000 |
| ATM2 | prelievo 200 | 800 |
| ATM3 | deposito 100 | 900 |
| ATM4 | prelievo 500 | **400** |

Il token continua poi a circolare.

Prima di ogni inoltro c'è una **pausa di visualizzazione** di 3 secondi, e ogni transazione dura 1 secondo. Le pause non fanno parte dell'algoritmo: rendono leggibile il movimento del token, come le `Thread.sleep` dell'esempio `TokenRingNode.java` del corso. Con le variabili d'ambiente `TR_RITARDO=0` e `TR_DURATA_CS=0` le pause vengono azzerate.

## Come funziona

- **Topologia**: anello logico su localhost. Ogni nodo conosce solo il proprio successore, ricevuto con `--successore`.
- **Messaggi**: un solo tipo, `TOKEN`, inviato come riga JSON su una connessione TCP verso il successore. Il token non contiene il saldo: possederlo significa solo avere il permesso di entrare in sezione critica.
- **Risorsa condivisa**: il saldo sta nel file `conto.txt`. I nodi non condividono memoria: ogni processo ha la propria. Il file viene letto e scritto solo dal nodo che possiede il token. Anche il registro `registro_operazioni.txt` viene scritto solo in sezione critica.
- **Algoritmo**: segue lo pseudocodice della dispensa "Mutua Esclusione (Token Ring)". Alla ricezione del token (`have_token = true`), se c'è una richiesta in attesa (`need_cs`), il nodo esegue una transazione atomica:
  1. legge il saldo da `conto.txt`;
  2. valida l'operazione (un prelievo superiore al saldo viene rifiutato);
  3. calcola il nuovo saldo;
  4. scrive il nuovo saldo, su un file temporaneo poi sostituito in un solo passo;
  5. registra l'operazione nel registro.

  Poi invia il token al successore e imposta `have_token = false`.
- **Token unico**: creato una sola volta dall'initial holder (ATM1) all'avvio.

Le parti richieste dalla traccia sono commentate in [`NodoAtm.cs`](src/TokenRingAtm/NodoAtm.cs):

- gestione del token: `GestisciToken`, `CreaToken`;
- scambio di messaggi: `InviaToken`, `RiceviMessaggio`;
- logica della sezione critica: `SezioneCritica`.

## Log

Ogni nodo stampa a terminale e salva in `logs/ATMx.log`:

- ricezione e inoltro del token;
- inizio e fine della transazione;
- saldo aggiornato.

## Test automatici

```bash
dotnet test
```

I test avviano i 4 nodi come processi separati (porte dedicate da 6101, cartella temporanea) e verificano dai log e da `conto.txt`:

- saldo finale 400 e registro con le tre operazioni;
- ordine delle transazioni ATM2 → ATM3 → ATM4;
- nessuna sovrapposizione tra sezioni critiche;
- percorso del token lungo l'anello;
- un solo detentore del token alla volta;
- una sola transazione per nodo a ogni passaggio del token;
- rifiuto di un prelievo superiore al saldo.

## Struttura

```text
src/TokenRingAtm/          nodo ATM (un processo per nodo)
  Program.cs               argomenti da riga di comando, Ctrl+C
  NodoAtm.cs               algoritmo Token Ring e sezione critica
  Terminale.cs             colori e simboli con cui gli eventi appaiono a terminale
  Configurazione.cs        saldo iniziale, scenario, pause di visualizzazione
tests/TokenRingAtm.Tests/  test di integrazione (xUnit)
.vscode/                  configurazioni di debug e task per VS Code
avvia_*.sh / .bat          avvio dei 4 terminali (macOS, Linux, Windows)
ferma_nodi*.sh / .bat      arresto dei nodi
registra_demo.sh           registrazione del video dimostrativo (macOS)
consegna/                  PDF di consegna
video/                     video dimostrativo
```

## Limiti

Il progetto non gestisce i guasti, che la traccia non richiede. Se un nodo viene terminato, l'anello si interrompe e il predecessore resta in attesa di poter inoltrare il token. Se si riavvia ATM1 mentre gli altri nodi sono attivi, viene creato un secondo token. Per una nuova esecuzione conviene quindi riavviare tutti e 4 i nodi.
