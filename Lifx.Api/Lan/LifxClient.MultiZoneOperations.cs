using Microsoft.Extensions.Logging;

namespace Lifx.Api.Lan;

using Lifx.Api.Models.Lan;

public partial class LifxLanClient : IDisposable
{
	/// <summary>
	/// Reads the zones of a multizone device. A device with more zones than the
	/// requested range answers with several messages; the first to arrive is
	/// returned, and its ZonesCount is the device's true zone count either way.
	/// Anything that is not multizone never answers, so this times out.
	/// </summary>
	/// <param name="bulb"></param>
	/// <param name="startIndex">First zone to read.</param>
	/// <param name="endIndex">Last zone to read. 255 reads to the end.</param>
	/// <param name="cancellationToken"></param>
	public async Task<StateMultiZoneResponse?> GetColorZonesAsync(
		LightBulb bulb,
		byte startIndex,
		byte endIndex,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(bulb);

		FrameHeader header = new()
		{
			Identifier = GetNextIdentifier(),
			AcknowledgeRequired = false
		};

		return await BroadcastMessageAsync<StateMultiZoneResponse>(
			bulb.HostName,
			header,
			MessageType.GetColorZones,
			cancellationToken,
			startIndex,
			endIndex);
	}

	/// <summary>
	/// Reads every zone in one message. Only firmware new enough for extended
	/// multizone answers, so a reply is also the capability check for
	/// <see cref="SetExtendedColorZonesAsync"/>.
	/// </summary>
	public async Task<StateExtendedColorZonesResponse?> GetExtendedColorZonesAsync(
		LightBulb bulb,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(bulb);

		FrameHeader header = new()
		{
			Identifier = GetNextIdentifier(),
			AcknowledgeRequired = false
		};

		return await BroadcastMessageAsync<StateExtendedColorZonesResponse>(
			bulb.HostName,
			header,
			MessageType.GetExtendedColorZones,
			cancellationToken);
	}

	/// <summary>
	/// Sets one colour across a range of zones. Each distinct colour costs a
	/// message, so a strip painted this way costs as many packets as it has
	/// runs - see <see cref="SetExtendedColorZonesAsync"/> for the one packet
	/// version.
	/// </summary>
	/// <param name="bulb"></param>
	/// <param name="startIndex">First zone to write.</param>
	/// <param name="endIndex">Last zone to write, inclusive.</param>
	/// <param name="color"></param>
	/// <param name="kelvin">2500..9000</param>
	/// <param name="transitionDuration"></param>
	/// <param name="apply">
	/// Use <see cref="ZoneApplicationRequest.NoApply"/> for every run but the
	/// last, or the colour visibly sweeps along the strip.
	/// </param>
	/// <param name="acknowledge">
	/// Waits for the device to confirm. Off by default: at frame rate the ack
	/// traffic costs more than a dropped packet does.
	/// </param>
	/// <param name="cancellationToken"></param>
	public async Task SetColorZonesAsync(
		LightBulb bulb,
		byte startIndex,
		byte endIndex,
		Color color,
		ushort kelvin,
		TimeSpan transitionDuration,
		ZoneApplicationRequest apply = ZoneApplicationRequest.Apply,
		bool acknowledge = false,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(bulb);

		var duration = ToDuration(transitionDuration);

		CheckKelvin(kelvin);

		if (endIndex < startIndex)
		{
			throw new ArgumentOutOfRangeException(
				nameof(endIndex),
				"endIndex must not be before startIndex");
		}

		var hsb = Utilities.RgbToHsl(color);

		logger.LogDebug(
			"Setting zones {Start}..{End} for {HostName}",
			startIndex,
			endIndex,
			bulb.HostName);

		await SendZoneMessageAsync(
			bulb,
			MessageType.SetColorZones,
			acknowledge,
			cancellationToken,
			startIndex,
			endIndex,
			hsb[0],
			hsb[1],
			hsb[2],
			kelvin,
			duration,
			(byte)apply);
	}

	/// <summary>
	/// Sets up to 82 zones in a single message. Needs firmware that supports
	/// extended multizone - use <see cref="GetExtendedColorZonesAsync"/> to
	/// find out, and fall back to <see cref="SetColorZonesAsync"/> if not.
	/// </summary>
	/// <param name="bulb"></param>
	/// <param name="colours">One colour per zone, from <paramref name="zoneIndex"/> on.</param>
	/// <param name="kelvin">2500..9000</param>
	/// <param name="transitionDuration"></param>
	/// <param name="zoneIndex">First zone the colours apply to.</param>
	/// <param name="apply"></param>
	/// <param name="acknowledge">
	/// Waits for the device to confirm. Off by default: at frame rate the ack
	/// traffic costs more than a dropped packet does.
	/// </param>
	/// <param name="cancellationToken"></param>
	public async Task SetExtendedColorZonesAsync(
		LightBulb bulb,
		IReadOnlyList<Color> colours,
		ushort kelvin,
		TimeSpan transitionDuration,
		ushort zoneIndex = 0,
		ZoneApplicationRequest apply = ZoneApplicationRequest.Apply,
		bool acknowledge = false,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(bulb);
		ArgumentNullException.ThrowIfNull(colours);

		var duration = ToDuration(transitionDuration);

		CheckKelvin(kelvin);

		if (colours.Count > MultiZonePayload.MaxExtendedZones)
		{
			throw new ArgumentOutOfRangeException(
				nameof(colours),
				$"At most {MultiZonePayload.MaxExtendedZones} zones can be set in one message");
		}

		logger.LogDebug(
			"Setting {Count} zones from {Index} for {HostName}",
			colours.Count,
			zoneIndex,
			bulb.HostName);

		await SendZoneMessageAsync(
			bulb,
			MessageType.SetExtendedColorZones,
			acknowledge,
			cancellationToken,
			duration,
			(byte)apply,
			zoneIndex,
			(byte)colours.Count,
			MultiZonePayload.BuildColourBlock(colours, kelvin));
	}

	/// <summary>
	/// Unacknowledged sends skip the completion bookkeeping entirely: asking
	/// for UnknownResponse leaves no task to wait on, which is what makes a
	/// zone write cheap enough to repeat at frame rate.
	/// </summary>
	private async Task SendZoneMessageAsync(
		LightBulb bulb,
		MessageType type,
		bool acknowledge,
		CancellationToken cancellationToken,
		params object[] args)
	{
		FrameHeader header = new()
		{
			Identifier = GetNextIdentifier(),
			AcknowledgeRequired = acknowledge
		};

		if (acknowledge)
		{
			_ = await BroadcastMessageAsync<AcknowledgementResponse>(
				bulb.HostName,
				header,
				type,
				cancellationToken,
				args);

			return;
		}

		_ = await BroadcastMessageAsync<UnknownResponse>(
			bulb.HostName,
			header,
			type,
			cancellationToken,
			args);
	}

	private static uint ToDuration(TimeSpan transitionDuration)
		=> transitionDuration.TotalMilliseconds > uint.MaxValue || transitionDuration.Ticks < 0
			? throw new ArgumentOutOfRangeException(nameof(transitionDuration))
			: (uint)transitionDuration.TotalMilliseconds;

	private static void CheckKelvin(ushort kelvin)
	{
		if (kelvin is < 2500 or > 9000)
		{
			throw new ArgumentOutOfRangeException(nameof(kelvin), "Kelvin must be between 2500 and 9000");
		}
	}
}
