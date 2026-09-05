namespace AVPeripherals;

/// <summary>Initializes a new instance of the <see cref="VolumeChangedEventArgs"/> class.</summary>
/// <param name="changeType">The type of volume change that occurred.</param>
public class VolumeChangedEventArgs(eVolumeChangeType changeType) : EventArgs
{
	/// <summary>Gets the type of volume change that occurred.</summary>
	public eVolumeChangeType ChangeType { get; } = changeType;
}
