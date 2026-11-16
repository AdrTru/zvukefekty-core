namespace ZvukEfekty.Core.Graph;

/// <summary>
/// A named starting point for a module's knobs — "Šplouchnutí", "Malá místnost", "Kovadlina" — so
/// the user picks the system they want to begin from instead of hunting for it knob by knob.
///
/// <para>Only what a preset names is changed: knob values (by parameter name) and, for modules that
/// keep text such as the modal bank's mode table, those texts. Wiring, modulation depths and seeds
/// are left alone — a preset is "start from this sound", not "replace this module", and a curve
/// already patched into Aktivita should keep driving it afterwards.</para>
/// </summary>
/// <param name="Name">What the list in the inspector calls it.</param>
/// <param name="Description">One sentence on what it is for, shown as the tooltip.</param>
/// <param name="Values">Parameter name → value. For an option knob, the option's index.</param>
/// <param name="Texts">Text name → text, or null when the preset sets none.</param>
public sealed record ModulePreset(
    string Name,
    string Description,
    IReadOnlyDictionary<string, double> Values,
    IReadOnlyDictionary<string, string>? Texts = null);
