namespace ZvukEfekty.Core.Audio;

/// <summary>Audio decoded from a file, ready to play: mono samples plus the rate they were recorded at.</summary>
/// <param name="Samples">The whole file, mono. Multi-channel input is averaged down — see
/// <see cref="AudioSampleCache"/> on why that is a downmix and not a limitation of the format.</param>
/// <param name="SampleRate">The file's own rate, which is usually NOT the render rate; whoever plays
/// this is responsible for stepping through it at the right speed.</param>
/// <param name="Channels">How many channels the source had, before the downmix.</param>
public sealed record DecodedSample(float[] Samples, int SampleRate, int Channels)
{
    public double DurationSeconds => SampleRate <= 0 ? 0 : Samples.Length / (double)SampleRate;
}

/// <summary>
/// Decoded audio files, kept so the same file is decoded once rather than once per key press.
///
/// This exists because of how a live voice works: every note-on clones the whole patch through the
/// serializer, so a sampler in that patch is a NEW module instance for every key. Without a cache
/// shared outside those instances, holding a chord would decode the same WAV once per note, and
/// every repeated press would decode it again.
///
/// Entries are keyed by path together with the file's timestamp and length, so replacing the file
/// on disk is picked up rather than serving stale audio from before the edit. That check is not
/// airtight — a file rewritten to the SAME length within the filesystem's timestamp resolution
/// would keep its key and keep playing the old audio. Hashing the contents would close it, but
/// only by reading the whole file, which is the cost this cache exists to avoid; the timestamp is
/// the usual trade and the failure needs a rewrite faster than the clock can see.
///
/// <para><b>Decoding never belongs on the audio thread</b> — reading and converting a file takes far
/// longer than a block's budget. <see cref="Graph.ModuleBase.Prepare"/> is the hook that pulls it
/// onto a thread that can afford it; a miss inside Process would be a dropout.</para>
///
/// <para><b>Vzorky z paměti</b> (od 2026-09-19) jsou tu proto, že cizí program nemusí mít zvuk
/// v souboru: vyrobí si ho, rozbalí z vlastního balíku nebo ho dostane po síti, a ukládat ho na
/// disk jen kvůli tomu, aby na něj Sampler mohl ukázat cestou, je oklika přes disk i přes
/// úklid. <see cref="VlozVzorek(string, DecodedSample)"/> ho vloží pod jménem a Sampler na něj
/// ukáže jako na <c>pamet:jméno</c>.</para>
///
/// <para>⚠️ <b>Proč jménem, a ne odkazem přímo v modulu.</b> Živý hlas si celou sestavu klonuje
/// přes serializér (<c>Audio.Voice.ClonePatch</c>), takže <b>každá nota je nová instance modulu</b>
/// postavená z JSONu. Pole s vzorky pověšené na instanci by klon nepřežilo a druhá nota by hrála
/// ticho. Jméno v <c>Path</c> projde JSONem beze změny a klon si vzorek vyzvedne tady — to je celý
/// důvod, proč paměťová cesta vypadá jako cesta k souboru.</para>
/// </summary>
public static class AudioSampleCache
{
    /// <summary>
    /// Ceiling on what is held, counted in samples rather than entries — one long recording costs
    /// what many short ones do. About 26.5 million is ten minutes of 44.1 kHz mono, roughly 106 MB,
    /// which is far more than a patch full of one-shots needs and still bounded.
    /// </summary>
    private const long MaxCachedSamples = 26_460_000;

    /// <summary>Předpona, kterou se v <c>Path</c> Sampleru pozná vzorek z paměti od cesty k souboru.
    /// Windows za rootovanou cestu <c>pamet:jméno</c> nepovažuje (dvojtečka není na druhém znaku),
    /// takže se o ni musí vědět všude, kde se relativní cesta dopočítává — viz
    /// <c>Persistence.PatchSerializer.Deserialize</c>.</summary>
    public const string PrefixPameti = "pamet:";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, DecodedSample> Entries = new();

    /// <summary>
    /// Vzorky vložené programem, klíčované jménem bez předpony.
    ///
    /// <para>⚠️ <b>Schválně mimo <see cref="Entries"/> a mimo <see cref="Usage"/>.</b> Souborová
    /// cache je zahoditelná — co vypadne, se při dalším hraní načte z disku znovu. Paměťový vzorek
    /// druhý zdroj nemá, takže kdyby ho vystrnadil strop <see cref="MaxCachedSamples"/>, sestava by
    /// po dlouhém renderu zmlkla a nic by to nenahlásilo. Kolik paměti to zabere, ví ten, kdo vzorky
    /// vkládá; úklid je na něm (<see cref="OdeberVzorek"/>, <see cref="VyprazdniPamet"/>).</para>
    /// </summary>
    private static readonly Dictionary<string, DecodedSample> Pamet = new();

    // Least-recently-used first, so eviction drops what has gone longest without being played.
    private static readonly LinkedList<string> Usage = new();
    private static long _cachedSamples;

    /// <summary>
    /// The decoded contents of <paramref name="path"/>, decoding it if this is the first ask.
    /// Returns null and explains why in <paramref name="error"/> rather than throwing: a patch can
    /// legitimately be saved with no file chosen yet, or be opened on a machine where the file has
    /// moved, and neither should take the whole render down.
    /// </summary>
    public static DecodedSample? Get(string path, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (JeVPameti(path))
        {
            var jmeno = path[PrefixPameti.Length..];
            lock (Gate)
            {
                if (Pamet.TryGetValue(jmeno, out var vlozeny))
                    return vlozeny;
            }

            // Hláška míří na program, ne na uživatele: sestava uloženou paměťovou cestu nese dál,
            // ale vzorek do paměti musí vložit ten, kdo ji pouští, a musí to stihnout dřív, než se
            // moduly připraví. Ticho by tady byla ta nejhorší odpověď.
            error = $"Vzorek „{jmeno}\" není v paměti. Program, který sestavu pouští, ho tam musí "
                + "vložit (AudioSampleCache.VlozVzorek) dřív, než se sestava připraví.";
            return null;
        }

        string key;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                error = "Soubor neexistuje.";
                return null;
            }

            key = $"{info.FullName}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
        }
        catch (Exception ex)
        {
            error = $"K souboru se nedá dostat: {ex.Message}";
            return null;
        }

        lock (Gate)
        {
            if (Entries.TryGetValue(key, out var cached))
            {
                Touch(key);
                return cached;
            }
        }

        DecodedSample decoded;
        try
        {
            decoded = Decode(path);
        }
        catch (Exception ex)
        {
            error = $"Soubor se nepodařilo načíst: {ex.Message}";
            return null;
        }

        if (decoded.Samples.Length == 0)
        {
            error = "Soubor neobsahuje žádný zvuk.";
            return null;
        }

        lock (Gate)
        {
            // Another thread may have decoded the same file while this one was working. Keep
            // whichever landed first, so every caller ends up holding the same array.
            if (Entries.TryGetValue(key, out var raced))
            {
                Touch(key);
                return raced;
            }

            Entries[key] = decoded;
            Usage.AddLast(key);
            _cachedSamples += decoded.Samples.Length;
            EvictWhileOver();
        }

        return decoded;
    }

    /// <summary>Empties the cache. For tests and for freeing memory; playing again re-decodes.
    /// <b>Vzorků vložených do paměti se nedotýká</b> — ty se znovu načíst nedají, na ně je
    /// <see cref="VyprazdniPamet"/>.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
            Usage.Clear();
            _cachedSamples = 0;
        }
    }

    public static (int Entries, long Samples) Stats()
    {
        lock (Gate)
            return (Entries.Count, _cachedSamples);
    }

    /// <summary>Jestli tahle cesta ukazuje do paměti, a ne na disk. Rozhoduje o tom předpona
    /// <see cref="PrefixPameti"/>, ne obsah rejstříku — cesta do paměti zůstává cestou do paměti,
    /// i když tam vzorek zrovna není vložený, jinak by se z chybějícího vzorku stal chybějící
    /// soubor a hláška by ukazovala jinam, než kde je chyba.</summary>
    public static bool JeVPameti(string? path)
        => path is not null && path.StartsWith(PrefixPameti, StringComparison.Ordinal);

    /// <summary>
    /// Vloží hotové vzorky pod jménem a vrátí cestu, kterou se na ně Sampler odkáže
    /// (<c>pamet:jméno</c>) — tu stačí zapsat do <c>Soubor</c> v modulu, nebo rovnou do sestavy.
    ///
    /// <para>Vzorky musí být <b>mono</b> a <paramref name="vzorek"/> nese jejich vlastní kmitočet,
    /// ne kmitočet renderu; Sampler si krok dopočítá sám. Stejné jméno podruhé vzorek přepíše
    /// a moduly si nový vyzvednou při nejbližší přípravě — pole se nikdy nepřepisuje na místě,
    /// takže si render, který zrovna běží, dohraje ten svůj.</para>
    /// </summary>
    /// <returns>Cesta s předponou, k zapsání do <c>ModuleFile.Path</c>.</returns>
    public static string VlozVzorek(string jmeno, DecodedSample vzorek)
    {
        ArgumentNullException.ThrowIfNull(vzorek);

        if (string.IsNullOrWhiteSpace(jmeno))
            throw new ArgumentException("Vzorek v paměti musí mít jméno, odkazuje se na něj jím.", nameof(jmeno));

        // Jméno s předponou by se po zapsání do Path složilo na „pamet:pamet:x" a nenašlo se.
        if (JeVPameti(jmeno))
            throw new ArgumentException($"Jméno vzorku nesmí začínat na „{PrefixPameti}\"; předponu přidá tahle metoda.", nameof(jmeno));

        if (vzorek.Samples.Length == 0)
            throw new ArgumentException("Vzorek neobsahuje žádný zvuk.", nameof(vzorek));

        if (vzorek.SampleRate <= 0)
            throw new ArgumentException($"Nesmyslný vzorkovací kmitočet {vzorek.SampleRate} Hz.", nameof(vzorek));

        lock (Gate)
            Pamet[jmeno] = vzorek;

        return PrefixPameti + jmeno;
    }

    /// <summary>Totéž pro vzorky, které volající drží jako pole — ušetří psaní
    /// <see cref="DecodedSample"/>.</summary>
    /// <param name="vzorky">Mono, v rozsahu ±1. Pole se <b>nekopíruje</b>: kdo ho po vložení
    /// přepíše, přepíše ho i hrajícímu Sampleru.</param>
    public static string VlozVzorek(string jmeno, float[] vzorky, int kmitocet)
        => VlozVzorek(jmeno, new DecodedSample(vzorky ?? throw new ArgumentNullException(nameof(vzorky)), kmitocet, 1));

    /// <summary>
    /// Vloží obsah souboru <c>.wav</c> držený v paměti — rozbalí ho
    /// <see cref="WavDekoder"/>, tedy stejný kód jako u souboru na disku, a případné stereo se
    /// smíchá do mona.
    ///
    /// <para>⚠️ <b>Rozbaluje se tady a hned</b>, ne až při hraní: je to práce na desítky milisekund
    /// a v <c>Process</c> by to byl výpadek. Volat mimo zvukové vlákno.</para>
    ///
    /// <para><b>Komprimované formáty tudy nejdou.</b> <see cref="Dekoder"/> je šev na <i>cestu
    /// k souboru</i>, ne na bajty, takže <c>.mp3</c> v paměti tahle metoda odmítne srozumitelnou
    /// hláškou. Kdo ho má, rozbalí si ho po svém a výsledek vloží
    /// <see cref="VlozVzorek(string, float[], int)"/>.</para>
    /// </summary>
    /// <exception cref="InvalidDataException">Data nejsou WAV, nebo je to tvar, který neumíme.</exception>
    public static string VlozWav(string jmeno, byte[] wav)
        => VlozVzorek(jmeno, WavDekoder.Dekoduj(wav ?? throw new ArgumentNullException(nameof(wav))));

    /// <summary>Vyhodí vzorek z paměti. Sestava, která na něj ukazuje, začne od příští přípravy
    /// hlásit, že vzorek chybí — mlčky hrát nezačne.</summary>
    /// <returns>Jestli tam vůbec byl.</returns>
    public static bool OdeberVzorek(string jmeno)
    {
        lock (Gate)
            return Pamet.Remove(jmeno);
    }

    /// <summary>Vyprázdní rejstřík vzorků v paměti. Oddělené od <see cref="Clear"/> schválně:
    /// souborová cache se dá zahodit kdykoli, tahle ne — co se odsud vyhodí, není odkud vzít.</summary>
    public static void VyprazdniPamet()
    {
        lock (Gate)
            Pamet.Clear();
    }

    /// <summary>Kolik vzorků je vložených a kolik zabírají, aby se dalo hlídat, co drží cizí program.
    /// Souborová cache se počítá zvlášť ve <see cref="Stats"/>.</summary>
    public static (int Pocet, long Vzorku) StatsPameti()
    {
        lock (Gate)
        {
            long celkem = 0;
            foreach (var vzorek in Pamet.Values)
                celkem += vzorek.Samples.Length;

            return (Pamet.Count, celkem);
        }
    }

    /// <summary>
    /// Čím se soubor promění ve vzorky, když to není <c>.wav</c>. ⚠️ <b>Tohle je šev mezi syntézou
    /// a zvukovou vrstvou</b>, a od 2026-09-19 vede jen ke komprimovaným formátům: <c>.wav</c>
    /// rozbalí <see cref="WavDekoder"/> tady v Core, bez jakékoli závislosti, a má přednost i když
    /// je registrovaný dekodér k dispozici. Sampler nad nahrávkou tedy funguje i tam, kde se
    /// nepřehrává — a to hra potřebuje.
    ///
    /// <para>Komprimované formáty registruje <c>NAudioDekoder.Zaregistruj()</c>, což dělá přehrávač
    /// sám. Kdo si přilinkuje jen tuhle knihovnu a dá Sampleru <c>.mp3</c>, dostane srozumitelnou
    /// hlášku místo ticha — a ticho je přesně to selhání, které se jinak pozná až posloucháním.</para>
    /// </summary>
    public static Func<string, DecodedSample>? Dekoder { get; set; }

    private static DecodedSample Decode(string path)
    {
        // WAV si Core rozbalí samo a bez čehokoli navíc (od 2026-09-19), takže Sampler nad
        // nahrávkou funguje i tam, kde se nepřehrává. Registrovaný dekodér tedy dostane jen to,
        // co WavDekoder neumí — komprimované formáty. Je to schválně JEDNA cesta na formát: dokud
        // .wav chodilo dvěma podle toho, co je přilinkované, mohly se tiše rozejít.
        if (Dekoder is null || WavDekoder.Umi(path))
            return WavDekoder.Dekoduj(path);

        return Dekoder(path);
    }

    private static void Touch(string key)
    {
        Usage.Remove(key);
        Usage.AddLast(key);
    }

    private static void EvictWhileOver()
    {
        while (_cachedSamples > MaxCachedSamples && Usage.First is { } oldest)
        {
            if (Entries.Remove(oldest.Value, out var dropped))
                _cachedSamples -= dropped.Samples.Length;

            Usage.RemoveFirst();
        }
    }
}
