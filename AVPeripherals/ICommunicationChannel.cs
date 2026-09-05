using System.Threading.Channels;

namespace AVPeripherals;

/// <summary>Specifies an AV peripheral that utilizes channels for its communications.</summary>
/// <typeparam name="T">The data type used for communication with the equipment.</typeparam>
public interface ICommunicationChannel<T>
{
	/// <summary>The stream used for outbound communication from the class.</summary>
	ChannelWriter<T>? ChannelTx { get; }
	/// <summary>The stream used for inbound communication to the class.</summary>
	ChannelReader<T>? ChannelRx { get; }
	/// <summary>Adds or replaces the channel writers and readers to the class.</summary>
	/// <param name="tx">The channel writer for outbound communication.</param>
	/// <param name="rx">The channel reader for inbound communication.</param>
	void LinkCommChannels(ChannelWriter<T> tx, ChannelReader<T> rx);
	/// <summary>Starts communicating to the device using the configured channels. Once this task is complete, communication is no longer occurring.</summary>
	/// <param name="ct">A token used to shut down the communication channels.</param>
	/// <returns>An awaitable tasks that processes communication while active.</returns>
	Task ConnectAsync(CancellationToken ct);
}
