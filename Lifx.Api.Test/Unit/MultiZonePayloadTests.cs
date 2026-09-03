using AwesomeAssertions;
using Lifx.Api.Lan;
using Lifx.Api.Models.Lan;

namespace Lifx.Api.Test.Unit;

/// <summary>
/// Tests for the SetExtendedColorZones colour block and the zone readers that
/// parse StateMultiZone and StateExtendedColorZones payloads.
/// </summary>
[Collection("Unit Tests")]
public class MultiZonePayloadTests
{
	private static readonly Color Red = new() { R = 255, G = 0, B = 0 };
	private static readonly Color Green = new() { R = 0, G = 255, B = 0 };
	private static readonly Color Blue = new() { R = 0, G = 0, B = 255 };

	#region Colour block

	[Fact]
	public void BuildColourBlock_Should_Always_Be_82_Zones_Wide()
	{
		// Act
		var block = MultiZonePayload.BuildColourBlock([Red], 3500);

		// Assert
		block.Should().HaveCount(82 * 8);
	}

	[Fact]
	public void BuildColourBlock_Should_Write_Each_Colour_As_Hsbk()
	{
		// Arrange
		var expected = Utilities.RgbToHsl(Green);

		// Act
		var block = MultiZonePayload.BuildColourBlock([Red, Green, Blue], 3500);

		// Assert - green is the second zone, so 8 bytes in
		BitConverter.ToUInt16(block, 8).Should().Be(expected[0]);
		BitConverter.ToUInt16(block, 10).Should().Be(expected[1]);
		BitConverter.ToUInt16(block, 12).Should().Be(expected[2]);
		BitConverter.ToUInt16(block, 14).Should().Be(3500);
	}

	[Fact]
	public void BuildColourBlock_Should_Leave_Unused_Zones_Zeroed()
	{
		// Act
		var block = MultiZonePayload.BuildColourBlock([Red, Green], 3500);

		// Assert - the device reads only as far as colors_count, so the tail
		// carrying a stale kelvin would be harmless but is not written either
		block.Skip(16).Should().AllSatisfy(b => b.Should().Be(0));
	}

	[Fact]
	public void BuildColourBlock_Should_Not_Overrun_On_Too_Many_Colours()
	{
		// Arrange
		var colours = Enumerable.Repeat(Red, 200).ToList();

		// Act
		var block = MultiZonePayload.BuildColourBlock(colours, 3500);

		// Assert
		block.Should().HaveCount(82 * 8);
	}

	[Fact]
	public void BuildColourBlock_With_No_Colours_Should_Be_All_Zeroes()
	{
		// Act
		var block = MultiZonePayload.BuildColourBlock([], 3500);

		// Assert
		block.Should().AllSatisfy(b => b.Should().Be(0));
	}

	#endregion Colour block

	#region Zone reader

	[Fact]
	public void ReadColours_Should_Round_Trip_A_Built_Block()
	{
		// Arrange
		var block = MultiZonePayload.BuildColourBlock([Red, Green, Blue], 3500);
		var expected = Utilities.RgbToHsl(Blue);

		// Act
		var colours = MultiZonePayload.ReadColours(block, 0, 3);

		// Assert
		colours.Should().HaveCount(3);
		colours[2].Hue.Should().Be(expected[0]);
		colours[2].Saturation.Should().Be(expected[1]);
		colours[2].Brightness.Should().Be(expected[2]);
		colours[2].Kelvin.Should().Be(3500);
	}

	[Fact]
	public void ReadColours_Should_Stop_Short_On_A_Truncated_Payload()
	{
		// Arrange - two zones of HSBK, but a count claiming eight
		var payload = new byte[16];

		// Act
		var colours = MultiZonePayload.ReadColours(payload, 0, 8);

		// Assert
		colours.Should().HaveCount(2);
	}

	[Fact]
	public void ReadColours_Should_Be_Empty_When_Offset_Is_Past_The_Payload()
	{
		// Act
		var colours = MultiZonePayload.ReadColours([1, 2], 5, 8);

		// Assert
		colours.Should().BeEmpty();
	}

	[Fact]
	public void ReadColours_Should_Honour_The_Offset()
	{
		// Arrange - a StateMultiZone payload starts with zones_count and index
		var payload = new byte[2 + 8];
		var block = MultiZonePayload.BuildColourBlock([Green], 3500);
		Array.Copy(block, 0, payload, 2, 8);

		var expected = Utilities.RgbToHsl(Green);

		// Act
		var colours = MultiZonePayload.ReadColours(payload, 2, 8);

		// Assert
		colours.Should().ContainSingle();
		colours[0].Hue.Should().Be(expected[0]);
	}

	#endregion Zone reader
}
