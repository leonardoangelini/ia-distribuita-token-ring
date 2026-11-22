namespace TokenRingAtm.Tests;

/// <summary>Esegue una sola volta lo scenario della traccia per tutti i test della classe.</summary>
public sealed class ScenarioTraccia
{
    public AnelloDiProva Anello { get; } = new(portaBase: 6101);
}

/// <summary>
/// Scenario della traccia: saldo iniziale 1000, ATM2 preleva 200, ATM3
/// deposita 100, ATM4 preleva 500. Le verifiche usano i log dei 4 processi e
/// la risorsa condivisa conto.txt.
/// </summary>
public sealed class TokenRingTests(ScenarioTraccia scenario) : IClassFixture<ScenarioTraccia>
{
    private static readonly string[] Nodi = ["ATM1", "ATM2", "ATM3", "ATM4"];
    private readonly AnelloDiProva _anello = scenario.Anello;

    [Fact]
    public void SaldoFinaleNelContoE400()
    {
        Assert.Equal("400", _anello.Conto);
        var fine = _anello.Eventi.Where(e => e.Testo.Contains("FINE TRANSAZIONE")).ToList();
        Assert.Equal(3, fine.Count);
        Assert.EndsWith("SALDO AGGIORNATO: 400", fine[^1].Testo);
    }

    [Fact]
    public void RegistroContieneLeTreOperazioniInOrdine()
    {
        var registro = _anello.Registro;
        Assert.Equal(3, registro.Length);
        Assert.Contains("ATM2 prelievo 200 ESEGUITA saldo=800", registro[0]);
        Assert.Contains("ATM3 deposito 100 ESEGUITA saldo=900", registro[1]);
        Assert.Contains("ATM4 prelievo 500 ESEGUITA saldo=400", registro[2]);
    }

    [Fact]
    public void OrdineDelleTransazioniSegueIlToken()
    {
        var inizi = _anello.Eventi.Where(e => e.Testo.Contains("INIZIO TRANSAZIONE")).ToList();
        Assert.Equal(["ATM2", "ATM3", "ATM4"], inizi.Select(e => e.Nodo));
    }

    [Fact]
    public void MutuaEsclusione_NessunaSovrapposizioneTraSezioniCritiche()
    {
        string? dentro = null;
        foreach (var e in _anello.Eventi)
        {
            if (e.Testo.Contains("INIZIO TRANSAZIONE"))
            {
                Assert.True(dentro is null, $"{e.Nodo} entra mentre {dentro} e' in sezione critica");
                dentro = e.Nodo;
            }
            else if (e.Testo.Contains("FINE TRANSAZIONE"))
            {
                Assert.Equal(dentro, e.Nodo);
                dentro = null;
            }
        }
        Assert.Null(dentro);
    }

    [Fact]
    public void IlTokenPercorreLAnelloInOrdine()
    {
        var ricezioni = _anello.Eventi.Where(e => e.Testo.Contains("TOKEN RICEVUTO")).Select(e => e.Nodo).ToList();
        Assert.True(ricezioni.Count >= 12, $"ricezioni: {ricezioni.Count}");
        for (var i = 0; i < ricezioni.Count; i++)
            Assert.Equal(Nodi[i % 4], ricezioni[i]);
    }

    [Fact]
    public void UnSoloDetentoreDelTokenAllaVolta()
    {
        string? detentore = null;
        foreach (var e in _anello.Eventi)
        {
            if (e.Testo.Contains("TOKEN RICEVUTO"))
            {
                Assert.True(detentore is null, $"{e.Nodo} riceve mentre {detentore} ha il token");
                detentore = e.Nodo;
            }
            else if (e.Testo.Contains("TOKEN INOLTRATO"))
            {
                Assert.Equal(detentore, e.Nodo);
                detentore = null;
            }
        }
    }
}

/// <summary>
/// Come nello pseudocodice della dispensa, a ogni arrivo del token un nodo
/// serve UNA sola richiesta: se ATM2 ne ha due, la seconda attende il giro
/// successivo e nel frattempo il token passa dagli altri nodi.
/// </summary>
public sealed class UnaRichiestaPerPassaggioTests
{
    [Fact]
    public void LaSecondaRichiestaAttendeIlGiroSuccessivo()
    {
        var anello = new AnelloDiProva(portaBase: 6111, inputAtm2: "deposito 10\ndeposito 20\n",
                                       senzaScenario: true);

        var eventi = anello.Eventi;
        var inizi = eventi.Select((e, i) => (e, i))
                          .Where(x => x.e.Nodo == "ATM2" && x.e.Testo.Contains("INIZIO TRANSAZIONE"))
                          .Select(x => x.i).ToList();
        Assert.Equal(2, inizi.Count);
        Assert.Contains(eventi.Skip(inizi[0]).Take(inizi[1] - inizi[0]),
                        e => e.Nodo == "ATM3" && e.Testo.Contains("TOKEN RICEVUTO"));
        Assert.Equal("1030", anello.Conto);
    }

    [Fact]
    public void PrelievoSuperioreAlSaldoVieneRifiutato()
    {
        var anello = new AnelloDiProva(portaBase: 6121, inputAtm2: "prelievo 5000\n", senzaScenario: true, giri: 1);

        Assert.Equal("1000", anello.Conto);
        Assert.Contains(anello.Eventi, e => e.Testo.Contains("RIFIUTATA - saldo insufficiente"));
        Assert.Contains("ATM2 prelievo 5000 RIFIUTATA saldo=1000", Assert.Single(anello.Registro));
    }
}
