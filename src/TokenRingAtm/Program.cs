// Avvio di un nodo ATM (un processo per terminale).
//
//   dotnet out/TokenRingAtm.dll ATM1 --porta 5001 --successore ATM2:5002 --initial-holder
//   dotnet out/TokenRingAtm.dll ATM2 --porta 5002 --successore ATM3:5003
//   dotnet out/TokenRingAtm.dll ATM3 --porta 5003 --successore ATM4:5004
//   dotnet out/TokenRingAtm.dll ATM4 --porta 5004 --successore ATM1:5001

using System.Runtime.InteropServices;
using TokenRingAtm;

const string Uso =
    "Uso: dotnet out/TokenRingAtm.dll <NOME> --porta <PORTA> --successore <NOME:PORTA> " +
    "[--initial-holder] [--senza-scenario]";

string? nome = null, successore = null;
int porta = 0, portaSuccessore = 0;
bool initialHolder = false, senzaScenario = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--porta" when i + 1 < args.Length && int.TryParse(args[i + 1], out porta):
            i++;
            break;
        case "--successore" when i + 1 < args.Length:
            var parti = args[++i].Split(':');
            if (parti.Length != 2 || !int.TryParse(parti[1], out portaSuccessore))
                return Errore("--successore deve avere la forma NOME:PORTA, es. ATM3:5003");
            successore = parti[0].ToUpperInvariant();
            break;
        case "--initial-holder":
            initialHolder = true;
            break;
        case "--senza-scenario":
            senzaScenario = true;
            break;
        default:
            if (args[i].StartsWith("--") || nome is not null)
                return Errore($"argomento non valido: {args[i]}");
            nome = args[i].ToUpperInvariant();
            break;
    }
}

if (nome is null || porta == 0 || successore is null)
    return Errore("mancano nome, --porta o --successore");

using var nodo = new NodoAtm(nome, porta, successore, portaSuccessore, initialHolder, !senzaScenario);

// Ctrl+C (SIGINT) e SIGTERM: stampa "nodo terminato" ed esce.
void Ferma(PosixSignalContext contesto)
{
    contesto.Cancel = true;
    nodo.Termina();
    Environment.Exit(0);
}
using var sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Ferma);
using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Ferma);

nodo.Avvia();
return 0;

static int Errore(string messaggio)
{
    Console.Error.WriteLine($"Errore: {messaggio}\n{Uso}");
    return 2;
}
