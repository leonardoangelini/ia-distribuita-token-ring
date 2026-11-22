using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TokenRingAtm;

/// <summary>
/// Messaggio TOKEN scambiato tra i nodi. Contiene solo dati di servizio
/// (giro, numero di passaggi, mittente): il saldo NON viaggia nel token ma sta
/// nella risorsa condivisa conto.txt. Possedere il token significa solo avere
/// il permesso di entrare in sezione critica.
/// </summary>
public sealed record Token(string Tipo, int Giro, int Hop, string? Da);

/// <summary>
/// Nodo ATM del sistema bancario distribuito con mutua esclusione Token Ring.
///
/// Ogni nodo e' un processo separato, con la propria memoria privata, eseguito
/// in un terminale distinto. I nodi comunicano solo tramite messaggi TCP su
/// localhost e ognuno conosce SOLTANTO il proprio successore.
///
/// Corrispondenza con lo pseudocodice della dispensa
/// "Mutua Esclusione (Token Ring)", Figura 1:
///   need_cs                         -> NeedCs (c'e' una richiesta in attesa)
///   have_token                      -> _haveToken
///   initial_holder                  -> _initialHolder
///   upon need_cs_becomes_true       -> AggiungiRichiesta()
///   upon receive(TOKEN)             -> GestisciToken()
///   enter/exit_critical_section()   -> SezioneCritica()
///   send TOKEN to successor(i)      -> InviaToken()
/// </summary>
public sealed class NodoAtm : IDisposable
{
    private static readonly Dictionary<string, ConsoleColor> Colori = new()
    {
        ["ATM1"] = ConsoleColor.Cyan,
        ["ATM2"] = ConsoleColor.Green,
        ["ATM3"] = ConsoleColor.Yellow,
        ["ATM4"] = ConsoleColor.Magenta,
    };

    private readonly string _nome;
    private readonly int _porta;
    private readonly string _successore;      // unica informazione sulla topologia
    private readonly int _portaSuccessore;
    private readonly bool _initialHolder;

    // Richieste di transazione in attesa del token (FIFO locale al nodo).
    // La richiesta e' implicita: non si inviano messaggi, si aspetta il token.
    private readonly ConcurrentQueue<(string Tipo, int Importo)> _richieste = new();

    // have_token: vero solo mentre il nodo possiede fisicamente il token.
    private volatile bool _haveToken;

    private readonly Lock _lockLog = new();

    // Diventa false su Ctrl+C / SIGTERM: termina il ciclo di ascolto.
    private volatile bool _attivo = true;
    private readonly StreamWriter _fileLog;
    private readonly Terminale _terminale;

    public NodoAtm(string nome, int porta, string successore, int portaSuccessore,
                   bool initialHolder, bool usaScenario)
    {
        _nome = nome;
        _porta = porta;
        _successore = successore;
        _portaSuccessore = portaSuccessore;
        _initialHolder = initialHolder;
        _terminale = new Terminale(nome, Colori.GetValueOrDefault(nome, ConsoleColor.White));

        if (usaScenario && Configurazione.Scenario.TryGetValue(nome, out var proprie))
            foreach (var richiesta in proprie)
                _richieste.Enqueue(richiesta);

        // File di log del nodo: testo semplice, orario in microsecondi.
        Directory.CreateDirectory("logs");
        _fileLog = new StreamWriter(Path.Combine("logs", $"{nome}.log"), append: true) { AutoFlush = true };
    }

    /// <summary>need_cs: c'e' almeno una richiesta di transazione in attesa.</summary>
    private bool NeedCs => !_richieste.IsEmpty;

    // ------------------------------------------------------------------ log

    /// <summary>
    /// Registra un evento: nel file di log come testo semplice (<paramref name="testo"/>),
    /// a terminale con colori e simboli (<paramref name="aTerminale"/>, se diverso).
    /// </summary>
    private void Log(Stile stile, string testo, string? aTerminale = null)
    {
        var adesso = DateTime.Now;
        lock (_lockLog)
        {
            _terminale.Scrivi(adesso, stile, aTerminale ?? testo);
            _fileLog.WriteLine($"[{_nome} {adesso:HH:mm:ss.ffffff}] {testo}");
        }
    }

    // ------------------------------------------------- scambio di messaggi

    /// <summary>
    /// send TOKEN to successor(i): apre una connessione TCP verso il successore e
    /// invia il token come una riga JSON. Se il successore non e' ancora in
    /// ascolto (es. non e' stato avviato) si riprova ogni secondo: il token non
    /// va perso ne' duplicato, resta a questo nodo finche' l'invio non riesce.
    /// </summary>
    private void InviaToken(Token token)
    {
        var riga = JsonSerializer.Serialize(token) + "\n";
        var avvisato = false;
        while (true)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect(Configurazione.Host, _portaSuccessore);
                var dati = Encoding.UTF8.GetBytes(riga);
                client.GetStream().Write(dati, 0, dati.Length);
                return;
            }
            catch (SocketException)
            {
                if (!avvisato)
                {
                    Log(Stile.Avviso, $"successore {_successore} non raggiungibile, riprovo ogni secondo...");
                    avvisato = true;
                }
                Thread.Sleep(1000);
            }
        }
    }

    /// <summary>Legge un messaggio (una riga JSON) da una connessione accettata.</summary>
    private static Token? RiceviMessaggio(TcpClient connessione)
    {
        using var lettore = new StreamReader(connessione.GetStream(), Encoding.UTF8);
        var riga = lettore.ReadLine();
        return string.IsNullOrWhiteSpace(riga) ? null : JsonSerializer.Deserialize<Token>(riga);
    }

    // ---------------------------------------------- gestione del token

    /// <summary>
    /// upon receive(TOKEN), come nello pseudocodice della dispensa:
    ///   have_token := true
    ///   if need_cs then enter_critical_section(); need_cs := false; exit_critical_section()
    ///   send TOKEN to successor(i)
    ///   have_token := false
    /// Viene servita UNA richiesta per passaggio del token: le altre richieste
    /// dello stesso nodo attendono il giro successivo.
    /// </summary>
    private void GestisciToken(Token token)
    {
        _haveToken = true;

        // Solo l'initial holder conta i giri completi dell'anello.
        if (_initialHolder && token.Hop > 0)
            token = token with { Giro = token.Giro + 1 };

        // Vista locale dell'anello: il nodo conosce solo chi gli ha passato il
        // token (dal messaggio) e il proprio successore.
        var da = token.Da ?? "creazione";
        Log(Stile.TokenRicevuto,
            $"TOKEN RICEVUTO da {da} (giro {token.Giro})",
            $"ricevuto da {da}  ·  giro {token.Giro}      {token.Da ?? "·"} ──▶ [{_nome}] ──▶ {_successore}");

        if (NeedCs && _richieste.TryDequeue(out var richiesta))   // need_cs := false
        {
            SezioneCritica(richiesta.Tipo, richiesta.Importo);
            if (NeedCs)
                Log(Stile.Richiesta, $"altre {_richieste.Count} richieste in attesa del prossimo giro");
        }
        else
        {
            Log(Stile.Inattivo, "nessuna transazione in attesa",
                "nessuna richiesta in attesa: non entro in sezione critica");
        }

        // Pausa di visualizzazione (non fa parte dell'algoritmo, vedi Configurazione).
        Thread.Sleep(TimeSpan.FromSeconds(Configurazione.RitardoInoltro));

        var prossimo = token with { Da = _nome, Hop = token.Hop + 1 };
        Log(Stile.Inoltro, $"TOKEN INOLTRATO a {_successore}",
            $"token inoltrato a {_successore}   (ora {_nome} attende il prossimo giro)");
        InviaToken(prossimo);
        _haveToken = false;   // have_token := false solo dopo l'invio
    }

    /// <summary>
    /// enter_critical_section() ... exit_critical_section().
    /// Transazione atomica sulla risorsa condivisa conto.txt, eseguita SOLO
    /// mentre il nodo possiede il token: e' questa la garanzia di mutua
    /// esclusione. Il token resta trattenuto localmente fino alla fine.
    /// </summary>
    private void SezioneCritica(string tipo, int importo)
    {
        if (!_haveToken)
            throw new InvalidOperationException("violazione: sezione critica senza token");

        Log(Stile.InizioCs, $">>> INIZIO TRANSAZIONE: {tipo} {importo} (ingresso in sezione critica)",
            $"{tipo} {importo}   (solo {_nome} puo' accedere a {Configurazione.FileConto})");

        // 1. Lettura del saldo corrente dalla risorsa condivisa.
        var saldo = LeggiSaldo();
        Log(Stile.PassoCs, $"    1. lettura saldo corrente da {Configurazione.FileConto}: {saldo}",
            $"1. lettura       {Configurazione.FileConto} → {saldo}");

        // 2. Validazione dell'operazione.
        string? motivo = null;
        if (importo <= 0)
            motivo = "importo non positivo";
        else if (tipo == "prelievo" && importo > saldo)
            motivo = $"saldo insufficiente ({importo} > {saldo})";
        var valida = motivo is null;
        if (valida)
            Log(Stile.PassoCs, "    2. validazione: OK", "2. validazione   OK");
        else
            Log(Stile.Rifiuto, $"    2. validazione: RIFIUTATA - {motivo}", $"2. validazione   RIFIUTATA: {motivo}");

        var saldoFinale = saldo;
        if (valida)
        {
            // 3. Aggiornamento del saldo.
            saldoFinale = tipo == "deposito" ? saldo + importo : saldo - importo;
            var segno = tipo == "deposito" ? "+" : "-";
            Log(Stile.PassoCs, $"    3. aggiornamento: {saldo} {segno} {importo} = {saldoFinale}",
                $"3. aggiornamento {saldo} {segno} {importo} = {saldoFinale}");

            // Durata simulata dell'operazione: il token resta qui.
            Thread.Sleep(TimeSpan.FromSeconds(Configurazione.DurataSezioneCritica));

            // 4. Scrittura del nuovo valore nella risorsa condivisa.
            ScriviSaldo(saldoFinale);
            Log(Stile.PassoCs, $"    4. scrittura nuovo saldo in {Configurazione.FileConto}: {saldoFinale}",
                $"4. scrittura     {Configurazione.FileConto} ← {saldoFinale}");
        }

        // 5. Registrazione (log) dell'operazione nel registro condiviso.
        var esito = valida ? "ESEGUITA" : "RIFIUTATA";
        File.AppendAllText(Configurazione.FileRegistro,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {_nome} {tipo} {importo} {esito} saldo={saldoFinale}\n");
        Log(Stile.PassoCs, $"    5. registrazione in {Configurazione.FileRegistro}: {tipo} {importo} {esito}",
            $"5. registrazione {Configurazione.FileRegistro} ({esito})");

        Log(Stile.FineCs,
            $"<<< FINE TRANSAZIONE (uscita dalla sezione critica) - SALDO AGGIORNATO: {saldoFinale}",
            valida ? $"SALDO AGGIORNATO: {saldoFinale}" : $"SALDO INVARIATO: {saldoFinale}");
    }

    private static int LeggiSaldo() => int.Parse(File.ReadAllText(Configurazione.FileConto).Trim());

    /// <summary>
    /// Scrive il saldo su un file temporaneo e lo sostituisce a conto.txt in un
    /// solo passo: il file non resta mai scritto a meta'.
    /// </summary>
    private static void ScriviSaldo(int saldo)
    {
        var temporaneo = Configurazione.FileConto + ".tmp";
        File.WriteAllText(temporaneo, saldo + "\n");
        File.Move(temporaneo, Configurazione.FileConto, overwrite: true);
    }

    // ------------------------------------------------ input da terminale

    /// <summary>upon need_cs_becomes_true: registra la richiesta, nessun messaggio.</summary>
    private void AggiungiRichiesta(string tipo, int importo)
    {
        _richieste.Enqueue((tipo, importo));
        Log(Stile.Richiesta, $"richiesta registrata: {tipo} {importo} (in attesa del token)",
            $"richiesta in coda: {tipo} {importo}   (need_cs = true, attendo il token)");
    }

    /// <summary>Legge nuove richieste da tastiera, es. "deposito 50".</summary>
    private void LeggiComandi()
    {
        string? riga;
        while ((riga = Console.In.ReadLine()) is not null)
        {
            var parti = riga.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parti.Length == 0) continue;
            if (parti.Length == 2 && parti[0] is "deposito" or "prelievo" && int.TryParse(parti[1], out var importo))
                AggiungiRichiesta(parti[0], importo);
            else
                Log(Stile.Avviso, "comando non valido. Usa: deposito <importo> | prelievo <importo>");
        }
    }

    // ------------------------------------------------------------ avvio

    /// <summary>Solo l'initial holder: crea l'unico token e inizializza il conto.</summary>
    private void CreaToken()
    {
        _haveToken = true;   // have_token := (i == initial_holder)
        Log(Stile.Info,
            $"creo l'unico TOKEN e inizializzo {Configurazione.FileConto} (saldo {Configurazione.SaldoIniziale})");
        ScriviSaldo(Configurazione.SaldoIniziale);
        File.WriteAllText(Configurazione.FileRegistro,
            $"# Registro operazioni - saldo iniziale {Configurazione.SaldoIniziale}\n");
        GestisciToken(new Token("TOKEN", Giro: 1, Hop: 0, Da: null));
    }

    public void Avvia()
    {
        Console.OutputEncoding = Encoding.UTF8;   // simboli e riquadri anche su Windows
        _terminale.Intestazione(_porta, _successore, _portaSuccessore, _initialHolder);
        try
        {
            Console.Title = _nome;
        }
        catch (PlatformNotSupportedException)
        {
            // Terminale che non supporta il titolo: il nodo resta riconoscibile
            // da intestazione, colore e prefisso [ATMx] di ogni riga.
        }

        var server = new TcpListener(System.Net.IPAddress.Parse(Configurazione.Host), _porta);
        server.Start();
        Log(Stile.Info, $"in ascolto su {Configurazione.Host}:{_porta}");

        if (_richieste.IsEmpty)
            Log(Stile.Info, "nessuna transazione richiesta");
        else
            Log(Stile.Richiesta,
                "transazioni richieste: " + string.Join(", ", _richieste.Select(r => $"{r.Tipo} {r.Importo}")),
                "richiesta in coda: " + string.Join(", ", _richieste.Select(r => $"{r.Tipo} {r.Importo}")) +
                "   (need_cs = true, attendo il token)");

        new Thread(LeggiComandi) { IsBackground = true }.Start();

        if (_initialHolder)
            new Thread(CreaToken) { IsBackground = true }.Start();

        // Ciclo principale: i messaggi vengono gestiti uno alla volta.
        while (_attivo)
        {
            using var connessione = server.AcceptTcpClient();
            var messaggio = RiceviMessaggio(connessione);
            if (messaggio?.Tipo == "TOKEN")
                GestisciToken(messaggio);
        }
    }

    /// <summary>Chiamato su Ctrl+C / SIGTERM.</summary>
    public void Termina()
    {
        _attivo = false;
        Log(Stile.Info, "nodo terminato");
    }

    public void Dispose() => _fileLog.Dispose();
}
