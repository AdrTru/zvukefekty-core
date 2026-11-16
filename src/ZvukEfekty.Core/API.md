# ZvukEfekty.Core — reference API 0.4.0

Úplný popis veřejného rozhraní knihovny: všechny moduly s porty a knoflíky, všechny veřejné typy a všechny jejich
veřejné členy. Vygenerováno z přeložené knihovny, takže tu není nic, co knihovna nemá, a nechybí nic, co má.
Jak knihovnu použít, říká `README.md`; tohle je příručka k nahlížení.

- **Část 1 — Moduly:** co který modul dělá, jak se jmenuje v sestavě, jeho vstupy, výstupy a knoflíky s rozsahy.
- **Část 2 — Bloky:** předpřipravená zapojení modulů z palety editoru.
- **Část 3 — Typy:** všechny veřejné typy po jmenných prostorech, každý s popisem a tabulkou členů.

Značky u typů říkají, k čemu typ je: **Přehrávání a vzorky**, **Sestava: graf, moduly, knoflíky, porty**, **Křivky, melodie, text a formule**, **Katalog modulů a moduly**, **Soubor .zvuky**, **Bloky (předpřipravená zapojení)**, **Vnitřek renderu (běžný program nepotřebuje)**, **Pro editor (cizí program je nepotřebuje)**.
Signatury jsou v C#; `?` znamená, že hodnota smí být `null`.

## 1. Moduly

**Jméno v sestavě** je to, co v sestavě stojí před dovětkem autora (`ModuleBase.TypeName`): modul „Filter otvírání“ je
typu Filter s dovětkem „otvírání“. **Knoflík s drátem** (sloupec Mod.) má vlastní vstup stejného jména; signál, který do
něj vede, se přičte k hodnotě knoflíku vynásobený hloubkou (`ModulationAmount`). **Nota** znamená, že knoflík jde
přepnout na notovou hodnotu (osmina, takt…) a pak se počítá z tempa sestavy. Výchozí hodnoty jsou ty, se kterými
modul vznikne z palety; v konkrétní sestavě je autor obvykle změnil.

| kategorie | modul | jméno v sestavě | typ C# | k čemu |
|---|---|---|---|---|
| Zdroje | [Nota (klávesnice)](#modul-notemodule) | `Nota` | `NoteModule` | Zdroj výšky tónu a gate signálu. |
| Zdroje | [Sekvencer](#modul-sequencermodule) | `Sekvencer` | `SequencerModule` | Přehraje zapsanou melodii: výstup Hz zapoj na Frequency oscilátoru (základ 0, hloubka 1) a výstup Gate na Gate obálky. |
| Zdroje | [Oscillator](#modul-oscillatormodule) | `Oscillator` | `OscillatorModule` | Generátor periodického průběhu (Sine/Square/Saw/Triangle/Pulse/Random) s nastavitelnou frekvencí a hlasitostí — základní zdroj tónu. |
| Zdroje | [Morfující oscilátor](#modul-wavemorphmodule) | `Morfující oscilátor` | `WaveMorphModule` | Oscilátor, jehož vlna je plynulý přechod mezi dvěma nakreslenými tvary. |
| Zdroje | [XOR Oscillator](#modul-xoroscillatormodule) | `XOR Oscillator` | `XorOscillatorModule` | Tři čtvercové oscilátory kombinované bitovým XOR místo mixování — hustý, drsný, prakticky neperiodický zvuk (klasický "digital chaos" generátor pro cimbál/šum), ale pořád laditelný. |
| Zdroje | [Noise](#modul-noisemodule) | `Noise` | `NoiseModule` | Generátor šumu v sedmi barvách; barva říká, jak je energie rozložená po spektru. |
| Zdroje | [Bubliny](#modul-fluidflowmodule) | `Bubliny` | `FluidFlowModule` | Populace bublin ve vodě (van den Doel / Turchet) — šplouchnutí, klokotání, kaluž, vír. |
| Zdroje | [Kolize](#modul-collisionpopulationmodule) | `Kolize` | `CollisionPopulationModule` | Populace drobných tvrdých kolizí — štěrk pod nohou, kamínky v dlani, křupání, chrastítko. |
| Zdroje | [Trhliny](#modul-crackcascademodule) | `Trhliny` | `CrackCascadeModule` | Kaskáda trhlin — zamrzání ledu, štípání dřeva, praskání glazury, lom kamene. |
| Zdroje | [Harmonický hlas](#modul-harmonicvoicemodule) | `Harmonický hlas` | `HarmonicVoiceModule` | Harmonický zdroj hlasu pro zvířata a nestvůry — vytí, vrčení, výkřik, sténání. |
| Zdroje | [Tření](#modul-frictioncontactmodule) | `Tření` | `FrictionContactModule` | Tření se zadíráním — kámen tažený po kameni, pant dveří, dřevo po dřevě, skřípění. |
| Zdroje | [Struna](#modul-karplusstrongmodule) | `Struna` | `KarplusStrongModule` | Drnkaná struna (Karplus–Strong): zpožďovací linka dlouhá jednu periodu, zapojená sama do sebe přes tlumení výšek. |
| Zdroje | [Sampler](#modul-samplermodule) | `Sampler` | `SamplerModule` | Přehraje zvukový soubor jako zdroj zvuku. |
| Modulace | [LFO](#modul-lfomodule) | `LFO` | `LfoModule` | Nízkofrekvenční oscilátor pro modulaci jiných parametrů v čase (tremolo, vibrato apod.) — není určen k přímému poslechu. |
| Modulace | [Envelope](#modul-envelopemodule) | `Envelope` | `EnvelopeModule` | Klasická ADSR obálka hlasitosti. |
| Modulace | [Obálka z křivky](#modul-curveenvelopemodule) | `Obálka z křivky` | `CurveEnvelopeModule` | Obálka hlasitosti nakreslená jako křivka místo čtyř čísel. |
| Modulace | [Křivka](#modul-curvemodule) | `Křivka` | `CurveModule` | Nakreslí tvar a půjčí ho zbytku sestavy. |
| Modulace | [Události](#modul-eventsequencemodule) | `Události` | `EventSequenceModule` | Vypálí události v přesně zapsaných časech — dopad střepu, série přeskoků, kroky. |
| Modulace | [Nepravidelný spouštěč](#modul-poissontriggermodule) | `Nepravidelný spouštěč` | `PoissonTriggerModule` | Impulsy s náhodnými rozestupy (Poissonův proces) — praskání ohně, kapky, jiskření, skřípání. |
| Modulace | [Vzorkovač (S&H)](#modul-sampleholdmodule) | `Vzorkovač` | `SampleHoldModule` | Přečte hodnotu na vstupu ve chvíli, kdy dorazí impuls na Trig, a drží ji až do dalšího impulsu — ze spojitého signálu udělá schody. |
| Modulace | [Sklouznutí](#modul-slewmodule) | `Sklouznutí` | `SlewModule` | Zaobluje skoky v signálu — místo okamžitého přeskoku se hodnota k nové plynule dojede. |
| Modulace | [Sledovač obálky](#modul-envelopefollowermodule) | `Sledovač obálky` | `EnvelopeFollowerModule` | Změří hlasitost signálu a vydá ji jako řídicí signál — zvuk tak může řídit sám sebe: auto-wah (hlasitější úder otevře filtr), ducking (bicí ztlumí podklad), dynamické zkreslení. |
| Modulace | [Invertor](#modul-invertermodule) | `Invertor` | `InverterModule` | Překlopí signál kolem nastavené hladiny: co je nad ní, jde o stejný kus pod ni, a co je přesně na ní, zůstane na místě. |
| Modulace | [Převodník lin/log](#modul-linlogmodule) | `Převodník` | `LinLogModule` | Ohne řídicí signál mezi „rovnoměrně v hodnotě“ (Lin) a „rovnoměrně v tom, co slyšíš“ (Log). |
| Modulace | [Dělič impulsů](#modul-pulsedividermodule) | `Dělič impulsů` | `PulseDividerModule` | Počítá impulsy na vstupu Trig a pustí dál každý N-tý — z jedněch hodin udělá pomalejší, které se s nimi nemůžou rozejít. |
| Modulace | [Kvantizér](#modul-quantizermodule) | `Kvantizér` | `QuantizerModule` | Zaokrouhlí řídicí signál na nejbližší tón stupnice a vydá ho jako Hz — zapoj na Frequency oscilátoru se základem 0 a hloubkou 1, stejně jako Sekvencer. |
| Zpracování | [Gain](#modul-gainmodule) | `Gain` | `GainModule` | Škáluje hlasitost procházejícího signálu — jednoduché zesílení/zeslabení. |
| Zpracování | [Mixer](#modul-mixermodule) | `Mixer` | `MixerModule` | Sloučí až čtyři vstupní signály do jednoho — volitelně součtem, průměrem nebo násobením. |
| Zpracování | [Filter](#modul-filtermodule) | `Filter` | `FilterModule` | Filtr se stavovým prostorem s laditelným mezním kmitočtem (Cutoff) a rezonancí. |
| Zpracování | [Resonator](#modul-resonatormodule) | `Resonator` | `ResonatorModule` | Modální rezonátor — úzkopásmový "zvonící" filtr laděný na jednu frekvenci s nastavitelnou dobou doznívání (Decay). |
| Zpracování | [Banka módů](#modul-modalbankmodule) | `Banka módů` | `ModalBankModule` | Banka modálních rezonátorů se společným vstupem — rozeznělé těleso v jedné krabičce místo sítě Resonatorů a Mixerů. |
| Zpracování | [Delay](#modul-delaymodule) | `Delay` | `DelayModule` | Zpětnovazební zpožďovací linka (echo) s nastavitelným časem, zpětnou vazbou (Feedback) a mixem. |
| Zpracování | [Zpoždění](#modul-delaylinemodule) | `Zpoždění` | `DelayLineModule` | Čisté zpoždění signálu — žádný suchý signál navíc. |
| Zpracování | [Reverb](#modul-reverbmodule) | `Reverb` | `ReverbModule` | Dozvuk (Schroeder): osm paralelních hřebenových filtrů s tlumením a za nimi čtyři all-passy. |
| Zpracování | [Distortion](#modul-distortionmodule) | `Distortion` | `DistortionModule` | Waveshaping zkreslení — tvrdé/měkké oříznutí (Clip) nebo bitcrush (snížení bitové hloubky). |
| Zpracování | [Waveshaper](#modul-waveshapermodule) | `Waveshaper` | `WaveshaperModule` | Zkreslení podle nakreslené převodní křivky — pro každou vstupní úroveň říká křivka výstupní. |
| Zpracování | [Ring Modulator](#modul-ringmodulatormodule) | `Ring Modulator` | `RingModulatorModule` | Násobí dva signály vzorek po vzorku — klasický kovový/robotický zvuk. |
| Zpracování | [Granulátor](#modul-granulatormodule) | `Granulátor` | `GranulatorModule` | Rozřeže signál na vstupu na krátká zrna a přehraje je překrytá přes sebe. |
| Zpracování | [Kompresor](#modul-compressormodule) | `Kompresor` | `CompressorModule` | Srovná hlasitost: co je nad Prahem, ztlumí v poměru, který nastavíš — 4:1 znamená, že z každých 4 dB nad prahem projde 1 dB. |
| Zpracování | [Roj](#modul-swarmmodule) | `Roj` | `SwarmModule` | Otevírá roj: všechno, co je zapojené za ním až po Konec roje, se při každém impulsu na Trig zahraje jako nová kopie a kopie se sečtou — shluk partiklů z jedné řady krabiček. |
| Zpracování | [Konec roje](#modul-swarmendmodule) | `Konec roje` | `SwarmEndModule` | Uzavírá roj: na vstup dostane všechny kopie roje sečtené, každou pod její obálkou, a pošle je dál. |
| Zpracování | [Formule](#modul-formulamodule) | `Formule` | `FormulaModule` | Spočítá na každý vzorek to, co je napsané ve Formuli. |
| Vizualizace | [Osciloskop / Spektrum](#modul-scopemodule) | `Scope` | `ScopeModule` | Průchozí monitor — zobrazuje procházející signál jako osciloskop (tvar vlny) nebo spektrální analyzátor (FFT), podle zvoleného Mode. |
| Vizualizace | [VU Metr](#modul-vumetermodule) | `VU Metr` | `VuMeterModule` | Průchozí monitor hlasitosti — ukazatel špičky (Peak) a RMS úrovně procházejícího signálu. |
| Vizualizace | [Spektrogram](#modul-spectrogrammodule) | `Spektrogram` | `SpectrogramModule` | Průchozí monitor, který kreslí spektrogram — čas vodorovně, frekvence svisle, jas podle hlasitosti. |
| Výstup | [Output](#modul-outputmodule) | `Output` | `OutputModule` | Koncový bod sestavy — signál na jeho vstupu je to, co se přehraje nebo exportuje. |

<a id="modul-notemodule"></a>
### Nota (klávesnice)

*Zdroje · jméno v sestavě `Nota` · typ `NoteModule`*

Zdroj výšky tónu a gate signálu. Výstup Hz napojte na Frequency oscilátoru, výstup Gate do vstupu Gate na Envelope. Parametr Klávesnice omezuje, na které klávesy modul reaguje. Držení platí jen pro Přehrát/Export — říká, jak dlouho zůstane klávesa pomyslně stisknutá, než se pustí a nechá dozvuk doběhnout; při hraní na klávesnici ho určuje skutečný stisk. Platí i pro obálku bez zapojeného Gate a i pro sestavu, která obálku vůbec nemá — tón se v obou případech ukončí po Držení, ne až na konci klipu. Vlastní Držení na obálce má přednost.

- **Vstupy:** žádné
- **Výstupy:** `Hz`, `Gate`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Octave` | 0 | -3 – 3 |  | ano |  |  |
| `Klávesnice` | Celá | Celá, Dolní řada, Horní řada |  |  |  |  |
| `Držení` | 0.5 | 0 – 10 | s |  | ano |  |

<a id="modul-sequencermodule"></a>
### Sekvencer

*Zdroje · jméno v sestavě `Sekvencer` · typ `SequencerModule`*

Přehraje zapsanou melodii: výstup Hz zapoj na Frequency oscilátoru (základ 0, hloubka 1) a výstup Gate na Gate obálky. Výška se zapisuje jako nota, délka jako počet kroků, takže transpozice je přičtení a změna tempa je jeden knoflík. Artikulace říká, jakou část tónu drží gate: pod 1 se každý tón spustí znovu (a mezi nimi je mezera), na 1 gate nezavře vůbec a tóny jdou legato. Přeladí se vždy na hranici kroku, kde je obálka na nule, takže přejezd výšky slyšet není. Délka kroku se dá navázat na tempo.

Zapojený Clock přebírá čas: na každý impuls se postoupí o krok a Délka kroku se pro krokování přestane číst. Tím jde sekvenci hnát z Děliče impulsů, z Nepravidelného spouštěče nebo z Trig na Sledovači obálky — tedy nepravidelně, se swingem nebo od zvuku. Artikulace ale pořád potřebuje vědět, jak dlouhý krok je: Délka kroku podle = Hodin ji odvodí z mezery mezi posledními dvěma impulsy (tón se tedy zkracuje s tempem), = Knoflíku ji vezme z Délky kroku (každý tón je pak stejně dlouhý, ať impulsy chodí jakkoli). Reset vrátí sekvenci na začátek; s hodinami ji rozehraje až další impuls.

- **Vstupy:** `Clock`, `Reset`
- **Výstupy:** `Hz`, `Gate`
- **Melodie:** `Kroky` (seznam not, `ModuleSequence`)

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Délka kroku` | 0.25 | 0.02 – 4 | s | ano | ano |  |
| `Délka kroku podle` | Hodin | Hodin, Knoflíku |  |  |  |  |
| `Artikulace` | 0.85 | 0.05 – 1 |  | ano |  |  |
| `Oktáva` | 0 | -3 – 3 |  | ano |  |  |
| `Opakovat` | Ne | Ne, Ano |  |  |  |  |

<a id="modul-oscillatormodule"></a>
### Oscillator

*Zdroje · jméno v sestavě `Oscillator` · typ `OscillatorModule`*

Generátor periodického průběhu (Sine/Square/Saw/Triangle/Pulse/Random) s nastavitelnou frekvencí a hlasitostí — základní zdroj tónu. Vyhlazení hran potlačí aliasování u Square, Saw a Pulse: bez něj se harmonické nad polovinou vzorkovací frekvence překlopí zpátky dolů na neharmonické frekvence a tón je nahoře drsný. Trojúhelník a sinus to nepotřebují. Výchozí je vypnuto, aby starší sestavy zněly jako dřív.

- **Vstupy:** `Vlastní vlna` (křivka)
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Waveform` | Sine | Sine, Square, Saw, Triangle, Pulse, Random, Custom |  |  |  |  |
| `Vyhlazení hran` | Ne | Ne, Ano |  |  |  |  |
| `Frequency` | 440 | 20 – 5000 | Hz | ano |  |  |
| `Amplitude` | 0.8 | 0 – 1 |  | ano |  |  |
| `Seed` | náhodné | 0 – 1000000 |  |  |  | semínko náhody |

<a id="modul-wavemorphmodule"></a>
### Morfující oscilátor

*Zdroje · jméno v sestavě `Morfující oscilátor` · typ `WaveMorphModule`*

Oscilátor, jehož vlna je plynulý přechod mezi dvěma nakreslenými tvary. Vlna A a Vlna B jsou ty dva konce (nezapojené: sinus a obdélník), Přechod říká, jak se mezi nimi jede (osa X = průběh, osa Y = 0 je A, 1 je B). Kde v přechodu zrovna jsme, určuje Pozice plus postup za Délka sekund; Délka 0 přechod zastaví, takže pozicí pak hýbe jen modulace Pozice. Obě vlny se čtou na stejné fázi, proto to zní jako jedna měnící se vlna, ne jako dva tóny přes sebe. Morf říká, JAK se ty dva tvary míchají: Vektory stěhují zlomové body a táhla jednoho tvaru na druhý, takže hrana vzniká až ke konci přejezdu a narůstá postupně; Vzorky sčítají obě křivky vzorek po vzorku, takže hrana je výsledku hned od začátku, jen tišší — to je ten ostrší, skokovější nástup.

- **Vstupy:** `Vlna A` (křivka), `Vlna B` (křivka), `Přechod` (křivka)
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Frequency` | 440 | 20 – 5000 | Hz | ano |  |  |
| `Amplitude` | 0.8 | 0 – 1 |  | ano |  |  |
| `Pozice` | 0 | 0 – 1 |  | ano |  |  |
| `Délka` | 1 | 0 – 60 | s | ano | ano |  |
| `Opakovat` | Ne | Ne, Ano |  |  |  |  |
| `Morf` | Vektory | Vektory, Vzorky |  |  |  |  |

<a id="modul-xoroscillatormodule"></a>
### XOR Oscillator

*Zdroje · jméno v sestavě `XOR Oscillator` · typ `XorOscillatorModule`*

Tři čtvercové oscilátory kombinované bitovým XOR místo mixování — hustý, drsný, prakticky neperiodický zvuk (klasický "digital chaos" generátor pro cimbál/šum), ale pořád laditelný.

- **Vstupy:** žádné
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Frequency 1` | 220 | 20 – 5000 | Hz | ano |  |  |
| `Frequency 2` | 311 | 20 – 5000 | Hz | ano |  |  |
| `Frequency 3` | 417 | 20 – 5000 | Hz | ano |  |  |
| `Amplitude` | 0.8 | 0 – 1 |  | ano |  |  |

<a id="modul-noisemodule"></a>
### Noise

*Zdroje · jméno v sestavě `Noise` · typ `NoiseModule`*

Generátor šumu v sedmi barvách; barva říká, jak je energie rozložená po spektru. Bílý má všude stejně — sykot, dech, praskání. Růžový ubývá o 3 dB na oktávu, což ucho slyší jako vyrovnaný šum — vítr, moře, déšť, vzdálený dav. Hnědý ubývá o 6 dB, takže zbude skoro jen spodek — hřmění, rachot, dunění. Modrý naopak o 3 dB přibývá a fialový o 6: tenký sykot bez těla, vzduch v místnosti, dither, plíšek činelu. Šedý je tvarovaný tak, aby zněl stejně hlasitě ve VŠECH pásmech — ucho slyší nejlíp kolem 3 kHz, takže tam má nejmíň a na okrajích víc; je to ten nejméně únavný šum na dlouhé plochy a na maskování. Černý ubývá o 12 dB na oktávu, takže ho skoro neuslyšíte: 81 % energie je pod 137 Hz. Je na rachot pod něčím jiným — a hlavně jako řídicí signál, který se sám pomalu plazí, což jinak neumí nic bez LFO a jeho pravidelnosti. Barevné šumy jsou při stejné Amplitude tišší než bílý: mají stejný strop, ale bílý se o něj opírá pořád, kdežto ty ostatní ho jen občas dosáhnou. Seed určuje konkrétní podobu šumu – stejný seed dá při každém vykreslení stejný zvuk, tlačítkem 🎲 vylosujete jinou variantu.

- **Vstupy:** žádné
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Barva` | Bílý | Bílý, Růžový, Hnědý, Modrý, Fialový, Šedý, Černý |  |  |  |  |
| `Amplitude` | 0.5 | 0 – 1 |  | ano |  |  |
| `Seed` | náhodné | 0 – 1000000 |  |  |  | semínko náhody |

<a id="modul-karplusstrongmodule"></a>
### Struna

*Zdroje · jméno v sestavě `Struna` · typ `KarplusStrongModule`*

Drnkaná struna (Karplus–Strong): zpožďovací linka dlouhá jednu periodu, zapojená sama do sebe přes tlumení výšek. Nezapojená se sama drnkne na začátku každé noty — stačí pustit Nota.Hz do Frequency (základ 0, hloubka 1) a je to hotový nástroj. Decay je doba poklesu základního tónu o 60 dB, Tlumení říká, oč rychleji nad ním doznívají vyšší harmonické (0 = kovový hřeben bez tlumení, 1 = měkká struna). Vysoké tóny dozní dřív, než Decay slibuje — utlumená struna prostě tak dlouho nevydrží. Do Trig se dá drnknout znovu, do In pustit vlastní buzení (šum přes obálku, úder, cokoli) místo vnitřního šumu. Na výstupu je jen struna, bez suchého signálu — hřebenový filtr z ní uděláte smícháním se vstupem v Mixeru.

- **Vstupy:** `In`, `Trig`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Frequency` | 220 | 20 – 5000 | Hz | ano |  |  |
| `Decay` | 2 | 0.05 – 10 | s | ano | ano |  |
| `Tlumení` | 0.5 | 0 – 1 |  | ano |  |  |
| `Gain` | 1 | 0 – 4 |  | ano |  |  |
| `Seed` | náhodné | 0 – 1000000 |  |  |  | semínko náhody |

<a id="modul-fluidflowmodule"></a>
### Bubliny

*Zdroje · jméno v sestavě `Bubliny` · typ `FluidFlowModule`*

Populace bublin ve vodě (van den Doel / Turchet) — šplouchnutí, klokotání, kaluž, vír. Každá bublina zvoní podle svého poloměru: malá vysoko a krátce, velká hluboko, dlouho a hlasitě, a během doznívání trochu stoupá (Stoupání). Aktivita 0–1 řídí, kolik bublin vzniká (Četnost × Aktivita za sekundu) a do 0,25 i jak silné jsou; 0 = žádné nové. Poloměr, buzení, útlum a stoupání se čtou v okamžiku zrodu bubliny. Úroveň 1 odpovídá přijatému šplouchnutí V8 — za modul dej low-pass kolem 6 kHz jako tam. Trig = jedna bublina hned na náběžnou hranu, První bublina Hned = jedna hned na začátku; obě v plné síle bez ohledu na Aktivitu. Jedna samotná bublina: Četnost 0 a První bublina Hned.

- **Vstupy:** `Trig`, `Aktivita`, `Četnost`, `Poloměr min`, `Poloměr max`, `Exponent poloměru`, `Buzení min`, `Buzení max`, `Exponent buzení`, `Útlum`, `Stoupání`, `Úroveň` (modulace)
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Aktivita` | 0.5 | 0 – 1 |  | ano |  |  |
| `Četnost` | 350 | 0 – 2000 | 1/s | ano |  |  |
| `Poloměr min` | 0.45 | 0.2 – 20 | mm | ano |  |  |
| `Poloměr max` | 4.5 | 0.2 – 20 | mm | ano |  |  |
| `Exponent poloměru` | 2.2 | 0 – 5 |  | ano |  |  |
| `Buzení min` | 0.01 | 0 – 1 |  | ano |  |  |
| `Buzení max` | 0.1 | 0 – 1 |  | ano |  |  |
| `Exponent buzení` | 2 | 0 – 5 |  | ano |  |  |
| `Útlum` | 1 | 0.1 – 10 | × | ano |  |  |
| `Stoupání` | 0.1 | 0 – 0.3 |  | ano |  |  |
| `Úroveň` | 1 | 0 – 2 |  | ano |  |  |
| `Strop` | 32 | 1 – 128 |  |  |  |  |
| `Plný strop` | Ukrást nejslabší | Ukrást nejslabší, Zahodit bublinu |  |  |  |  |
| `První bublina` | Náhodně | Náhodně, Hned |  |  |  |  |
| `Seed` | náhodné | 0 – 1000000 |  |  |  | semínko náhody |

<a id="modul-collisionpopulationmodule"></a>
### Kolize

*Zdroje · jméno v sestavě `Kolize` · typ `CollisionPopulationModule`*

Populace drobných tvrdých kolizí — štěrk pod nohou, kamínky v dlani, křupání, chrastítko. Energie se plní Aktivitou (plynule, sem patří křivka zatížení) a rázem na Trig (o Energii) a uniká s Doznívání energie; kolizí je Četnost × energie za sekundu. Každá kolize je jiný kámen: výška náhodně mezi Výška min a max, větší (nižší) kámen je hlasitější a zní déle. Zní krátkým tónem (Doznění) a šumovým klikem (Podíl šumu); Tvrdost zkracuje klik. Bez Aktivity i Trig je ticho.

- **Vstupy:** `Trig`, a modulace všech číselných knoflíků kromě `Strop`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Aktivita` | 0.5 | 0 – 1 |  | ano |  |  |
| `Energie` | 0.5 | 0 – 1 |  | ano |  |  |
| `Doznívání energie` | 0.3 | 0.02 – 5 | s | ano |  |  |
| `Četnost` | 80 | 0 – 2000 | 1/s | ano |  |  |
| `Výška min` | 1200 | 100 – 12000 | Hz | ano |  |  |
| `Výška max` | 5000 | 100 – 12000 | Hz | ano |  |  |
| `Doznění` | 0.015 | 0.001 – 0.5 | s | ano |  |  |
| `Tvrdost` | 0.5 | 0 – 1 |  | ano |  |  |
| `Podíl šumu` | 0.4 | 0 – 1 |  | ano |  |  |
| `Variabilita` | 0.5 | 0 – 1 |  | ano |  |  |
| `Úroveň` | 1 | 0 – 2 |  | ano |  |  |
| `Strop` | 32 | 1 – 128 |  |  |  |  |
| `Plný strop` | Ukrást nejslabší | Ukrást nejslabší, Zahodit kolizi |  |  |  |  |
| `Seed` | náhodné | 0 – 1000000 |  |  |  | semínko náhody |

<a id="modul-crackcascademodule"></a>
### Trhliny

*Zdroje · jméno v sestavě `Trhliny` · typ `CrackCascadeModule`*

Kaskáda trhlin — zamrzání ledu, štípání dřeva, praskání glazury, lom kamene. Trhlina vznikne na Trig (o Síle) nebo sama z Napětí × Četnost kořenů; po náhodném zpoždění (Zpoždění min–max) z ní s pravděpodobností Větvení vyrazí až Max dětí menších trhlin, které dohromady nesou nejvýš Poměr energie rodiče, a tak dál do Max hloubky. Menší trhlina zní výš, tišeji a kratší dobu. Každá zní tónem (Výška ÷ velikost, Doznění) a šumovým klikem (Podíl šumu). Impuls a Trig hlásí každou trhlinu ven — pro Banku módů nebo Resonator. Bez Trig a Napětí je ticho.

- **Vstupy:** `Trig`, a modulace číselných knoflíků kromě `Max dětí` a `Max hloubka`
- **Výstupy:** `Out`, `Impuls`, `Trig`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Síla` | 1 | 0 – 1 |  | ano |  |  |
| `Napětí` | 0 | 0 – 1 |  | ano |  |  |
| `Četnost kořenů` | 5 | 0 – 200 | 1/s | ano |  |  |
| `Větvení` | 0.6 | 0 – 0.95 |  | ano |  |  |
| `Max dětí` | 2 | 0 – 4 |  |  |  |  |
| `Max hloubka` | 3 | 0 – 8 |  |  |  |  |
| `Zpoždění min` | 3 | 0.1 – 500 | ms | ano |  |  |
| `Zpoždění max` | 60 | 0.1 – 500 | ms | ano |  |  |
| `Poměr energie` | 0.5 | 0 – 1 |  | ano |  |  |
| `Variabilita` | 0.3 | 0 – 1 |  | ano |  |  |
| `Výška` | 3000 | 100 – 12000 | Hz | ano |  |  |
| `Doznění` | 0.022 | 0.001 – 0.5 | s | ano |  |  |
| `Podíl šumu` | 0.3 | 0 – 1 |  | ano |  |  |
| `Úroveň` | 1 | 0 – 2 |  | ano |  |  |
| `Seed` | náhodné | 0 – 1000000 |  |  |  | semínko náhody |

<a id="modul-harmonicvoicemodule"></a>
### Harmonický hlas

*Zdroje · jméno v sestavě `Harmonický hlas` · typ `HarmonicVoiceModule`*

Harmonický zdroj hlasu pro zvířata a nestvůry — vytí, vrčení, výkřik, sténání. Dává jen buzení (jako hlasivky); barvu a samohlásku mu dá až filtr nebo rezonátory za ním. Frekvence je základní tón (sem Nota.Hz nebo křivka), Intenzita hlasitost (0 = ticho), Sklon jak rychle slábnou vyšší harmonické (−6 ostrý, −18 měkký). Subharmonické ÷2/÷3 s Hloubkou sub přidají drsné zdvojení periody — vrčení, řev. Jitter rozkolísá délku period, Shimmer jejich hlasitost, Dech přimíchá šum výdechu. Nic nepřesáhne 0,45 vzorkovací frekvence.

- **Vstupy:** modulace číselných knoflíků (`Frekvence`, `Intenzita`, `Sklon`, `Hloubka sub`, `Jitter`, `Shimmer`, `Dech`)
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Frekvence` | 120 | 20 – 4000 | Hz | ano |  |  |
| `Intenzita` | 0.5 | 0 – 1 |  | ano |  |  |
| `Sklon` | -12 | -30 – -3 | dB/okt | ano |  |  |
| `Subharmonické` | Vypnuto | Vypnuto, ÷2, ÷3 |  |  |  |  |
| `Hloubka sub` | 0 | 0 – 1 |  | ano |  |  |
| `Jitter` | 0 | 0 – 5 | % | ano |  |  |
| `Shimmer` | 0 | 0 – 30 | % | ano |  |  |
| `Dech` | 0 | 0 – 1 |  | ano |  |  |
| `Harmonických` | 64 | 32, 64, 128 |  |  |  |  |
| `Seed` | náhodné | 0 – 1000000 |  |  |  | semínko náhody |

<a id="modul-frictioncontactmodule"></a>
### Tření

*Zdroje · jméno v sestavě `Tření` · typ `FrictionContactModule`*

Tření se zadíráním — kámen tažený po kameni, pant dveří, dřevo po dřevě, skřípění. Těleso táhne pružina (ruka) Rychlostí, Přítlak ho tlačí k podkladu; kontakt střídá zadření a prokluz podle Statického a Dynamického tření (Přechod tření = jak rychle se mezi nimi přechází). Drsnost povrchu rozkmitá přítlak podle ujeté dráhy, takže rychlejší tažení zní rychleji samo; Zrnitost mění velikost zrn. Třecí síla rozezní Módy podkladu a Módy tělesa (řádky Hz, T60, hmotnost, vyzařování). Výstup Síla je třecí síla ÷ přítlak — buzení pro Banku módů. Počet kontaktů = kolik drobných výstupků se dotýká; každý se zadírá sám, víc kontaktů = jemnější, rozsypané zrnění místo úderů. Model je fyzikální a chaotický: malá změna knoflíku může změnit charakter, a tření se umí chytit na mód a zakvílet — proto je Měkký ořez (pod 0,8 beze změny, strop 0,94) zapnutý.

- **Vstupy:** modulace číselných knoflíků
- **Výstupy:** `Out`, `Síla`
- **Texty:** `Módy podkladu`, `Módy tělesa` — jeden mód na řádek: frekvence [Hz], T60 [s], nepovinně hmotnost [kg] (1) a vyzařování (1); nejvýš 16 módů.

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Rychlost` | 0.15 | -0.5 – 0.5 | m/s | ano |  |  |
| `Přítlak` | 20 | 0 – 100 | N | ano |  |  |
| `Statické tření` | 0.7 | 0 – 2 |  | ano |  |  |
| `Dynamické tření` | 0.45 | 0 – 2 |  | ano |  |  |
| `Přechod tření` | 0.05 | 0.001 – 0.5 | m/s | ano |  |  |
| `Tuhost kontaktu` | 5.3 | 3 – 7 | log N/m | ano |  |  |
| `Drsnost` | 0.5 | 0 – 1 |  | ano |  |  |
| `Zrnitost` | 1 | 0.1 – 10 | × | ano |  |  |
| `Hmotnost tělesa` | 1.2 | 0.05 – 10 | kg | ano |  |  |
| `Pružnost ruky` | 3.6 | 2 – 5 | log N/m | ano |  |  |
| `Úroveň` | 1 | 0 – 4 |  | ano |  |  |
| `Měkký ořez` | Ano | Ne, Ano |  |  |  |  |
| `Počet kontaktů` | 12 | 1 – 32 |  |  |  |  |
| `Seed` | náhodné | 0 – 1000000 |  |  |  | semínko náhody (profil povrchu) |

<a id="modul-samplermodule"></a>
### Sampler

*Zdroje · jméno v sestavě `Sampler` · typ `SamplerModule`*

Přehraje zvukový soubor jako zdroj zvuku. Speed mění rychlost i výšku zároveň (jako u pásku), Start posune začátek přehrávání. Bez zapojeného Gate hraje od začátku vykreslení, se zapojeným se spustí znovu při každém stisku klávesy. Stereo soubor se smíchá do mona. Vstup Hz z modulu Nota rozehraje vzorek hudebně: řekni v Základní notě, na jaké výšce nahrávka je, a modul dopočítá rychlost sám. Speed pak zůstává jako doladění, 1 = přesná výška. Záporná rychlost čte soubor pozpátku, 0 zastaví čtení na místě (pozor, to je konstantní hodnota = stejnosměrná složka — utlum ji obálkou). Pozpátku se hraje od místa daného Startem, takže pro přehrání celého souboru odzadu dej Start na 1; ve smyčce se přetečení přes začátek obtočí na konec souboru samo. Místo cesty k souboru může stát pamet:jméno — to je zvuk, který do paměti vložil program, co sestavu pouští; v aplikaci samotné takový vzorek nevzniká.

- **Vstupy:** `Gate`, `Hz`, `Průběh čtení` (křivka)
- **Výstupy:** `Out`
- **Soubor:** `Soubor` (cesta k vzorku, nebo `pamet:jméno`)

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Speed` | 1 | -4 – 4 |  | ano |  |  |
| `Start` | 0 | 0 – 1 |  | ano |  |  |
| `Amplitude` | 1 | 0 – 1 |  | ano |  |  |
| `Loop` | Ne | Ne, Ano |  |  |  |  |
| `Základní nota` | C4 | C-1 … G9 (128 voleb, hodnota = index) |  |  |  |  |

<a id="modul-lfomodule"></a>
### LFO

*Modulace · jméno v sestavě `LFO` · typ `LfoModule`*

Nízkofrekvenční oscilátor pro modulaci jiných parametrů v čase (tremolo, vibrato apod.) — není určen k přímému poslechu. Zapojte výstup do vstupu modulace jiného modulu.

- **Vstupy:** `Vlastní vlna` (křivka)
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Waveform` | Sine | Sine, Square, Saw, Triangle, Pulse, Random, Custom |  |  |  |  |
| `Frequency` | 2 | 0.05 – 20 | Hz | ano | ano |  |
| `Amplitude` | 1 | 0 – 1 |  | ano |  |  |
| `Seed` | náhodné | 0 – 1000000 |  |  |  | semínko náhody |

<a id="modul-envelopemodule"></a>
### Envelope

*Modulace · jméno v sestavě `Envelope` · typ `EnvelopeModule`*

Klasická ADSR obálka hlasitosti. Sustain je HLADINA (0-1); jak dlouho se ta hladina drží, říká Držení. Držení na nule znamená: převezmi ho z modulu Nota, a když v sestavě žádný není, roztáhni plošinku přes zbytek vykreslení a uvolni Release před jeho koncem. Se zapojeným Gate se Držení neuplatní — tam délku tónu určuje klávesa, a totéž platí při hraní na klávesnici i bez toho drátu. Výstup Env nese syrovou křivku 0-1 pro modulaci jiných parametrů.

- **Vstupy:** `In`, `Gate`
- **Výstupy:** `Out`, `Env`, `A`, `D`, `S`, `R`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Attack` | 0.02 | 0 – 10 | s | ano | ano |  |
| `Decay` | 0.1 | 0 – 10 | s | ano | ano |  |
| `Sustain` | 0.7 | 0 – 1 |  | ano |  |  |
| `Držení` | 0 | 0 – 10 | s |  | ano |  |
| `Release` | 0.3 | 0 – 10 | s | ano | ano |  |

<a id="modul-curveenvelopemodule"></a>
### Obálka z křivky

*Modulace · jméno v sestavě `Obálka z křivky` · typ `CurveEnvelopeModule`*

Obálka hlasitosti nakreslená jako křivka místo čtyř čísel. Bod sustainu dělí křivku na dvě části: co je před ním, se přehraje za Nástup sekund, hodnota v něm se drží, a co je za ním, doznívá Dozvuk sekund. Jak dlouho se drží, říká bez zapojeného Gate parametr Držení; na nule se převezme z modulu Nota, a bez něj se drží až do Dozvuku před koncem vykreslení, stejně jako Envelope. Se zapojeným Gate drží, dokud je klávesa dole. Výstup Env nese samotnou křivku 0-1.

- **Vstupy:** `In`, `Gate`, `Tvar` (křivka)
- **Výstupy:** `Out`, `Env`, `A`, `S`, `R`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Nástup` | 0.12 | 0 – 10 | s | ano | ano |  |
| `Držení` | 0 | 0 – 10 | s |  | ano |  |
| `Dozvuk` | 0.3 | 0 – 10 | s | ano | ano |  |
| `Bod sustainu` | 0.35 | 0 – 1 |  |  |  |  |

<a id="modul-curvemodule"></a>
### Křivka

*Modulace · jméno v sestavě `Křivka` · typ `CurveModule`*

Nakreslí tvar a půjčí ho zbytku sestavy. Výstup Tvar zapoj do křivkového vstupu (Oscillator, LFO, Waveshaper, Obálka z křivky) — tam se použije jako tvar, ne jako signál, a každý si ho srovná do rozsahu, který potřebuje. Výstup Out je obyčejný signál: tvar rozvinutý přes Délku sekund, takže jde pustit do jakéhokoli modulačního vstupu v programu. Jedna křivka může krmit libovolně mnoho modulů zároveň.

- **Vstupy:** žádné
- **Výstupy:** `Tvar` (křivka), `Out`
- **Křivka:** `Tvar` (osa X: Průběh, osa Y: Hodnota)

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Délka` | 1 | 0.001 – 60 | s | ano | ano |  |
| `Amplitude` | 1 | 0 – 1 |  | ano |  |  |
| `Opakovat` | Ne | Ne, Ano |  |  |  |  |

<a id="modul-poissontriggermodule"></a>
### Nepravidelný spouštěč

*Modulace · jméno v sestavě `Nepravidelný spouštěč` · typ `PoissonTriggerModule`*

Impulsy s náhodnými rozestupy (Poissonův proces) — praskání ohně, kapky, jiskření, skřípání. Četnost je PRŮMĚRNÝ počet impulsů za sekundu, ne pevný takt. Nepravidelnost 0 = přesný metronom, 1 = plná náhoda; průměrná četnost zůstává stejná. Trig je jednovzorkový impuls, Gate drží nastavenou dobu, Síla drží 0-1 podle toho, jak dlouhá byla mezera před impulsem (delší čekání = silnější impuls).

- **Vstupy:** žádné
- **Výstupy:** `Trig`, `Gate`, `Síla`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Četnost` | 8 | 0.1 – 200 | 1/s | ano | ano |  |
| `Nepravidelnost` | 1 | 0 – 1 |  | ano |  |  |
| `Délka gate` | 0.02 | 0.0005 – 2 | s | ano | ano |  |
| `Seed` | náhodné | 0 – 1000000 |  |  |  | semínko náhody |

<a id="modul-sampleholdmodule"></a>
### Vzorkovač (S&H)

*Modulace · jméno v sestavě `Vzorkovač` · typ `SampleHoldModule`*

Přečte hodnotu na vstupu ve chvíli, kdy dorazí impuls na Trig, a drží ji až do dalšího impulsu — ze spojitého signálu udělá schody. Když do In nic nezapojíte, vzorkuje si vlastní bílý šum, takže sám o sobě je to generátor náhodných hodnot (a Seed říká kterých; se zapojeným In je Seed bez vlivu). Spouštěč určuje RYTMUS schodů: z LFO jsou pravidelné, z Nepravidelného spouštěče nepravidelné. Ostré hrany schodů se dají zaoblit modulem Sklouznutí.

- **Vstupy:** `In`, `Trig`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Seed` | náhodné | 0 – 1000000 |  |  |  | semínko náhody |

<a id="modul-slewmodule"></a>
### Sklouznutí

*Modulace · jméno v sestavě `Sklouznutí` · typ `SlewModule`*

Zaobluje skoky v signálu — místo okamžitého přeskoku se hodnota k nové plynule dojede. Na melodii je to portamento, na schodech z Vzorkovače nebo Sekvenceru je to rozdíl mezi cvaknutím a přejezdem. Nahoru a Dolů jsou časy, za které signál urazí 90 % vzdálenosti k nové hodnotě, a platí bez ohledu na to, jak je ten skok velký: stejné nastavení zní stejně na signálu 0–1 i na výšce v hertzech. Různé časy nahoru a dolů z toho udělají sledovač špiček — rychle nahoru, pomalu dolů. Nula vypne vyhlazení v daném směru.

- **Vstupy:** `In`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Nahoru` | 0.05 | 0 – 10 | s | ano | ano |  |
| `Dolů` | 0.05 | 0 – 10 | s | ano | ano |  |

<a id="modul-envelopefollowermodule"></a>
### Sledovač obálky

*Modulace · jméno v sestavě `Sledovač obálky` · typ `EnvelopeFollowerModule`*

Změří hlasitost signálu a vydá ji jako řídicí signál — zvuk tak může řídit sám sebe: auto-wah (hlasitější úder otevře filtr), ducking (bicí ztlumí podklad), dynamické zkreslení. Nástup je čas, za který se obálka vyšplhá za hlasitostí, Dozvuk čas, za který ji zase pouští; krátký nástup a dlouhý dozvuk sleduje špičky, obojí dlouhé měří průměrnou hlasitost. Gate se otevře, když obálka překročí Práh, a Trig v tu chvíli vydá jeden impuls — to je jediná cesta, jak ze zvuku udělat spouštěč pro Obálku nebo Vzorkovač. Pozor: obálka sinusovky o amplitudě 1 se ustálí na 0,64, ne na 1.

- **Vstupy:** `In`
- **Výstupy:** `Out`, `Gate`, `Trig`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Nástup` | 0.01 | 0 – 1 | s | ano | ano |  |
| `Dozvuk` | 0.1 | 0 – 10 | s | ano | ano |  |
| `Práh` | 0.1 | 0 – 1 |  | ano |  |  |

<a id="modul-invertermodule"></a>
### Invertor

*Modulace · jméno v sestavě `Invertor` · typ `InverterModule`*

Překlopí signál kolem nastavené hladiny: co je nad ní, jde o stejný kus pod ni, a co je přesně na ní, zůstane na místě. S hladinou 0 je to obrácení fáze — a tím i odčítání, protože Mixer umí jen sčítat: rozdíl dvou signálů vznikne jedině tak, že se jeden pošle přes Invertor. S hladinou 0,5 se z řídicího signálu 0–1 stane jeho doplněk (1−x), tedy crossfader: jeden hlas ubývá přesně tak rychle, jak druhý přibývá. Hladina se dá modulovat, takže osa překlopení se sama hýbe. Nezapojený vstup dělá z modulu konstantu o hodnotě dvojnásobku hladiny.

- **Vstupy:** `In`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Hladina` | 0 | -1 – 1 |  | ano |  |  |

<a id="modul-linlogmodule"></a>
### Převodník lin/log

*Modulace · jméno v sestavě `Převodník` · typ `LinLogModule`*

Ohne řídicí signál mezi „rovnoměrně v hodnotě“ (Lin) a „rovnoměrně v tom, co slyšíš“ (Log). Kam zapojíš, říká, jaký signál máš; odkud bereš, říká, jaký chceš. Na výšku: obálku pusť do Lin, ber z Log a teprve to veď do Frequency — lineární obálka je totiž lineární v HERTZÍCH, takže sama vystřelí nahoru a pak se plazí. Poměr nastav na (základ + hloubka) ÷ základ toho knoflíku, do kterého to teče: u 110 + 2400 je to 22,8. Poměr 1 znamená průchod beze změny. Zisk se uplatní až na konci, na obou výstupech stejně. Záporné hodnoty se ohýbají symetricky, takže to funguje i na obousměrné LFO (vibrato pak drží stejný interval nahoře i dole); mimo −1…1 se drží krajní hodnota.

- **Vstupy:** `Lin`, `Log`
- **Výstupy:** `Lin`, `Log`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Poměr` | 16 | 1 – 1024 | × | ano |  |  |
| `Zisk` | 1 | 0 – 2 |  | ano |  |  |

<a id="modul-eventsequencemodule"></a>
### Události

*Modulace · jméno v sestavě `Události` · typ `EventSequenceModule`*

Vypálí události v přesně zapsaných časech — dopad střepu ve 28,4 ms se silou 0,8, série přeskoků, kroky. Na rozdíl od Sekvenceru jsou časy v sekundách a místo výšky má každá událost sílu. Impuls je jeden vzorek o velikosti síly (rovnou do Banky módů nebo Resonatoru jako úder), Trig jeden vzorek 1, Gate drží Délku gate (nebo délku z řádku), Síla drží sílu poslední události. Opakovat Ano přehrává dokola po Periodě; události po konci periody se pak nepřehrají. Reset (náběžná hrana) začne od začátku. Čas se počítá na vzorky přesně a nezávisí na velikosti bloku.

- **Vstupy:** `Reset`
- **Výstupy:** `Impuls`, `Trig`, `Gate`, `Síla`
- **Text:** `Události` — jedna událost na řádek: čas [s], síla, nepovinně délka gate [s]; oddělovač mezera/tabulátor/středník, desetinná tečka i čárka, `#` poznámka, pořadí řádků nevadí; nejvýš 1024 událostí.

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Délka gate` | 0.02 | 0.0005 – 2 | s |  |  |  |
| `Opakovat` | Ne | Ne, Ano |  |  |  |  |
| `Perioda` | 1 | 0.001 – 60 | s |  |  |  |

<a id="modul-pulsedividermodule"></a>
### Dělič impulsů

*Modulace · jméno v sestavě `Dělič impulsů` · typ `PulseDividerModule`*

Počítá impulsy na vstupu Trig a pustí dál každý N-tý — z jedněch hodin udělá pomalejší, které se s nimi nemůžou rozejít. Posun říká, KTERÝ impuls cyklu projde: dva děliče čtyřmi s posunem 0 a 2 se střídají, tedy protiváha. Reset vrátí počítadlo na začátek, takže další impuls je zase první. Hodiny k tomu je Nepravidelný spouštěč s Nepravidelností 0 — pak je to přesný metronom a Četnost se dá navázat na tempo. Vstup ale nemusí být pravidelný: z nepravidelného spouštěče projde každá třetí kapka. Dělitel i Posun se zaokrouhlují.

- **Vstupy:** `Trig`, `Reset`
- **Výstupy:** `Trig`, `Gate`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Dělitel` | 4 | 1 – 16 |  | ano |  |  |
| `Posun` | 0 | 0 – 15 |  | ano |  |  |
| `Délka gate` | 0.05 | 0.0005 – 2 | s | ano | ano |  |

<a id="modul-quantizermodule"></a>
### Kvantizér

*Modulace · jméno v sestavě `Kvantizér` · typ `QuantizerModule`*

Zaokrouhlí řídicí signál na nejbližší tón stupnice a vydá ho jako Hz — zapoj na Frequency oscilátoru se základem 0 a hloubkou 1, stejně jako Sekvencer. Bez něj hraje cokoli náhodného mimo, protože modulace se přičítá v hertzích, ne v půltónech. Vstup se čte jako půltóny kolem Základního tónu: ±1 na vstupu je ±Rozsah půltónů, takže −1 až 1 z Vzorkovače nebo LFO sahá symetricky nad i pod zvolenou notu. Nezapojený vstup drží Základní tón. Trig a Gate označí chvíli, kdy se nota změní: Trig je jeden vzorek, Gate drží nastavenou dobu. Do Gate obálky patří Gate — jednovzorkový impuls obálku nespustí vůbec, změřeno jako přesné ticho. Před kvantizér patří Vzorkovač (S&H): sám o sobě kvantizér notu nedrží, takže signál kmitající přesně na hranici dvou tónů přeskakuje sem a tam.

- **Vstupy:** `In`
- **Výstupy:** `Hz`, `Trig`, `Gate`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Základní tón` | C4 | C-1 … G9 (128 voleb, hodnota = index) |  |  |  |  |
| `Stupnice` | Pentatonika moll | Chromatická, Dur, Moll, Harmonická moll, Pentatonika dur, Pentatonika moll, Blues, Dórská, Frygická, Lydická, Mixolydická, Celotónová, Zmenšená, Kvinty, Oktávy |  |  |  |  |
| `Rozsah` | 24 | 1 – 60 | půltónů | ano |  |  |
| `Délka gate` | 0.05 | 0.0005 – 2 | s | ano | ano |  |

<a id="modul-gainmodule"></a>
### Gain

*Zpracování · jméno v sestavě `Gain` · typ `GainModule`*

Škáluje hlasitost procházejícího signálu — jednoduché zesílení/zeslabení.

- **Vstupy:** `In`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Gain` | 1 | 0 – 2 |  | ano |  |  |

<a id="modul-mixermodule"></a>
### Mixer

*Zpracování · jméno v sestavě `Mixer` · typ `MixerModule`*

Sloučí až čtyři vstupní signály do jednoho — volitelně součtem, průměrem nebo násobením.

- **Vstupy:** `In 1`, `In 2`, `In 3`, `In 4`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Mode` | Sum | Sum, Average, Multiply |  |  |  |  |

<a id="modul-filtermodule"></a>
### Filter

*Zpracování · jméno v sestavě `Filter` · typ `FilterModule`*

Filtr se stavovým prostorem s laditelným mezním kmitočtem (Cutoff) a rezonancí. Dolní, horní a pásmová propust propouštějí jen část pásma; Notch naopak jednu frekvenci vyřízne (na sweepu zní jako phaser), Peak ji zvýrazní a zbytek nechá být (Resonance je tu výška zvonu, ne strmost — při 10 je to +26 dB) a All-pass propustí všechno beze změny hlasitosti a otočí jen fázi. Stabilní na celém rozsahu Cutoffu i při nejnižší Resonance.

- **Vstupy:** `In`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Type` | Low-pass | Low-pass, High-pass, Band-pass, Notch, Peak, All-pass |  |  |  |  |
| `Cutoff` | 1000 | 20 – 16000 | Hz | ano |  |  |
| `Resonance` | 1 | 0.5 – 10 |  | ano |  |  |

<a id="modul-resonatormodule"></a>
### Resonator

*Zpracování · jméno v sestavě `Resonator` · typ `ResonatorModule`*

Modální rezonátor — úzkopásmový "zvonící" filtr laděný na jednu frekvenci s nastavitelnou dobou doznívání (Decay). Nakrmte ho krátkým impulzem (např. Noise přes rychlou Envelope) pro zvuky úderu do skla/dřeva/kovu.

- **Vstupy:** `In`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Frequency` | 800 | 20 – 8000 | Hz | ano |  |  |
| `Decay` | 0.3 | 0.01 – 4 | s | ano | ano |  |
| `Gain` | 1 | 0 – 4 |  | ano |  |  |

<a id="modul-modalbankmodule"></a>
### Banka módů

*Zpracování · jméno v sestavě `Banka módů` · typ `ModalBankModule`*

Banka modálních rezonátorů se společným vstupem — rozeznělé těleso (kov, dřevo, sklo, keramika) v jedné krabičce místo sítě Resonatorů a Mixerů. Každý řádek Módů je jeden mód: frekvence, Decay a Gain se stejným významem jako u Resonatoru. Výška posouvá celé těleso (křivka, LFO). Do vstupu Hz patří Nota.Hz: nejnižší mód pak zní na zahrané notě, ostatní drží své poměry a Výška násobí navrch; Nota.Gate do Trig udeří při stisku. Doznívání násobí všechny Decay. Hlasitost módů Váha = špička (výchozí) = Gain je přímo špička módu při jakékoli frekvenci; Jako Resonator = hluboké módy hlasitější, na převod sítě Resonatorů. Nezapojená se na začátku sama jednou udeří (jednovzorkový impuls), Trig udeří znovu na náběžnou hranu; do In patří vlastní buzení — šum přes rychlou obálku, škrábání, cokoli. Nečitelný text pustí vstup beze změny a napíše proč.

- **Vstupy:** `In`, `Trig`, `Hz`, `Výška`, `Doznívání`, `Úroveň` (modulace)
- **Výstupy:** `Out`
- **Text:** `Módy` — jeden mód na řádek: frekvence [Hz], Decay [s do −60 dB], Gain; oddělovač mezera/tabulátor/středník, desetinná tečka i čárka, `#` poznámka; řádek tabulky MÓDY ze `zvuk analyza` jde vložit, jak je; nejvýš 64 módů.

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Výška` | 1 | 0.25 – 4 | × | ano |  |  |
| `Doznívání` | 1 | 0.1 – 4 | × | ano |  |  |
| `Úroveň` | 1 | 0 – 2 |  | ano |  |  |
| `Hlasitost módů` | Váha = špička | Jako Resonator, Váha = špička |  |  |  |  |

<a id="modul-delaymodule"></a>
### Delay

*Zpracování · jméno v sestavě `Delay` · typ `DelayModule`*

Zpětnovazební zpožďovací linka (echo) s nastavitelným časem, zpětnou vazbou (Feedback) a mixem. Režim mixu Přičíst (výchozí) nechá suchý signál vždy naplno a ozvěnu přičte × Mix — součet může být hlasitější než vstup. Prolnout dělá skutečný poměr: Mix 0 jen suchý, 1 jen ozvěna.

- **Vstupy:** `In`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Time` | 0.3 | 0.01 – 4 | s | ano | ano |  |
| `Feedback` | 0.4 | 0 – 0.95 |  | ano |  |  |
| `Mix` | 0.5 | 0 – 1 |  | ano |  |  |
| `Režim mixu` | Přičíst (dosavadní) | Přičíst (dosavadní), Prolnout |  |  |  |  |

<a id="modul-delaylinemodule"></a>
### Zpoždění

*Zpracování · jméno v sestavě `Zpoždění` · typ `DelayLineModule`*

Čisté zpoždění signálu — žádný suchý signál navíc. Na srovnání vrstev mezi sebou, na hřebenový filtr (smíchej se suchým přes Mixer) nebo jako pre-delay. S Časem modulovaným z LFO dělá flanger/chorus. Zpětná vazba nechá signál kroužit dokola a tím hřeben rozezní: kladná zvýrazní násobky 1/Čas, záporná ty poloviční liché. Pro ozvěnu použij modul Delay.

- **Vstupy:** `In`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Čas` | 5 | 0.02 – 250 | ms | ano | ano |  |
| `Zpětná vazba` | 0 | -0.95 – 0.95 |  | ano |  |  |

<a id="modul-reverbmodule"></a>
### Reverb

*Zpracování · jméno v sestavě `Reverb` · typ `ReverbModule`*

Dozvuk (Schroeder): osm paralelních hřebenových filtrů s tlumením a za nimi čtyři all-passy. Na rozdíl od modulu Delay, který dělá jednotlivé opakovatelné ozvěny, je tohle hustý difuzní ocas. Dozvuk je čas poklesu o 60 dB, Velikost mění tvar prostoru (malý = plechový, velký = sál) při stejné délce, Tlumení ubírá výškám s každým odrazem jako v opravdové místnosti. Mix 1 = jen dozvuk bez suchého signálu — to je to, co potřebuje obrácený dozvuk. Je MONO: šířku prostoru neudělá, délku ano.

- **Vstupy:** `In`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Dozvuk` | 1.8 | 0.1 – 10 | s | ano | ano |  |
| `Velikost` | 1 | 0.2 – 1.5 |  | ano |  |  |
| `Tlumení` | 0.4 | 0 – 1 |  | ano |  |  |
| `Předzpoždění` | 0 | 0 – 120 | ms | ano | ano |  |
| `Mix` | 0.3 | 0 – 1 |  | ano |  |  |

<a id="modul-distortionmodule"></a>
### Distortion

*Zpracování · jméno v sestavě `Distortion` · typ `DistortionModule`*

Waveshaping zkreslení — tvrdé/měkké oříznutí (Clip) nebo bitcrush (snížení bitové hloubky). Podvzorkování drží každý vzorek několikrát po sobě, čímž sníží vzorkovací frekvenci a přidá aliasing — to je ta "lo-fi/telefonní" barva, kterou samotné Bits neudělají. 1 = vypnuto, při 44,1 kHz je 4 ≈ 11 kHz a 10 ≈ 4,4 kHz. Působí až za oříznutím, takže jde kombinovat i s Clip typy.

- **Vstupy:** `In`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Type` | Hard Clip | Hard Clip, Soft Clip, Bitcrush |  |  |  |  |
| `Drive` | 3 | 0.1 – 20 |  | ano |  |  |
| `Bits` | 4 | 1 – 16 |  | ano |  |  |
| `Podvzorkování` | 1 | 1 – 64 | x | ano |  |  |

<a id="modul-waveshapermodule"></a>
### Waveshaper

*Zpracování · jméno v sestavě `Waveshaper` · typ `WaveshaperModule`*

Zkreslení podle nakreslené převodní křivky — pro každou vstupní úroveň říká křivka výstupní. Drive žene signál dál po křivce, Mix prolíná mezi čistým a tvarovaným.

- **Vstupy:** `In`, `Tvar` (křivka)
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Drive` | 1 | 0.1 – 20 |  | ano |  |  |
| `Mix` | 1 | 0 – 1 |  | ano |  |  |

<a id="modul-ringmodulatormodule"></a>
### Ring Modulator

*Zpracování · jméno v sestavě `Ring Modulator` · typ `RingModulatorModule`*

Násobí dva signály vzorek po vzorku — klasický kovový/robotický zvuk.

- **Vstupy:** `In`, `Modulator`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Mix` | 1 | 0 – 1 |  | ano |  |  |

<a id="modul-granulatormodule"></a>
### Granulátor

*Zpracování · jméno v sestavě `Granulátor` · typ `GranulatorModule`*

Rozřeže signál na vstupu na krátká zrna a přehraje je překrytá přes sebe. Pozice říká, jak daleko do minulosti zrno sahá (až 2 s), Výška jak rychle se zrno čte — a na sobě nezávisí, takže jde zvuk natáhnout beze změny výšky nebo zmrazit na místě. Malá Délka a velký Rozptyl dělají mrak/texturu. Zmrazit (od 0,5) zastaví nahrávání, takže se pořád dokola vrací týž úsek — to je zmrazení i beat repeat; bez něj Pozice znamená jen zpoždění a materiál pod zrny protéká. Rozladění rozhází výšku po zrnech (mrak, chorus). Na výstupu jsou jen zrna, suchý signál ne — přimíchej ho Mixerem. Soubor granuluješ tak, že do In zapojíš Sampler.

- **Vstupy:** `In`, `Obálka zrna` (křivka)
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Četnost` | 20 | 0.5 – 200 | 1/s | ano | ano |  |
| `Délka` | 0.05 | 0.002 – 0.5 | s | ano | ano |  |
| `Pozice` | 0.2 | 0 – 2 | s | ano | ano |  |
| `Rozptyl` | 0.02 | 0 – 2 | s | ano | ano |  |
| `Výška` | 1 | -4 – 4 |  | ano |  |  |
| `Rozladění` | 0 | 0 – 1200 | c | ano |  |  |
| `Nepravidelnost` | 0 | 0 – 1 |  | ano |  |  |
| `Zmrazit` | 0 | 0 – 1 |  | ano |  |  |
| `Seed` | náhodné | 0 – 1000000 |  |  |  | semínko náhody |

<a id="modul-compressormodule"></a>
### Kompresor

*Zpracování · jméno v sestavě `Kompresor` · typ `CompressorModule`*

Srovná hlasitost: co je nad Prahem, ztlumí v poměru, který nastavíš — 4:1 znamená, že z každých 4 dB nad prahem projde 1 dB. Koleno prahu ohýbá, takže se komprese nezapíná skokem. Nástup je, jak rychle chytne, Dozvuk jak rychle pustí; krátký nástup drží špičky, delší pustí úder napřed a srovná až tělo zvuku. Vyrovnáním se vrátí hlasitost, kterou komprese ubrala. Výstup Zisk vydává samotné zeslabení (1 = neztlumeno) — zapoj ho do Gainu jiné větve a máš ducking: co projde kompresorem, uhýbá podkladu z cesty. Předstih nech na nule, dokud přes tebe nepropadají špičky: zdrží celý signál o svou hodnotu.

- **Vstupy:** `In`
- **Výstupy:** `Out`, `Zisk`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Práh` | -18 | -60 – 0 | dB | ano |  |  |
| `Poměr` | 4 | 1 – 20 |  | ano |  |  |
| `Koleno` | 6 | 0 – 24 | dB | ano |  |  |
| `Nástup` | 0.01 | 0 – 1 | s | ano | ano |  |
| `Dozvuk` | 0.1 | 0 – 2 | s | ano | ano |  |
| `Vyrovnání` | 0 | 0 – 24 | dB | ano |  |  |
| `Předstih` | 0 | 0 – 20 | ms |  |  |  |

<a id="modul-swarmmodule"></a>
### Roj

*Zpracování · jméno v sestavě `Roj` · typ `SwarmModule`*

Otevírá roj: všechno, co je zapojené za ním až po Konec roje, se při každém impulsu na Trig zahraje jako nová kopie a kopie se sečtou — shluk partiklů z jedné řady krabiček. Co vede přes Vstup → Hodnota, si kopie zamrazí v okamžiku svého impulsu (rozladění z Vzorkovače na stejném spouštěči); drát zapojený dovnitř odjinud zůstává živý a společný. Každá kopie má vlastní obálku (Attack, Decay, Sustain, Držení, Release — čtou se při impulsu) a po jejím doběhnutí skončí. Strop je kolik kopií zní naráz; když jsou všechny obsazené, impuls nejstarší kopii ukradne (ztlumí ji za 5 ms), nebo se podle volby Plný strop zahodí — zahazování ale hustý roj rozpulzuje do nárazů. Kopie se sčítají a hlasitost s jejich počtem roste; Normalizace 1/√Strop ji ztlumí tak, aby plný roj zněl v průměru zhruba jako jedna kopie — řídký roj s velkým stropem pak ale zní potichu. Bez Konce roje končí roj na Outputu — pak se ale kopíruje i dozvuk a efekty za ním.

- **Vstupy:** `Trig`, `Vstup 1`, `Vstup 2`, `Vstup 3`, `Vstup 4`
- **Výstupy:** `Hodnota 1`, `Hodnota 2`, `Hodnota 3`, `Hodnota 4`, `Env`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Attack` | 0.005 | 0 – 10 | s | ano | ano |  |
| `Decay` | 0.05 | 0 – 10 | s | ano | ano |  |
| `Sustain` | 0.6 | 0 – 1 |  | ano |  |  |
| `Držení` | 0.05 | 0 – 10 | s | ano | ano |  |
| `Release` | 0.3 | 0 – 10 | s | ano | ano |  |
| `Strop` | 8 | 1 – 128 |  |  |  |  |
| `Plný strop` | Ukrást nejstarší | Ukrást nejstarší, Zahodit impuls |  |  |  |  |
| `Normalizace` | Žádná | Žádná, 1/√Strop |  |  |  |  |

<a id="modul-swarmendmodule"></a>
### Konec roje

*Zpracování · jméno v sestavě `Konec roje` · typ `SwarmEndModule`*

Uzavírá roj: na vstup dostane všechny kopie roje sečtené, každou pod její obálkou, a pošle je dál. Za něj patří dozvuk, delay a další zpracování celku — spočítají se jednou, ne pro každou kopii zvlášť. Z vnitřku roje nesmí vést drát ven jinudy než sem. Měkký ořez je záchrana pro hustý roj: pod 0,8 se signálu nedotkne, nad tím ho plynule ohne tak, aby nepřesáhl 0,94 (−0,54 dBFS). Pokud za Koncem roje hlasitost stahujete Gainem, vypněte ho — ohnul by špičky, které by jinak prošly čistě.

- **Vstupy:** `In`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Měkký ořez` | Ano | Ne, Ano |  |  |  |  |

<a id="modul-formulamodule"></a>
### Formule

*Zpracování · jméno v sestavě `Formule` · typ `FormulaModule`*

Spočítá na každý vzorek to, co je napsané ve Formuli. Vstupy jsou a, b, c, d (nezapojený je 0), knoflíky p a q, čas t, vzorkovací kmitočet sr — a hlavně y1 a y2, tedy vlastní výstup o jeden a dva vzorky zpět, kudy vede zpětná vazba, kterou graf jinde zakazuje. Tím se dá napsat jednopólový filtr, odstraňovač stejnosměrné složky nebo integrátor, což z krabiček poskládat nejde. ⚠️ Je to nouzový východ, ne způsob stavění: co jde zapojit z krabiček, patří do krabiček — ty jdou rozebrat a pochopit, formule ne. Nesmyslná formule (dělení nulou) nezhavaruje, vrátí nulu; nepřeložitelná pustí vstup a beze změny dál a napíše proč.

- **Vstupy:** `a`, `b`, `c`, `d`
- **Výstupy:** `Out`
- **Text:** `Formule` — Proměnné: a, b, c, d · a1, a2 · y1, y2 · t · p, q · sr · pi, e · Funkce: sin(v), cos(v), tan(v) · abs(v), sign(v) · sqrt(v), exp(v), log(v) · tanh(v) · floor(v), round(v), frac(v) · min(u, v), max(u, v) · pow(u, v) · clamp(v, lo, hi) · if(podmínka, pak, jinak) · Operátory: + − * / ^ (mocnina), % (zbytek po dělení), závorky, porovnání < > <= >= == != vrací 1 nebo 0. Desetinná čísla s TEČKOU (0.5) — čárka odděluje hodnoty ve funkcích

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `p` | 0 | -1 – 1 |  | ano |  |  |
| `q` | 0 | -1 – 1 |  | ano |  |  |

<a id="modul-scopemodule"></a>
### Osciloskop / Spektrum

*Vizualizace · jméno v sestavě `Scope` · typ `ScopeModule`*

Průchozí monitor — zobrazuje procházející signál jako osciloskop (tvar vlny) nebo spektrální analyzátor (FFT), podle zvoleného Mode. Neovlivňuje zvuk.

- **Vstupy:** `In`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Mode` | Waveform | Waveform, Spektrum |  |  |  |  |

<a id="modul-vumetermodule"></a>
### VU Metr

*Vizualizace · jméno v sestavě `VU Metr` · typ `VuMeterModule`*

Průchozí monitor hlasitosti — ukazatel špičky (Peak) a RMS úrovně procházejícího signálu. Neovlivňuje zvuk.

- **Vstupy:** `In`
- **Výstupy:** `Out`

Nemá žádné knoflíky.

<a id="modul-spectrogrammodule"></a>
### Spektrogram

*Vizualizace · jméno v sestavě `Spektrogram` · typ `SpectrogramModule`*

Průchozí monitor, který kreslí spektrogram — čas vodorovně, frekvence svisle, jas podle hlasitosti. Na rozdíl od Spektra v modulu Scope je vidět VÝVOJ v čase: klouzavá výška, doznívání, kolísání. Logaritmická osa je výchozí, protože na lineární se všechno pod 1 kHz vejde do spodních pár procent obrázku. Barvy jsou jen pro oko — Šedá je výchozí a je to ta, ve které se obrázek dá spolehlivě ČÍST, protože jas roste monotónně s hlasitostí. Neovlivňuje zvuk.

- **Vstupy:** `In`
- **Výstupy:** `Out`

| knoflík | výchozí | rozsah / volby | jednotka | mod. | nota | pozn. |
|---|---|---|---|---|---|---|
| `Osa` | Logaritmicky | Logaritmicky, Lineárně |  |  |  |  |
| `Barvy` | Šedá | Šedá, Inferno, Viridis, Duha |  |  |  |  |
| `Strop` | 12000 | 500 – 20000 | Hz |  |  |  |
| `Práh` | -85 | -110 – -40 | dB |  |  |  |

<a id="modul-outputmodule"></a>
### Output

*Výstup · jméno v sestavě `Output` · typ `OutputModule`*

Koncový bod sestavy — signál na jeho vstupu je to, co se přehraje nebo exportuje.

- **Vstupy:** `In`
- **Výstupy:** `Out`

Nemá žádné knoflíky.

## 2. Bloky

Blok je hotové zapojení několika obyčejných modulů, které editor vloží najednou (`BlockCatalog.Entries`). V souboru
`.zvuky` po něm nezůstane nic zvláštního — jen moduly a dráty, které vložil. Cizí program je proto nepotřebuje znát;
seznam je tu kvůli úplnosti.

| blok | co dělá | moduly uvnitř |
|---|---|---|
| Tón z klávesy | Nota, oscilátor, obálka, výstup — celá cesta od klávesy ke zvuku. | Nota, Oscillator, Envelope, Output |
| Brána | Pustí zvuk, jen když je dost hlasitý — a nelupne u toho. | Gain (vstup), Sledovač obálky, Sklouznutí, Gain (hlasitost) |
| Ducking | Úder uhne podkladu z cesty — sidechain přes výstup Zisk na Kompresoru. | Gain (úder), Kompresor, Gain (podklad), Mixer |
| Chorus | Tři rozladěné hlavy proti suchému signálu. Zpoždění samo tohle neumí. | Gain, Mixer, LFO 1, Zpoždění 1, LFO 2, Zpoždění 2, LFO 3, Zpoždění 3 |
| Rezonanční flanger | Ježdící hřeben, který si zvoní. Zpětná vazba je ten rozdíl proti chorusu. | Gain, LFO, Zpoždění, Mixer |
| Phaser | Čtyři All-passy proti suchému signálu. Filtr sám nezní — slyšet je až ten součet. | Gain, LFO, Filter 1, Filter 2, Filter 3, Filter 4, Mixer |
| Unisono | Čtyři rozladěné pily z jedné výšky. Detune je hloubka modulace, ne knoflík. | Gain, Mixer, Oscillator -14 c, Oscillator -5 c, Oscillator +5 c, Oscillator +14 c |
| Hodiny | Přesný takt a k němu pomalejší, který se s ním nemůže rozejít. | Nepravidelný spouštěč (hodiny), Dělič impulsů |
| Náhodná melodie | Náhoda, která hraje ve stupnici — a spouští u toho obálku. | Nepravidelný spouštěč (hodiny), Vzorkovač, Kvantizér |
| Dva díly | Náběh a po něm rána, obojí z jedné obálky. Hrana mezi nimi se nekreslí. | Envelope (dirigent), Envelope (díl 2) |

## 3. Typy

### ZvukEfekty.Core.Audio

<a id="typ-audiosamplecache"></a>
#### `AudioSampleCache`

*statická třída · Přehrávání a vzorky*

Rozbalené vzorky pro Sampler: mezipaměť souborů z disku a rejstřík vzorků vložených programem. Každý soubor se rozbalí jednou a sdílí ho všechny hlasy. Klíčem je cesta, čas změny a délka souboru, takže upravený soubor se načte znovu. Souborová mezipaměť má strop zhruba 26,5 milionu vzorků (deset minut zvuku při 44,1 kHz, ~106 MB) a nejdéle nepoužité vyhazuje; vzorky vložené programem pod strop nespadají a uklízí je program. Celá třída je zamčená a dá se volat z libovolného vlákna. `.wav` rozbaluje sama (`WavDekoder`), ostatní formáty jen přes `Dekoder`.

| člen | popis |
|---|---|
| `const string PrefixPameti = "pamet:"` | Předpona cesty, kterou se pozná vzorek vložený do paměti: `pamet:jméno`. Tak se zapisuje do `ModuleFile.Path` Sampleru. |
| `static Func<string, DecodedSample>? Dekoder { get; set; }` | Dekodér pro formáty, které knihovna sama neumí (`.mp3`, `.flac`, `.ogg`…): funkce „cesta k souboru → `DecodedSample`“. Dodává ho program. `.wav` jde vždy přes `WavDekoder`, i když je dekodér nastavený. `null` (výchozí) = jiné formáty než `.wav` skončí chybou „není to WAV“ a Sampler hraje ticho. |
| `static void Clear()` | Vyprázdní mezipaměť souborů (příští použití je rozbalí znovu). Vzorků vložených do paměti se nedotkne — na ty je `VyprazdniPamet`. |
| `static DecodedSample? Get(string path, out string? error)` | Vrátí rozbalený obsah cesty (souboru nebo `pamet:jméno`); při prvním dotazu soubor rozbalí. Nikdy nehází: když vzorek sehnat nejde, vrátí `null` a důvod dá do `error` („Soubor neexistuje.“, „…není to WAV…“). Neúspěch si nepamatuje, další dotaz to zkusí znovu. Rozbalení trvá desítky ms — nevolat ze zvukového vlákna. |
| `static bool JeVPameti(string? path)` | `true`, když cesta začíná `pamet:` — tedy ukazuje do rejstříku vložených vzorků, ne na disk. Rozhoduje jen předpona, ne to, jestli tam vzorek opravdu je. |
| `static bool OdeberVzorek(string jmeno)` | Vyhodí vložený vzorek z paměti (jméno bez předpony). Sestava, která na něj ukazuje, od příští přípravy hlásí chybějící vzorek. Vrací, jestli tam byl. |
| `static (int Entries, long Samples) Stats()` | Kolik souborů drží mezipaměť a kolik mají dohromady vzorků. Vložené vzorky se sem nepočítají. |
| `static (int Pocet, long Vzorku) StatsPameti()` | Kolik vzorků je vložených do paměti a kolik mají dohromady vzorků — aby program viděl, kolik paměti drží. |
| `static string VlozVzorek(string jmeno, DecodedSample vzorek)` | Vloží hotové vzorky pod jménem a vrátí cestu `pamet:jméno`, kterou se na ně Sampler odkáže. Vzorky musí být mono; `SampleRate` je jejich vlastní kmitočet (Sampler převzorkuje sám). Stejné jméno podruhé vzorek nahradí; hlas, který právě hraje, dohraje ten starý. |
| `static string VlozVzorek(string jmeno, float[] vzorky, int kmitocet)` | Totéž z pole mono vzorků a jejich kmitočtu. Pole se nekopíruje: kdo ho potom přepíše, přepíše ho i hrajícímu Sampleru. |
| `static string VlozWav(string jmeno, byte[] wav)` | Rozbalí obsah souboru `.wav` z pole bajtů (stereo zprůměruje do mona), vloží ho pod jménem a vrátí cestu `pamet:jméno`. Rozbaluje hned, ne při hraní — volat mimo zvukové vlákno. Komprimované formáty nebere; ty rozbal sám a vlož přes `VlozVzorek`. Hází `InvalidDataException`, když data nejsou WAV v podporovaném tvaru. |
| `static void VyprazdniPamet()` | Vyhodí všechny vložené vzorky. Mezipaměti souborů se nedotkne. |

<a id="typ-decodedsample"></a>
#### `DecodedSample`

*record · Přehrávání a vzorky*

Rozbalený zvuk: mono vzorky a jejich vlastní kmitočet. Vrací ho `AudioSampleCache.Get` a `WavDekoder`, vkládá se přes `AudioSampleCache.VlozVzorek` a vrací ho i dekodér programu (`AudioSampleCache.Dekoder`).

| člen | popis |
|---|---|
| `DecodedSample(float[] Samples, int SampleRate, int Channels)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `int Channels { get; init; }` | Kolik kanálů měl zdroj, než se smíchal do mona. Jen informace, `Samples` jsou vždy mono. |
| `double DurationSeconds { get; }` | Délka v sekundách (`Samples.Length / SampleRate`). |
| `int SampleRate { get; init; }` | Kmitočet vzorků tak, jak je měl soubor — obvykle jiný než kmitočet renderu. Převzorkování obstará Sampler. |
| `float[] Samples { get; init; }` | Celý zvuk, mono, rozsah ±1. |

<a id="typ-levelmeterhub"></a>
#### `LevelMeterHub`

*statická třída · Pro editor (cizí program je nepotřebuje)*

Most mezi moduly VU metr a ukazatelem úrovně v editoru. Modul VU metr do něj při renderu posílá úroveň bloku; editor si ji čte a kreslí. Cizí program ho nepotřebuje: VU metr v sestavě do něj publikuje i tam (malá data podle `Id` modulu), ale nikdo je nemusí číst.

| člen | popis |
|---|---|
| `static bool IsRecording { get; }` | `true` mezi `BeginRecording` a `EndRecording` — úrovně se pak ukládají na časovou osu dávkového renderu místo průběžného součtu. |
| `static void AdvancePlayback(double elapsedSeconds)` | Při přehrávání nahraného dávkového renderu zveřejní úroveň, která patří do času `elapsedSeconds`. |
| `static void BeginPass()` | Volá editor před průchodem všemi hlasy živé hry, aby se úrovně tohoto průchodu sčítaly od nuly. |
| `static void BeginRecording()` | Začne ukládat úrovně na časovou osu — volá editor před dávkovým Přehrát/Export. |
| `static void BeginSession()` | Zahodí sloty všech měřidel na začátku přehrávání. |
| `static void ClearDisplays()` | Vrátí všechna měřidla do klidu (konec přehrávání). |
| `static void Contribute(Guid moduleId, float[] block, double elapsedSeconds = 0)` | Přidá blok modulu s daným `Id` (volá ho modul VU metr při renderu). |
| `static void EndPass()` | Ukončí průchod živé hry a zveřejní jeho úrovně. |
| `static void EndRecording()` | Ukončí ukládání na časovou osu. |
| `static void RemoveSlot(Guid moduleId)` | Zahodí slot jednoho modulu (modul byl smazán ze sestavy). |
| `static LevelSnapshot? TryGet(Guid moduleId)` | Poslední zveřejněná úroveň modulu, nebo `null`. |

<a id="typ-levelsnapshot"></a>
#### `LevelSnapshot`

*struktura · Pro editor (cizí program je nepotřebuje)*

Jedno čtení VU metru: vyhlazená špička a RMS, obojí zhruba 0…1.

| člen | popis |
|---|---|
| `LevelSnapshot(float Peak, float Rms)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `float Peak { get; init; }` | Vyhlazená špička (rychlý náběh, pomalý pokles). |
| `float Rms { get; init; }` | Efektivní hodnota bloku. |

<a id="typ-midinotes"></a>
#### `MidiNotes`

*statická třída · Přehrávání a vzorky*

Převody MIDI čísel not: kmitočet a jméno. Rovnoměrně temperované ladění, A4 (MIDI 69) = 440 Hz.

| člen | popis |
|---|---|
| `const int MiddleC = 60` | MIDI číslo středního C (C4) = 60. Výchozí nota pro sestavy, kterým na notě nezáleží. |
| `static bool IsSharp(int midiNote)` | `true` pro noty, které klavír kreslí černou klávesou. |
| `static string Name(int midiNote)` | Mezinárodní jméno noty: „C4“, „F#3“ (B je B, ne H). |
| `static double ToFrequencyHz(int midiNote)` | Kmitočet noty v Hz. Tohle patří do konstruktoru `Voice`. |

<a id="typ-visualizationhub"></a>
#### `VisualizationHub`

*statická třída · Pro editor (cizí program je nepotřebuje)*

Most mezi moduly Osciloskop / Spektrum, Spektrogram a Morfující oscilátor a jejich náhledem v editoru. Moduly do něj při renderu publikují okna vzorků; editor si je čte a kreslí. Cizí program ho nepotřebuje — moduly do něj publikují i tam (malá data podle `Id` modulu), nikdo je nemusí číst.

| člen | popis |
|---|---|
| `static int Generation { get; }` | Počítadlo spuštěných přehrávání. Náhled, který si data skládá (spektrogram), podle změny pozná, že má začít znovu. |
| `static bool IsRecording { get; }` | `true` mezi `BeginRecording` a `EndRecording` — okna se ukládají na časovou osu dávkového renderu. |
| `static void AdvancePlayback(double elapsedSeconds)` | Při přehrávání nahraného dávkového renderu zveřejní okna, která patří do času `elapsedSeconds`. |
| `static void BeginPass()` | Volá editor před průchodem všemi hlasy živé hry, aby se okna tohoto průchodu sčítala od nuly. |
| `static void BeginRecording()` | Začne ukládat okna na časovou osu — volá editor před dávkovým Přehrát/Export. |
| `static void BeginSession()` | Ohlásí nové přehrávání (zvýší `Generation`). |
| `static void ClearDisplays()` | Vyprázdní všechny živé náhledy (konec přehrávání). |
| `static float[] ComputeSpectrum(float[] window)` | Spočítá z okna vzorků spektrum (FFT) pro zobrazení. |
| `static void Contribute(Guid moduleId, float[] window, double elapsedSeconds = 0, bool average = false)` | Přidá okno modulu s daným `Id` (volají ho vizualizační moduly při renderu). `average` = průměrovat místo sčítání přes hlasy. |
| `static void EndPass()` | Ukončí průchod živé hry a zveřejní jeho okna. |
| `static void EndRecording()` | Ukončí ukládání na časovou osu. |
| `static void RemoveSlot(Guid moduleId)` | Zahodí slot jednoho modulu (modul byl smazán ze sestavy). |
| `static VisualizationSnapshot? TryGet(Guid moduleId)` | Poslední zveřejněné okno modulu, nebo `null`. |

<a id="typ-visualizationsnapshot"></a>
#### `VisualizationSnapshot`

*record · Pro editor (cizí program je nepotřebuje)*

Jedno zveřejněné okno náhledu: vzorky z osciloskopu nebo spektrogramu.

| člen | popis |
|---|---|
| `VisualizationSnapshot(float[] Values)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `float[] Values { get; init; }` | Hodnoty okna (vzorky, zhruba ±1). |

<a id="typ-voice"></a>
#### `Voice`

*třída · Přehrávání a vzorky*

Jedna znějící nota jedné sestavy — hlas, který se renderuje po blocích. Při stavbě si sestavu naklonuje, připraví moduly (rozbalí vzorky), postaví roje a posune semínka náhody, aby dva hlasy téže sestavy nezněly bit po bitu stejně. Hraje se, dokud se nezavolá `Release` (nota se pustí a dozní) nebo `Steal` (utne s fade); pak se rozsvítí `IsFinished`. Hlas nemá zámky: `RenderBlock` smí v jednu chvíli volat jen jedno vlákno.

| člen | popis |
|---|---|
| `Voice(ModuleGraph templatePatch, int midiNote, double frequencyHz, double bpm)` | Postaví hlas ze sestavy (`PatchLoadResult.Graph`), MIDI noty, jejího kmitočtu (`MidiNotes.ToFrequencyHz`) a tempa (`PatchLoadResult.Timing.Bpm`). Sestavu klonuje, takže pozdější změny šablony se ho netýkají. Stojí milisekundy (víc, když rozbaluje vzorek) — nevolat ze zvukového vlákna. Hodí `InvalidOperationException`, když sestava nemá modul Output, a `ModuleGraph.InvalidWiringException` při cyklu nebo chybném zapojení roje. Chybějící vzorek výjimku **nehodí**, Sampler pak hraje ticho. |
| `const float SilenceThreshold = 0.0005f` | Práh ticha 0,0005 (−66 dBFS): blok s nižší špičkou se po `Release` počítá za tichý. |
| `bool IsBeingStolen { get; }` | `true` od zavolání `Steal` — hlas dozní krátkým fade a skončí. |
| `bool IsFinished { get; }` | `true`, když hlas dohrál: po `Release` vyrenderoval dva tiché bloky za sebou (nebo uplynula pojistka 5 s), nebo doběhl fade po `Steal`. Pak ho zahoď. |
| `bool IsReleasing { get; }` | `true` od `Release` (nebo `Steal`) — nota je puštěná a zní už jen doznění. |
| `float LastBlockPeak { get; }` | Největší \|vzorek\| posledního bloku (0…1), před fade z `Steal`. Nula do prvního bloku. |
| `int MidiNote { get; }` | MIDI nota, se kterou byl hlas postaven. |
| `void Release()` | Pustí notu: obálky přejdou do doznění a hlas po dozvuku sám skončí. Jednorázový zvuk se pouští po `PatchTiming.DurationSeconds`, smyčka až když má přestat. |
| `float[] RenderBlock(int sampleCount, int sampleRate)` | Vyrenderuje dalších `sampleCount` vzorků při kmitočtu `sampleRate` a vrátí je (mono). **Pole je půjčené:** hlas vrací pokaždé tentýž objekt a další volání ho přepíše — přičti ho nebo zkopíruj hned. Délku bloku drž stálou, jinak si hlas alokuje nové buffery. |
| `void Retrigger()` | Znovu otevře bránu hlasu, který doznívá — pro rychle opakovaný stisk téže klávesy místo nového hlasu. |
| `void Steal(int fadeSamples)` | Utne hlas: během `fadeSamples` vzorků ho stáhne do ticha (bez lupnutí) a ukončí. Pro plný strop hlasů a přechody scén. Druhé zavolání nic nezmění. |

<a id="typ-wavdekoder"></a>
#### `WavDekoder`

*statická třída · Přehrávání a vzorky*

Rozbalení souborů `.wav` na mono vzorky, bez jakékoli závislosti. Umí PCM 8/16/24/32 bit, IEEE float 32/64 bit a `WAVE_FORMAT_EXTENSIBLE`; víc kanálů zprůměruje do mona. Používá ho `AudioSampleCache` automaticky; přímo ho voláš jen tehdy, když chceš vzorky sám.

| člen | popis |
|---|---|
| `static DecodedSample Dekoduj(Stream stream)` | Rozbalí `.wav` z proudu. Proud musí umět `Length` a posun (`Seek`) — souborový i `MemoryStream` ano, síťový obvykle ne. Hází `InvalidDataException` u dat, která nejsou podporovaný WAV. |
| `static DecodedSample Dekoduj(byte[] data)` | Rozbalí `.wav` z pole bajtů. |
| `static DecodedSample Dekoduj(string path)` | Rozbalí soubor `.wav` z disku. |
| `static bool Umi(string path)` | `true`, když cesta končí na `.wav` (rozhoduje jen přípona). |

### ZvukEfekty.Core.Graph

<a id="typ-blockscratch"></a>
#### `BlockScratch`

*třída · Vnitřek renderu (běžný program nepotřebuje)*

Půjčovna bufferů jednoho hlasu: pole pro jeden průchod renderu, používaná znovu blok za blokem. Kvůli ní je pole z `Voice.RenderBlock` půjčené. Hlas si ji vytváří sám; cizí program ji nepotřebuje. Není sdílená mezi vlákny.

| člen | popis |
|---|---|
| `BlockScratch()` | Prázdná půjčovna. |
| `void BeginPass(int sampleCount)` | Začátek průchodu: další `Rent` vrátí zase první pole. Při jiné délce bloku stará pole zahodí. |
| `float[] Rent()` | Další pole tohoto průchodu, s obsahem z minula („špinavé“) — pro volajícího, který přepíše každý vzorek. |
| `float[] RentCleared()` | Další pole tohoto průchodu, vynulované. |
| `float[]? RentIfLength(int length)` | Další pole, je-li blok dlouhý právě `length` vzorků; jinak `null`. |

<a id="typ-connection"></a>
#### `Connection`

*třída · Sestava: graf, moduly, knoflíky, porty*

Drát v sestavě: z výstupního portu jednoho modulu do vstupního portu jiného. Vzniká přes `ModuleGraph.Connect`; všechny dráty jsou v `ModuleGraph.Connections`.

| člen | popis |
|---|---|
| `Connection(Port source, Port target)` | Vytvoří drát, ale do grafu ho nepřidá — na to je `ModuleGraph.Connect`. |
| `Port Source { get; }` | Výstupní port, ze kterého drát vede. |
| `Port Target { get; }` | Vstupní port, do kterého drát vede. |

<a id="typ-curve"></a>
#### `Curve`

*třída · Křivky, melodie, text a formule*

Nakreslený tvar: body spojené Bézierovými úseky, funkce x → y na intervalu 0…1. Slouží jako přenosová funkce waveshaperu, jedna perioda vlny oscilátoru i průběh obálky — liší se jen tím, co znamenají osy. Je neměnná (změna = nová křivka přes `WithPoints`). Vyhodnocuje se z předpočítané tabulky (`Lookup`).

| člen | popis |
|---|---|
| `Curve(IEnumerable<CurvePoint> points)` | Křivka z bodů. Body se seřadí podle X. |
| `const int LookupSize = 4096` | Počet bodů tabulky (4096), ze které se křivka čte při renderu. |
| `float[] Lookup { get; }` | Předpočítaná tabulka hodnot křivky na 0…1, `LookupSize` bodů. |
| `IReadOnlyList<CurvePoint> Points { get; }` | Body křivky seřazené podle X. |
| `((double X, double Y) Out, (double X, double Y) In) Controls(int segment)` | Dva řídicí body Bézierova úseku, který začíná bodem `segment` — tak, jak se opravdu kreslí (automatické tečny už dopočítané). |
| `double Evaluate(double x)` | Přesná hodnota v bodě `x`. Mimo 0…1 drží krajní hodnotu. |
| `float Sample(double x)` | Hodnota v bodě `x` z tabulky (lineární interpolace) — rychlá cesta pro render. |
| `Curve WithPoints(IEnumerable<CurvePoint> points)` | Nová křivka s jinými body. |
| `static Curve Identity()` | Přímka y = x na −1…1 — waveshaper, který nic nemění. |
| `static Curve Line(double from, double to)` | Přímka z (0, `from`) do (1, `to`). |
| `static Curve Morph(Curve from, Curve to, double amount)` | Tvar mezi dvěma křivkami: `amount` 0 = `from`, 1 = `to`. Posouvá body a tečny, nemíchá hodnoty. |
| `static Curve Pulse(double duty)` | Jedna perioda obdélníku, který je `duty` času nahoře (0,5 = čtverec). |
| `static Curve Saw(bool rising = true)` | Jedna perioda pily; `rising` false = klesající. |
| `static Curve Sine()` | Jedna perioda sinu. |
| `static Curve Square()` | Jedna perioda čtverce, ve fázi se sinem. |
| `static Curve Triangle()` | Jedna perioda trojúhelníku, ve fázi se sinem. |

<a id="typ-curvehandle"></a>
#### `CurveHandle`

*struktura · Křivky, melodie, text a formule*

Jedna polovina tečny v bodě křivky: kam a jak daleko se křivka z bodu odklání. `Dx`, `Dy` jsou posun od bodu v souřadnicích křivky. `Auto` = tečna, které se nikdo nedotkl; dopočítá se sama.

| člen | popis |
|---|---|
| `CurveHandle(double Dx, double Dy)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `static readonly CurveHandle Auto` | Nedotčená tečna (dopočítá se automaticky). |
| `double Dx { get; init; }` | Vodorovný posun konce tečny od bodu. |
| `double Dy { get; init; }` | Svislý posun konce tečny od bodu. |
| `bool IsAuto { get; }` | `true` pro nedotčenou tečnu. |

<a id="typ-curveinput"></a>
#### `CurveInput`

*třída · Křivky, melodie, text a formule*

Zásuvka modulu pro křivku dodanou jiným modulem (drátem typu `PortKind.Curve`). Když nic zapojeno není, vrací vestavěný náhradní tvar. Každou příchozí křivku přizpůsobí tomu, co modul potřebuje (`Output`).

| člen | popis |
|---|---|
| `Curve Curve { get; }` | Tvar, který modul právě použije: zapojený (přizpůsobený), nebo náhradní. Nikdy `null`. |
| `bool IsConnected { get; }` | `true`, když je zapojená křivka z jiného modulu. |
| `CurveOutput Output { get; }` | Rozsah a spojitost, kterou modul od křivky potřebuje. |
| `Port Port { get; }` | Vstupní port zásuvky (druh `PortKind.Curve`). |
| `event EventHandler? Changed` | Vyvolá se, když se tvar zásuvky změní (drát zapojen nebo vytažen). |

<a id="typ-curvemorphladder"></a>
#### `CurveMorphLadder`

*třída · Vnitřek renderu (běžný program nepotřebuje)*

Předpočítaná řada tvarů mezi dvěma křivkami, aby šlo přechod číst po vzorcích. Používá ji Morfující oscilátor. Stavba stojí kolem 10 ms — nikdy ne ze zvukového vlákna.

| člen | popis |
|---|---|
| `const int Steps = 64` | Počet kroků řady (64). |
| `float Sample(double mix, double x)` | Hodnota přechodu `mix` (0…1) v bodě `x`. |
| `static CurveMorphLadder For(Curve from, Curve to)` | Řada mezi dvěma tvary, ze sdílené mezipaměti, nebo nově postavená. |

<a id="typ-curveoutput"></a>
#### `CurveOutput`

*record · Křivky, melodie, text a formule*

Co modul potřebuje od křivky: rozsah hodnot a jestli se musí potkat konce. Tři použití se v tom liší: přenosová funkce (−1…1, konce volné), perioda vlny (−1…1, konce spojené), obálka (0…1).

| člen | popis |
|---|---|
| `CurveOutput(double Min, double Max, bool TieEndpoints)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `static readonly CurveOutput Bipolar` | −1…1, konce volné — přenosová funkce waveshaperu. |
| `static readonly CurveOutput Cycle` | −1…1, konce spojené — perioda vlny oscilátoru nebo LFO. |
| `static readonly CurveOutput Unipolar` | 0…1 — obálka. |
| `double Max { get; init; }` | Nejvyšší použitelná hodnota. |
| `double Min { get; init; }` | Nejnižší použitelná hodnota. |
| `bool TieEndpoints { get; init; }` | Jestli se y(0) a y(1) musí ve výchozím stavu rovnat. |
| `Curve Conform(Curve curve, bool tieEndpoints)` | Přizpůsobí křivku: body a tečny stáhne do rozsahu, při spojených koncích srovná poslední bod s prvním. |

<a id="typ-curvepoint"></a>
#### `CurvePoint`

*struktura · Křivky, melodie, text a formule*

Jeden bod křivky a dvě tečny, které se v něm potkávají.

| člen | popis |
|---|---|
| `CurvePoint(double X, double Y, CurveHandle Out = default, CurveHandle In = default)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `CurveHandle In { get; init; }` | Tečna přicházející do bodu z předchozího úseku (u prvního bodu bez významu). |
| `CurveHandle Out { get; init; }` | Tečna odcházející z bodu do dalšího úseku (u posledního bodu bez významu). |
| `double X { get; init; }` | Poloha na křivce, 0…1 (čas, fáze nebo vstupní úroveň — podle modulu). |
| `double Y { get; init; }` | Hodnota v bodě, −1…1. |
| `CurvePoint Clamped()` | Kopie bodu stažená do rozsahu X 0…1 a Y −1…1. |

<a id="typ-curvepreset"></a>
#### `CurvePreset`

*record · Křivky, melodie, text a formule*

Pojmenovaný výchozí tvar, který editor nabízí (sinus, pila…).

| člen | popis |
|---|---|
| `CurvePreset(string Name, Func<Curve> Create, bool UntieEndpoints = false)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `Func<Curve> Create { get; init; }` | Postaví novou křivku tvaru. |
| `string Name { get; init; }` | Jméno v nabídce. |
| `bool UntieEndpoints { get; init; }` | `true` pro tvar, který dává smysl jen s rozpojenými konci (pila). |

<a id="typ-curvepresets"></a>
#### `CurvePresets`

*statická třída · Křivky, melodie, text a formule*

Seznam hotových tvarů pro křivky.

| člen | popis |
|---|---|
| `static IReadOnlyList<CurvePreset> All { get; }` | Všechny hotové tvary. |

<a id="typ-formula"></a>
#### `Formula`

*třída · Křivky, melodie, text a formule*

Malý aritmetický jazyk modulu Formule: výraz se jednou přeloží a počítá na každém vzorku. Jen výraz, žádné cykly ani volání ven. Proměnné: `a`–`d` vstupy, `a1`, `a2` (a obdobně `b1`…) vstupy o 1 a 2 vzorky zpět, `y1`, `y2` vlastní výstup zpět, `t` čas v s, `p`, `q` knoflíky, `sr` kmitočet, `pi`, `e`. Seznamy a nápověda jsou ve `Variables`, `Functions`, `OperatorHelp`.

| člen | popis |
|---|---|
| `static readonly IReadOnlyList<(string Name, string Meaning)> Functions` | Funkce jazyka a jejich význam (jméno, popis). |
| `const string OperatorHelp = "+ − * / ^ (mocnina), % (zbytek po dělení), závorky, porovnání < > <= >= == != vrací 1 nebo 0. Desetinná čísla s TEČKOU (0.5) — čárka odděluje hodnoty ve funkcích"` | Nápověda k operátorům: `+ − * / ^ %`, závorky, porovnání vracející 1 nebo 0, desetinná tečka. |
| `static readonly IReadOnlyList<(string Name, string Meaning)> Variables` | Proměnné jazyka a jejich význam (jméno, popis). |
| `double[] Slots { get; }` | Hodnoty proměnných, které přeložený výraz čte, indexované podle `Formula.Slot`. Modul je před každým vzorkem zapíše. |
| `double Evaluate()` | Spočítá výraz z aktuálních `Slots`. |
| `static Formula? TryCompile(string source, out string? error)` | Přeloží výraz. Nikdy nehází: při chybě vrátí `null` a důvod dá do `error`. |

<a id="typ-formula-slot"></a>
#### `Formula.Slot`

*statická třída · Vnitřek renderu (běžný program nepotřebuje)*

Indexy proměnných v `Formula.Slots`.

| člen | popis |
|---|---|
| `const int A = 0` | Index vstupu `a`. |
| `const int B = 1` | Index vstupu `b`. |
| `const int C = 2` | Index vstupu `c`. |
| `const int Count = 18` | Počet slotů. |
| `const int D = 3` | Index vstupu `d`. |
| `const int History = 4` | První z osmi slotů historie (`a1, a2, b1, b2, c1, c2, d1, d2`); viz `Past`. |
| `const int P = 15` | Index knoflíku `p`. |
| `const int Q = 16` | Index knoflíku `q`. |
| `const int SampleRate = 17` | Index `sr` (vzorkovací kmitočet). |
| `const int T = 14` | Index `t` (čas v sekundách). |
| `const int Y1 = 12` | Index `y1` (výstup o vzorek zpět). |
| `const int Y2 = 13` | Index `y2` (výstup o dva vzorky zpět). |
| `static int Past(int input, int age)` | Index slotu se vstupem `input` (0–3 = a–d) před `age` (1–2) vzorky. |

<a id="typ-modulebase"></a>
#### `ModuleBase`

*abstraktní třída · Sestava: graf, moduly, knoflíky, porty*

Základ každého modulu („krabičky“) v sestavě: porty, knoflíky a render jednoho bloku. Všechny moduly z `ModuleCatalog` od něj dědí. Jméno modulu v sestavě je `Name` = `TypeName` + dovětek `Label`. Kromě číselných knoflíků (`Parameters`) může modul nést křivky, soubory, texty a melodie. Vlastní modul jde napsat zděděním (chráněné metody `Add…` stavějí porty a knoflíky v konstruktoru, `Process` renderuje), ale soubor `.zvuky` ho nenačte ani neuloží rozpoznatelně — serializér zná jen moduly z `ModuleCatalog`. Do grafu ho jde přidat jen kódem.

| člen | popis |
|---|---|
| `ModuleBase(string name)` | Konstruktor pro odvozený modul; `name` je `TypeName` (jméno typu v paletě a v sestavě). |
| `List<CurveInput> CurveInputs { get; }` | Zásuvky pro křivky z jiných modulů. |
| `List<ModuleCurve> Curves { get; }` | Křivky, které modul sám definuje (dnes jen modul Křivka). |
| `virtual string Description { get; }` | Česky, co modul dělá — tentýž text, jaký editor ukazuje pod knoflíky. Pro moduly z katalogu je celý v `API.md`, část 1. |
| `virtual IReadOnlyList<ModulePreset> Presets { get; }` | Hotové výchozí body knoflíků (předvolby), které editor nabízí v rozbalovátku „Předvolba…". Prázdné u modulů, kde předvolba nedává smysl; dnes je mají `Bubliny`, `Kolize`, `Trhliny`, `Harmonický hlas`, `Tření`, `Banka módů`, `Reverb` a `Delay`. |
| `void ApplyPreset(ModulePreset preset)` | Nastaví knoflíky a texty, které předvolba jmenuje; ostatní nechá. Hodnoty ořízne do rozsahu knoflíku, notovou hodnotu přepne zpět na číslo, Seed nikdy nemění. Dráty a hloubky modulace zůstávají. |
| `List<ModuleFile> Files { get; }` | Soubory, které modul čte (Sampler: vzorek). |
| `Guid Id { get; }` | Identita instance modulu (Guid). Ukládá se do souboru; klony hlasů ji sdílejí s originálem. |
| `List<Port> Inputs { get; }` | Vstupní porty, včetně modulačních vstupů knoflíků (ty se jmenují jako knoflík). |
| `string Label { get; set; }` | Dovětek autora („otvírání“, „vstup“); prázdný, když modul žádný nemá. Dá se přepsat. |
| `string Name { get; }` | Jméno modulu v sestavě: `TypeName`, za ním mezera a `Label`, je-li nějaký. Pod tímhle jménem se modul hledá. |
| `List<Port> Outputs { get; }` | Výstupní porty. |
| `List<ModuleParameter> Parameters { get; }` | Číselné a výběrové knoflíky modulu (`ModuleParameter`). |
| `List<ModuleSequence> Sequences { get; }` | Melodie, které modul přehrává (dnes jen Sekvencer). |
| `List<ModuleText> Texts { get; }` | Texty, které modul čte (dnes jen výraz Formule). |
| `string TypeName { get; }` | Jméno typu modulu tak, jak stojí v paletě a v sestavě („Filter“, „Nota“, „Vzorkovač“). Nedá se změnit. |
| `protected ModuleCurve AddCurve(string name, Curve curve, CurveOutput output, string xLabel, string yLabel)` | Pro odvozený modul: přidá vlastní křivku (s rozsahem a popisky os). |
| `protected CurveInput AddCurveInput(string name, Curve fallback, CurveOutput output)` | Pro odvozený modul: přidá zásuvku pro křivku z jiného modulu s náhradním tvarem. |
| `protected Port AddCurveOutput(string name)` | Pro odvozený modul: přidá výstup, který vydává křivku místo vzorků. |
| `protected ModuleFile AddFile(string name, string dialogFilter)` | Pro odvozený modul: přidá slot pro soubor (filtr dialogu je pro editor). |
| `protected Port AddInput(string name)` | Pro odvozený modul: přidá zvukový vstup. |
| `protected Port AddOutput(string name)` | Pro odvozený modul: přidá zvukový výstup. |
| `protected ModuleParameter AddParameter(string name, double value, double min, double max, string unit = "", string[]? options = null, bool modulatable = true, bool randomizable = false, bool isSeed = false, TempoSyncKind tempoSync = None)` | Pro odvozený modul: přidá knoflík s výchozí hodnotou, rozsahem a jednotkou. `options` = výběrový knoflík (hodnota je index volby). `modulatable` (výchozí true) přidá číselnému knoflíku modulační vstup stejného jména. `tempoSync` povolí notovou hodnotu. |
| `protected SeededRandomSource AddSeed()` | Pro odvozený modul: přidá knoflík `Seed` (náhodné výchozí semínko) a zdroj náhody, který z něj kreslí. |
| `protected ModuleSequence AddSequence(string name, IEnumerable<SequenceStep> steps)` | Pro odvozený modul: přidá melodii. |
| `protected ModuleText AddText(string name, string text, string hint)` | Pro odvozený modul: přidá text s výchozím obsahem a nápovědou. |
| `virtual double BatchNoteHoldSeconds(double bpm)` | Jak dlouho podle tohoto modulu drží nota při renderu celého klipu; 0 = modul nemá co říct (všechny kromě Noty). |
| `virtual Curve? GetCurveOutput(Port port)` | Křivka, kterou modul vydává na výstupu `port`; `null`, když žádnou nevydává. |
| `virtual void Prepare()` | Jednorázová příprava před renderem (rozbalení souboru, stavba tabulek). Volá ji hlas při stavbě a dávkový render předem — vždy z vlákna, které smí čekat. |
| `abstract IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)` | Vyrenderuje jeden blok: ze vstupních bufferů (jen zapojené vstupy; nezapojený = ticho) vrátí buffery výstupů. Volá ho `ModuleGraph.RenderModule`, ne program. |
| `virtual void ResetState()` | Vynuluje stav nesený mezi bloky (fáze, historie filtru, fáze obálky). Před dávkovým renderem se volá vždy; živý hlas stav naopak drží. |
| `protected static float[] GetInputOrSilence(IReadOnlyDictionary<Port, float[]> inputBuffers, Port port, RenderContext ctx)` | Pro odvozený modul: buffer vstupu, nebo vynulovaný (u živého hlasu půjčený), když vstup není zapojený. |
| `protected static float[] GetInputOrSilence(IReadOnlyDictionary<Port, float[]> inputBuffers, Port port, int sampleCount)` | Pro odvozený modul: buffer vstupu, nebo nové pole ticha dané délky. |
| `protected static float[] GetModulatedValues(ModuleParameter parameter, RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)` | Pro odvozený modul: hodnoty knoflíku po vzorcích = hodnota (případně z notové hodnoty) + modulace × hloubka. |
| `protected static float[] NewBuffer(RenderContext ctx)` | Pro odvozený modul: vynulovaný buffer na výstup (u živého hlasu půjčený z `BlockScratch`). |

<a id="typ-modulecurve"></a>
#### `ModuleCurve`

*třída, implementuje `INotifyPropertyChanged` · Křivky, melodie, text a formule*

Pojmenovaná křivka, kterou modul sám nese — knoflík, jehož hodnotou je tvar.

| člen | popis |
|---|---|
| `ModuleCurve(string name, Curve curve, CurveOutput output, string xLabel, string yLabel)` | Křivka se jménem, výchozím tvarem, požadovaným rozsahem a popisky os. |
| `bool AreEndpointsTied { get; }` | Jestli jsou konce křivky právě spojené. |
| `Curve Curve { get; set; }` | Aktuální tvar. Při zápisu se přizpůsobí `Output`. |
| `bool EndpointsUntied { get; set; }` | `true`, když autor konce úmyslně rozpojil (pila). |
| `bool IsDefault { get; }` | `true`, dokud křivka drží přesně výchozí tvar (pak se do souboru nezapisuje). |
| `string Name { get; }` | Jméno křivky. |
| `CurveOutput Output { get; }` | Rozsah a spojitost, které modul vyžaduje. |
| `IReadOnlyList<CurvePreset> Presets { get; }` | Hotové tvary, které editor u téhle křivky nabízí. |
| `string XLabel { get; }` | Co znamená vodorovná osa. |
| `string YLabel { get; }` | Co znamená svislá osa. |
| `event PropertyChangedEventHandler? PropertyChanged` | Oznámení o změně (pro vazby v editoru). |
| `void Apply(CurvePreset preset)` | Nahradí tvar hotovým tvarem (a případně rozpojí konce). |

<a id="typ-modulefile"></a>
#### `ModuleFile`

*třída, implementuje `INotifyPropertyChanged` · Sestava: graf, moduly, knoflíky, porty*

Pojmenovaná cesta k souboru, který modul čte — u Sampleru vzorek. `Path` je cesta (po `PatchSerializer.Load` absolutní), nebo `pamet:jméno`. `Status` po přípravě modulu říká, jak čtení dopadlo.

| člen | popis |
|---|---|
| `ModuleFile(string name, string dialogFilter)` | Slot se jménem a filtrem souborového dialogu. |
| `string DialogFilter { get; }` | Filtr souborového dialogu editoru. |
| `string Name { get; }` | Jméno slotu („Soubor“). |
| `string Path { get; set; }` | Cesta k souboru, nebo `pamet:jméno`; prázdná, když není vybraný. Přepsat ji jde kdykoli, platí od příští přípravy modulu. |
| `string? Status { get; set; }` | Výsledek posledního čtení: po úspěchu popis vzorku („2,31 s, mono, 44100 Hz“), po neúspěchu důvod („Soubor neexistuje.“); `null` před první přípravou. U hlasu se zapisuje do jeho klonu, ne do načtené sestavy. |
| `event PropertyChangedEventHandler? PropertyChanged` | Oznámení o změně (pro vazby v editoru). |

<a id="typ-modulegraph"></a>
#### `ModuleGraph`

*třída · Sestava: graf, moduly, knoflíky, porty*

Sestava jako graf: moduly a dráty mezi nimi. Renderuje se v pořadí závislostí, bez zpětných vazeb. Měň ji jen přes `AddModule`, `RemoveModule`, `Connect` a `Disconnect` — graf si nad seznamy drží mezipaměť pořadí a přímý zásah do `Modules`/`Connections` by ji nechal zastaralou.

| člen | popis |
|---|---|
| `ModuleGraph()` | Prázdná sestava. |
| `List<Connection> Connections { get; }` | Všechny dráty. Jen ke čtení — měnit přes `Connect`/`Disconnect`. |
| `List<ModuleBase> Modules { get; }` | Všechny moduly v pořadí souboru. Jen ke čtení — měnit přes `AddModule`/`RemoveModule`. |
| `void AddModule(ModuleBase module)` | Přidá modul. |
| `double BatchNoteHoldSeconds(double bpm)` | Jak dlouho drží nota při renderu celého klipu (nejdelší `Držení` ze všech Not); 0, když sestava Notu nemá. |
| `Connection Connect(Port source, Port target)` | Spojí výstup se vstupem a vrátí drát. Starý drát do téhož vstupu nahradí (bez hlášení). Hodí `InvalidOperationException` při špatném směru, drátu do sebe sama a spojení zvukového portu s křivkovým. Cyklus nekontroluje — ten se ozve až při renderu. |
| `void Disconnect(Connection connection)` | Odebere drát. |
| `void PrepareSwarms(ModuleBase target)` | Postaví předem kopie všech rojů na cestě k modulu `target` (obvykle Output). Hlas to dělá sám; hodí se k ověření zapojení roje hned po změně drátu (`InvalidWiringException`). |
| `void RemoveModule(ModuleBase module)` | Odebere modul i všechny jeho dráty. |
| `IReadOnlyDictionary<Port, float[]> RenderModule(ModuleBase targetModule, RenderContext ctx)` | Vyrenderuje modul `targetModule` a všechno, co do něj vede, a vrátí buffery všech výstupů na cestě (výstup sestavy je `vysledek[output.Outputs[0]]`). Hodí `CycleDetectedException` při cyklu a `InvalidWiringException` při chybném zapojení roje. Pro render celého klipu nejdřív `ResolveCurveInputs`, `Prepare`, `ResetState` a `NoteModule.SetBatchNote` — viz README, kapitola 3. |
| `void ResolveCurveInputs()` | Rozdá křivkovým zásuvkám tvary, které do nich vedou drátem. Před renderem celého klipu nutné, jinak křivkové obálky hrají jako rovná čára. |

<a id="typ-modulegraph-cycledetectedexception"></a>
#### `ModuleGraph.CycleDetectedException`

*výjimka, dědí z `ModuleGraph.InvalidWiringException` · Sestava: graf, moduly, knoflíky, porty*

Výjimka: sestava obsahuje cyklus (zpětnou vazbu drátem), který render neumí.

| člen | popis |
|---|---|
| `CycleDetectedException()` | Výjimka s českou hláškou o cyklu. |

<a id="typ-modulegraph-invalidwiringexception"></a>
#### `ModuleGraph.InvalidWiringException`

*výjimka, dědí z `Exception` · Sestava: graf, moduly, knoflíky, porty*

Výjimka: zapojení, které render odmítne (cyklus, drát z vnitřku roje ven mimo Konec roje, dva konce, roj v roji). Základ pro všechny takové chyby — stačí chytat tuhle. Hláška je česky a dá se ukázat uživateli.

| člen | popis |
|---|---|
| `InvalidWiringException(string message)` | Výjimka s hláškou. |

<a id="typ-moduleparameter"></a>
#### `ModuleParameter`

*třída, implementuje `INotifyPropertyChanged` · Sestava: graf, moduly, knoflíky, porty*

Knoflík modulu: číslo v rozsahu, nebo výběr z voleb. U výběrového knoflíku (`Options` není `null`) je `Value` index volby. Číselný knoflík může mít modulační vstup (`ModulationInput`): signál z drátu se přičítá k `Value` vynásobený `ModulationAmount`. Některé knoflíky umí notovou hodnotu (`IsTempoSynced`) — pak modul čte délku noty v tempu sestavy, ne `Value`.

| člen | popis |
|---|---|
| `ModuleParameter(string name, double value, double min, double max, string unit = "", string[]? options = null)` | Knoflík se jménem, hodnotou, rozsahem, jednotkou a případně volbami. Moduly je zakládají přes `ModuleBase.AddParameter`. |
| `NoteDivision Division { get; }` | Vybraná notová hodnota jako objekt. |
| `string DivisionName { get; set; }` | Jméno vybrané notové hodnoty („1 takt“, „1/4“, „1/4.“, „1/4T“…); jedna z `DivisionNames`. |
| `IReadOnlyList<string> DivisionNames { get; }` | Notové hodnoty, ze kterých jde vybírat. |
| `bool IsAutoSeeded { get; set; }` | U semínka: `true` = při každém spuštění se losuje nové, `Value` se ignoruje. Přepis `Value` pak nemá účinek. |
| `bool IsEnum { get; }` | `true` pro výběrový knoflík. |
| `bool IsModulatable { get; }` | `true`, když má knoflík modulační vstup. |
| `bool IsRandomizable { get; init; }` | `true`, když editor nabízí tlačítko „nové náhodné“ (semínka). |
| `bool IsSeed { get; init; }` | `true` pro knoflík `Seed` — semínko náhody modulu. Hlas ho posouvá, aby dva hlasy nezněly stejně. |
| `bool IsTempoSyncable { get; }` | `true`, když jde knoflík přepnout na notovou hodnotu. |
| `bool IsTempoSynced { get; set; }` | `true` = modul čte notovou hodnotu (`DivisionName`) v tempu sestavy místo `Value`. Přepis `Value` pak nemá účinek. |
| `double Max { get; }` | Horní mez rozsahu. |
| `double Min { get; }` | Dolní mez rozsahu. |
| `double ModulationAmount { get; set; }` | Hloubka modulace: čím se násobí signál z modulačního vstupu, než se přičte k `Value`. |
| `double ModulationAmountLimit { get; }` | Doporučená mez hloubky pro posuvník editoru (podle rozsahu knoflíku). |
| `Port? ModulationInput { get; }` | Modulační vstup knoflíku (port se jménem knoflíku), nebo `null` u knoflíku, který modulovat nejde. |
| `string Name { get; }` | Jméno knoflíku („Cutoff“, „Držení“). |
| `string[]? Options { get; }` | Jména voleb výběrového knoflíku, jinak `null`. |
| `bool ShowsDivisionPicker { get; }` | Pro editor: ukázat výběr notové hodnoty. |
| `bool ShowsPlainValue { get; }` | Pro editor: ukázat posuvník s číslem. |
| `TempoSyncKind TempoSync { get; init; }` | Jakou jednotku má notová hodnota (sekundy, milisekundy, rychlost), nebo `None`. |
| `string Unit { get; }` | Jednotka pro zobrazení („Hz“, „s“, „ms“), může být prázdná. |
| `double Value { get; set; }` | Hodnota knoflíku (u výběru index volby). Zapisovat jde cokoli; rozsah hlídá jen volající. Zápis platí pro hlasy postavené potom. |
| `event PropertyChangedEventHandler? PropertyChanged` | Oznámení o změně (pro vazby v editoru). |
| `bool IsTempoValueClamped(double bpm)` | `true`, když se notová hodnota při daném tempu nevejde do rozsahu a byla oříznuta. |
| `void Randomize()` | Nastaví náhodnou celočíselnou hodnotu z rozsahu (tlačítko u semínka). |
| `double ResolveValue(double bpm)` | Hodnota, kterou modul opravdu čte při tempu `bpm`: z notové hodnoty, je-li zapnutá, jinak `Value`; oříznutá do rozsahu. |

<a id="typ-modulesequence"></a>
#### `ModuleSequence`

*třída, implementuje `INotifyPropertyChanged` · Křivky, melodie, text a formule*

Pojmenovaná melodie modulu — seznam not (`SequenceStep`).

| člen | popis |
|---|---|
| `ModuleSequence(string name, IEnumerable<SequenceStep> steps)` | Melodie se jménem a kroky. |
| `static readonly IReadOnlyList<string> NoteNames` | Jména všech 128 MIDI not („C-1“ … „G9“), pro výběr v editoru. |
| `string Name { get; }` | Jméno melodie („Kroky“). |
| `ObservableCollection<SequenceStep> Steps { get; }` | Kroky melodie (pozorovatelná kolekce). |
| `int TotalSteps { get; }` | Délka celé melodie v krocích (součet `Length`). |
| `event PropertyChangedEventHandler? PropertyChanged` | Oznámení o změně (pro vazby v editoru). |
| `SequenceStep Add(SequenceStep? after = null)` | Přidá krok za `after` (nebo na konec) se stejnou výškou a vrátí ho. |
| `void Remove(SequenceStep step)` | Odebere krok. |
| `void Replace(IEnumerable<SequenceStep> steps)` | Nahradí všechny kroky najednou. |

<a id="typ-modulepreset"></a>
#### `ModulePreset`

*záznam (`record`) · Křivky, melodie, text a formule*

Pojmenovaný výchozí bod knoflíků modulu — viz `ModuleBase.Presets` a `ModuleBase.ApplyPreset`.

| člen | popis |
|---|---|
| `ModulePreset(string Name, string Description, IReadOnlyDictionary<string, double> Values, IReadOnlyDictionary<string, string>? Texts = null)` | Jméno, jedna věta popisu, hodnoty knoflíků podle jména (u výběrového knoflíku index volby) a případně texty podle jména (u `Banka módů` text `Módy`). |

<a id="typ-moduletext"></a>
#### `ModuleText`

*třída, implementuje `INotifyPropertyChanged` · Křivky, melodie, text a formule*

Pojmenovaný text, který modul čte jako obsah — dnes jen výraz modulu Formule.

| člen | popis |
|---|---|
| `ModuleText(string name, string text, string hint)` | Text se jménem, obsahem a nápovědou. |
| `string? Error { get; set; }` | Proč modul text nemohl použít (chyba výrazu), nebo `null`. |
| `bool HasError { get; }` | `true`, když je `Error` nastavená. |
| `string Hint { get; }` | Nápověda, co se sem píše. |
| `string Name { get; }` | Jméno textu (u Formule „Formule“). |
| `string Text { get; set; }` | Obsah. |
| `event PropertyChangedEventHandler? PropertyChanged` | Oznámení o změně (pro vazby v editoru). |

<a id="typ-notedivision"></a>
#### `NoteDivision`

*record · Sestava: graf, moduly, knoflíky, porty*

Notová hodnota pro knoflík synchronizovaný s tempem („4 takty“ … „1/64“, s tečkou „1/4.“, triola „1/4T“), měřená v dobách.

| člen | popis |
|---|---|
| `NoteDivision(string Name, double Beats)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `static IReadOnlyList<NoteDivision> All { get; }` | Všechny notové hodnoty, od nejdelší. |
| `double Beats { get; init; }` | Délka v dobách (čtvrťových notách). Tečka ×1,5, triola ×2/3. |
| `static NoteDivision Default { get; }` | Výchozí hodnota: jedna doba. |
| `string Name { get; init; }` | Jméno, pod kterým se ukládá do souboru. |
| `static IReadOnlyList<string> Names { get; }` | Jména všech notových hodnot. |
| `double ToSeconds(double bpm)` | Délka v sekundách při tempu `bpm`. |
| `static NoteDivision ByName(string? name)` | Notová hodnota podle jména; neznámé jméno vrátí `Default` (soubor z novější verze se otevře). |

<a id="typ-port"></a>
#### `Port`

*třída · Sestava: graf, moduly, knoflíky, porty*

Port modulu: vstup nebo výstup, zvukový nebo křivkový.

| člen | popis |
|---|---|
| `Port(ModuleBase owner, string name, PortDirection direction, PortKind kind = Audio)` | Port modulu. Moduly je zakládají přes `ModuleBase.AddInput`/`AddOutput`. |
| `PortDirection Direction { get; }` | Vstup, nebo výstup. |
| `PortKind Kind { get; }` | Co port nese: zvuk (vzorky), nebo křivku (tvar). |
| `string Name { get; }` | Jméno portu („In“, „Out“, „Gate“, nebo jméno knoflíku u modulačního vstupu). |
| `ModuleBase Owner { get; }` | Modul, kterému port patří. |

Přepisuje z `Object`: `ToString`.

<a id="typ-portdirection"></a>
#### `PortDirection`

*výčet · Sestava: graf, moduly, knoflíky, porty*

Směr portu.

| člen | popis |
|---|---|
| `Input = 0` | Vstup. |
| `Output = 1` | Výstup. |

<a id="typ-portkind"></a>
#### `PortKind`

*výčet · Sestava: graf, moduly, knoflíky, porty*

Co teče drátem: zvuk po vzorcích, nebo tvar (křivka). Různé druhy spojit nejde.

| člen | popis |
|---|---|
| `Audio = 0` | Zvuk nebo řídicí signál: `float[]` na každý blok. |
| `Curve = 1` | Tvar (`Curve`), předaný jednou před renderem. |

<a id="typ-rendercontext"></a>
#### `RenderContext`

*třída · Sestava: graf, moduly, knoflíky, porty*

Parametry jednoho průchodu renderu: kmitočet, počet vzorků, tempo. Pro render celého klipu ho vytváříš sám (`new RenderContext(kmitočet, vzorků, Timing.Bpm)`); hlas si ho vytváří na každý blok.

| člen | popis |
|---|---|
| `RenderContext(int sampleRate, int sampleCount, double bpm = 128)` | Kontext pro `sampleCount` vzorků při `sampleRate` Hz a tempu `bpm`. Tempo dávej z `PatchTiming.Bpm` sestavy, jinak knoflíky na notové hodnotě zazní ve 128 BPM. |
| `double Bpm { get; }` | Tempo, podle kterého se počítají notové hodnoty. |
| `double DurationSeconds { get; }` | Délka průchodu v sekundách. |
| `bool? LiveKeyDown { get; }` | U živého hlasu: jestli je nota stisknutá; `null` při renderu celého klipu. |
| `bool NoteHoldConsumed { get; }` | Nastaví obálka, která v průchodu tvaruje délku noty, aby ji neořízl ještě modul Output. |
| `double NoteHoldSeconds { get; }` | Jak dlouho drží nota při renderu celého klipu (z modulu Nota); 0, když sestava neříká. |
| `int SampleCount { get; }` | Počet vzorků průchodu. |
| `int SampleRate { get; }` | Vzorkovací kmitočet. |

<a id="typ-seededrandomsource"></a>
#### `SeededRandomSource`

*třída · Vnitřek renderu (běžný program nepotřebuje)*

Zdroj náhody svázaný s knoflíkem `Seed`, aby náhodné moduly hrály pokaždé stejně.

| člen | popis |
|---|---|
| `SeededRandomSource(ModuleParameter seed)` | Zdroj nad knoflíkem semínka. |
| `const int MaxSeed = 1000000` | Horní mez semínka (1 000 000). |
| `Random GetGenerator(out bool wasReseeded)` | Generátor náhody; když se semínko od minula změnilo, postaví nový (`wasReseeded` = true). |
| `void Reset()` | Začne posloupnost znovu od semínka (nebo s novým, když se losuje). |

<a id="typ-sequencestep"></a>
#### `SequenceStep`

*třída, implementuje `INotifyPropertyChanged` · Křivky, melodie, text a formule*

Jedna nota melodie: výška, délka v krocích a jestli je to pomlka.

| člen | popis |
|---|---|
| `SequenceStep()` | Nota C4 dlouhá jeden krok. |
| `SequenceStep(int note, int length = 1, bool rest = false)` | Nota `note` (MIDI) dlouhá `length` kroků; `rest` = pomlka. |
| `const int MaxLength = 64` | Nejdelší nota v krocích (64). |
| `double FrequencyHz { get; }` | Kmitočet noty. |
| `int Length { get; set; }` | Kolik kroků nota trvá. |
| `int Note { get; set; }` | MIDI číslo noty (60 = C4). |
| `string NoteName { get; }` | Jméno noty („E4“). |
| `bool Rest { get; set; }` | `true` = pomlka stejné délky místo noty. |
| `event PropertyChangedEventHandler? PropertyChanged` | Oznámení o změně (pro vazby v editoru). |

<a id="typ-tempodisplay"></a>
#### `TempoDisplay`

*třída, implementuje `INotifyPropertyChanged` · Pro editor (cizí program je nepotřebuje)*

Tempo, které editor ukazuje u knoflíků na notové hodnotě. Na render nemá vliv.

| člen | popis |
|---|---|
| `double Bpm { get; set; }` | Zobrazované tempo. |
| `static TempoDisplay Current { get; }` | Sdílená instance. |
| `event PropertyChangedEventHandler? PropertyChanged` | Oznámení o změně (pro vazby v editoru). |

<a id="typ-temposynckind"></a>
#### `TempoSyncKind`

*výčet · Sestava: graf, moduly, knoflíky, porty*

Co znamená notová hodnota knoflíku po převodu na číslo.

| člen | popis |
|---|---|
| `None = 0` | Knoflík notovou hodnotu neumí (výšky, hlasitosti, semínka). |
| `Seconds = 1` | Délka v sekundách (fáze obálky, zpoždění, doznění). |
| `Milliseconds = 2` | Délka v milisekundách. |
| `Rate = 3` | Rychlost (LFO v Hz, spouštěč za sekundu): notová hodnota je perioda, modul dostane její převrácenou hodnotu. |

### ZvukEfekty.Core.Modules

<a id="typ-blockbuilder"></a>
#### `BlockBuilder`

*třída · Bloky (předpřipravená zapojení)*

Pomocník pro psaní definice bloku: sbírá moduly, dráty a vývody.

| člen | popis |
|---|---|
| `BlockBuilder(string title, string description)` | Nový blok s titulkem a popisem. |
| `T Add<T>(T module, double x, double y)` | Přidá modul na pozici (x, y) vůči levému hornímu rohu bloku a vrátí ho. |
| `BlockDefinition Build()` | Hotová definice bloku. |
| `void Publish(string name, Port port)` | Vyvede vnitřní port na okraj bloku pod jménem bloku. |
| `void Wire(Port source, Port target)` | Přidá drát mezi dvěma moduly bloku. |
| `static void Drive(ModuleBase module, string parameter, double baseValue, double amount)` | Nastaví knoflíku `parameter` základní hodnotu a hloubku modulace najednou. |
| `static Port In(ModuleBase module, string name)` | Vstupní port modulu podle jména (hodí výjimku, když neexistuje). |
| `static ModuleParameter Knob(ModuleBase module, string name)` | Knoflík modulu podle jména (hodí výjimku, když neexistuje). |
| `static Port Out(ModuleBase module, string name)` | Výstupní port modulu podle jména (hodí výjimku, když neexistuje). |

<a id="typ-blockcatalog"></a>
#### `BlockCatalog`

*statická třída · Bloky (předpřipravená zapojení)*

Bloky, které editor nabízí v paletě. Seznam s popisy je v `API.md`, část 2.

| člen | popis |
|---|---|
| `const string CategoryName = "Bloky"` | Jméno kategorie bloků v paletě („Bloky“). |
| `static IReadOnlyList<BlockCatalogEntry> Entries { get; }` | Všechny bloky. |

<a id="typ-blockcatalogentry"></a>
#### `BlockCatalogEntry`

*record, implementuje `IPaletteEntry` · Bloky (předpřipravená zapojení)*

Položka palety pro jeden blok: jméno, popis a jak ho postavit.

| člen | popis |
|---|---|
| `BlockCatalogEntry(string DisplayName, string Summary, Func<BlockDefinition> Create)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `string Category { get; }` | Kategorie v paletě (vždy `BlockCatalog.CategoryName`). |
| `Func<BlockDefinition> Create { get; init; }` | Postaví novou definici bloku (nové instance modulů). |
| `string DisplayName { get; init; }` | Jméno bloku v paletě. |
| `string Summary { get; init; }` | Jednou větou, k čemu blok je. |

<a id="typ-blockdefinition"></a>
#### `BlockDefinition`

*třída · Bloky (předpřipravená zapojení)*

Hotové zapojení bloku: moduly s pozicemi, dráty mezi nimi a vývody na okraji. Každé zavolání `BlockCatalogEntry.Create` vrátí novou definici s novými instancemi modulů.

| člen | popis |
|---|---|
| `IReadOnlyList<(Port Source, Port Target)> Connections { get; }` | Dráty uvnitř bloku (výstup, vstup). |
| `string Description { get; }` | Popis do poznámky rámečku pod titulkem. |
| `IReadOnlyList<BlockModule> Modules { get; }` | Moduly bloku s pozicemi. |
| `IReadOnlyList<BlockPort> Ports { get; }` | Vývody na okraji bloku. |
| `string Title { get; }` | Titulek rámečku. |

<a id="typ-blockmodule"></a>
#### `BlockModule`

*record · Bloky (předpřipravená zapojení)*

Modul uvnitř bloku a jeho pozice vůči levému hornímu rohu bloku.

| člen | popis |
|---|---|
| `BlockModule(ModuleBase Module, double X, double Y)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `ModuleBase Module { get; init; }` | Modul. |
| `double X { get; init; }` | Vodorovná pozice. |
| `double Y { get; init; }` | Svislá pozice. |

<a id="typ-blockport"></a>
#### `BlockPort`

*record · Bloky (předpřipravená zapojení)*

Vývod na okraji bloku a vnitřní port, za který stojí.

| člen | popis |
|---|---|
| `BlockPort(string Name, Port Port)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `string Name { get; init; }` | Jméno vývodu. |
| `Port Port { get; init; }` | Vnitřní port. |

<a id="typ-collisionpopulationmodule"></a>
#### `CollisionPopulationModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Kolize“ (kategorie Zdroje). Co dělá, porty a knoflíky: [část 1, Kolize](#modul-collisionpopulationmodule).

| člen | popis |
|---|---|
| `CollisionPopulationModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Presets`, `Process`, `ResetState`.

<a id="typ-compressormodule"></a>
#### `CompressorModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Kompresor“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Kompresor](#modul-compressormodule).

| člen | popis |
|---|---|
| `CompressorModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-crackcascademodule"></a>
#### `CrackCascadeModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Trhliny“ (kategorie Zdroje). Co dělá, porty a knoflíky: [část 1, Trhliny](#modul-crackcascademodule).

| člen | popis |
|---|---|
| `CrackCascadeModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `long DroppedCracks { get; }` | Kolik trhlin se od posledního `ResetState` nevešlo do fronty (256) a zahodilo se — diagnostika. |

Přepisuje z `ModuleBase`: `Description`, `Presets`, `Process`, `ResetState`.

<a id="typ-curveenvelopemodule"></a>
#### `CurveEnvelopeModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Obálka z křivky“ (kategorie Modulace). Co dělá, porty a knoflíky: [část 1, Obálka z křivky](#modul-curveenvelopemodule).

| člen | popis |
|---|---|
| `CurveEnvelopeModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `const string SustainPointName = "Bod sustainu"` | Jméno knoflíku bodu, na kterém obálka drží, dokud je nota stisknutá („Bod sustainu“). |
| `CurveInput Shape { get; }` | Tvar, který obálka opravdu hraje: zapojená křivka (stažená do 0…1), nebo vestavěné ADSR. |
| `ModuleParameter SustainPoint { get; }` | Knoflík „Bod sustainu“. |
| `double OneShotSecondsAt(double bpm, double noteHoldSeconds = 0)` | Jak dlouho obálka potřebuje na doběhnutí bez zapojené brány, při tempu `bpm` a držení noty `noteHoldSeconds`. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-curvemodule"></a>
#### `CurveModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Křivka“ (kategorie Modulace). Co dělá, porty a knoflíky: [část 1, Křivka](#modul-curvemodule).

| člen | popis |
|---|---|
| `CurveModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `const string LengthName = "Délka"` | Jméno knoflíku délky křivky („Délka“). |

Přepisuje z `ModuleBase`: `Description`, `GetCurveOutput`, `Process`, `ResetState`.

<a id="typ-delaylinemodule"></a>
#### `DelayLineModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Zpoždění“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Zpoždění](#modul-delaylinemodule).

| člen | popis |
|---|---|
| `DelayLineModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-delaymodule"></a>
#### `DelayModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Delay“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Delay](#modul-delaymodule).

| člen | popis |
|---|---|
| `DelayModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Presets`, `Process`, `ResetState`.

<a id="typ-distortionmodule"></a>
#### `DistortionModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Distortion“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Distortion](#modul-distortionmodule).

| člen | popis |
|---|---|
| `DistortionModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-envelopefollowermodule"></a>
#### `EnvelopeFollowerModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Sledovač obálky“ (kategorie Modulace). Co dělá, porty a knoflíky: [část 1, Sledovač obálky](#modul-envelopefollowermodule).

| člen | popis |
|---|---|
| `EnvelopeFollowerModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-envelopemodule"></a>
#### `EnvelopeModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Envelope“ (kategorie Modulace). Co dělá, porty a knoflíky: [část 1, Envelope](#modul-envelopemodule).

| člen | popis |
|---|---|
| `EnvelopeModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `const double AutomaticHold = 0` | Hodnota knoflíku `Držení` (0), která znamená „plošinku natáhni do konce klipu“. |
| `const string HoldName = "Držení"` | Jméno knoflíku držení („Držení“). |
| `IReadOnlyList<ModuleParameter> ShapeParameters { get; }` | Knoflíky, ze kterých je tvar obálky (pro náhled). |
| `double OneShotSecondsAt(double bpm, double noteHoldSeconds = 0)` | Jak dlouho obálka potřebuje na doběhnutí včetně doznění bez zapojené brány, při tempu `bpm` a držení noty `noteHoldSeconds`. |
| `Curve ShapeAsCurve()` | Tvar obálky jako křivka, pro náhled. Na render se nepoužívá. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-filtermodule"></a>
#### `FilterModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Filter“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Filter](#modul-filtermodule).

| člen | popis |
|---|---|
| `FilterModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-formulamodule"></a>
#### `FormulaModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Formule“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Formule](#modul-formulamodule).

| člen | popis |
|---|---|
| `FormulaModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `ModuleText Expression { get; }` | Text výrazu (`ModuleText`); `Error` říká, proč se nepřeložil. |

Přepisuje z `ModuleBase`: `Description`, `Prepare`, `Process`, `ResetState`.

<a id="typ-gainmodule"></a>
#### `GainModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Gain“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Gain](#modul-gainmodule).

| člen | popis |
|---|---|
| `GainModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`.

<a id="typ-granulatormodule"></a>
#### `GranulatorModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Granulátor“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Granulátor](#modul-granulatormodule).

| člen | popis |
|---|---|
| `GranulatorModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-ipaletteentry"></a>
#### `IPaletteEntry`

*rozhraní · Katalog modulů a moduly*

Cokoli, co umí paleta editoru ukázat: modul (`ModuleCatalogEntry`) nebo blok (`BlockCatalogEntry`).

| člen | popis |
|---|---|
| `abstract string Category { get; }` | Kategorie v paletě. |
| `abstract string DisplayName { get; }` | Jméno v paletě. |
| `abstract string Summary { get; }` | K čemu položka je (nápověda v paletě). |

<a id="typ-harmonicvoicemodule"></a>
#### `HarmonicVoiceModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Harmonický hlas“ (kategorie Zdroje). Co dělá, porty a knoflíky: [část 1, Harmonický hlas](#modul-harmonicvoicemodule).

| člen | popis |
|---|---|
| `HarmonicVoiceModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Presets`, `Process`, `ResetState`.

<a id="typ-invertermodule"></a>
#### `InverterModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Invertor“ (kategorie Modulace). Co dělá, porty a knoflíky: [část 1, Invertor](#modul-invertermodule).

| člen | popis |
|---|---|
| `InverterModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`.

<a id="typ-eventsequencemodule"></a>
#### `EventSequenceModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Události“ (kategorie Modulace). Co dělá, porty a knoflíky: [část 1, Události](#modul-eventsequencemodule).

| člen | popis |
|---|---|
| `EventSequenceModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `const int MaxEvents` | Nejvíc událostí v textu `Události` (1024). |
| `ModuleText Events { get; }` | Text `Události`; jeho `Error` říká, proč se nedal přečíst. |

Přepisuje z `ModuleBase`: `Description`, `Prepare`, `Process`, `ResetState`.

<a id="typ-frictioncontactmodule"></a>
#### `FrictionContactModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Tření“ (kategorie Zdroje). Co dělá, porty a knoflíky: [část 1, Tření](#modul-frictioncontactmodule).

| člen | popis |
|---|---|
| `FrictionContactModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Presets`, `Prepare`, `Process`, `ResetState`.

<a id="typ-fluidflowmodule"></a>
#### `FluidFlowModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Bubliny“ (kategorie Zdroje). Co dělá, porty a knoflíky: [část 1, Bubliny](#modul-fluidflowmodule).

| člen | popis |
|---|---|
| `FluidFlowModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Presets`, `Process`, `ResetState`.

<a id="typ-karplusstrongmodule"></a>
#### `KarplusStrongModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Struna“ (kategorie Zdroje). Co dělá, porty a knoflíky: [část 1, Struna](#modul-karplusstrongmodule).

| člen | popis |
|---|---|
| `KarplusStrongModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-lfomodule"></a>
#### `LfoModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „LFO“ (kategorie Modulace). Co dělá, porty a knoflíky: [část 1, LFO](#modul-lfomodule).

| člen | popis |
|---|---|
| `LfoModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-linlogmodule"></a>
#### `LinLogModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Převodník lin/log“ (kategorie Modulace). Co dělá, porty a knoflíky: [část 1, Převodník lin/log](#modul-linlogmodule).

| člen | popis |
|---|---|
| `LinLogModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `const string RatioName = "Poměr"` | Jméno knoflíku poměru („Poměr“). |

Přepisuje z `ModuleBase`: `Description`, `Process`.

<a id="typ-mixermodule"></a>
#### `MixerModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Mixer“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Mixer](#modul-mixermodule).

| člen | popis |
|---|---|
| `MixerModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`.

<a id="typ-modulecatalog"></a>
#### `ModuleCatalog`

*statická třída · Katalog modulů a moduly*

Seznam všech modulů, které knihovna zná — a tedy i těch, které umí načíst ze souboru. Soubor `.zvuky` s modulem, který tu není, se nenačte. Celý seznam s porty a knoflíky je v `API.md`, část 1.

| člen | popis |
|---|---|
| `static IReadOnlyList<ModuleCategory> Categories { get; }` | Moduly a bloky rozdělené do kategorií palety (Zdroje, Modulace, Zpracování, Vizualizace, Výstup, Bloky). |
| `static IReadOnlyList<ModuleCatalogEntry> Entries { get; }` | Všechny moduly v pořadí palety. |

<a id="typ-modulecatalogentry"></a>
#### `ModuleCatalogEntry`

*record, implementuje `IPaletteEntry` · Katalog modulů a moduly*

Položka katalogu pro jeden modul: jméno v paletě, kategorie a jak ho vytvořit.

| člen | popis |
|---|---|
| `ModuleCatalogEntry(string DisplayName, string Category, Func<ModuleBase> Create)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `string Category { get; init; }` | Kategorie v paletě. |
| `Func<ModuleBase> Create { get; init; }` | Vytvoří nový modul s výchozími knoflíky. |
| `string DisplayName { get; init; }` | Jméno v paletě — nemusí se shodovat s `ModuleBase.TypeName` („Vzorkovač (S&H)“ proti „Vzorkovač“). |
| `string Summary { get; }` | Popis modulu (`ModuleBase.Description`). |

<a id="typ-modulecategory"></a>
#### `ModuleCategory`

*record · Katalog modulů a moduly*

Jedna kategorie palety a její položky.

| člen | popis |
|---|---|
| `ModuleCategory(string Name, IReadOnlyList<IPaletteEntry> Entries)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `IReadOnlyList<IPaletteEntry> Entries { get; init; }` | Položky kategorie (moduly nebo bloky). |
| `string Name { get; init; }` | Jméno kategorie. |

<a id="typ-modalbankmodule"></a>
#### `ModalBankModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Banka módů“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Banka módů](#modul-modalbankmodule).

| člen | popis |
|---|---|
| `ModalBankModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `const int MaxModes` | Nejvíc módů v textu `Módy` (64). |
| `ModuleText Modes { get; }` | Text `Módy`; jeho `Error` říká, proč se nedal přečíst. |

Přepisuje z `ModuleBase`: `Description`, `Presets`, `Prepare`, `Process`, `ResetState`.

<a id="typ-noisemodule"></a>
#### `NoiseModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Noise“ (kategorie Zdroje). Co dělá, porty a knoflíky: [část 1, Noise](#modul-noisemodule).

| člen | popis |
|---|---|
| `NoiseModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-notemodule"></a>
#### `NoteModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Nota (klávesnice)“ (kategorie Zdroje). Co dělá, porty a knoflíky: [část 1, Nota (klávesnice)](#modul-notemodule).

| člen | popis |
|---|---|
| `NoteModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `const int UpperRowFirstMidiNote = 60` | První MIDI nota horní řady klávesnice editoru (60). |
| `double CurrentFrequencyHz { get; set; }` | Kmitočet znějící noty; nastavuje ho hlas před každým blokem (0 = nic nezní). |
| `int CurrentMidiNote { get; set; }` | MIDI číslo znějící noty; nastavuje ho hlas. |
| `bool IsDrivenLive { get; set; }` | `true`, když modul řídí živý hlas; `false` při renderu celého klipu (pak bránu zavře sám po `Držení`). Nastavuje hlas, ne program. |
| `bool IsGateOpen { get; set; }` | Jestli je nota stisknutá; nastavuje hlas. |
| `ModuleParameter KeyRangeParameter { get; }` | Knoflík „Klávesnice“: na které noty modul reaguje (celá, dolní řada, horní řada). |
| `bool AcceptsMidiNote(int midiNote)` | `true`, když nota spadá do rozsahu knoflíku „Klávesnice“. |
| `double HoldSecondsAt(double bpm)` | Jak dlouho je nota při renderu celého klipu stisknutá (knoflík `Držení`) při tempu `bpm`. |
| `void SetBatchNote(int midiNote)` | Nastaví notu pro render celého klipu. Bez toho sestava s Notou v dávce mlčí. Hlas to nepotřebuje. |

Přepisuje z `ModuleBase`: `Description`, `BatchNoteHoldSeconds`, `Process`.

<a id="typ-oscillatormodule"></a>
#### `OscillatorModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Oscillator“ (kategorie Zdroje). Co dělá, porty a knoflíky: [část 1, Oscillator](#modul-oscillatormodule).

| člen | popis |
|---|---|
| `OscillatorModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `const string AntialiasName = "Vyhlazení hran"` | Jméno knoflíku vyhlazení hran („Vyhlazení hran“). |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-outputmodule"></a>
#### `OutputModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Output“ (kategorie Výstup). Co dělá, porty a knoflíky: [část 1, Output](#modul-outputmodule).

| člen | popis |
|---|---|
| `OutputModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-poissontriggermodule"></a>
#### `PoissonTriggerModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Nepravidelný spouštěč“ (kategorie Modulace). Co dělá, porty a knoflíky: [část 1, Nepravidelný spouštěč](#modul-poissontriggermodule).

| člen | popis |
|---|---|
| `PoissonTriggerModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-pulsedividermodule"></a>
#### `PulseDividerModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Dělič impulsů“ (kategorie Modulace). Co dělá, porty a knoflíky: [část 1, Dělič impulsů](#modul-pulsedividermodule).

| člen | popis |
|---|---|
| `PulseDividerModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-quantizermodule"></a>
#### `QuantizerModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Kvantizér“ (kategorie Modulace). Co dělá, porty a knoflíky: [část 1, Kvantizér](#modul-quantizermodule).

| člen | popis |
|---|---|
| `QuantizerModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-resonatormodule"></a>
#### `ResonatorModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Resonator“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Resonator](#modul-resonatormodule).

| člen | popis |
|---|---|
| `ResonatorModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-reverbmodule"></a>
#### `ReverbModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Reverb“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Reverb](#modul-reverbmodule).

| člen | popis |
|---|---|
| `ReverbModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Presets`, `Process`, `ResetState`.

<a id="typ-ringmodulatormodule"></a>
#### `RingModulatorModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Ring Modulator“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Ring Modulator](#modul-ringmodulatormodule).

| člen | popis |
|---|---|
| `RingModulatorModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`.

<a id="typ-sampleholdmodule"></a>
#### `SampleHoldModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Vzorkovač (S&H)“ (kategorie Modulace). Co dělá, porty a knoflíky: [část 1, Vzorkovač (S&H)](#modul-sampleholdmodule).

| člen | popis |
|---|---|
| `SampleHoldModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-samplermodule"></a>
#### `SamplerModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Sampler“ (kategorie Zdroje). Co dělá, porty a knoflíky: [část 1, Sampler](#modul-samplermodule).

| člen | popis |
|---|---|
| `SamplerModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Prepare`, `Process`, `ResetState`.

<a id="typ-scopemodule"></a>
#### `ScopeModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Osciloskop / Spektrum“ (kategorie Vizualizace). Co dělá, porty a knoflíky: [část 1, Osciloskop / Spektrum](#modul-scopemodule).

| člen | popis |
|---|---|
| `ScopeModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `bool IsSpectrumMode { get; }` | `true`, když náhled kreslí spektrum místo průběhu (jen editor). |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-sequencermodule"></a>
#### `SequencerModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Sekvencer“ (kategorie Zdroje). Co dělá, porty a knoflíky: [část 1, Sekvencer](#modul-sequencermodule).

| člen | popis |
|---|---|
| `SequencerModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `const string SequenceName = "Kroky"` | Jméno melodie („Kroky“). |
| `const string StepLengthName = "Délka kroku"` | Jméno knoflíku délky kroku („Délka kroku“). |
| `ModuleSequence Sequence { get; }` | Melodie, kterou modul hraje. |
| `double TotalSecondsAt(double bpm)` | Jak dlouho celá melodie trvá při tempu `bpm`. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-slewmodule"></a>
#### `SlewModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Sklouznutí“ (kategorie Modulace). Co dělá, porty a knoflíky: [část 1, Sklouznutí](#modul-slewmodule).

| člen | popis |
|---|---|
| `SlewModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-spectrogrammodule"></a>
#### `SpectrogramModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Spektrogram“ (kategorie Vizualizace). Co dělá, porty a knoflíky: [část 1, Spektrogram](#modul-spectrogrammodule).

| člen | popis |
|---|---|
| `SpectrogramModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `double FloorDb { get; }` | Nejnižší zobrazená úroveň v dB (knoflík). |
| `bool IsLogarithmicAxis { get; }` | `true`, když je osa kmitočtu logaritmická. |
| `double MaxHz { get; }` | Nejvyšší zobrazený kmitočet. |
| `SpectrogramPalette Palette { get; }` | Barevná škála náhledu. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-spectrogrampalette"></a>
#### `SpectrogramPalette`

*výčet · Katalog modulů a moduly*

Barevná škála, kterou modul Spektrogram kreslí v editoru. Na zvuk nemá vliv.

| člen | popis |
|---|---|
| `Grey = 0` | Šedá. |
| `Inferno = 1` | Inferno (černá → fialová → žlutá). |
| `Viridis = 2` | Viridis (fialová → zelená → žlutá). |
| `Rainbow = 3` | Duha. |

<a id="typ-swarmendmodule"></a>
#### `SwarmEndModule`

*třída, dědí z `ModuleBase`, implementuje `ISwarmCloser` · Katalog modulů a moduly*

Modul „Konec roje“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Konec roje](#modul-swarmendmodule).

| člen | popis |
|---|---|
| `SwarmEndModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`.

<a id="typ-swarmmodule"></a>
#### `SwarmModule`

*třída, dědí z `ModuleBase`, implementuje `ISwarmOpener` · Katalog modulů a moduly*

Modul „Roj“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Roj](#modul-swarmmodule).

| člen | popis |
|---|---|
| `SwarmModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Prepare`, `Process`, `ResetState`.

<a id="typ-vumetermodule"></a>
#### `VuMeterModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „VU Metr“ (kategorie Vizualizace). Co dělá, porty a knoflíky: [část 1, VU Metr](#modul-vumetermodule).

| člen | popis |
|---|---|
| `VuMeterModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

<a id="typ-wavemorphmodule"></a>
#### `WaveMorphModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Morfující oscilátor“ (kategorie Zdroje). Co dělá, porty a knoflíky: [část 1, Morfující oscilátor](#modul-wavemorphmodule).

| člen | popis |
|---|---|
| `WaveMorphModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |
| `const string LengthName = "Délka"` | Jméno knoflíku délky přechodu („Délka“). |
| `const string PositionName = "Pozice"` | Jméno knoflíku polohy přechodu („Pozice“). |
| `IReadOnlyList<ModuleParameter> ShapeParameters { get; }` | Knoflíky, na kterých závisí náhled tvaru. |
| `IReadOnlyList<CurveInput> ShapeSockets { get; }` | Tři křivkové zásuvky: dvě vlny a průběh přechodu. |
| `Curve ShapeAsCurve()` | Vlna, kterou modul hraje v poloze knoflíku, pro náhled. Na render se nepoužívá. |
| `Curve ShapeForMix(double mix)` | Vlna při přechodu `mix` (0 = Vlna A, 1 = Vlna B), pro náhled. |

Přepisuje z `ModuleBase`: `Description`, `Prepare`, `Process`, `ResetState`.

<a id="typ-waveshapermodule"></a>
#### `WaveshaperModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „Waveshaper“ (kategorie Zpracování). Co dělá, porty a knoflíky: [část 1, Waveshaper](#modul-waveshapermodule).

| člen | popis |
|---|---|
| `WaveshaperModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`.

<a id="typ-xoroscillatormodule"></a>
#### `XorOscillatorModule`

*třída, dědí z `ModuleBase` · Katalog modulů a moduly*

Modul „XOR Oscillator“ (kategorie Zdroje). Co dělá, porty a knoflíky: [část 1, XOR Oscillator](#modul-xoroscillatormodule).

| člen | popis |
|---|---|
| `XorOscillatorModule()` | Nový modul s výchozími knoflíky — totéž, co vloží paleta editoru. |

Přepisuje z `ModuleBase`: `Description`, `Process`, `ResetState`.

### ZvukEfekty.Core.Persistence

<a id="typ-connectionrecord"></a>
#### `ConnectionRecord`

*třída · Soubor .zvuky*

Jeden drát v souboru.

| člen | popis |
|---|---|
| `ConnectionRecord()` | Prázdný záznam. |
| `int SourceModuleId { get; set; }` | Pole `SourceModuleId`: `ModuleRecord.Id` modulu, ze kterého drát vede. |
| `string SourcePort { get; set; }` | Pole `SourcePort`: jméno výstupního portu. |
| `int TargetModuleId { get; set; }` | Pole `TargetModuleId`: `ModuleRecord.Id` modulu, do kterého drát vede. |
| `string TargetPort { get; set; }` | Pole `TargetPort`: jméno vstupního portu. |

<a id="typ-curvepointrecord"></a>
#### `CurvePointRecord`

*třída · Soubor .zvuky*

Jeden bod křivky v souboru.

| člen | popis |
|---|---|
| `CurvePointRecord()` | Prázdný záznam. |
| `double InDx { get; set; }` | Pole `InDx`: vodorovná složka přicházející tečny (chybí u nedotčené). |
| `double InDy { get; set; }` | Pole `InDy`: svislá složka přicházející tečny. |
| `double OutDx { get; set; }` | Pole `OutDx`: vodorovná složka odcházející tečny (chybí u nedotčené). |
| `double OutDy { get; set; }` | Pole `OutDy`: svislá složka odcházející tečny. |
| `double Tension { get; set; }` | Pole `Tension`: prohnutí úseku ze souborů před 2026-09-04; jen se čte a převede na tečny. |
| `double X { get; set; }` | Pole `X`: poloha bodu, 0…1. |
| `double Y { get; set; }` | Pole `Y`: hodnota bodu, −1…1. |

<a id="typ-curverecord"></a>
#### `CurveRecord`

*třída · Soubor .zvuky*

Jedna křivka modulu v souboru.

| člen | popis |
|---|---|
| `CurveRecord()` | Prázdný záznam. |
| `bool EndpointsUntied { get; set; }` | Pole `EndpointsUntied`: `true`, když autor rozpojil konce; jinak chybí. |
| `string Name { get; set; }` | Pole `Name`: jméno křivky. |
| `List<CurvePointRecord> Points { get; set; }` | Pole `Points`: body křivky. |

<a id="typ-filerecord"></a>
#### `FileRecord`

*třída · Soubor .zvuky*

Jeden soubor modulu v souboru sestavy (vzorek Sampleru).

| člen | popis |
|---|---|
| `FileRecord()` | Prázdný záznam. |
| `string Name { get; set; }` | Pole `Name`: jméno slotu („Soubor“). |
| `string Path { get; set; }` | Pole `Path`: cesta tak, jak byla zapsaná — relativní od sestavy, absolutní, nebo `pamet:jméno`. |

<a id="typ-groupportrecord"></a>
#### `GroupPortRecord`

*třída · Soubor .zvuky*

Vývod na okraji rámečku: zástupce za vnitřní port modulu.

| člen | popis |
|---|---|
| `GroupPortRecord()` | Prázdný záznam. |
| `bool IsInput { get; set; }` | Pole `IsInput`: vývod je vstup (jinak výstup). |
| `int ModuleId { get; set; }` | Pole `ModuleId`: `ModuleRecord.Id` vnitřního modulu. |
| `string Name { get; set; }` | Pole `Name`: jméno vývodu. |
| `string Port { get; set; }` | Pole `Port`: jméno portu vnitřního modulu. |

<a id="typ-grouprecord"></a>
#### `GroupRecord`

*třída · Soubor .zvuky*

Rámeček na plátně editoru (titulek, poznámka, rozměr). Na zvuk nemá vliv. Které moduly do něj patří, se odvozuje z polohy, nezapisuje se.

| člen | popis |
|---|---|
| `GroupRecord()` | Prázdný záznam. |
| `bool Collapsed { get; set; }` | Pole `Collapsed`: rámeček je sbalený. |
| `string? Description { get; set; }` | Pole `Description`: poznámka pod titulkem. |
| `double Height { get; set; }` | Pole `Height`: výška. |
| `bool Locked { get; set; }` | Pole `Locked`: obsah rámečku je zamčený. |
| `List<GroupPortRecord>? Ports { get; set; }` | Pole `Ports`: vývody rámečku. |
| `string Title { get; set; }` | Pole `Title`: titulek. |
| `List<Guid>? Visuals { get; set; }` | Pole `Visuals`: které náhledy sbalený rámeček ukazuje (`InstanceId` modulů). |
| `double Width { get; set; }` | Pole `Width`: šířka. |
| `double X { get; set; }` | Pole `X`: vodorovná pozice. |
| `double Y { get; set; }` | Pole `Y`: svislá pozice. |

<a id="typ-modulerecord"></a>
#### `ModuleRecord`

*třída · Soubor .zvuky*

Jeden modul v souboru: typ, dovětek, pozice, knoflíky a nečíselný obsah.

| člen | popis |
|---|---|
| `ModuleRecord()` | Prázdný záznam. |
| `List<CurveRecord>? Curves { get; set; }` | Pole `Curves`: vlastní křivky modulu; chybí, když žádné nemá (nebo drží výchozí tvar). |
| `List<FileRecord>? Files { get; set; }` | Pole `Files`: soubory (Sampler); chybí, když žádné nejsou. |
| `int Id { get; set; }` | Pole `Id`: číslo modulu v rámci souboru, na které ukazují dráty. |
| `Guid InstanceId { get; set; }` | Pole `InstanceId`: `ModuleBase.Id` modulu. |
| `string? Label { get; set; }` | Pole `Label`: dovětek; chybí, když žádný není. |
| `string? Name { get; set; }` | Pole `Name`: celé jméno, jak ho psaly starší soubory (jen se čte, nezapisuje). |
| `List<ParameterRecord> Parameters { get; set; }` | Pole `Parameters`: knoflíky. |
| `List<SequenceRecord>? Sequences { get; set; }` | Pole `Sequences`: melodie (Sekvencer). |
| `List<TextRecord>? Texts { get; set; }` | Pole `Texts`: texty (Formule). |
| `string Type { get; set; }` | Pole `Type`: jméno typu C# („OscillatorModule“), podle kterého se modul hledá v `ModuleCatalog`. |
| `double X { get; set; }` | Pole `X`: vodorovná pozice na plátně. |
| `double Y { get; set; }` | Pole `Y`: svislá pozice na plátně. |

<a id="typ-parameterrecord"></a>
#### `ParameterRecord`

*třída · Soubor .zvuky*

Jeden knoflík v souboru.

| člen | popis |
|---|---|
| `ParameterRecord()` | Prázdný záznam. |
| `bool? AutoSeed { get; set; }` | Pole `AutoSeed`: `true` u semínka, které se losuje při každém spuštění; jinak chybí. |
| `string? Division { get; set; }` | Pole `Division`: jméno notové hodnoty, když je knoflík synchronizovaný; jinak chybí. |
| `double ModulationAmount { get; set; }` | Pole `ModulationAmount`: hloubka modulace. |
| `string Name { get; set; }` | Pole `Name`: jméno knoflíku. Neznámé jméno se při načtení přeskočí. |
| `bool? TempoSynced { get; set; }` | Pole `TempoSynced`: `true`, když knoflík čte notovou hodnotu; jinak chybí. |
| `double Value { get; set; }` | Pole `Value`: hodnota (u výběru index volby). |

<a id="typ-patchdocument"></a>
#### `PatchDocument`

*třída · Soubor .zvuky*

Celý soubor `.zvuky` jako objekt: verze, časování, moduly, dráty a rámečky. Odpovídá kořeni JSON. `PatchSerializer` ho čte a píše; program s ním pracovat nemusí.

| člen | popis |
|---|---|
| `PatchDocument()` | Prázdný dokument. |
| `int Bars { get; set; }` | Pole `Bars`: počet taktů (viz `PatchTiming`). |
| `double Bpm { get; set; }` | Pole `Bpm`: tempo. |
| `List<ConnectionRecord> Connections { get; set; }` | Pole `Connections`: dráty. |
| `double DurationSeconds { get; set; }` | Pole `DurationSeconds`: délka. |
| `List<GroupRecord>? Groups { get; set; }` | Pole `Groups`: rámečky; chybí, když žádné nejsou. |
| `bool IsTempoLocked { get; set; }` | Pole `IsTempoLocked`. |
| `List<ModuleRecord> Modules { get; set; }` | Pole `Modules`: moduly. |
| `int Version { get; set; }` | Pole `Version`: verze formátu. |

<a id="typ-patchloadresult"></a>
#### `PatchLoadResult`

*record · Přehrávání a vzorky*

Výsledek načtení sestavy: graf, časování, pozice modulů a rámečky.

| člen | popis |
|---|---|
| `PatchLoadResult(ModuleGraph Graph, Dictionary<ModuleBase, (double X, double Y)> Positions, PatchTiming Timing, List<GroupRecord> Groups)` | Vytvoří hodnotu ze všech jejích polí (viz vlastnosti níž). |
| `ModuleGraph Graph { get; init; }` | Moduly a dráty — to, co se renderuje a co dostane `Voice`. |
| `List<GroupRecord> Groups { get; init; }` | Rámečky na plátně editoru (jen rozvržení, na zvuk nemají vliv). |
| `IReadOnlyDictionary<int, ModuleBase> ModulesByFileId { get; init; }` | Načtené moduly pod čísly, která jim dal soubor (`ModuleRecord.Id`). |
| `Dictionary<ModuleBase, (double X, double Y)> Positions { get; init; }` | Pozice modulů na plátně editoru (jen rozvržení). |
| `PatchTiming Timing { get; init; }` | Délka a tempo sestavy (`PatchTiming`). |

<a id="typ-patchserializer"></a>
#### `PatchSerializer`

*statická třída · Přehrávání a vzorky*

Načítání a ukládání sestav `.zvuky` (JSON). Moduly se v souboru poznají podle jména typu C# a hledají se v `ModuleCatalog`; neznámý typ = výjimka, neznámý knoflík nebo pole se tiše přeskočí. Starší soubory se načtou vždy.

| člen | popis |
|---|---|
| `static PatchLoadResult Deserialize(string json, bool freshInstanceIds = false)` | Sestava z textu JSON. Relativní cesty ke vzorkům nechá relativní (na rozdíl od `Load`). `freshInstanceIds` = dát modulům nová `Id` (pro vložení do sestavy, která už existuje, a pro nezávislé kopie). Hodí `InvalidOperationException` u neznámého typu modulu a u prázdného nebo poškozeného souboru, `JsonException` u neplatného JSON. |
| `static PatchLoadResult Load(string path, bool freshInstanceIds = false)` | Sestava ze souboru. Relativní cesty ke vzorkům dopočítá od složky souboru (`pamet:` nechá být). Jinak jako `Deserialize`. Tohle je cesta pro přehrávání. |
| `static void Save(string path, ModuleGraph graph, IReadOnlyDictionary<ModuleBase, (double X, double Y)> positions, PatchTiming timing, IReadOnlyList<GroupRecord>? groups = null)` | Uloží sestavu do souboru — totéž co `Serialize` a zápis. Cesty ke vzorkům zapíše, jak jsou (po `Load` tedy absolutní). |
| `static string Serialize(IReadOnlyList<ModuleBase> modules, IReadOnlyList<Connection> connections, IReadOnlyDictionary<ModuleBase, (double X, double Y)> positions, PatchTiming timing, IReadOnlyList<GroupRecord>? groups = null)` | Zapíše jen vybrané moduly a dráty mezi nimi (výběr, blok) ve stejném formátu jako celou sestavu. |
| `static string Serialize(ModuleGraph graph, IReadOnlyDictionary<ModuleBase, (double X, double Y)> positions, PatchTiming timing, IReadOnlyList<GroupRecord>? groups = null)` | Sestava jako text JSON. Pozice a rámečky předej z načtení (`PatchLoadResult.Positions`, `.Groups`), jinak se v editoru moduly sesypou na hromadu. Knoflíky, které knihovna nezná, se do výstupu nedostanou. Klon sestavy = `Deserialize(Serialize(…), freshInstanceIds: true)`. |

<a id="typ-patchtiming"></a>
#### `PatchTiming`

*třída · Přehrávání a vzorky*

Délka jednorázového zvuku a tempo sestavy. Buď volná délka v sekundách (`DurationSeconds`), nebo délka v taktech při tempu (`IsTempoLocked`, `Bpm`, `Bars`; takt = 4 doby). `DurationSeconds` platí v obou případech a podle ní se pouští jednorázový hlas.

| člen | popis |
|---|---|
| `PatchTiming()` | Výchozí časování: 1,5 s, 128 BPM, 4 takty, volná délka. |
| `const double DefaultBpm = 128` | Výchozí tempo (128). |
| `const double MaxBpm = 200` | Nejvyšší tempo (200). |
| `const double MaxFreeDurationSeconds = 60` | Nejdelší volná délka (60 s). Délka v taktech se neořezává. |
| `const double MinBpm = 60` | Nejnižší tempo (60). |
| `const double MinDurationSeconds = 0.1` | Nejkratší délka (0,1 s). |
| `int Bars { get; init; }` | Počet taktů, když je délka vázaná na tempo. |
| `double Bpm { get; init; }` | Tempo sestavy. Patří do `Voice` a do `RenderContext`. |
| `double DurationSeconds { get; init; }` | Délka jednorázového zvuku v sekundách — po ní se pouští nota hlasu a tolik vyrenderuje dávka. |
| `bool IsTempoLocked { get; init; }` | `true` = délka se odvozuje z `Bpm` a `Bars`. |
| `static double SecondsForBars(double bpm, int bars)` | Délka `bars` taktů 4/4 při tempu `bpm`, v sekundách. |

<a id="typ-sequencerecord"></a>
#### `SequenceRecord`

*třída · Soubor .zvuky*

Jedna melodie v souboru.

| člen | popis |
|---|---|
| `SequenceRecord()` | Prázdný záznam. |
| `string Name { get; set; }` | Pole `Name`: jméno melodie. |
| `List<StepRecord> Steps { get; set; }` | Pole `Steps`: kroky. |

<a id="typ-steprecord"></a>
#### `StepRecord`

*třída · Soubor .zvuky*

Jeden krok melodie v souboru.

| člen | popis |
|---|---|
| `StepRecord()` | Prázdný záznam. |
| `int Length { get; set; }` | Pole `Length`: délka v krocích. |
| `int Note { get; set; }` | Pole `Note`: MIDI číslo noty. |
| `bool Rest { get; set; }` | Pole `Rest`: `true` = pomlka; jinak chybí. |

<a id="typ-textrecord"></a>
#### `TextRecord`

*třída · Soubor .zvuky*

Jeden text modulu v souboru (výraz Formule).

| člen | popis |
|---|---|
| `TextRecord()` | Prázdný záznam. |
| `string Name { get; set; }` | Pole `Name`: jméno textu. |
| `string Text { get; set; }` | Pole `Text`: obsah, jak byl napsaný. |

