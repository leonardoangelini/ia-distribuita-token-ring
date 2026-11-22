namespace TokenRingAtm;

/// <summary>
/// Parametri comuni letti all'avvio da ogni processo. Non e' memoria condivisa:
/// ogni nodo ne ha una propria copia. La topologia NON e' qui: ogni nodo riceve
/// da riga di comando solo la propria porta e il proprio successore.
/// </summary>
public static class Configurazione
{
    /// <summary>Tutti i nodi girano su localhost (requisito della traccia).</summary>
    public const string Host = "127.0.0.1";

    /// <summary>Saldo con cui l'initial holder inizializza il conto (dalla traccia).</summary>
    public const int SaldoIniziale = 1000;

    /// <summary>
    /// Risorsa condivisa: file con il saldo del conto. Viene letto e scritto
    /// SOLO dal nodo che possiede il token (sezione critica).
    /// </summary>
    public const string FileConto = "conto.txt";

    /// <summary>Registro delle operazioni, anch'esso scritto solo in sezione critica.</summary>
    public const string FileRegistro = "registro_operazioni.txt";

    /// <summary>
    /// Richieste di transazione dell'esempio della traccia.
    /// Ogni nodo carica SOLO quelle associate al proprio nome.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Tipo, int Importo)[]> Scenario =
        new Dictionary<string, (string, int)[]>
        {
            ["ATM1"] = [],
            ["ATM2"] = [("prelievo", 200)],
            ["ATM3"] = [("deposito", 100)],
            ["ATM4"] = [("prelievo", 500)],
        };

    /// <summary>
    /// Pausa di visualizzazione (secondi) prima di ogni inoltro del token. Non fa
    /// parte dell'algoritmo: rende leggibile il movimento del token, come le
    /// Thread.sleep dell'esempio TokenRingNode.java del corso. Con TR_RITARDO=0
    /// il token viene inoltrato immediatamente.
    /// </summary>
    public static double RitardoInoltro => LeggiSecondi("TR_RITARDO", 3.0);

    /// <summary>Durata simulata di una transazione (secondi).</summary>
    public static double DurataSezioneCritica => LeggiSecondi("TR_DURATA_CS", 1.0);

    private static double LeggiSecondi(string variabile, double predefinito) =>
        double.TryParse(Environment.GetEnvironmentVariable(variabile),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var valore)
            ? valore
            : predefinito;
}
