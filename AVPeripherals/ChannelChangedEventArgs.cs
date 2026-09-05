namespace AVPeripherals;

/// <summary>Represents the event arguments for a channel change event.</summary>
/// <param name="newChannel">The new channel.</param>
public class ChannelChangedEventArgs(Channel newChannel) : EventArgs
{
	/// <summary>Gets the new channel.</summary>
	public Channel NewChannel { get; } = newChannel;
}
