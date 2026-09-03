namespace Lifx.Api.Models.Lan;

/// <summary>
/// A single zone's colour on a multizone device, in the wire's own HSBK units.
/// </summary>
/// <param name="Hue">0..65535</param>
/// <param name="Saturation">0..65535</param>
/// <param name="Brightness">0..65535</param>
/// <param name="Kelvin">2500..9000</param>
public readonly record struct ZoneColour(
	ushort Hue,
	ushort Saturation,
	ushort Brightness,
	ushort Kelvin);
