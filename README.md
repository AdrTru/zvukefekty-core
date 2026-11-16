# ZvukEfekty.Core

[Česky](#česky) · [English](#english)

---

## Česky

Syntézní engine pro zvukové sestavy `.zvuky`. Sestava je graf modulů — oscilátory, šum, filtry,
rezonátory, obálky, křivky, sampler, dozvuk a další — a knihovna z ní spočítá zvuk jako `float[]`,
mono, v rozsahu ±1. Pro .NET 8, **bez dalších závislostí** a bez vazby na platformu: nepřehrává,
jen počítá. Přehrání obstará tvůj program (Godot, NAudio, OpenAL…). Běží v hotové hře na Godotu 4.

### Co tu je

| | |
|---|---|
| [`src/ZvukEfekty.Core/`](src/ZvukEfekty.Core/) | knihovna (C#, `net8.0`) |
| [`src/ZvukEfekty.Core/README.md`](src/ZvukEfekty.Core/README.md) | **návod**: zapojení, první zvuk do souboru, přehrávání v běžícím programu, NAudio, Godot 4, tiché pasti |
| [`src/ZvukEfekty.Core/API.md`](src/ZvukEfekty.Core/API.md) | reference: všechny moduly s knoflíky a rozsahy, všechny veřejné typy a jejich členy |
| [`Sestavy/`](Sestavy/) | ukázkové sestavy: údery, laser, led, vítr, roj jisker, sub dropy a hratelné nástroje |
| [`LICENSE`](LICENSE) | licence (anglicky a česky) |

### Sestavení

```text
dotnet build src/ZvukEfekty.Core -c Release
dotnet pack  src/ZvukEfekty.Core -c Release     # balíček .nupkg do src/ZvukEfekty.Core/bin/Release/
```

Potřebuješ .NET 8 SDK. Hotovou knihovnu i s balíčkem a sestavami najdeš také ke stažení na
<https://www.pekelec.eu/zvuky/#stazeni> (`zvuky-core.zip`).

### Licence — přečti si ji, než knihovnu použiješ

Je to licence MIT s **jednou přidanou podmínkou: sdílením sestav**.

- Knihovnu smíš užívat, upravovat, šířit i prodávat, v uzavřeném i komerčním programu.
- **Každou sestavu, kterou použiješ ve zveřejněném díle** (hra, aplikace, video, balík zvuků…),
  nahraješ **do 30 dnů** do veřejné knihovny sestav na
  <https://www.pekelec.eu/zvuky/knihovna.html>, pod licencí CC BY 4.0. Počítá se i zvuk ze sestavy
  předem vykreslený do `.wav`.
- Netýká se to pokusů a soukromého užití, ani sestav stažených z knihovny nebo přibalených
  k Core, které používáš beze změny.

Tohle shrnutí licenci nenahrazuje — platí [`LICENSE`](LICENSE). Protože to není čistá MIT, GitHub
i NuGet ji ukazují jako vlastní licenci.

### Kde sestavy vznikají

Sestavy se skládají v editoru **Zvukové efekty** (Windows, zdarma, bez instalace):
<https://www.pekelec.eu/zvuky/>. Sdílené sestavy k poslechu a stažení jsou v
[knihovně sestav](https://www.pekelec.eu/zvuky/knihovna.html).

### Chyby a nápady

Hlaste je v [Issues](../../issues). Repozitář se plní exportem z vývojového repozitáře autora, takže
pull requesty se nepřebírají přímo — změna z nich se do knihovny dostane ručně, s odkazem na PR.

---

## English

A synthesis engine for `.zvuky` sound patches. A patch is a graph of modules — oscillators, noise,
filters, resonators, envelopes, curves, a sampler, reverb and more — and the library renders it to
sound as `float[]`, mono, in the ±1 range. For .NET 8, **no further dependencies** and no platform
ties: it does not play anything, it only computes. Your program does the playback (Godot, NAudio,
OpenAL…). It runs in a shipped game on Godot 4.

**The documentation (`src/ZvukEfekty.Core/README.md`, `API.md`) is in Czech.** Most type and
member names in the code are English (a few are Czech), and the guide's code samples read well
with a machine translation of the text around them.

### What is here

| | |
|---|---|
| [`src/ZvukEfekty.Core/`](src/ZvukEfekty.Core/) | the library (C#, `net8.0`) |
| [`src/ZvukEfekty.Core/README.md`](src/ZvukEfekty.Core/README.md) | **guide** (Czech): setup, first sound to a file, playback in a running program, NAudio, Godot 4, silent traps |
| [`src/ZvukEfekty.Core/API.md`](src/ZvukEfekty.Core/API.md) | reference (Czech): every module with its knobs and ranges, every public type and member |
| [`Sestavy/`](Sestavy/) | example patches: hits, laser, ice, wind, spark swarm, sub drops and playable instruments |
| [`LICENSE`](LICENSE) | the licence (English and Czech) |

### Building

```text
dotnet build src/ZvukEfekty.Core -c Release
dotnet pack  src/ZvukEfekty.Core -c Release     # .nupkg package in src/ZvukEfekty.Core/bin/Release/
```

You need the .NET 8 SDK. The built library with the package and the patches can also be downloaded
from <https://www.pekelec.eu/zvuky/#stazeni> (`zvuky-core.zip`).

### Licence — read it before you use the library

It is the MIT licence with **one added condition: sharing patches**.

- You may use, modify, distribute and sell the library, in closed-source and commercial programs.
- **Every patch you use in a published work** (a game, an application, a video, a sound pack…)
  you upload **within 30 days** to the public patch library at
  <https://www.pekelec.eu/zvuky/knihovna.html?lang=en>, under CC BY 4.0. Sound rendered from a
  patch to `.wav` in advance counts too.
- This does not apply to experiments and private use, nor to patches downloaded from the library
  or shipped with Core that you use unchanged.

This summary does not replace the licence — [`LICENSE`](LICENSE) does. Because it is not plain MIT,
GitHub and NuGet show it as a custom licence.

### Where patches are made

Patches are built in the **Zvukové efekty** editor (Windows, free, no installation, Czech user
interface): <https://www.pekelec.eu/zvuky/>. Shared patches to listen to and download are in the
[patch library](https://www.pekelec.eu/zvuky/knihovna.html?lang=en).

### Bugs and ideas

Report them in [Issues](../../issues), in English or Czech. The repository is filled by an export
from the author's development repository, so pull requests are not merged directly — a change from
one is carried over by hand, with a reference to the PR.
