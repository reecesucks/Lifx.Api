namespace Lifx.Api.Lan;

using Lifx.Api.Models.Lan;

internal static class MultiZonePayload
{
	/// <summary>
	/// The colour block of SetExtendedColorZones is a fixed 82 slots however
	/// few zones are actually being written.
	/// </summary>
	internal const int MaxExtendedZones = 82;

	private const int HsbkSize = 8;

	/// <summary>
	/// Reads <paramref name="count"/> HSBK entries, stopping short if the
	/// packet is truncated. A parse that throws here would surface inside the
	/// receive loop, which is the only thread draining the socket.
	/// </summary>
	internal static ZoneColour[] ReadColours(byte[] payload, int offset, int count)
	{
		var available = (payload.Length - offset) / HsbkSize;

		count = Math.Clamp(count, 0, Math.Max(available, 0));

		var colours = new ZoneColour[count];

		for (var i = 0; i < count; i++)
		{
			var at = offset + (i * HsbkSize);

			colours[i] = new ZoneColour(
				BitConverter.ToUInt16(payload, at),
				BitConverter.ToUInt16(payload, at + 2),
				BitConverter.ToUInt16(payload, at + 4),
				BitConverter.ToUInt16(payload, at + 6));
		}

		return colours;
	}

	/// <summary>
	/// Builds the 656 byte colour block. Slots past <paramref name="colours"/>
	/// stay zeroed - the colors_count field tells the device how many to read.
	/// </summary>
	internal static byte[] BuildColourBlock(IReadOnlyList<Color> colours, ushort kelvin)
	{
		var block = new byte[MaxExtendedZones * HsbkSize];
		var count = Math.Min(colours.Count, MaxExtendedZones);

		for (var i = 0; i < count; i++)
		{
			var hsb = Utilities.RgbToHsl(colours[i]);
			var at = i * HsbkSize;

			_ = BitConverter.TryWriteBytes(block.AsSpan(at), hsb[0]);
			_ = BitConverter.TryWriteBytes(block.AsSpan(at + 2), hsb[1]);
			_ = BitConverter.TryWriteBytes(block.AsSpan(at + 4), hsb[2]);
			_ = BitConverter.TryWriteBytes(block.AsSpan(at + 6), kelvin);
		}

		return block;
	}
}
