namespace AVPeripherals;

/// <summary>Provides power control and state for a peripheral.</summary>
public interface IPowerControls
{
	/// <summary>The current power state of the peripheral.</summary>
	ePowerState PowerState { get; }
	/// <summary>Sets the power state of the peripheral.</summary>
	/// <param name="powerState">The desired power state.</param>
	void SetPowerState(ePowerState powerState);
	/// <summary>Toggles the power state of the peripheral.</summary>
	void TogglePowerState();
	/// <summary>Occurs when the power state of the peripheral changes.</summary>
	event EventHandler<PowerChangedEventArgs>? PowerStateChanged;
}
