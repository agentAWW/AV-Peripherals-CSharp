namespace AVPeripherals;

/// <summary>Provides volume control and state for a peripheral.</summary>
public interface IVolumeControls
{
	/// <summary>Gets the current volume level.</summary>
	ushort Volume { get; }
	/// <summary>Gets a value indicating whether the peripheral is muted.</summary>
	bool IsMuted { get; }
	/// <summary>Sets the volume level.</summary>
	/// <param name="volume">The desired volume level.</param>
	void SetVolume(ushort volume);
	/// <summary>Sets the mute state.</summary>
	/// <param name="mute">The desired mute state.</param>
	void SetMute(bool mute);
	/// <summary>Toggles the mute state of the peripheral.</summary>
	void ToggleMute();
	/// <summary>Occurs when the volume state of the peripheral changes.</summary>
	event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
}
