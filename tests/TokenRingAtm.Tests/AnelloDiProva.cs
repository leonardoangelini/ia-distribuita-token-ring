using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using TokenRingAtm;

namespace TokenRingAtm.Tests;

/// <summary>Una riga di log: nodo, orario, testo.</summary>
public sealed record Evento(DateTime Ora, string Nodo, string Testo);

/// <summary>
/// Avvia i 4 nodi come processi separati (porte dedicate, cartella temporanea
/// come directory di lavoro), lascia girare il token per alcuni giri, li
/// termina e raccoglie i log e la risorsa condivisa.
/// </summary>
public sealed class AnelloDiProva
{
    private static readonly string[] Nodi = ["ATM1", "ATM2", "ATM3", "ATM4"];
    private static readonly Regex Riga = new(@"^\[(ATM\d) (\d\d:\d\d:\d\d\.\d{6})\] (.*)$");

    public string Cartella { get; } = Directory.CreateTempSubdirectory("tokenring_").FullName;
    public List<Evento> Eventi { get; private set; } = [];

    public AnelloDiProva(int portaBase, string? inputAtm2 = null, bool senzaScenario = false, int giri = 3)
    {
        // Il progetto di test referenzia TokenRingAtm: la sua dll e' nella stessa cartella.
        var dll = typeof(NodoAtm).Assembly.Location;
        var processi = new List<Process>();

        // Avvio in ordine inverso: ATM1 (initial holder) parte per ultimo.
        for (var i = 3; i >= 0; i--)
        {
            var argomenti = $"\"{dll}\" {Nodi[i]} --porta {portaBase + i} " +
                            $"--successore {Nodi[(i + 1) % 4]}:{portaBase + (i + 1) % 4}";
            if (i == 0) argomenti += " --initial-holder";
            if (senzaScenario) argomenti += " --senza-scenario";

            var info = new ProcessStartInfo("dotnet", argomenti)
            {
                WorkingDirectory = Cartella,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            info.Environment["TR_RITARDO"] = "0.2";
            info.Environment["TR_DURATA_CS"] = "0.3";

            var p = Process.Start(info)!;
            p.OutputDataReceived += (_, _) => { };
            p.BeginOutputReadLine();
            if (Nodi[i] == "ATM2" && inputAtm2 is not null)
                p.StandardInput.Write(inputAtm2);
            p.StandardInput.Close();
            processi.Add(p);
        }

        var limite = DateTime.Now.AddSeconds(40);
        while (DateTime.Now < limite &&
               !LeggiLog().Any(e => e.Nodo == "ATM1" && e.Testo.Contains($"giro {giri + 1}")))
            Thread.Sleep(300);

        foreach (var p in processi)
        {
            p.Kill();
            p.WaitForExit();
        }
        Eventi = LeggiLog();
    }

    /// <summary>Tutte le righe dei 4 file di log, ordinate per orario.</summary>
    private List<Evento> LeggiLog()
    {
        var eventi = new List<Evento>();
        foreach (var nodo in Nodi)
        {
            var percorso = Path.Combine(Cartella, "logs", $"{nodo}.log");
            if (!File.Exists(percorso)) continue;
            using var flusso = new FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var lettore = new StreamReader(flusso);
            while (lettore.ReadLine() is { } riga)
            {
                var m = Riga.Match(riga);
                if (m.Success)
                    eventi.Add(new Evento(
                        DateTime.ParseExact(m.Groups[2].Value, "HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
                        m.Groups[1].Value, m.Groups[3].Value));
            }
        }
        return eventi.OrderBy(e => e.Ora).ToList();
    }

    public string Conto => File.ReadAllText(Path.Combine(Cartella, Configurazione.FileConto)).Trim();

    public string[] Registro =>
        File.ReadAllLines(Path.Combine(Cartella, Configurazione.FileRegistro))
            .Where(r => !r.StartsWith('#')).ToArray();
}
