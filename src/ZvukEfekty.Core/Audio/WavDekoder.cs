namespace ZvukEfekty.Core.Audio;

/// <summary>
/// RIFF/WAVE straight to mono samples, with no dependency on anything at all.
///
/// <para><b>Why this exists in Core.</b> The Sampler is not an edge case — a real game patch needs
/// recorded material — and until 2026-09-19 the only decoder lived in <c>ZvukEfekty.Prehravac</c>
/// behind NAudio, so a Sampler patch could not be rendered anywhere the player was not linked.
/// A WAV file is a header and a block of samples, and writing one is already done here
/// (<c>WavExporter</c>), so reading one needs nothing external either. <b>Compressed formats are
/// a different matter</b>: MP3 and friends stay with the player, because a decoder for those is
/// thousands of lines nobody here should own.</para>
///
/// <para><b>The output contract belongs to the seam, not to this file</b> — see
/// <see cref="AudioSampleCache.Dekoder"/>: mono samples at the FILE own rate, never the render one,
/// and several channels are AVERAGED rather than summed. Summing two correlated channels would come
/// out 6 dB louder than the source and clip material that was fine.</para>
/// </summary>
public static class WavDekoder
{
    private const ushort TagPcm = 1;
    private const ushort TagFloat = 3;
    private const ushort TagExtensible = 0xFFFE;

    /// <summary>Whether this decoder is the one for <paramref name="path"/>, judged by extension.
    /// A decoder gets picked before anything is opened, so there is nothing else to judge by — and
    /// a file that lies about its name fails in <see cref="Dekoduj"/> with its own message.</summary>
    public static bool Umi(string path)
    {
        var pripona = Path.GetExtension(path);
        return pripona.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || pripona.Equals(".wave", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>File to mono samples at the file own sample rate.</summary>
    /// <exception cref="InvalidDataException">Not a WAVE file, or a shape this does not decode. The
    /// message says which, because it reaches the user through <see cref="AudioSampleCache.Get"/>.
    /// </exception>
    public static DecodedSample Dekoduj(string path)
    {
        using var stream = File.OpenRead(path);
        return Dekoduj(stream);
    }

    /// <summary>
    /// Obsah souboru <c>.wav</c> držený v paměti → mono vzorky. Pro program, který si zvuk vyrobí
    /// nebo rozbalí sám a nechce ho kvůli Sampleru ukládat na disk — viz
    /// <see cref="AudioSampleCache.VlozWav"/>.
    /// </summary>
    /// <remarks>Rozbaluje to <b>úplně stejný kód</b> jako cestu ze souboru, ne druhou implementaci.
    /// Dva rozbalovače téhož formátu se tiše rozejdou — na to ukazuje celá poznámka nahoře
    /// o tom, proč <c>.wav</c> vede jen jednou cestou.</remarks>
    public static DecodedSample Dekoduj(byte[] data)
        => Dekoduj(new MemoryStream(data ?? throw new ArgumentNullException(nameof(data)), writable: false));

    /// <summary>
    /// Rozbal z otevřeného proudu. ⚠️ <b>Proud musí umět <see cref="Stream.Length"/> a posuv</b> —
    /// úseky RIFF se přeskakují nastavením pozice a délka souboru je to jediné, čím se dá ověřit,
    /// že hlavička nelže o velikosti úseku. Souborový proud i <see cref="MemoryStream"/> to umí,
    /// síťový obvykle ne.
    /// </summary>
    /// <remarks>Proud se nezavírá — patří volajícímu.</remarks>
    public static DecodedSample Dekoduj(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        if (!JeZnacka(reader, "RIFF"))
            // Nemluvit o „souboru": tudy chodí i zvuk z paměti a tam je tohle nejpravděpodobněji
            // někdo, kdo poslal komprimovaná data. Ta ať se poznají z hlášky, ne až z ticha.
            throw new InvalidDataException("Zvuk nezačíná na RIFF, není to WAV. Komprimované formáty "
                + "(MP3 a spol.) tudy nejdou — rozbal si je a vlož hotové vzorky.");

        reader.ReadUInt32();                                  // velikost, které se nevěří
        if (!JeZnacka(reader, "WAVE"))
            throw new InvalidDataException("RIFF kontejner není typu WAVE.");

        ushort tag = 0, kanalu = 0, bitu = 0;
        var kmitocet = 0;
        var mameFormat = false;

        // Projít úseky, ne spoléhat na to, že `fmt ` a `data` leží hned za sebou: mezi nimi bývá
        // `LIST` s popisem, `fact`, a u našich vlastních exportů i `INFO` s tempem a notou.
        while (stream.Position + 8 <= stream.Length)
        {
            var id = reader.ReadBytes(4);
            var velikost = reader.ReadUInt32();
            var zbyva = stream.Length - stream.Position;

            // Hlavičce se nevěří dvakrát: velikost větší než zbytek souboru znamená usekaný soubor
            // (typicky přerušené kopírování), a ten je pořád použitelný.
            var delka = velikost > (ulong)zbyva ? zbyva : velikost;

            if (Je(id, "fmt "))
            {
                if (delka < 16)
                    throw new InvalidDataException("Úsek fmt je kratší než 16 bajtů.");

                tag = reader.ReadUInt16();
                kanalu = reader.ReadUInt16();
                kmitocet = (int)reader.ReadUInt32();
                reader.ReadUInt32();                          // byte rate, dopočitatelné
                reader.ReadUInt16();                          // block align, dtto
                bitu = reader.ReadUInt16();

                // WAVE_FORMAT_EXTENSIBLE nese skutečný formát v GUIDu podformátu a jeho první dva
                // bajty jsou právě ta značka. Bez tohohle by 24bit soubor z běžného editoru vyšel
                // jako „formát číslo 65534, ten neumím".
                if (tag == TagExtensible && delka >= 40)
                {
                    reader.ReadUInt16();                      // cbSize
                    reader.ReadUInt16();                      // platných bitů
                    reader.ReadUInt32();                      // maska kanálů
                    tag = reader.ReadUInt16();                // GUID: první dva bajty = značka
                    Preskoc(stream, delka - 26);
                }
                else
                {
                    Preskoc(stream, delka - 16);
                }

                mameFormat = true;
                continue;
            }

            if (Je(id, "data"))
            {
                if (!mameFormat)
                    throw new InvalidDataException("Úsek data přišel dřív než fmt.");

                return Vzorky(reader, tag, kanalu, bitu, kmitocet, delka);
            }

            Preskoc(stream, delka + (delka % 2));              // RIFF zarovnává úseky na dva bajty
        }

        throw new InvalidDataException(mameFormat
            ? "Soubor neobsahuje úsek data."
            : "Soubor neobsahuje úsek fmt.");
    }

    private static DecodedSample Vzorky(BinaryReader reader, ushort tag, ushort kanalu, ushort bitu, int kmitocet, long delka)
    {
        if (kanalu == 0)
            throw new InvalidDataException("Soubor hlásí nula kanálů.");
        if (kmitocet <= 0)
            throw new InvalidDataException($"Nesmyslný vzorkovací kmitočet {kmitocet} Hz.");
        if (bitu == 0 || bitu % 8 != 0)
            throw new InvalidDataException($"{bitu} bitů na vzorek; umím jen celé bajty.");

        var bajtuNaVzorek = bitu / 8;
        var bajtuNaRamec = bajtuNaVzorek * kanalu;
        var ramcu = (int)(delka / bajtuNaRamec);
        var mono = new float[ramcu];
        var ramec = new byte[bajtuNaRamec];

        for (var i = 0; i < ramcu; i++)
        {
            if (reader.Read(ramec, 0, bajtuNaRamec) < bajtuNaRamec)
            {
                // Vrátit, co v souboru doopravdy je, ne spadnout na posledním rámci.
                Array.Resize(ref mono, i);
                break;
            }

            var soucet = 0f;
            for (var k = 0; k < kanalu; k++)
                soucet += Vzorek(ramec, k * bajtuNaVzorek, tag, bitu);

            mono[i] = soucet / kanalu;
        }

        return new DecodedSample(mono, kmitocet, kanalu);
    }

    /// <summary>
    /// One sample to float. ⚠️ <b>The divisors here are not free choices</b> — they are the ones
    /// NAudio uses, so the same file comes out bit for bit identical whether this decoder unpacks it
    /// or the one in the player does. Eight-bit PCM is the only UNSIGNED case, centred on 128.
    /// </summary>
    private static float Vzorek(byte[] ramec, int od, ushort tag, ushort bitu)
    {
        if (tag == TagFloat)
            return bitu switch
            {
                32 => BitConverter.ToSingle(ramec, od),
                64 => (float)BitConverter.ToDouble(ramec, od),
                _ => throw new InvalidDataException($"IEEE float o {bitu} bitech neumím."),
            };

        if (tag != TagPcm)
            throw new InvalidDataException($"Formát WAV číslo {tag} neumím; umím PCM a IEEE float. "
                + "Komprimované formáty (MP3 a spol.) rozbalí až ZvukEfekty.Prehravac.");

        return bitu switch
        {
            8 => (ramec[od] - 128) / 128f,
            16 => BitConverter.ToInt16(ramec, od) / 32768f,
            24 => (ramec[od] | (ramec[od + 1] << 8) | ((sbyte)ramec[od + 2] << 16)) / 8388608f,
            32 => BitConverter.ToInt32(ramec, od) / 2147483648f,
            _ => throw new InvalidDataException($"PCM o {bitu} bitech neumím."),
        };
    }

    private static bool JeZnacka(BinaryReader reader, string znacka) => Je(reader.ReadBytes(4), znacka);

    private static bool Je(byte[] id, string znacka)
    {
        if (id.Length != 4)
            return false;

        for (var i = 0; i < 4; i++)
            if (id[i] != (byte)znacka[i])
                return false;

        return true;
    }

    private static void Preskoc(Stream stream, long o)
    {
        if (o > 0)
            stream.Position = Math.Min(stream.Position + o, stream.Length);
    }
}
