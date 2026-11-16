using System.Runtime.CompilerServices;

// ⚠️ Od 2026-09-19 tady stojí OPAČNÝ důvod, než pro který to bylo zavedené. Číst pozor.
//
// Původně: přehrávač byl do 2026-09-19 součástí téhle knihovny a `Voice` si proto sahal na
// `RenderContext.LiveKeyDown` a `RenderContext.Scratch`, které jsou uvnitř. **Tenhle důvod
// zanikl** — `Voice` se vrátil do Core (`Audio/Voice.cs`), takže si na ně sahá zevnitř.
//
// Dnes: přes tuhle bránu chodí **jediná věc a opačným směrem** — `Voice.StartOrder` je `internal`
// a nastavuje ho `VoiceManager`, který zůstal v přehrávači. Je to pořadí note-onů, podle kterého
// se krade hlas při dosažení polyfonie; mimo správce hlasů nemá význam a do veřejného rozhraní
// `Voice` nepatří. Zmizí ta brána tím dnem, kdy si `VoiceManager` to pořadí povede sám.
//
// Zůstává platné, proč se `RenderContext.LiveKeyDown` a `.Scratch` nezveřejnily: obojí je knoflík,
// kterým jde render rozbít. `Scratch` půjčuje pole, která platí jeden průchod, a kdo by si je
// nechal, dostane cizí data v dalším bloku (viz `todo.md` „Živé hraní b)"). Rozdělení projektu
// nebylo důvod měnit, co je součást veřejného rozhraní — a jeho zpětné scelení taky ne.
[assembly: InternalsVisibleTo("ZvukEfekty.Prehravac")]
