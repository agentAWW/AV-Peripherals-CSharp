namespace AVPeripherals;

/// <summary>Initializes a new instance of the <see cref="PowerChangedEventArgs"/> class.</summary>
/// <param name="newPowerState">The new power state.</param>
public class PowerChangedEventArgs(ePowerState newPowerState) : EventArgs
{
	/// <summary>Gets the new power state.</summary>
	public ePowerState NewPowerState { get; } = newPowerState;
}
