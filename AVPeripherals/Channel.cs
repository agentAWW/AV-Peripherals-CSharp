namespace AVPeripherals;

/// <summary>Represents a TV channel in a larger dataset.</summary>
public class Channel
{
	/// <summary>The name of the channel.</summary>
	public string? Name { get; set; }
	/// <summary>The major channel number.</summary>
	public int DtvMajor { get; set; }
	/// <summary>The minor channel number.</summary>
	public int DtvMinor { get; set; }
	/// <summary>The user-readable channel number.</summary>
	public int ChNum { get; set; }
	/// <summary>The subscription package to which the channel belongs.</summary>
	public string? Package { get; set; }
	/// <summary>The CSS class used to represent the channel on a UI.</summary>
	public string? CssClass { get; set; }
	/// <summary>Returns a string showing the channel information in the form of "DtvMajor-DtvMinor: Name"</summary>
	/// <returns>The formatted channel information.</returns>
	public override string ToString() => $"{DtvMajor}-{DtvMinor}: {Name}";
}
