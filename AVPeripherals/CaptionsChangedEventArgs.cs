namespace AVPeripherals;

/// <summary>Represents the event arguments for a change in closed captioning status.</summary>
/// <param name="captionsEnabled">A value indicating whether closed captions are enabled.</param>
public class CaptionsChangedEventArgs(bool captionsEnabled) : EventArgs
{
	/// <summary>Gets a value indicating whether closed captions are enabled.</summary>
	public bool CaptionsEnabled { get; init; } = captionsEnabled;
}
