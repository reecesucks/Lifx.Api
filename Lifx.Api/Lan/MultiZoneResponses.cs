namespace Lifx.Api.Lan;

using Lifx.Api.Models.Lan;

/// <summary>
/// Response to GetColorZones when the requested range resolves to a single
/// zone. A wider range answers with <see cref="StateMultiZoneResponse"/>.
/// </summary>
public class StateZoneResponse : LifxResponse
{
	internal StateZoneResponse(FrameHeader header, MessageType type, byte[] payload, uint source)
		: base(header, type, payload, source)
	{
		if (payload.Length >= 2)
		{
			ZonesCount = payload[0];
			Index = payload[1];
		}

		var colours = MultiZonePayload.ReadColours(payload, 2, 1);

		if (colours.Length > 0)
		{
			Colour = colours[0];
		}
	}

	/// <summary>
	/// Total zones on the device, not the number described by this message.
	/// </summary>
	public byte ZonesCount { get; }

	/// <summary>
	/// Index of the zone this message describes.
	/// </summary>
	public byte Index { get; }

	/// <summary>
	/// The zone's colour.
	/// </summary>
	public ZoneColour Colour { get; }
}

/// <summary>
/// Response to GetColorZones covering more than one zone. A device with more
/// than eight zones answers with several of these, so the first to arrive
/// carries only the first eight - <see cref="ZonesCount"/> is complete in all
/// of them.
/// </summary>
public class StateMultiZoneResponse : LifxResponse
{
	internal StateMultiZoneResponse(FrameHeader header, MessageType type, byte[] payload, uint source)
		: base(header, type, payload, source)
	{
		if (payload.Length >= 2)
		{
			ZonesCount = payload[0];
			Index = payload[1];
		}

		Colours = MultiZonePayload.ReadColours(payload, 2, 8);
	}

	/// <summary>
	/// Total zones on the device.
	/// </summary>
	public byte ZonesCount { get; }

	/// <summary>
	/// Index of the first zone in <see cref="Colours"/>.
	/// </summary>
	public byte Index { get; }

	/// <summary>
	/// Up to eight zones starting at <see cref="Index"/>.
	/// </summary>
	public IReadOnlyList<ZoneColour> Colours { get; }
}

/// <summary>
/// Response to GetExtendedColorZones. Only devices on firmware new enough for
/// extended multizone answer this at all, so a reply doubles as the capability
/// check.
/// </summary>
public class StateExtendedColorZonesResponse : LifxResponse
{
	internal StateExtendedColorZonesResponse(FrameHeader header, MessageType type, byte[] payload, uint source)
		: base(header, type, payload, source)
	{
		if (payload.Length >= 5)
		{
			ZonesCount = BitConverter.ToUInt16(payload, 0);
			Index = BitConverter.ToUInt16(payload, 2);
			Colours = MultiZonePayload.ReadColours(payload, 5, payload[4]);
		}
		else
		{
			Colours = [];
		}
	}

	/// <summary>
	/// Total zones on the device.
	/// </summary>
	public ushort ZonesCount { get; }

	/// <summary>
	/// Index of the first zone in <see cref="Colours"/>.
	/// </summary>
	public ushort Index { get; }

	/// <summary>
	/// Up to 82 zones starting at <see cref="Index"/>.
	/// </summary>
	public IReadOnlyList<ZoneColour> Colours { get; }
}
