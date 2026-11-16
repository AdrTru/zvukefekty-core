# ZvukEfekty.Core

Knihovna, která ve tvém programu **spočítá zvuk** z hotových zvukových sestav (soubory `.zvuky`).
Sestavy vyrábí autor zvuků ve svém editoru. Ty dostaneš soubory a tuhle knihovnu, editor
nepotřebuješ. Knihovna vrací vzorky: `float[]`, **mono**, rozsah ±1. Pošleš je do zvukového
výstupu, který tvůj program už má: herní engine, NAudio, OpenAL a podobně.

**Co knihovna nedělá:** nepřehrává (nemá výstup na zvukovou kartu), nemíchá víc zvuků dohromady,
nehlídá hlasitost součtu a neumisťuje zvuk v prostoru. Návod k tomu dává hotový kód k převzetí:
mixér, vlákno renderu a adaptéry pro NAudio a pro Godot 4.

**Požadavky:** .NET 8 nebo novější (`net8.0`). Knihovna nemá žádné další závislosti.

**Jak číst:** kapitoly 1–3 dají první zvuk do souboru za pár minut. Kapitoly 4–6 jsou o přehrávání
v běžícím programu. Zbytek je reference. Kapitola 14 je přehled všech typů. Úplná reference všech
členů a všech modulů s knoflíky je v `API.md`.

**Jak je to ověřené:** všechen kód v návodu byl přeložen a spuštěn proti knihovně 0.3.0; s 0.4.0
byla ověřena obnova balíku na čistém projektu a načtení a vykreslení všech sestav z balíku.
Výjimkou jsou adaptér pro Godot a ukázka s NAudio výstupem na kartu. Ty jsou jen přeložené
(Godot proti `Godot.NET.Sdk 4.7.2`). Stejný postup s Godotem ale běží v hotové hře. Čísla jsou
změřená na i5-9400F, 48 kHz, blok 30 ms.

**Licence:** MIT s jednou přidanou podmínkou. **Sestavy, které použiješ ve zveřejněném díle**
(hra, aplikace, video, balík zvuků…), **nahraješ do veřejné knihovny sestav** na
<https://www.pekelec.eu/zvuky/knihovna.html>, do 30 dnů a pod licencí CC BY 4.0. Pokusy a soukromé
užití se netýkají. Podrobnosti v kapitole 15 a v souboru `LICENSE`.

## 1. Co dostaneš a co si vyžádat

| | co to je |
|---|---|
| **knihovna** | `ZvukEfekty.Core.dll`, nebo balíček `ZvukEfekty.Core.<verze>.nupkg` |
| **dokumentace** | tenhle `README.md` (jak na to) a `API.md` (úplná reference: moduly, typy, členy). V balíčku jsou oba |
| **sestavy** | soubory `.zvuky` (JSON, jeden soubor = jeden zvuk nebo nástroj) |
| **vzorky** | `.wav` (případně `.mp3`), na které sestavy s modulem Sampler odkazují. **Struktura složek musí zůstat stejná.** Sestava hledá vzorek relativně od sebe, třeba `../vzorky/dvere.wav` |

Vyžádej si k tomu od autora zvuků tohle. Ze souboru se to nepozná, nebo jen špatně:

- **U každého zvuku, jestli je jednorázový, nebo smyčka** (výstřel, nebo déšť). Knihovna je
  nerozlišuje. Smyčka je zvuk, kterému program sám řekne „dost". Viz 5.2.
- **U hratelných sestav** (nástrojů, které mají modul `Nota`) jakými notami se hrají.
- **Které knoflíky smí program měnit** na variace a v jakém rozsahu to ještě zní dobře. Viz 7.
- **Verzi knihovny, se kterou sestavy vznikly.** Novější sestava se ve starší knihovně nemusí
  načíst. Viz 11.
- Volitelně **referenční `.wav`** každého zvuku. S ním ověříš, že tvůj program hraje totéž (9).

## 2. Zapojení knihovny

**Jako `.dll`:** zkopíruj ji do svého repozitáře (třeba `lib/`) a do `.csproj` přidej:

```xml
<ItemGroup>
  <Reference Include="ZvukEfekty.Core">
    <HintPath>lib/ZvukEfekty.Core.dll</HintPath>
  </Reference>
</ItemGroup>
```

**Jako balíček:** `.nupkg` dej do složky v repozitáři (třeba `balicky/`). Vedle `.csproj` přidej
`nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="zvuky" value="balicky" />
  </packageSources>
</configuration>
```

a do `.csproj` přidej `<PackageReference Include="ZvukEfekty.Core" Version="0.4.0" />`. Ověřeno na
čistém projektu s prázdnou cache NuGetu. ⚠️ NuGet si jednou stažený balíček dané verze drží
v globální cache (`%USERPROFILE%\.nuget\packages`). Když dostaneš nový balíček se **stejným
číslem verze**, nepoužije se. Smaž ho z cache, nebo si řekni o nové číslo.

**Kterou verzi máš:** `typeof(ZvukEfekty.Core.Audio.Voice).Assembly.GetName().Version` vrátí
`0.4.0.0`. Zapiš si ji vedle knihovny (kapitola 11 vysvětluje proč).

Jmenné prostory: `ZvukEfekty.Core.Persistence` (načítání), `ZvukEfekty.Core.Audio` (hlas,
vzorky), `ZvukEfekty.Core.Graph` a `ZvukEfekty.Core.Modules` (vnitřek sestavy).

Ukázkové soubory v návodu mají všechny `using` napsané samy. Přeloží se i v projektu bez
`<ImplicitUsings>`, který Godot ve výchozím stavu nezapíná. Ověřeno s vypnutými.

## 3. První zvuk do souboru

Nejkratší cesta, jak ověřit, že knihovna i sestava fungují: celý zvuk najednou, zapsaný do `.wav`.
Je to tatáž cesta, jakou zvuk slyší autor v editoru.

```csharp
using System;
using System.IO;
using System.Linq;
using ZvukEfekty.Core.Graph;
using ZvukEfekty.Core.Modules;
using ZvukEfekty.Core.Persistence;

public static partial class Pomocnici
{
    /// <summary>Celý klip najednou — tatáž cesta, jakou zvuk slyší autor v editoru (Přehrát, Export).</summary>
    public static float[] VykresliCele(PatchLoadResult sestava, int kmitocet = 48000, int midi = 60, double? sekund = null)
    {
        var graf = sestava.Graph;
        var vystup = graf.Modules.OfType<OutputModule>().FirstOrDefault()
            ?? throw new InvalidOperationException("Sestava nemá modul Output.");

        graf.ResolveCurveInputs();               // bez toho hraje křivková obálka jako rovná čára
        foreach (var modul in graf.Modules)
        {
            modul.Prepare();                     // tady se rozbalí vzorky a zapečou knoflíky
            modul.ResetState();
        }
        foreach (var nota in graf.Modules.OfType<NoteModule>())
            nota.SetBatchNote(midi);             // bez toho sestava s Notou mlčí

        var delka = (int)(kmitocet * (sekund ?? sestava.Timing.DurationSeconds));
        var vysledek = graf.RenderModule(vystup, new RenderContext(kmitocet, delka, sestava.Timing.Bpm));
        return vysledek[vystup.Outputs[0]];
    }

    /// <summary>Mono .wav, 16 bit PCM.</summary>
    public static void ZapisWav(string cesta, float[] vzorky, int kmitocet)
    {
        using var w = new BinaryWriter(File.Create(cesta));
        w.Write("RIFF"u8);
        w.Write(36 + vzorky.Length * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);              // PCM
        w.Write((short)1);              // mono
        w.Write(kmitocet);
        w.Write(kmitocet * 2);          // bajtů za sekundu
        w.Write((short)2);              // bajtů na vzorek
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(vzorky.Length * 2);
        foreach (var s in vzorky)
            w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
    }

    /// <summary>Špička v dBFS. −180 = ticho.</summary>
    public static double SpickaDb(ReadOnlySpan<float> vzorky)
    {
        var spicka = 0f;
        foreach (var s in vzorky)
            spicka = Math.Max(spicka, Math.Abs(s));
        return 20 * Math.Log10(Math.Max(spicka, 1e-9));
    }
}
```

```csharp
using ZvukEfekty.Core.Persistence;

var sestava = PatchSerializer.Load("zvuky/vystrel.zvuky");
var vzorky = Pomocnici.VykresliCele(sestava);
Pomocnici.ZapisWav("vystrel.wav", vzorky, 48000);
Console.WriteLine($"špička {Pomocnici.SpickaDb(vzorky):0.0} dBFS");   // −180 = ticho, něco je špatně
```

- **Načítej přes `PatchSerializer.Load(cesta)`.** Ten dopočítá relativní cesty ke vzorkům od
  souboru sestavy. `PatchSerializer.Deserialize(text)` je nechá relativní a vzorky se pak hledají
  vůči pracovní složce procesu. Když soubory nejsou na disku, viz 8.4.
- **Pořadí kroků ve `VykresliCele` je důležité.** Bez `ResolveCurveInputs` hraje kreslená obálka
  jako rovná čára. Bez `SetBatchNote` mlčí každá sestava s Notou. Bez `Timing.Bpm` zní knoflík
  nastavený na notovou hodnotu (třeba „osmina") ve 128 BPM místo v tempu sestavy.
- Klip má přesně délku ze sestavy (`Timing.DurationSeconds`). Co by znělo po ní, se uřízne.
- Render celého klipu **mění vnitřní stav modulů**. Na jednu načtenou sestavu ho nepouštěj ze dvou
  vláken zároveň. Na každé vlákno si ji načti zvlášť.
- Pamatuj na paměť: celý klip si drží pole přes celou délku pro každý modul i knoflík. U minutového
  zvuku jsou to stovky MB. Na dlouhé zvuky použij hlas po blocích (5.2).

## 4. Co je v sestavě (bez editoru)

Editor nemáš, takže si sestavu vypiš:

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using ZvukEfekty.Core.Modules;
using ZvukEfekty.Core.Persistence;

public static partial class Pomocnici
{
    /// <summary>Co v sestavě je: délka, tempo, moduly, knoflíky s rozsahem, soubory.</summary>
    public static void Vypis(PatchLoadResult sestava, TextWriter ven)
    {
        string C(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);
        var t = sestava.Timing;
        ven.WriteLine($"délka {C(t.DurationSeconds)} s, tempo {C(t.Bpm)} BPM" +
                      (sestava.Graph.Modules.OfType<NoteModule>().Any() ? ", má Notu (hraje se notou)" : ""));
        foreach (var m in sestava.Graph.Modules)
        {
            ven.WriteLine($"{m.Name}   ({m.GetType().Name})");
            foreach (var p in m.Parameters)
            {
                var hodnota = p.Options is { } volby
                    ? volby[Math.Clamp((int)Math.Round(p.Value), 0, volby.Length - 1)] + $"   volby: {string.Join(", ", volby)}"
                    : p.IsTempoSynced
                        ? $"{p.DivisionName} (notová hodnota)"
                        : $"{C(p.Value)} {p.Unit}   rozsah {C(p.Min)}–{C(p.Max)}";
                var drat = p.ModulationInput is { } vstup && sestava.Graph.Connections.Any(c => c.Target == vstup)
                    ? $"   + drát, hloubka {C(p.ModulationAmount)}" : "";
                ven.WriteLine($"    {p.Name} = {hodnota}{drat}");
            }
            foreach (var f in m.Files)
                ven.WriteLine($"    soubor {f.Name}: {(f.Path == "" ? "(žádný)" : f.Path)}");
        }
    }
}
```

`Pomocnici.Vypis(PatchSerializer.Load("laser.zvuky"), Console.Out)` vypíše (začátek):

```text
délka 1.6 s, tempo 128 BPM
Křivka výška nabíjení   (CurveModule)
    Délka = 1.6 s   rozsah 0.001–60
    Amplitude = 1    rozsah 0–1
    Opakovat = Ne   volby: Ne, Ano
Oscillator nabíjení – pila   (OscillatorModule)
    Waveform = Saw   volby: Sine, Square, Saw, Triangle, Pulse, Random, Custom
    Vyhlazení hran = Ano   volby: Ne, Ano
    Frequency = 90 Hz   rozsah 20–5000   + drát, hloubka 3110
    Amplitude = 0.4    rozsah 0–1
    Seed = 101    rozsah 0–1000000
Oscillator nabíjení – kvinta   (OscillatorModule)
    Waveform = Pulse   volby: Sine, Square, Saw, Triangle, Pulse, Random, Custom
    Vyhlazení hran = Ano   volby: Ne, Ano
    Frequency = 135 Hz   rozsah 20–5000   + drát, hloubka 4665
    Amplitude = 0.2    rozsah 0–1
    Seed = 102    rozsah 0–1000000
Mixer nabíjení   (MixerModule)
    Mode = Sum   volby: Sum, Average, Multiply
Filter otvírání nabíjení   (FilterModule)
    Type = Low-pass   volby: Low-pass, High-pass, Band-pass, Notch, Peak, All-pass
    Cutoff = 500 Hz   rozsah 20–16000   + drát, hloubka 7000
    Resonance = 1.6    rozsah 0.5–10
```

- Řádek modulu je jeho **jméno**: typ a dovětek od autora („Filter" + „otvírání nabíjení"). Pod
  tímhle jménem se hledá v kapitole 7. V závorce je typ v C#.
- **`+ drát, hloubka …`** znamená, že knoflík ovládá jiný modul (třeba obálka rozevírá filtr).
  Výsledná hodnota je hodnota knoflíku plus modulace. Když knoflík přepíšeš, posune se celý pohyb.
- **„má Notu"** v hlavičce: sestava je nástroj a hraje se notou (MIDI číslo, 60 = C4). Ostatní
  sestavy jsou efekty a nota je jim jedno.
- `Seed` je semínko náhody. Šum a náhodné moduly s ním hrají pokaždé stejně.
- **Co který modul dělá a jaké má knoflíky** (s výchozími hodnotami a rozsahy) je v `API.md`,
  část 1. Podle jména typu v závorce ho tam najdeš.

## 5. Přehrávání v běžícím programu

### 5.1 Jak to do sebe zapadá

```text
 PatchSerializer.Load ─► PatchLoadResult        jednou, při načítání hry/scény
          │
          │  MalaSmes.Zahraj(): new Voice        tvoje vlákno (klon sestavy, rozbalení vzorků)
          ▼
 MalaSmes: hlasy, strop, uvolnění po délce       vlákno renderu: VyrobBlok / PlnVlastniVystupy
          │
          ▼
 ZvukovyProud: kruh hotových vzorků (zásoba)     zvukové vlákno hostitele:
          │                                        Precti (hostitel si říká)
          ▼                                        PrectiPripravene (hostiteli se posílá)
 výstup hostitele (karta, engine, 3D)
```

Z knihovny je jen horní řádek a `Voice`. Mixér, kruh a proud jsou kód z tohoto návodu. Převezmi
ho, nebo si ho napiš po svém.

### 5.2 Hlas: `Voice`

Hlas je **jedna znějící nota jedné sestavy**. Při stavbě si sestavu naklonuje, takže jednu načtenou
sestavu můžeš pouštět kolikrát chceš a třeba i naráz.

```csharp
var hlas = new Voice(sestava.Graph, 60, MidiNotes.ToFrequencyHz(60), sestava.Timing.Bpm);
float[] blok = hlas.RenderBlock(1440, 48000);   // dalších 30 ms; PŮJČENÉ pole, viz níž
```

| člen | co dělá | odkud volat |
|---|---|---|
| `new Voice(graf, midi, Hz, bpm)` | klon sestavy, příprava modulů (**rozbalí vzorky**) | **ne ze zvukového vlákna**, stojí milisekundy (6) |
| `RenderBlock(n, kmitocet)` | dalších `n` vzorků | vlákno renderu |
| `Release()` | pustí notu, zvuk dozní podle své obálky | jednorázový zvuk po své délce, smyčka na konci |
| `Steal(vzorků)` | utne s krátkým fade, bez lupnutí | plný strop, přechod scény |
| `IsFinished` | po `Release` dva tiché bloky za sebou (pod −66 dBFS) nebo 5 s pojistka; po `Steal` konec fade | pak hlas zahoď |
| `IsReleasing`, `IsBeingStolen` | v jaké fázi hlas je | správa hlasů |
| `LastBlockPeak` | špička posledního bloku, 0…1 | měřidla |

⚠️ **Pole z `RenderBlock` je půjčené.** Hlas vrací pokaždé tentýž objekt a další volání ho přepíše.
Přičti ho do součtu nebo zkopíruj hned. Do fronty ho nedávej. Ověřeno na pěti sestavách: totéž pole,
obsah přepsaný.

**Jednorázový zvuk** pusť po **délce ze sestavy** (`Timing.DurationSeconds`), ne po knoflíku
`Držení` u Noty. Efekty mají `Držení` 0 a zazněly by jen jeden blok. Hlas pak dozní sám
a `IsFinished` se rozsvítí o něco později. Změřeno: laser s délkou 1,60 s skončil po 1,68 s, roj
jisker se 4,00 s po 4,11 s.

**Smyčka** (déšť, vítr, motor) je hlas, kterému `Release()` zavoláš, až má přestat. Délka ze
sestavy u ní nehraje roli. Změřeno: vítr hrál 20 s bez `Release` a nezeslábl. **Hlas, kterému
`Release()` nezavoláš nikdy, nikdy neskončí** a zabírá místo i výkon.

### 5.3 Mixér

Drží znějící hlasy, pouští je po délce, při plném stropu krade nejstarší a sčítá je do jednoho
bloku. Hlas může mít místo součtu i **vlastní výstup** (kruh). To je potřeba pro zvuky na místě ve
3D (5.7).

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using ZvukEfekty.Core.Audio;
using ZvukEfekty.Core.Persistence;

/// <summary>Znějící hlasy: strop, uvolnění po délce, součet. Bez vláken — volá ji
/// <see cref="ZvukovyProud"/>, nebo kdokoli, kdo render řídí sám.</summary>
public sealed class MalaSmes
{
    sealed class Hlas
    {
        public required Voice Voice;
        public required float Hlasitost;
        public required int PustitPo;   // vzorků do uvolnění noty; -1 = smyčka, pouští volající
        public Kruh? Vystup;            // vlastní výstup hlasu (3D); null = jde do součtu
        public int Odehrano;

        /// <summary>Další blok hlasu. Vrací PŮJČENÉ pole: přečíst hned, nedržet.</summary>
        public float[] Dalsi(int pocet, int kmitocet)
        {
            if (PustitPo >= 0 && !Voice.IsReleasing && Odehrano >= PustitPo)
                Voice.Release();
            Odehrano += pocet;
            return Voice.RenderBlock(pocet, kmitocet);
        }
    }

    readonly List<Hlas> _hlasy = new();
    readonly object _zamek = new();
    float[] _pomocny = Array.Empty<float>();

    public MalaSmes(int kmitocet) => Kmitocet = kmitocet;

    public int Kmitocet { get; }
    public int Strop { get; set; } = 16;
    public int PocetHlasu { get { lock (_zamek) return _hlasy.Count; } }

    /// <summary>Spustí sestavu. Volat MIMO zvukové vlákno: staví klon sestavy, rozbaluje vzorky.
    /// <paramref name="vlastniVystup"/> = hlas nejde do součtu, ale do vlastního kruhu (3D zdroj).</summary>
    public Voice Zahraj(PatchLoadResult sestava, float hlasitost = 1f, bool smycka = false, int midi = 60,
        Kruh? vlastniVystup = null)
    {
        var voice = new Voice(sestava.Graph, midi, MidiNotes.ToFrequencyHz(midi), sestava.Timing.Bpm);

        // Délka z .zvuky, ne Držení: jednorázové efekty mají Držení 0.
        var delka = sestava.Timing.DurationSeconds > 0
            ? sestava.Timing.DurationSeconds
            : sestava.Graph.BatchNoteHoldSeconds(sestava.Timing.Bpm);
        var hlas = new Hlas
        {
            Voice = voice,
            Hlasitost = hlasitost,
            PustitPo = smycka ? -1 : (int)Math.Round(delka * Kmitocet),
            Vystup = vlastniVystup,
        };

        lock (_zamek)
        {
            // Plný strop: nejstarší hlas dozní 10 ms a uvolní místo.
            var zive = _hlasy.Where(h => !h.Voice.IsBeingStolen).ToList();
            if (zive.Count >= Strop)
                zive[0].Voice.Steal(Kmitocet / 100);
            _hlasy.Add(hlas);
        }
        return voice;   // smyčku ukončí volající: voice.Release()
    }

    /// <summary>Utne všechno (krátký fade, bez lupnutí). Přechod mezi scénami.</summary>
    public void Umlc()
    {
        lock (_zamek)
            foreach (var h in _hlasy)
                h.Voice.Steal(Kmitocet / 100);
    }

    /// <summary>Přepíše <paramref name="cil"/> součtem hlasů BEZ vlastního výstupu.</summary>
    public void VyrobBlok(float[] cil, int pocet)
    {
        Array.Clear(cil, 0, pocet);
        lock (_zamek)
        {
            for (var i = _hlasy.Count - 1; i >= 0; i--)
            {
                var h = _hlasy[i];
                if (h.Vystup is not null)
                    continue;

                var blok = h.Dalsi(pocet, Kmitocet);
                for (var j = 0; j < pocet; j++)
                    cil[j] += blok[j] * h.Hlasitost;

                if (h.Voice.IsFinished)
                    _hlasy.RemoveAt(i);
            }
        }

        // Nouzový ořez. Core žádný limiter nemá; lepší je limiter na součtu u hostitele.
        for (var j = 0; j < pocet; j++)
            cil[j] = Math.Clamp(cil[j], -1f, 1f);
    }

    /// <summary>Doplní vlastní výstupy hlasů po blocích, dokud se vejdou. Vrací počet bloků.</summary>
    public int PlnVlastniVystupy(int pocet)
    {
        if (_pomocny.Length < pocet)
            _pomocny = new float[pocet];
        var bloku = 0;
        lock (_zamek)
        {
            for (var i = _hlasy.Count - 1; i >= 0; i--)
            {
                var h = _hlasy[i];
                if (h.Vystup is not { } kruh)
                    continue;

                while (!h.Voice.IsFinished && kruh.Volno >= pocet)
                {
                    var blok = h.Dalsi(pocet, Kmitocet);
                    for (var j = 0; j < pocet; j++)
                        _pomocny[j] = blok[j] * h.Hlasitost;
                    kruh.Zapis(_pomocny, pocet);
                    bloku++;
                }

                if (h.Voice.IsFinished)
                    _hlasy.RemoveAt(i);   // co je v kruhu, výstup ještě dohraje
            }
        }
        return bloku;
    }
}
```

Ověřeno: smyčka a jednorázový zvuk naráz, strop 2, třetí zvuk ukradl nejstarší (smyčku), dohrané
hlasy zmizely.

### 5.4 Vlákno renderu a kruh

Render nesmí běžet na zvukovém vlákně hostitele (tam se nesmí čekat) ani na hlavním vlákně programu
(tam by zvuk zadrhával s obrazem). Vlastní vlákno proto renderuje **dopředu** do kruhu a hostitel
z něj jen čte hotové vzorky.

```csharp
using System;
using System.Threading;

/// <summary>Kruhová fronta vzorků mezi dvěma vlákny: zapisuje JEDNO vlákno (render), čte JEDNO
/// (výstup). Při jednom zapisovateli a jednom čtenáři stačí dvě počítadla, zámek není potřeba.</summary>
public sealed class Kruh
{
    readonly float[] _data;
    long _zapsano, _precteno;

    public Kruh(int kapacita) => _data = new float[kapacita];

    public int Kapacita => _data.Length;

    /// <summary>Kolik vzorků čeká na přečtení.</summary>
    public int Pripraveno => (int)(Volatile.Read(ref _zapsano) - Volatile.Read(ref _precteno));

    /// <summary>Kolik se jich ještě vejde.</summary>
    public int Volno => _data.Length - Pripraveno;

    /// <summary>Jen zapisovatel. Volající hlídá <see cref="Volno"/>.</summary>
    public void Zapis(float[] zdroj, int pocet)
    {
        var z = _zapsano;
        for (var i = 0; i < pocet; i++)
            _data[(int)((z + i) % _data.Length)] = zdroj[i];
        Volatile.Write(ref _zapsano, z + pocet);
    }

    /// <summary>Jen čtenář. Přečte, kolik je připraveno (nejvýš délku <paramref name="cil"/>),
    /// a vrátí počet.</summary>
    public int Precti(Span<float> cil)
    {
        var n = Math.Min(cil.Length, Pripraveno);
        var p = _precteno;
        for (var i = 0; i < n; i++)
            cil[i] = _data[(int)((p + i) % _data.Length)];
        Volatile.Write(ref _precteno, p + n);
        return n;
    }
}
```

```csharp
using System;
using System.Threading;

/// <summary>Vlákno renderu nad <see cref="MalaSmes"/>: plní kruh součtu (a vlastní výstupy hlasů)
/// dopředu, aby zvukové vlákno hostitele jen četlo hotové vzorky.</summary>
public sealed class ZvukovyProud : IDisposable
{
    readonly Kruh _soucet;
    readonly int _blok;
    readonly float[] _pomocny;
    readonly Thread _vlakno;
    readonly AutoResetEvent _prace = new(false);
    volatile bool _konec;
    long _podbehy;

    /// <param name="blokMs">Délka bloku renderu. Drž ji stálou.</param>
    /// <param name="zasobaMs">Kolik zvuku se renderuje dopředu. Musí být víc, než si hostitel
    /// řekne najednou, jinak bude každé čtení podběh. Zároveň je to latence navíc.</param>
    public ZvukovyProud(int kmitocet, int blokMs = 30, int zasobaMs = 120)
    {
        Smes = new MalaSmes(kmitocet);
        _blok = kmitocet * blokMs / 1000;
        _pomocny = new float[_blok];
        _soucet = new Kruh(kmitocet * zasobaMs / 1000);
        _vlakno = new Thread(Smycka) { IsBackground = true, Name = "ZvukEfekty render" };
        _vlakno.Start();
    }

    public MalaSmes Smes { get; }
    public int Kmitocet => Smes.Kmitocet;
    public int Blok => _blok;

    /// <summary>Kolikrát čtení nedostalo celý požadavek (render nestíhá, nebo malá zásoba).</summary>
    public long Podbehy => Interlocked.Read(ref _podbehy);

    /// <summary>Pro hostitele, který si o vzorky ŘÍKÁ (callback): vždy vyplní celé
    /// <paramref name="cil"/>, co chybí, doplní tichem.</summary>
    public int Precti(Span<float> cil)
    {
        var n = _soucet.Precti(cil);
        if (n < cil.Length)
        {
            cil[n..].Clear();
            Interlocked.Increment(ref _podbehy);
        }
        _prace.Set();
        return cil.Length;
    }

    /// <summary>Pro hostitele, kterému se vzorky POSÍLAJÍ: přečte jen to, co je hotové.</summary>
    public int PrectiPripravene(Span<float> cil)
    {
        var n = _soucet.Precti(cil);
        _prace.Set();
        return n;
    }

    /// <summary>Pobídne vlákno renderu (třeba po spuštění hlasu s vlastním výstupem).</summary>
    public void Pobidni() => _prace.Set();

    void Smycka()
    {
        while (!_konec)
        {
            var neco = false;
            while (_soucet.Volno >= _blok)
            {
                Smes.VyrobBlok(_pomocny, _blok);
                _soucet.Zapis(_pomocny, _blok);
                neco = true;
            }
            if (Smes.PlnVlastniVystupy(_blok) > 0)
                neco = true;
            if (!neco)
                _prace.WaitOne(5);
        }
    }

    public void Dispose()
    {
        _konec = true;
        _prace.Set();
        _vlakno.Join();
    }
}
```

- **Zásoba musí být větší než jedna žádost hostitele.** Změřeno: hostitel, který chtěl 150 ms
  najednou proti zásobě 120 ms, dostal podběh při každém čtení (20 z 20). Při žádostech po 50 ms
  nebyl za 4 s ani jeden.
- **Zásoba je zároveň zpoždění.** Zvuk spuštěný teď zazní až po tom, co je v kruhu před ním, plus
  buffer hostitele. Kratší zásoba dá menší zpoždění a větší riziko podběhů.
- `Podbehy` > 0 znamená, že render nestíhá, nebo je zásoba menší než žádost.

### 5.5 Hostitel, který si o vzorky říká (NAudio)

Callback hostitele chce „dej mi N vzorků". To je `Precti`: vždycky vyplní celý požadavek a co
chybí, doplní tichem.

```csharp
using System;
using NAudio.Wave;

/// <summary>Hostitel, který si o vzorky ŘÍKÁ: NAudio volá Read ze svého vlákna.</summary>
public sealed class ZvukyProNAudio : ISampleProvider
{
    readonly ZvukovyProud _proud;

    public ZvukyProNAudio(ZvukovyProud proud)
    {
        _proud = proud;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(proud.Kmitocet, 1);   // mono float
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count) => _proud.Precti(buffer.AsSpan(offset, count));
}
```

```csharp
using System.Threading;
using NAudio.Wave;
using ZvukEfekty.Core.Persistence;

public static class UkazkaNAudio
{
    public static void Hraj()
    {
        using var proud = new ZvukovyProud(48000);
        // 2 buffery po 50 ms: hostitel si řekne o 50 ms, zásoba proudu je 120 ms — vejde se.
        // Výchozí NAudio (300 ms / 2) chce 150 ms najednou a podbíhalo by každé čtení.
        using var vystup = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 2 };
        vystup.Init(new ZvukyProNAudio(proud));
        vystup.Play();

        var vystrel = PatchSerializer.Load("zvuky/vystrel.zvuky");   // jednou, při načítání
        var dest = PatchSerializer.Load("zvuky/dest.zvuky");

        var podkres = proud.Smes.Zahraj(dest, hlasitost: 0.4f, smycka: true);
        proud.Smes.Zahraj(vystrel);                                   // dohraje a zmizí sám
        Thread.Sleep(3000);
        podkres.Release();                                            // smyčka dozní obálkou
        Thread.Sleep(1000);
    }
}
```

Ověřeno: `ZvukyProNAudio` čtený tempem reálného času po 50 ms, 2 s, žádný podběh. Výstup na kartu
(`WaveOutEvent.Play`) je jen přeložený.

### 5.6 Hostitel, kterému se vzorky posílají (Godot 4)

Godot má `AudioStreamGenerator`: program do něj vzorky **posílá** (`PushBuffer`), kolik se vejde.
To je `PrectiPripravene`: vrátí jen hotové vzorky, nic nedoplňuje. Godot si podběh ošetří sám.

```csharp
using System;
using System.Collections.Generic;
using Godot;
using ZvukEfekty.Core.Audio;
using ZvukEfekty.Core.Persistence;

/// <summary>Zvuky v Godotu 4: součet bez místa jedním přehrávačem a zvuky na místě ve 3D,
/// každý svým <see cref="AudioStreamPlayer3D"/>. Do Godotu se sahá jen tady, z hlavního vlákna;
/// vlákno renderu zná jen směs a kruhy.</summary>
public partial class ZvukyGodot : Node
{
    const float BufferGodotu = 0.1f;   // sekund ve frontě AudioStreamGenerator

    delegate int Ctecka(Span<float> cil);

    sealed class NaMiste
    {
        public required Voice Hlas;
        public required Kruh Kruh;
        public required AudioStreamPlayer3D Hrac;
        public required AudioStreamGeneratorPlayback Playback;
        public double? Dohrano;
    }

    public ZvukovyProud Proud { get; private set; } = null!;
    AudioStreamPlayer _soucet = null!;
    AudioStreamGeneratorPlayback _playbackSouctu = null!;
    readonly List<NaMiste> _naMiste = new();
    float[] _pomocny = new float[1];
    double _cas;

    public override void _Ready()
    {
        var kmitocet = (int)AudioServer.GetMixRate();
        Proud = new ZvukovyProud(kmitocet);
        _soucet = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = kmitocet, BufferLength = BufferGodotu },
        };
        AddChild(_soucet);
        _soucet.Play();
        _playbackSouctu = (AudioStreamGeneratorPlayback)_soucet.GetStreamPlayback();
    }

    /// <summary>Zvuk bez místa (rozhraní, hudba, náhled).</summary>
    public Voice Zahraj(PatchLoadResult sestava, float hlasitost = 1f, bool smycka = false)
        => Proud.Smes.Zahraj(sestava, hlasitost, smycka);

    /// <summary>Zvuk na místě ve světě: vlastní kruh a vlastní 3D přehrávač.</summary>
    public Voice ZahrajNaMiste(PatchLoadResult sestava, Node3D rodic, Vector3 poloha, float dosah,
        float hlasitost = 1f, bool smycka = false)
    {
        var kruh = new Kruh(Proud.Kmitocet * 120 / 1000);
        var hlas = Proud.Smes.Zahraj(sestava, hlasitost, smycka, vlastniVystup: kruh);
        Proud.Pobidni();   // naplnit kruh hned, ne až v dalším kole renderu

        var hrac = new AudioStreamPlayer3D
        {
            Stream = new AudioStreamGenerator { MixRate = Proud.Kmitocet, BufferLength = BufferGodotu },
            Position = poloha,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            UnitSize = 2f,
            MaxDistance = dosah,
        };
        rodic.AddChild(hrac);
        hrac.Play();
        _naMiste.Add(new NaMiste
        {
            Hlas = hlas, Kruh = kruh, Hrac = hrac,
            Playback = (AudioStreamGeneratorPlayback)hrac.GetStreamPlayback(),
        });
        return hlas;
    }

    public override void _Process(double delta)
    {
        _cas += delta;
        Preleve(_playbackSouctu, span => Proud.PrectiPripravene(span));

        for (var i = _naMiste.Count - 1; i >= 0; i--)
        {
            var z = _naMiste[i];
            if (!IsInstanceValid(z.Hrac))
            {
                // Rodič zmizel i s přehrávačem (přechod scény) — hlas nemá kam znít.
                z.Hlas.Steal(Proud.Kmitocet / 100);
                z.Playback.Dispose();
                _naMiste.RemoveAt(i);
                continue;
            }

            Preleve(z.Playback, span => z.Kruh.Precti(span));
            Proud.Pobidni();

            if (!z.Hlas.IsFinished || z.Kruh.Pripraveno > 0)
                continue;

            // Godot ještě dohrává svůj buffer. Napřed Stop, pak uvolnit — jinak Godot na konci
            // hlásí uniklé AudioStreamGeneratorPlayback a umí i spadnout.
            z.Dohrano ??= _cas;
            if (_cas - z.Dohrano.Value > BufferGodotu + 0.05)
            {
                z.Hrac.Stop();
                z.Hrac.QueueFree();
                z.Playback.Dispose();
                _naMiste.RemoveAt(i);
            }
        }
    }

    // Do fronty Godotu přidá, kolik se vejde a kolik je hotové. Mono na oba kanály, ořez ±1.
    void Preleve(AudioStreamGeneratorPlayback playback, Ctecka zdroj)
    {
        var volno = playback.GetFramesAvailable();
        if (volno <= 0)
            return;
        if (_pomocny.Length < volno)
            _pomocny = new float[volno];
        var n = zdroj(_pomocny.AsSpan(0, volno));
        if (n == 0)
            return;
        var snimky = new Vector2[n];
        for (var j = 0; j < n; j++)
        {
            var s = Math.Clamp(_pomocny[j], -1f, 1f);
            snimky[j] = new Vector2(s, s);
        }
        playback.PushBuffer(snimky);
    }

    public override void _ExitTree()
    {
        Proud.Dispose();
        _soucet.Stop();
        _playbackSouctu.Dispose();
        foreach (var z in _naMiste)
        {
            if (IsInstanceValid(z.Hrac))
                z.Hrac.Stop();
            z.Playback.Dispose();
        }
        _naMiste.Clear();
    }
}
```

```csharp
using Godot;
using ZvukEfekty.Core.Persistence;

public partial class Ukazka : Node3D
{
    ZvukyGodot _zvuky = null!;
    PatchLoadResult _vystrel = null!;

    public override void _Ready()
    {
        _zvuky = new ZvukyGodot();
        AddChild(_zvuky);
        _vystrel = SestavyGodot.Nacti("res://zvuky/vystrel.zvuky");   // jednou; funguje i z .pck
    }

    public void Vystrel(Vector3 kde)
    {
        _zvuky.ZahrajNaMiste(_vystrel, this, kde, dosah: 25f);       // 3D, útlum se vzdáleností
        _zvuky.Zahraj(_vystrel, hlasitost: 0.2f);                     // bez místa (rozhraní)
    }
}
```

Načítání sestav v Godotu (`SestavyGodot.Nacti`) je v 8.4. V exportované hře totiž `res://`
neleží na disku.

**Lekce z Godotu**, každá stála čas:

- **Do Godotu sahej jen z hlavního vlákna** (`_Process`). Vlákno renderu zná jen směs a kruhy.
- **Dohraný přehrávač napřed `Stop()`, pak `QueueFree()` a `Playback.Dispose()`.** Bez `Stop` hlásil
  Godot na konci hry „ObjectDB instances leaked" (`AudioStreamGeneratorPlayback`) a občas spadl.
  Při ukončení hry počkej ~250 ms, než AudioServer přehrávače pustí.
- Do `PushBuffer` jde `Vector2(s, s)`, tedy mono na oba kanály, ořezané na ±1.
- Kmitočet ber z `AudioServer.GetMixRate()`. Knihovna vyrenderuje jakýkoli.

### 5.7 Zvuky na místě (3D)

Výstup je mono, a to je pro 3D správně: 3D zdroj v enginu chce mono a umístí ho sám. Postup:
**každý hlas na místě dostane vlastní kruh a vlastní 3D zdroj enginu.** Render dál běží v jednom
vlákně (`PlnVlastniVystupy`) a hlavní vlákno přelévá každý kruh do jeho zdroje. Tak to dělá
`ZahrajNaMiste` v 5.6. Útlum se vzdáleností, panning a případně dozvuk prostoru pak dělá engine.

Ověřeno bez Godotu: hlas s vlastním kruhem, čtený po 16 ms, odehrál 1,68 s se špičkou −1,1 dBFS
(celý klip najednou −1,0) a po dohrání z mixéru zmizel.

### 5.8 Jiní hostitelé

Rozhoduje jediná otázka: **říká si hostitel o vzorky (callback), nebo mu je posíláš?**

| hostitel | tvar | čím | pozn. (nezkoušeno) |
|---|---|---|---|
| NAudio, SDL, PortAudio, CSCore | říká si | `Precti` | 5.5 |
| Godot 4 (.NET) | posíláš | `PrectiPripravene` | 5.6 |
| MonoGame | posíláš | `DynamicSoundEffectInstance.SubmitBuffer` + `BufferNeeded` | chce 16bit PCM v bajtech, převeď z `float`; pro `Apply3D` je mono povinné |
| OpenAL (Silk.NET, OpenTK) | posíláš | fronta bufferů zdroje | pro 3D je mono povinné |
| FMOD, Wwise | říká si | programmer sound / vlastní DSP callback | umí zaclonění a dozvuk podle prostoru |
| Unity | ❌ | — | knihovnu nenačte, viz 12 |

## 6. Co musí hlídat tvůj program

**Hlasitost součtu.** Knihovna ji nehlídá. Už jedna sestava může přetéct přes 0 dBFS (změřeno: úder
do skla +1,6 dBFS). Osm zvuků naráz dalo bez limiteru +13,6 dBFS. Na součet patří limiter
(v Godotu `AudioEffectHardLimiter` na Master, třeba se stropem −0,3 dB). Ořez ±1 v `MalaSmes` je
jen nouzový, při přetečení zkresluje.

**Strop hlasů.** Každý hlas stojí výkon a hlasy přibývají rychleji, než ubývají. `MalaSmes` krade
nejstarší. Lepší je krást podle důležitosti: nejdřív kroky, ambient naposled.

**Vlákna:**

- `new Voice(…)` (tedy `MalaSmes.Zahraj`) staví klon sestavy a rozbaluje vzorky. Změřeno: 0,7–2,5 ms
  na hlas. Se vzorkem, který ještě není rozbalený, 26 ms, tedy při 60 fps půldruhého snímku. Když
  už rozbalený je, 0,45 ms. Proto vzorky **předehřej** při načítání scény (8.3).
- `RenderBlock` volá v jednu chvíli jen jedno vlákno.
- `Release()` a `Steal()` jen přepnou příznak. Volat je z jiného vlákna, než renderuje, v praxi
  funguje (hotová hra to tak dělá), ale knihovna to nezaručuje. Kdo chce jistotu, pošle povel do
  vlákna renderu frontou.
- `AudioSampleCache` je zamčená a dá se volat odkudkoli.
- **Velikost bloku drž stálou.** Při změně si hlas zahodí své buffery a alokuje nové.

**Výkon.** V Godotu stál jeden hlas 2,3–3,4 % jednoho jádra. Kolik hlasů unese tvůj program, záleží
na sestavách a na hostiteli, tak si to změř. Jiný hostitel (vlastní mix nad NAudiem) unesl na
i5-9400F 72 zvuků naráz a u 80 začal vypadávat.

## 7. Varianty: knoflíky

Jedna sestava může dát celou rodinu zvuků: jiná výška, jasnější, delší. Knihovna má jen surové API
(`ModuleBase.Parameters`, `ModuleParameter.Value`). Hledání podle jména je tady:

```csharp
using System;
using System.Globalization;
using System.Linq;
using ZvukEfekty.Core.Graph;
using ZvukEfekty.Core.Persistence;

public static partial class Pomocnici
{
    /// <summary>Nezávislá kopie sestavy — pro varianty s jinými knoflíky.</summary>
    public static PatchLoadResult Kopie(PatchLoadResult sestava)
    {
        var json = PatchSerializer.Serialize(sestava.Graph, sestava.Positions, sestava.Timing, sestava.Groups);
        return PatchSerializer.Deserialize(json, freshInstanceIds: true);
    }

    /// <summary>Přepíše jeden knoflík. Modul se hledá podle jména, jak ho vypíše <see cref="Vypis"/>:
    /// celé jméno, nebo jeho jednoznačná část („otvírání“). Stejnojmenné moduly rozliší pořadí
    /// v souboru: „Gain#2“. Parametr podle jména. Velikost písmen nehraje roli, u výčtu jde zadat
    /// jméno volby. Nesedí-li to právě na jeden modul a knoflík, nebo je hodnota mimo rozsah,
    /// hodí výjimku a nic nezmění.</summary>
    public static void Nastav(ModuleGraph graf, string modul, string parametr, string hodnota)
    {
        var jmeno = modul.Trim();
        int? poradi = null;
        var mriz = jmeno.LastIndexOf('#');
        if (mriz > 0 && int.TryParse(jmeno[(mriz + 1)..], out var n))
            (jmeno, poradi) = (jmeno[..mriz].Trim(), n);

        var shody = graf.Modules.Where(m => string.Equals(m.Name, jmeno, StringComparison.OrdinalIgnoreCase)).ToList();
        if (shody.Count == 0)
            shody = graf.Modules.Where(m => m.Name.Contains(jmeno, StringComparison.OrdinalIgnoreCase)).ToList();
        if (poradi is { } k)
            shody = k >= 1 && k <= shody.Count ? new() { shody[k - 1] } : new();
        if (shody.Count != 1)
            throw new InvalidOperationException(shody.Count == 0
                ? $"Sestava nemá modul „{modul}“."
                : $"„{modul}“ sedí na {shody.Count} moduly: {string.Join(", ", shody.Select(m => m.Name))}.");

        var p = shody[0].Parameters.FirstOrDefault(x => string.Equals(x.Name, parametr, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Modul „{modul}“ nemá knoflík „{parametr}“.");
        if (p.IsTempoSynced || p.IsAutoSeeded)
            throw new InvalidOperationException($"{modul}.{parametr} se nebere z hodnoty (notová hodnota nebo losování) — přepis by nic neudělal.");

        double cislo;
        if (p.Options is { } volby && Array.FindIndex(volby, o => string.Equals(o, hodnota, StringComparison.OrdinalIgnoreCase)) is var i and >= 0)
            cislo = i;
        else if (!double.TryParse(hodnota.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out cislo))
            throw new InvalidOperationException($"{modul}.{parametr}: „{hodnota}“ není číslo ani jméno volby.");

        if (cislo < p.Min || cislo > p.Max)
            throw new InvalidOperationException($"{modul}.{parametr} = {hodnota} je mimo rozsah {p.Min:0.###}–{p.Max:0.###}.");
        p.Value = cislo;
    }
}
```

```csharp
var tmava = Pomocnici.Kopie(sestava);                 // načtená sestava zůstane netknutá
Pomocnici.Nastav(tmava.Graph, "otvírání", "Cutoff", "300");
Pomocnici.Nastav(tmava.Graph, "otvírání", "Type", "Band-pass");   // výčet jménem volby
proud.Smes.Zahraj(tmava);
```

- **Přepisuj před `Zahraj` (a před `VykresliCele`).** Hlas si sestavu klonuje při stavbě a moduly
  si hodnoty zapečou v přípravě. Změřeno: přepsání sestavy po stavbě hlasu ho nezměnilo ani o bit.
- Jména modulů a knoflíků a jejich rozsahy vypíše `Vypis` (4). `Nastav` odmítne hodnotu mimo
  rozsah, neznámé jméno i jméno, které sedí na víc modulů (pak použij celé jméno nebo `Filter#2`).
  Neusekává: useknutá hodnota by zazněla jinak, než autor zamýšlel.
- Knoflík na **notové hodnotě** a semínko, které se **losuje při každém spuštění**, přepsat nejde.
  Hodnotu nečtou. `Nastav` to řekne.
- **Přepsaný knoflík umí sestavu dostat do klipování.** Otevřený filtr pustí víc energie (změřeno
  na jiné sestavě: filtr z 900 na 4000 Hz dal +1,6 dBFS). Varianty změř (9).

## 8. Vzorky (modul Sampler)

### 8.1 Formáty

| vzorek | jak | stav |
|---|---|---|
| `.wav` na disku | cesta v sestavě, relativně od `.zvuky` | ✅ PCM 8/16/24/32 bit, float 32/64, mono i stereo (zprůměruje se do mona) |
| `.mp3`, `.flac`, `.ogg`… | dekodér dodá tvůj program (8.2) | ⚠️ knihovna je sama nerozbalí a sestava hraje ticho |
| obsah `.wav` v paměti | `AudioSampleCache.VlozWav("jméno", bajty)` | ✅ bitově shodné se souborem |
| hotové vzorky v paměti | `AudioSampleCache.VlozVzorek("jméno", float[] mono, kmitočet)` | ✅ |

Vložený vzorek najde Sampler pod cestou `pamet:jméno` (`AudioSampleCache.PrefixPameti` + jméno).
Tu zapiš do `sampler.Files[0].Path`. Musí být vložený **dřív, než se staví hlas**, jinak Sampler
hraje ticho. Vložené vzorky nevyhodí žádný strop, uklízí je program (`OdeberVzorek`,
`VyprazdniPamet`).

### 8.2 Dekodér pro komprimované formáty

Knihovna má na to šev `AudioSampleCache.Dekoder`: funkci „cesta → vzorky". `.wav` rozbaluje vždy
sama, dekodér dostane jen ostatní. Příklad s NAudio:

```csharp
using System;
using System.Collections.Generic;
using NAudio.Wave;
using ZvukEfekty.Core.Audio;

public static class DekoderNAudio
{
    /// <summary>Naučí Sampler .mp3 (a co dalšího NAudio přečte) — dekodér dodává hostitel.</summary>
    public static void Zaregistruj() => AudioSampleCache.Dekoder = cesta =>
    {
        using var ctecka = new AudioFileReader(cesta);            // float, kanály prokládané
        var kanalu = ctecka.WaveFormat.Channels;
        var vse = new List<float>();
        var buf = new float[ctecka.WaveFormat.SampleRate * kanalu];
        int n;
        while ((n = ctecka.Read(buf, 0, buf.Length)) > 0)
            vse.AddRange(buf.AsSpan(0, n).ToArray());

        var mono = new float[vse.Count / kanalu];                  // Core chce mono: průměr kanálů
        for (var i = 0; i < mono.Length; i++)
        {
            var soucet = 0f;
            for (var k = 0; k < kanalu; k++)
                soucet += vse[i * kanalu + k];
            mono[i] = soucet / kanalu;
        }
        return new DecodedSample(mono, ctecka.WaveFormat.SampleRate, kanalu);
    };
}
```

Ověřeno: Sampler nad `.mp3` bez dekodéru hlásí „Zvuk nezačíná na RIFF, není to WAV…" a hraje ticho.
S dekodérem zazní (špička −5,3 dBFS). Zaregistruj ho na startu programu. Neúspěšné načtení si
cache nepamatuje, takže pozdější registrace taky zabere.

### 8.3 Kontrola a předehřátí

```csharp
using System.Collections.Generic;
using System.Linq;
using ZvukEfekty.Core.Audio;
using ZvukEfekty.Core.Modules;
using ZvukEfekty.Core.Persistence;

public static partial class Pomocnici
{
    /// <summary>Vzorky, které Sampler nesežene — prázdný seznam = všechno v pořádku. Zároveň je
    /// rozbalí do cache, takže to slouží i jako předehřátí.</summary>
    public static List<string> ChybejiciVzorky(PatchLoadResult sestava)
    {
        var chyby = new List<string>();
        foreach (var sampler in sestava.Graph.Modules.OfType<SamplerModule>())
            foreach (var f in sampler.Files)
            {
                if (string.IsNullOrWhiteSpace(f.Path))
                    chyby.Add($"{sampler.Name}: není vybraný soubor");
                else if (AudioSampleCache.Get(f.Path, out var chyba) is null)
                    chyby.Add($"{sampler.Name}: {f.Path} — {chyba}");
            }
        return chyby;
    }
}
```

Volej ho při načítání scény na každou sestavu. Prázdný seznam znamená, že je všechno v pořádku.
Zároveň vzorky rozbalí do cache, takže první spuštění pak necukne. Jde to i na vedlejším vlákně.
⚠️ **Chybějící vzorek jinak nepoznáš:** `new Voice` projde bez výjimky a hlas hraje ticho
(−180 dBFS). Hlášku zapíše jen do své soukromé kopie sestavy.

### 8.4 Sestavy, které neleží na disku (Godot `.pck`, zip, vlastní balík)

`PatchSerializer.Load` čte přes `System.IO`. Z exportované Godot hry, ze zipu ani z vlastního
archivu soubor nepřečte. Pak načti text sestavy po svém a vzorky vlož do paměti:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using ZvukEfekty.Core.Audio;
using ZvukEfekty.Core.Modules;
using ZvukEfekty.Core.Persistence;

public static partial class Pomocnici
{
    static readonly HashSet<string> _vlozeneVzorky = new();

    /// <summary>Sestava z balíku, ke kterému se nedá dostat cestou na disku (Godot .pck, zip,
    /// vlastní archiv). <paramref name="nactiSoubor"/> dostane cestu ke vzorku tak, jak je zapsaná
    /// v sestavě (obvykle relativní), a vrátí obsah souboru. Každý .wav se rozbalí do paměti
    /// a Sampler se na něj přepojí (<c>pamet:…</c>). Umí jen .wav — jiný formát hodí výjimku.</summary>
    public static PatchLoadResult NactiZBaliku(string obsahSestavy, Func<string, byte[]> nactiSoubor)
    {
        var sestava = PatchSerializer.Deserialize(obsahSestavy);
        foreach (var sampler in sestava.Graph.Modules.OfType<SamplerModule>())
            foreach (var f in sampler.Files)
            {
                if (string.IsNullOrWhiteSpace(f.Path) || AudioSampleCache.JeVPameti(f.Path))
                    continue;
                var jmeno = f.Path.Replace('\\', '/');
                lock (_vlozeneVzorky)
                    if (_vlozeneVzorky.Add(jmeno))
                        AudioSampleCache.VlozWav(jmeno, nactiSoubor(f.Path));   // rozbalí hned
                f.Path = AudioSampleCache.PrefixPameti + jmeno;
            }
        return sestava;
    }
}
```

```csharp
using Godot;
using ZvukEfekty.Core.Persistence;

public static class SestavyGodot
{
    /// <summary>Sestava z <c>res://</c> — funguje i v exportované hře, kde soubory leží v .pck.
    /// Cesty vzorků se berou relativně od složky sestavy, jako na disku.</summary>
    public static PatchLoadResult Nacti(string resCesta)
    {
        var slozka = resCesta.GetBaseDir();
        return Pomocnici.NactiZBaliku(
            Godot.FileAccess.GetFileAsString(resCesta),
            cesta => Godot.FileAccess.GetFileAsBytes(slozka.PathJoin(cesta).SimplifyPath()));
    }
}
```

Ověřeno (čtení ze souborů jako bajty): výsledek je bitově shodný s načtením z disku a vzorek
sdílený dvěma sestavami se přečte jednou. Varianta pro Godot je jen přeložená. Druhá možnost je
nechat zvuky mimo `.pck`, ve složce vedle hry, a používat `Load`.

## 9. Ověření bez poslechu

Ticho se tu pozná jen měřením. Nic nehodí výjimku a sestava se normálně načte.

1. **Vzorky:** `Pomocnici.ChybejiciVzorky(sestava)` musí vrátit prázdný seznam.
2. **Zvuk:** `Pomocnici.VykresliCele` + `SpickaDb`. −180 dBFS je ticho. Nad 0 dBFS zvuk přetéká.
3. **Shoda s autorem:** zapiš `.wav` (3) a poslechni nebo porovnej s referenčním `.wav` od autora.
   Porovnávej celý klip (`VykresliCele`), protože tak zvuk vyrábí i autor. Hlas po blocích se může
   lišit (10, bod 5).
4. **Za běhu:** `ZvukovyProud.Podbehy` musí zůstat na nule. V Godotu si navíc měř špičku toho, co
   jde do `PushBuffer`.

Celé se to dá pustit headless jako automatický test: načíst všechny sestavy, ověřit vzorky,
vyrenderovat, hlídat špičku.

## 10. Tiché pasti

Žádná z nich nehodí výjimku. Program jen hraje špatně, nebo nehraje vůbec.

1. **Pole z `RenderBlock` je půjčené.** Další blok ho přepíše (5.2).
2. **Chybějící vzorek je ticho, ne výjimka** (8.3).
3. **Jednorázový zvuk puštěný po `Držení`** zazní jen jeden blok. Pouštěj po
   `Timing.DurationSeconds`. Hlas bez `Release` nikdy neskončí.
4. **`Deserialize` místo `Load`** nechá relativní cesty ke vzorkům hledat vůči pracovní složce
   procesu. Funguje, jen když program náhodou startuje ze správné složky.
5. **Hlas a celý klip nezní stejně.** Hlas posune semínka náhody (aby dva hlasy téhož zvuku nezněly
   bit po bitu stejně) a pouští notu jinak. Změřeno (RMS, celý klip → hlas): efekty o desetiny dB
   až 1,6 dB (laser −16,8 → −17,0, sklo −22,3 → −20,7). **Nástroje s Notou o jednotky dB**
   (flétna −11,0 → −8,3, bicí −24,0 → −19,7). Puštění po `Držení` to nesrovná. Shodu s autorem
   proto ověřuj celým klipem (9) a nástroj, který ve hře zní znatelně jinak, nahlas autorovi.
6. **Přetečení jedné sestavy** (6). Bez limiteru jde na kartu zkreslení.
7. **Nová sestava ve starší knihovně** zahraje jinak, nebo se nenačte (11).

## 11. Verze a kompatibilita

**Sestavy a verze knihovny patří k sobě.** Starší sestavy se v novější knihovně načtou. Opačně ne
vždy:

- **Sestava s modulem, který tvoje knihovna nezná, se nenačte:** `InvalidOperationException:
  Neznámý typ modulu v souboru: …`. To je hlasitá chyba a je v pořádku.
- **Sestava s knoflíkem, který tvoje knihovna nezná, se načte. Knoflík se tiše ignoruje** a zvuk
  zní jinak. Když takovou sestavu uložíš (`PatchSerializer.Save`/`Serialize`), knoflík se ztratí.
  Změřeno.

Příklad: verze 0.2.0 přidala modul Roj, takže 0.1.x sestavu s rojem odmítne. Verze 0.3.0 přidala
Roji knoflíky. 0.2.0 takovou sestavu načte, ale roj zahraje postaru. Verze 0.4.0 přidala moduly
Bubliny, Kolize, Trhliny, Tření, Harmonický hlas, Události a Banka módů (0.3.0 sestavu s nimi
odmítne) a Delay knoflík Režim mixu. Proto si u knihovny drž číslo
verze a od autora zvuků si nech říct, ve které vznikly.

Veřejné rozhraní nemá formálně slíbenou stabilitu. Při každé nové verzi si vyžádej seznam změn.

## 12. Kde to poběží

| kam | stav |
|---|---|
| Godot 4 .NET (`net8.0`) | ✅ běží v hotové hře (klient i editor modů) |
| konzole, služby, testy (`net8.0`) | ✅ všechny ukázky tady |
| WPF | ✅ (editor autora zvuků) |
| MonoGame, Avalonia | `net8.0` sedí, nikdo nezkoušel |
| **Unity** | ❌ Unity umí .NET Standard 2.1, `net8.0` knihovnu nenačte |
| Linux, macOS, ARM | knihovna nemá platformní závislost, takže by měla. **Nikdo nezkoušel** |
| program mimo .NET | ne přímo. Zbývá vyrenderovat `.wav` předem jiným .NET programem (3) |

## 13. Známá omezení

| co | dopad |
|---|---|
| Hlas a celý klip zní u nástrojů s Notou o jednotky dB jinak. Příčina se nehledala | Co autor slyší v editoru, se může lišit od hry (10, bod 5) |
| Chybějící vzorek z hlasu zjistit nejde | Kontrolu musí dělat program (8.3) |
| Žádný limiter, žádný mix, žádný prostor | Dělá je program nebo engine (5, 6) |
| Komprimované vzorky jen s dekodérem programu | 8.2 |
| Hledání knoflíku podle jména v knihovně není | `Pomocnici.Nastav` (7) |
| Balíček nemá XML dokumentaci | IntelliSense neukáže popisy. Popis je tenhle návod |
| Unity | nepodporováno (12) |

## 14. Přehled API

Všech 104 veřejných typů knihovny, rozdělených podle toho, k čemu jsou. Odkaz u typu vede do
**`API.md`**, který leží vedle tohoto souboru a je i v balíčku. Tam je u každého typu popis
a tabulka **všech** jeho členů (signatura a popis). V části 1 je navíc všech 39 modulů s porty
a knoflíky: jméno v sestavě, výchozí hodnota, rozsah, jednotka a jestli jde modulovat. `API.md`
je vygenerovaný z přeložené knihovny, takže v něm nic nechybí ani nepřebývá.

Běžnému programu stačí první skupina. Z druhé k ní patří `ModuleGraph`, `ModuleBase`,
`ModuleParameter` a `ModuleFile`. Skupiny „Vnitřek renderu" a „Pro editor" cizí program
nepotřebuje. Jsou veřejné jen proto, že je používá editor autora zvuků.

**Přehrávání a vzorky** (8)

| typ | jmenný prostor | druh | k čemu |
|---|---|---|---|
| [`AudioSampleCache`](API.md#typ-audiosamplecache) | Audio | statická třída | Rozbalené vzorky pro Sampler: mezipaměť souborů z disku a rejstřík vzorků vložených programem. |
| [`DecodedSample`](API.md#typ-decodedsample) | Audio | record | Rozbalený zvuk: mono vzorky a jejich vlastní kmitočet. |
| [`MidiNotes`](API.md#typ-midinotes) | Audio | statická třída | Převody MIDI čísel not: kmitočet a jméno. |
| [`Voice`](API.md#typ-voice) | Audio | třída | Jedna znějící nota jedné sestavy — hlas, který se renderuje po blocích. |
| [`WavDekoder`](API.md#typ-wavdekoder) | Audio | statická třída | Rozbalení souborů `.wav` na mono vzorky, bez jakékoli závislosti. |
| [`PatchLoadResult`](API.md#typ-patchloadresult) | Persistence | record | Výsledek načtení sestavy: graf, časování, pozice modulů a rámečky. |
| [`PatchSerializer`](API.md#typ-patchserializer) | Persistence | statická třída | Načítání a ukládání sestav `.zvuky` (JSON). |
| [`PatchTiming`](API.md#typ-patchtiming) | Persistence | třída | Délka jednorázového zvuku a tempo sestavy. |

**Sestava: graf, moduly, knoflíky, porty** (13)

| typ | jmenný prostor | druh | k čemu |
|---|---|---|---|
| [`Connection`](API.md#typ-connection) | Graph | třída | Drát v sestavě: z výstupního portu jednoho modulu do vstupního portu jiného. |
| [`ModuleBase`](API.md#typ-modulebase) | Graph | abstraktní třída | Základ každého modulu („krabičky“) v sestavě: porty, knoflíky a render jednoho bloku. |
| [`ModuleFile`](API.md#typ-modulefile) | Graph | třída | Pojmenovaná cesta k souboru, který modul čte — u Sampleru vzorek. |
| [`ModuleGraph`](API.md#typ-modulegraph) | Graph | třída | Sestava jako graf: moduly a dráty mezi nimi. Renderuje se v pořadí závislostí, bez zpětných vazeb. |
| [`ModuleGraph.CycleDetectedException`](API.md#typ-modulegraph-cycledetectedexception) | Graph | výjimka | Výjimka: sestava obsahuje cyklus (zpětnou vazbu drátem), který render neumí. |
| [`ModuleGraph.InvalidWiringException`](API.md#typ-modulegraph-invalidwiringexception) | Graph | výjimka | Výjimka: zapojení, které render odmítne (cyklus, drát z vnitřku roje ven mimo Konec roje, dva konce, roj v roji). |
| [`ModuleParameter`](API.md#typ-moduleparameter) | Graph | třída | Knoflík modulu: číslo v rozsahu, nebo výběr z voleb. |
| [`NoteDivision`](API.md#typ-notedivision) | Graph | record | Notová hodnota pro knoflík synchronizovaný s tempem („4 takty“ … „1/64“, s tečkou „1/4.“, triola „1/4T“), měřená v dobách. |
| [`Port`](API.md#typ-port) | Graph | třída | Port modulu: vstup nebo výstup, zvukový nebo křivkový. |
| [`PortDirection`](API.md#typ-portdirection) | Graph | výčet | Směr portu. |
| [`PortKind`](API.md#typ-portkind) | Graph | výčet | Co teče drátem: zvuk po vzorcích, nebo tvar (křivka). Různé druhy spojit nejde. |
| [`RenderContext`](API.md#typ-rendercontext) | Graph | třída | Parametry jednoho průchodu renderu: kmitočet, počet vzorků, tempo. |
| [`TempoSyncKind`](API.md#typ-temposynckind) | Graph | výčet | Co znamená notová hodnota knoflíku po převodu na číslo. |

**Křivky, melodie, text a formule** (12)

| typ | jmenný prostor | druh | k čemu |
|---|---|---|---|
| [`Curve`](API.md#typ-curve) | Graph | třída | Nakreslený tvar: body spojené Bézierovými úseky, funkce x → y na intervalu 0…1. |
| [`CurveHandle`](API.md#typ-curvehandle) | Graph | struktura | Jedna polovina tečny v bodě křivky: kam a jak daleko se křivka z bodu odklání. |
| [`CurveInput`](API.md#typ-curveinput) | Graph | třída | Zásuvka modulu pro křivku dodanou jiným modulem (drátem typu `PortKind.Curve`). |
| [`CurveOutput`](API.md#typ-curveoutput) | Graph | record | Co modul potřebuje od křivky: rozsah hodnot a jestli se musí potkat konce. |
| [`CurvePoint`](API.md#typ-curvepoint) | Graph | struktura | Jeden bod křivky a dvě tečny, které se v něm potkávají. |
| [`CurvePreset`](API.md#typ-curvepreset) | Graph | record | Pojmenovaný výchozí tvar, který editor nabízí (sinus, pila…). |
| [`CurvePresets`](API.md#typ-curvepresets) | Graph | statická třída | Seznam hotových tvarů pro křivky. |
| [`Formula`](API.md#typ-formula) | Graph | třída | Malý aritmetický jazyk modulu Formule: výraz se jednou přeloží a počítá na každém vzorku. |
| [`ModuleCurve`](API.md#typ-modulecurve) | Graph | třída | Pojmenovaná křivka, kterou modul sám nese — knoflík, jehož hodnotou je tvar. |
| [`ModuleSequence`](API.md#typ-modulesequence) | Graph | třída | Pojmenovaná melodie modulu — seznam not (`SequenceStep`). |
| [`ModulePreset`](API.md#typ-modulepreset) | Graph | záznam | Pojmenovaný výchozí bod knoflíků modulu (předvolba). |
| [`ModuleText`](API.md#typ-moduletext) | Graph | třída | Pojmenovaný text, který modul čte jako obsah — dnes jen výraz modulu Formule. |
| [`SequenceStep`](API.md#typ-sequencestep) | Graph | třída | Jedna nota melodie: výška, délka v krocích a jestli je to pomlka. |

**Katalog modulů a moduly** (44)

| typ | jmenný prostor | druh | k čemu |
|---|---|---|---|
| [`CollisionPopulationModule`](API.md#typ-collisionpopulationmodule) | Modules | třída | Modul „Kolize“ (kategorie Zdroje). |
| [`CompressorModule`](API.md#typ-compressormodule) | Modules | třída | Modul „Kompresor“ (kategorie Zpracování). |
| [`CrackCascadeModule`](API.md#typ-crackcascademodule) | Modules | třída | Modul „Trhliny“ (kategorie Zdroje). |
| [`CurveEnvelopeModule`](API.md#typ-curveenvelopemodule) | Modules | třída | Modul „Obálka z křivky“ (kategorie Modulace). |
| [`CurveModule`](API.md#typ-curvemodule) | Modules | třída | Modul „Křivka“ (kategorie Modulace). |
| [`DelayLineModule`](API.md#typ-delaylinemodule) | Modules | třída | Modul „Zpoždění“ (kategorie Zpracování). |
| [`DelayModule`](API.md#typ-delaymodule) | Modules | třída | Modul „Delay“ (kategorie Zpracování). |
| [`DistortionModule`](API.md#typ-distortionmodule) | Modules | třída | Modul „Distortion“ (kategorie Zpracování). |
| [`EnvelopeFollowerModule`](API.md#typ-envelopefollowermodule) | Modules | třída | Modul „Sledovač obálky“ (kategorie Modulace). |
| [`EnvelopeModule`](API.md#typ-envelopemodule) | Modules | třída | Modul „Envelope“ (kategorie Modulace). |
| [`EventSequenceModule`](API.md#typ-eventsequencemodule) | Modules | třída | Modul „Události“ (kategorie Modulace). |
| [`FilterModule`](API.md#typ-filtermodule) | Modules | třída | Modul „Filter“ (kategorie Zpracování). |
| [`FormulaModule`](API.md#typ-formulamodule) | Modules | třída | Modul „Formule“ (kategorie Zpracování). |
| [`GainModule`](API.md#typ-gainmodule) | Modules | třída | Modul „Gain“ (kategorie Zpracování). |
| [`GranulatorModule`](API.md#typ-granulatormodule) | Modules | třída | Modul „Granulátor“ (kategorie Zpracování). |
| [`HarmonicVoiceModule`](API.md#typ-harmonicvoicemodule) | Modules | třída | Modul „Harmonický hlas“ (kategorie Zdroje). |
| [`IPaletteEntry`](API.md#typ-ipaletteentry) | Modules | rozhraní | Cokoli, co umí paleta editoru ukázat: modul (`ModuleCatalogEntry`) nebo blok (`BlockCatalogEntry`). |
| [`InverterModule`](API.md#typ-invertermodule) | Modules | třída | Modul „Invertor“ (kategorie Modulace). |
| [`FrictionContactModule`](API.md#typ-frictioncontactmodule) | Modules | třída | Modul „Tření“ (kategorie Zdroje). |
| [`FluidFlowModule`](API.md#typ-fluidflowmodule) | Modules | třída | Modul „Bubliny“ (kategorie Zdroje). |
| [`KarplusStrongModule`](API.md#typ-karplusstrongmodule) | Modules | třída | Modul „Struna“ (kategorie Zdroje). |
| [`LfoModule`](API.md#typ-lfomodule) | Modules | třída | Modul „LFO“ (kategorie Modulace). |
| [`LinLogModule`](API.md#typ-linlogmodule) | Modules | třída | Modul „Převodník lin/log“ (kategorie Modulace). |
| [`MixerModule`](API.md#typ-mixermodule) | Modules | třída | Modul „Mixer“ (kategorie Zpracování). |
| [`ModalBankModule`](API.md#typ-modalbankmodule) | Modules | třída | Modul „Banka módů“ (kategorie Zpracování). |
| [`ModuleCatalog`](API.md#typ-modulecatalog) | Modules | statická třída | Seznam všech modulů, které knihovna zná — a tedy i těch, které umí načíst ze souboru. |
| [`ModuleCatalogEntry`](API.md#typ-modulecatalogentry) | Modules | record | Položka katalogu pro jeden modul: jméno v paletě, kategorie a jak ho vytvořit. |
| [`ModuleCategory`](API.md#typ-modulecategory) | Modules | record | Jedna kategorie palety a její položky. |
| [`NoiseModule`](API.md#typ-noisemodule) | Modules | třída | Modul „Noise“ (kategorie Zdroje). |
| [`NoteModule`](API.md#typ-notemodule) | Modules | třída | Modul „Nota (klávesnice)“ (kategorie Zdroje). |
| [`OscillatorModule`](API.md#typ-oscillatormodule) | Modules | třída | Modul „Oscillator“ (kategorie Zdroje). |
| [`OutputModule`](API.md#typ-outputmodule) | Modules | třída | Modul „Output“ (kategorie Výstup). |
| [`PoissonTriggerModule`](API.md#typ-poissontriggermodule) | Modules | třída | Modul „Nepravidelný spouštěč“ (kategorie Modulace). |
| [`PulseDividerModule`](API.md#typ-pulsedividermodule) | Modules | třída | Modul „Dělič impulsů“ (kategorie Modulace). |
| [`QuantizerModule`](API.md#typ-quantizermodule) | Modules | třída | Modul „Kvantizér“ (kategorie Modulace). |
| [`ResonatorModule`](API.md#typ-resonatormodule) | Modules | třída | Modul „Resonator“ (kategorie Zpracování). |
| [`ReverbModule`](API.md#typ-reverbmodule) | Modules | třída | Modul „Reverb“ (kategorie Zpracování). |
| [`RingModulatorModule`](API.md#typ-ringmodulatormodule) | Modules | třída | Modul „Ring Modulator“ (kategorie Zpracování). |
| [`SampleHoldModule`](API.md#typ-sampleholdmodule) | Modules | třída | Modul „Vzorkovač (S&H)“ (kategorie Modulace). |
| [`SamplerModule`](API.md#typ-samplermodule) | Modules | třída | Modul „Sampler“ (kategorie Zdroje). |
| [`ScopeModule`](API.md#typ-scopemodule) | Modules | třída | Modul „Osciloskop / Spektrum“ (kategorie Vizualizace). |
| [`SequencerModule`](API.md#typ-sequencermodule) | Modules | třída | Modul „Sekvencer“ (kategorie Zdroje). |
| [`SlewModule`](API.md#typ-slewmodule) | Modules | třída | Modul „Sklouznutí“ (kategorie Modulace). |
| [`SpectrogramModule`](API.md#typ-spectrogrammodule) | Modules | třída | Modul „Spektrogram“ (kategorie Vizualizace). |
| [`SpectrogramPalette`](API.md#typ-spectrogrampalette) | Modules | výčet | Barevná škála, kterou modul Spektrogram kreslí v editoru. Na zvuk nemá vliv. |
| [`SwarmEndModule`](API.md#typ-swarmendmodule) | Modules | třída | Modul „Konec roje“ (kategorie Zpracování). |
| [`SwarmModule`](API.md#typ-swarmmodule) | Modules | třída | Modul „Roj“ (kategorie Zpracování). |
| [`VuMeterModule`](API.md#typ-vumetermodule) | Modules | třída | Modul „VU Metr“ (kategorie Vizualizace). |
| [`WaveMorphModule`](API.md#typ-wavemorphmodule) | Modules | třída | Modul „Morfující oscilátor“ (kategorie Zdroje). |
| [`WaveshaperModule`](API.md#typ-waveshapermodule) | Modules | třída | Modul „Waveshaper“ (kategorie Zpracování). |
| [`XorOscillatorModule`](API.md#typ-xoroscillatormodule) | Modules | třída | Modul „XOR Oscillator“ (kategorie Zdroje). |

**Soubor .zvuky** (12)

| typ | jmenný prostor | druh | k čemu |
|---|---|---|---|
| [`ConnectionRecord`](API.md#typ-connectionrecord) | Persistence | třída | Jeden drát v souboru. |
| [`CurvePointRecord`](API.md#typ-curvepointrecord) | Persistence | třída | Jeden bod křivky v souboru. |
| [`CurveRecord`](API.md#typ-curverecord) | Persistence | třída | Jedna křivka modulu v souboru. |
| [`FileRecord`](API.md#typ-filerecord) | Persistence | třída | Jeden soubor modulu v souboru sestavy (vzorek Sampleru). |
| [`GroupPortRecord`](API.md#typ-groupportrecord) | Persistence | třída | Vývod na okraji rámečku: zástupce za vnitřní port modulu. |
| [`GroupRecord`](API.md#typ-grouprecord) | Persistence | třída | Rámeček na plátně editoru (titulek, poznámka, rozměr). Na zvuk nemá vliv. |
| [`ModuleRecord`](API.md#typ-modulerecord) | Persistence | třída | Jeden modul v souboru: typ, dovětek, pozice, knoflíky a nečíselný obsah. |
| [`ParameterRecord`](API.md#typ-parameterrecord) | Persistence | třída | Jeden knoflík v souboru. |
| [`PatchDocument`](API.md#typ-patchdocument) | Persistence | třída | Celý soubor `.zvuky` jako objekt: verze, časování, moduly, dráty a rámečky. |
| [`SequenceRecord`](API.md#typ-sequencerecord) | Persistence | třída | Jedna melodie v souboru. |
| [`StepRecord`](API.md#typ-steprecord) | Persistence | třída | Jeden krok melodie v souboru. |
| [`TextRecord`](API.md#typ-textrecord) | Persistence | třída | Jeden text modulu v souboru (výraz Formule). |

**Bloky (předpřipravená zapojení)** (6)

| typ | jmenný prostor | druh | k čemu |
|---|---|---|---|
| [`BlockBuilder`](API.md#typ-blockbuilder) | Modules | třída | Pomocník pro psaní definice bloku: sbírá moduly, dráty a vývody. |
| [`BlockCatalog`](API.md#typ-blockcatalog) | Modules | statická třída | Bloky, které editor nabízí v paletě. Seznam s popisy je v `API.md`, část 2. |
| [`BlockCatalogEntry`](API.md#typ-blockcatalogentry) | Modules | record | Položka palety pro jeden blok: jméno, popis a jak ho postavit. |
| [`BlockDefinition`](API.md#typ-blockdefinition) | Modules | třída | Hotové zapojení bloku: moduly s pozicemi, dráty mezi nimi a vývody na okraji. |
| [`BlockModule`](API.md#typ-blockmodule) | Modules | record | Modul uvnitř bloku a jeho pozice vůči levému hornímu rohu bloku. |
| [`BlockPort`](API.md#typ-blockport) | Modules | record | Vývod na okraji bloku a vnitřní port, za který stojí. |

**Vnitřek renderu (běžný program nepotřebuje)** (4)

| typ | jmenný prostor | druh | k čemu |
|---|---|---|---|
| [`BlockScratch`](API.md#typ-blockscratch) | Graph | třída | Půjčovna bufferů jednoho hlasu: pole pro jeden průchod renderu, používaná znovu blok za blokem. |
| [`CurveMorphLadder`](API.md#typ-curvemorphladder) | Graph | třída | Předpočítaná řada tvarů mezi dvěma křivkami, aby šlo přechod číst po vzorcích. |
| [`Formula.Slot`](API.md#typ-formula-slot) | Graph | statická třída | Indexy proměnných v `Formula.Slots`. |
| [`SeededRandomSource`](API.md#typ-seededrandomsource) | Graph | třída | Zdroj náhody svázaný s knoflíkem `Seed`, aby náhodné moduly hrály pokaždé stejně. |

**Pro editor (cizí program je nepotřebuje)** (5)

| typ | jmenný prostor | druh | k čemu |
|---|---|---|---|
| [`LevelMeterHub`](API.md#typ-levelmeterhub) | Audio | statická třída | Most mezi moduly VU metr a ukazatelem úrovně v editoru. |
| [`LevelSnapshot`](API.md#typ-levelsnapshot) | Audio | struktura | Jedno čtení VU metru: vyhlazená špička a RMS, obojí zhruba 0…1. |
| [`VisualizationHub`](API.md#typ-visualizationhub) | Audio | statická třída | Most mezi moduly Osciloskop / Spektrum, Spektrogram a Morfující oscilátor a jejich náhledem v editoru. |
| [`VisualizationSnapshot`](API.md#typ-visualizationsnapshot) | Audio | record | Jedno zveřejněné okno náhledu: vzorky z osciloskopu nebo spektrogramu. |
| [`TempoDisplay`](API.md#typ-tempodisplay) | Graph | třída | Tempo, které editor ukazuje u knoflíků na notové hodnotě. Na render nemá vliv. |

## 15. Licence

Plné znění je v souboru `LICENSE` (anglicky a česky, při pochybnosti platí česká verze). Je to
licence MIT, ke které přibyla jedna podmínka: **sdílení sestav**. Shrnutí, které licenci nenahrazuje:

| | |
|---|---|
| **smíš** | knihovnu užívat, upravovat, šířit i prodávat, v uzavřeném i komerčním programu, jako u MIT |
| **musíš** | v kopiích knihovny ponechat oznámení o autorském právu a licenci, jako u MIT |
| **a navíc** | každou sestavu, kterou použiješ ve **zveřejněném díle**, nahrát do knihovny sestav na <https://www.pekelec.eu/zvuky/knihovna.html>, **nejpozději do 30 dnů** od zveřejnění díla |

- **Dílo** je cokoli, co dáš k dispozici veřejnosti nebo komukoli dalšímu, zdarma i za peníze:
  hra, aplikace, video, nahrávka, balík zvuků, stream. Počítá se i zvuk, který ze sestavy
  knihovna vykreslila předem do `.wav` — nejen sestava přibalená k programu.
- **Nahrává se verze, kterou dílo používá.** Vzorky, které sestava čte (modul Sampler), přilož,
  smíš-li je sdílet. Nesmíš-li, napiš do popisu, odkud jsou.
- Nahranou sestavu tím poskytuješ všem pod **CC BY 4.0**: kdokoli ji smí použít i komerčně,
  když uvede autora. Vzorky si nechávají vlastní licenci.
- **Nahrávat nemusíš:** pokusy a soukromé užití, sestavy stažené z knihovny a použité beze změny
  a ukázkové sestavy, které přišly s knihovnou, beze změny. Upravíš-li je, nahraj svou verzi.
- **Odesláním je povinnost splněná.** Když knihovna nahranou sestavu odmítne, neporušil jsi nic.
  Když knihovna nepřijímá déle než 30 dnů, stačí sestavu zveřejnit pod CC BY 4.0 kdekoli veřejně
  a to místo uvést v dokumentaci nebo titulcích díla.

Licence se nejmenuje „MIT", protože čistá MIT o povinnosti sdílet mlčí. GitHub i NuGet ji proto
ukazují jako vlastní licenci ze souboru, ne jako MIT.
