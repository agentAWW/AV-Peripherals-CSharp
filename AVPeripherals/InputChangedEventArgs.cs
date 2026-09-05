namespace AVPeripherals;

/// <summary>Initializes a new instance of the <see cref="InputChangedEventArgs"/> class.</summary>
/// <param name="newInput">The new input source.</param>
public class InputChangedEventArgs(eInputSource newInput) : EventArgs
{
	/// <summary>Gets the new input source.</summary>
	public eInputSource NewInput { get; } = newInput;
}
