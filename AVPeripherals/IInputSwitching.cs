namespace AVPeripherals;

/// <summary>Represents a peripheral that can switch its input source.</summary>
public interface IInputSwitching
{
	/// <summary>Gets the current input source.</summary>
	eInputSource Input { get; }
	/// <summary>Sets the input source.</summary>
	/// <param name="input">The input source to set.</param>
	void SetInput(eInputSource input);
	/// <summary>Occurs when the input source of the peripheral changes.</summary>
	event EventHandler<InputChangedEventArgs>? InputChanged;
}
