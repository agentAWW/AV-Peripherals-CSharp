namespace AVPeripherals;

/// <summary>Represents a peripheral with a built-in TV tuner for cable, satellite, or over-the-air broadcasts.</summary>
public interface ITvTuner
{
	/// <summary>Gets the current channel.</summary>
	Channel? CurrentChannel { get; }
	/// <summary>Gets the channel catalog.</summary>
	ChannelCatalog? ChannelList { get; }
	/// <summary>Attaches a channel catalog instance to the tuner.</summary>
	/// <param name="catalog">The new channel catalog.</param>
	void SetCatalog(ChannelCatalog catalog);
	/// <summary>Sets the current channel.</summary>
	/// <param name="channel">The channel to set.</param>
	void SetChannel(Channel channel);
	/// <summary>Sets the current channel by number. Useful for IR-only control of tuners or for tuners on encrypted feeds, where the cable company uses human-readable channel numbers.</summary>
	/// <param name="channelNumber">The channel number to set.</param>
	void SetChannel(int channelNumber);
	/// <summary>Sets the current channel by tuning information. Useful for setting the channel by major-minor numbers, such as for OTA/unencrypted cable feeds.</summary>
	/// <param name="channelTuning">The tuning location of the channel.</param>
	void SetChannel(string channelTuning);
	/// <summary>Occurs when the current channel changes.</summary>
	event EventHandler<ChannelChangedEventArgs>? ChannelChanged;
	/// <summary>Gets a value indicating whether closed captions are currently active on the set top box.</summary>
	event EventHandler<CaptionsChangedEventArgs>? CaptionsChanged;
}
