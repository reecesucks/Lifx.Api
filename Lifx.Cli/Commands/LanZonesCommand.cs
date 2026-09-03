using Lifx.Api;
using Lifx.Api.Lan;
using Lifx.Api.Models.Lan;
using Spectre.Console;
using System.CommandLine;
using System.Globalization;
using Color = Lifx.Api.Models.Lan.Color;

namespace Lifx.Cli.Commands;

/// <summary>
/// Multizone commands for strips and beams. Kept apart from the single colour
/// LAN commands because everything here first has to work out what the device
/// is capable of.
/// </summary>
public static class LanZonesCommand
{
	private const int MaxExtendedZones = 82;

	public static Command Create()
	{
		var command = new Command("zones", "Inspect and paint the zones of a multizone strip or beam")
		{
			CreateInfoCommand(),
			CreateSetCommand()
		};

		command.Description =
			"Inspect and paint the zones of a multizone strip or beam" + Environment.NewLine +
			Environment.NewLine +
			"Subcommands:" + Environment.NewLine +
			"  info  - Report zone count, extended multizone support, current colours" + Environment.NewLine +
			"  set   - Paint colours across the zones" + Environment.NewLine +
			Environment.NewLine +
			"Examples:" + Environment.NewLine +
			"  lifx lan zones info D0:73:D5:41:99:0C" + Environment.NewLine +
			"  lifx lan zones set D0:73:D5:41:99:0C ff0000,00ff00,0000ff" + Environment.NewLine +
			"  lifx lan zones set D0:73:D5:41:99:0C ff0000,0000ff --legacy";

		return command;
	}

	private static Command CreateInfoCommand()
	{
		var command = new Command("info", "Report what a device's zones can do");

		var macArg = new Argument<string>(
			"mac-address",
			description: "MAC address of the light");

		command.AddArgument(macArg);

		command.SetHandler(async macAddress =>
		{
			using var client = new LifxClient(new LifxClientOptions { IsLanEnabled = true });

			var bulb = await LanCommand.DiscoverAndFindBulb(client, macAddress);
			if (bulb == null) return;

			var zones = await ProbeAsync(client.Lan!, bulb);

			var table = new Table();
			table.AddColumn("Property");
			table.AddColumn("Value");

			table.AddRow("MAC", bulb.MacAddressName);
			table.AddRow("Host", bulb.HostName);
			table.AddRow("Multizone", zones.IsMultizone ? "[green]yes[/]" : "[red]no[/]");
			table.AddRow("Extended", zones.Extended ? "[green]yes[/]" : "[yellow]no (legacy only)[/]");
			table.AddRow("Zones", zones.IsMultizone ? zones.Count.ToString(CultureInfo.InvariantCulture) : "-");

			AnsiConsole.Write(table);

			if (!zones.IsMultizone)
			{
				AnsiConsole.MarkupLine(
					"[yellow]No answer to either zone query. This is a single colour bulb, " +
					"or it was not reachable.[/]");

				return;
			}

			if (zones.Colours.Count > 0)
			{
				AnsiConsole.WriteLine();
				AnsiConsole.MarkupLine("Current zones:");

				for (var i = 0; i < zones.Colours.Count; i++)
				{
					var c = zones.Colours[i];

					AnsiConsole.MarkupLine(
						$"  [grey]{i,3}[/]  hue {c.Hue,5}  sat {c.Saturation,5}  " +
						$"bri {c.Brightness,5}  {c.Kelvin,4}K");
				}
			}

			await WarnIfOffAsync(client.Lan!, bulb);
		}, macArg);

		return command;
	}

	private static Command CreateSetCommand()
	{
		var command = new Command("set", "Paint colours across a device's zones");

		var macArg = new Argument<string>(
			"mac-address",
			description: "MAC address of the light");

		var coloursArg = new Argument<string>(
			"colours",
			description: "Comma separated hex colours, spread evenly across the zones (e.g. ff0000,00ff00,0000ff)");

		var kelvinOption = new Option<int>(
			aliases: ["--kelvin", "-k"],
			getDefaultValue: () => 3500,
			description: "White point, 2500-9000");

		var durationOption = new Option<double>(
			aliases: ["--duration", "-d"],
			getDefaultValue: () => 0.5,
			description: "Transition duration in seconds");

		var legacyOption = new Option<bool>(
			aliases: ["--legacy", "-l"],
			getDefaultValue: () => false,
			description: "Force the one-message-per-run SetColorZones path even if extended is supported");

		command.AddArgument(macArg);
		command.AddArgument(coloursArg);
		command.AddOption(kelvinOption);
		command.AddOption(durationOption);
		command.AddOption(legacyOption);

		command.SetHandler(async (macAddress, colourText, kelvin, duration, legacy) =>
		{
			if (kelvin is < 2500 or > 9000)
			{
				AnsiConsole.MarkupLine("[red]Kelvin must be between 2500 and 9000[/]");
				return;
			}

			if (!TryParseColours(colourText, out var colours))
			{
				AnsiConsole.MarkupLine(
					$"[red]Could not read '{colourText}' as colours. Expected hex like ff0000,00ff00[/]");

				return;
			}

			using var client = new LifxClient(new LifxClientOptions { IsLanEnabled = true });

			var bulb = await LanCommand.DiscoverAndFindBulb(client, macAddress);
			if (bulb == null) return;

			var zones = await ProbeAsync(client.Lan!, bulb);

			if (!zones.IsMultizone)
			{
				AnsiConsole.MarkupLine(
					"[red]x[/] This device did not answer either zone query, so it has no zones to set.");

				return;
			}

			var perZone = Spread(colours, zones.Count);
			var transition = TimeSpan.FromSeconds(duration);
			var useExtended = zones.Extended && !legacy;

			try
			{
				if (useExtended)
				{
					await client.Lan!.SetExtendedColorZonesAsync(
						bulb,
						perZone,
						(ushort)kelvin,
						transition,
						acknowledge: true,
						cancellationToken: CancellationToken.None);
				}
				else
				{
					await SetByRunsAsync(client.Lan!, bulb, perZone, (ushort)kelvin, transition);
				}
			}
			catch (TimeoutException)
			{
				AnsiConsole.MarkupLine(
					"[red]x[/] The device did not acknowledge the write. The packet still went " +
					"out, so check whether the strip changed anyway.");

				return;
			}

			var path = useExtended
				? "extended (1 message)"
				: $"legacy ({CountRuns(perZone)} messages)";

			AnsiConsole.MarkupLine(
				$"[green]ok[/] Painted {perZone.Count} zones on {bulb.MacAddressName} via {path}");

			await WarnIfOffAsync(client.Lan!, bulb);
		}, macArg, coloursArg, kelvinOption, durationOption, legacyOption);

		return command;
	}

	private readonly record struct ZoneInfo(
		bool IsMultizone,
		bool Extended,
		int Count,
		IReadOnlyList<ZoneColour> Colours);

	/// <summary>
	/// Extended multizone answers with the whole strip in one message and only
	/// firmware that supports it replies at all, so it doubles as the
	/// capability check. Anything that stays quiet gets the legacy question.
	/// </summary>
	private static async Task<ZoneInfo> ProbeAsync(LifxLanClient lan, LightBulb bulb)
	{
		try
		{
			var extended = await lan.GetExtendedColorZonesAsync(bulb, CancellationToken.None);

			if (extended is not null)
			{
				return new ZoneInfo(true, true, extended.ZonesCount, extended.Colours);
			}
		}
		catch (TimeoutException)
		{
		}

		try
		{
			var legacy = await lan.GetColorZonesAsync(bulb, 0, 255, CancellationToken.None);

			if (legacy is not null)
			{
				return new ZoneInfo(true, false, legacy.ZonesCount, legacy.Colours);
			}
		}
		catch (TimeoutException)
		{
		}

		return new ZoneInfo(false, false, 0, []);
	}

	/// <summary>
	/// Legacy SetColorZones carries one colour, so consecutive zones sharing a
	/// colour go out as a single run. Every run but the last is staged, or the
	/// strip visibly repaints itself one run at a time.
	/// </summary>
	private static async Task SetByRunsAsync(
		LifxLanClient lan,
		LightBulb bulb,
		IReadOnlyList<Color> perZone,
		ushort kelvin,
		TimeSpan transition)
	{
		var runs = BuildRuns(perZone);

		for (var i = 0; i < runs.Count; i++)
		{
			var (start, end, colour) = runs[i];

			await lan.SetColorZonesAsync(
				bulb,
				start,
				end,
				colour,
				kelvin,
				transition,
				apply: i == runs.Count - 1
					? ZoneApplicationRequest.Apply
					: ZoneApplicationRequest.NoApply,
				acknowledge: true,
				cancellationToken: CancellationToken.None);
		}
	}

	private static List<(byte Start, byte End, Color Colour)> BuildRuns(IReadOnlyList<Color> perZone)
	{
		var runs = new List<(byte Start, byte End, Color Colour)>();

		for (var i = 0; i < perZone.Count && i <= byte.MaxValue; i++)
		{
			var colour = perZone[i];

			if (runs.Count > 0 && Same(runs[^1].Colour, colour))
			{
				runs[^1] = (runs[^1].Start, (byte)i, runs[^1].Colour);

				continue;
			}

			runs.Add(((byte)i, (byte)i, colour));
		}

		return runs;
	}

	private static int CountRuns(IReadOnlyList<Color> perZone) => BuildRuns(perZone).Count;

	private static bool Same(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;

	/// <summary>
	/// Spreads the given colours over the zones as equal blocks rather than
	/// blending them. Hard edges make it obvious which zone got what, and which
	/// end of the strip zone 0 is.
	/// </summary>
	private static List<Color> Spread(IReadOnlyList<Color> colours, int zoneCount)
	{
		var count = Math.Clamp(zoneCount, 1, MaxExtendedZones);
		var perZone = new List<Color>(count);

		for (var i = 0; i < count; i++)
		{
			var index = (int)((long)i * colours.Count / count);

			perZone.Add(colours[Math.Min(index, colours.Count - 1)]);
		}

		return perZone;
	}

	private static bool TryParseColours(string text, out List<Color> colours)
	{
		colours = [];

		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var hex = part.TrimStart('#');

			if (hex.Length != 6
				|| !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
			{
				return false;
			}

			colours.Add(new Color
			{
				R = (byte)((value >> 16) & 0xFF),
				G = (byte)((value >> 8) & 0xFF),
				B = (byte)(value & 0xFF)
			});
		}

		return colours.Count > 0;
	}

	/// <summary>
	/// Zones are written whether or not the light is on, and a dark strip after
	/// an apparently successful write is the easiest wrong conclusion to reach.
	/// </summary>
	private static async Task WarnIfOffAsync(LifxLanClient lan, LightBulb bulb)
	{
		try
		{
			var state = await lan.GetLightStateAsync(bulb, CancellationToken.None);

			if (state is { IsOn: false })
			{
				AnsiConsole.MarkupLine(
					"[yellow]![/] This light is currently off, so nothing will be visible. " +
					$"Turn it on with: lifx lan lights on {bulb.MacAddressName}");
			}
		}
		catch (TimeoutException)
		{
		}
	}
}
