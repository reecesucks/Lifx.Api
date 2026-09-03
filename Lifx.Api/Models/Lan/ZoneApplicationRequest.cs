namespace Lifx.Api.Models.Lan;

/// <summary>
/// Whether a zone write is shown immediately or staged for a later apply.
/// Staging several writes and applying once avoids the colour visibly
/// sweeping along the strip.
/// </summary>
public enum ZoneApplicationRequest : byte
{
	/// <summary>
	/// Buffer this write without changing what the device is showing.
	/// </summary>
	NoApply = 0,

	/// <summary>
	/// Buffer this write, then show everything buffered so far.
	/// </summary>
	Apply = 1,

	/// <summary>
	/// Show everything already buffered, ignoring the colour in this message.
	/// </summary>
	ApplyOnly = 2
}
