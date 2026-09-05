using System.Text.Json;

namespace AVPeripherals;

/// <summary>Represents a collection of TV channels.</summary>
public class ChannelCatalog
{
	/// <summary>TV channels as an array.</summary>
	public Channel[]? Channels { get; set; }

	private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

	/// <summary>Creates a channel catalog from a JSON file.</summary>
	/// <param name="filePath">The path to the JSON file.</param>
	/// <param name="ct">A cancellation token to shut down the operation..</param>
	/// <returns>A task that represents the asynchronous operation. The task result contains the channel catalog, or null if the file could not be read.</returns>
	public static async Task<ChannelCatalog?> FromFile(string filePath, CancellationToken ct)
	{
		using FileStream openStream = File.OpenRead(filePath);
		return await JsonSerializer.DeserializeAsync<ChannelCatalog>(openStream, _jsonOptions, ct);
	}
}
