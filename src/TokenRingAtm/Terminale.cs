namespace TokenRingAtm;

/// <summary>Tipo di evento: decide colori e simboli con cui viene mostrato a terminale.</summary>
public enum Stile
{
    Info,           // messaggi di servizio (avvio, ascolto, ...)
    Richiesta,      // nuova richiesta in coda: need_cs diventa vero
    TokenRicevuto,  // receive(TOKEN): il nodo ora possiede il token
    Inattivo,       // nessuna richiesta: il token viene passato subito
    Inoltro,        // send TOKEN to successor(i)
    InizioCs,       // enter_critical_section()
    PassoCs,        // uno dei 5 passi della transazione
    Rifiuto,        // validazione fallita
    FineCs,         // exit_critical_section() con saldo aggiornato
    Avviso,         // problemi (es. successore non raggiungibile)
}

/// <summary>
/// Presentazione degli eventi nel terminale: colori, badge e un riquadro
/// attorno alla sezione critica, per rendere visibile il meccanismo del
/// Token Ring. Non contiene logica dell'algoritmo.
/// </summary>
public sealed class Terminale(string nome, ConsoleColor coloreNodo)
{
    private readonly Lock _lock = new();

    /// <summary>Intestazione mostrata all'avvio del nodo.</summary>
    public void Intestazione(int porta, string successore, int portaSuccessore, bool initialHolder)
    {
        lock (_lock)
        {
            const string Linea = "══════════════════════════════════════════════";
            Console.ForegroundColor = coloreNodo;
            Console.WriteLine(Linea);
            Console.WriteLine($"  NODO {nome}   ·   porta {porta}");
            Console.WriteLine($"  successore: {successore} (porta {portaSuccessore})");
            Console.WriteLine($"  risorsa condivisa: {Configurazione.FileConto}");
            if (initialHolder)
                Console.WriteLine("  INITIAL HOLDER: crea l'unico token");
            Console.WriteLine(Linea);
            Console.ResetColor();
        }
    }

    /// <summary>Scrive una riga: prefisso [nodo orario] nel colore del nodo, poi l'evento.</summary>
    public void Scrivi(DateTime ora, Stile stile, string testo)
    {
        lock (_lock)
        {
            Console.ForegroundColor = coloreNodo;
            Console.Write($"[{nome} {ora:HH:mm:ss.fff}] ");
            Console.ResetColor();

            switch (stile)
            {
                case Stile.TokenRicevuto:
                    Badge(" ● TOKEN ", ConsoleColor.Black, ConsoleColor.Yellow);
                    Testo(" " + testo, ConsoleColor.Yellow);
                    break;
                case Stile.Inattivo:
                    Testo("  " + testo, ConsoleColor.DarkGray);
                    break;
                case Stile.Inoltro:
                    Testo("  ──▶ " + testo, ConsoleColor.DarkYellow);
                    break;
                case Stile.InizioCs:
                    Testo("  ┌─", ConsoleColor.Red);
                    Badge(" ■ SEZIONE CRITICA ", ConsoleColor.White, ConsoleColor.DarkRed);
                    Testo(" " + testo, ConsoleColor.Red);
                    break;
                case Stile.PassoCs:
                    Testo("  │ ", ConsoleColor.Red);
                    Testo(testo, ConsoleColor.White);
                    break;
                case Stile.Rifiuto:
                    Testo("  │ ", ConsoleColor.Red);
                    Testo(testo, ConsoleColor.Red);
                    break;
                case Stile.FineCs:
                    Testo("  └─", ConsoleColor.Red);
                    Badge($" {testo} ", ConsoleColor.Black, ConsoleColor.Green);
                    Testo("  uscita dalla sezione critica", ConsoleColor.Green);
                    break;
                case Stile.Richiesta:
                    Testo("  + " + testo, ConsoleColor.Cyan);
                    break;
                case Stile.Avviso:
                    Testo("  ! " + testo, ConsoleColor.Red);
                    break;
                default:
                    Testo(testo, ConsoleColor.Gray);
                    break;
            }
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    private static void Badge(string testo, ConsoleColor primoPiano, ConsoleColor sfondo)
    {
        Console.ForegroundColor = primoPiano;
        Console.BackgroundColor = sfondo;
        Console.Write(testo);
        Console.ResetColor();
    }

    private static void Testo(string testo, ConsoleColor colore)
    {
        Console.ForegroundColor = colore;
        Console.Write(testo);
    }
}
