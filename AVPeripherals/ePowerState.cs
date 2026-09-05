namespace AVPeripherals;

/// <summary>Represents the power state of a peripheral.</summary>
public enum ePowerState
{
	/// <summary>The power state is unknown.</summary>
	Unknown = -1,
	/// <summary>The peripheral is powered off.</summary>
	Off,
	/// <summary>The peripheral is powered on.</summary>
	On,
	/// <summary>The peripheral is cooling down.</summary>
	Cool_Down,
	/// <summary>The peripheral is warming up.</summary>
	Warm_Up
}
