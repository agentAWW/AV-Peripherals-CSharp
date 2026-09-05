using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace AVPeripherals.Displays;

/// <summary>Represents a Planar display that uses a well-structured protocol.</summary>
public partial class PlanarDisplay : ICommunicationChannel<string>, IPowerControls, IInputSwitching, IVolumeControls
{
	#region Inherited Properties
	/// <inheritdoc/>
	public ChannelWriter<string>? ChannelTx { get; private set; }
	/// <inheritdoc/>
	public ChannelReader<string>? ChannelRx { get; private set; }
	/// <inheritdoc/>
	public ePowerState PowerState { get; private set; } = ePowerState.Unknown;
	/// <inheritdoc/>
	public eInputSource Input { get; private set; } = eInputSource.Unknown;
	/// <inheritdoc/>
	public ushort Volume { get; private set; } = 0;
	/// <inheritdoc/>
	public bool IsMuted { get; private set; } = false;
	/// <inheritdoc/>
	public event EventHandler<PowerChangedEventArgs>? PowerStateChanged;
	/// <inheritdoc/>
	public event EventHandler<InputChangedEventArgs>? InputChanged;
	/// <inheritdoc/>
	public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
	#endregion Inherited Properties

	#region Regex Matchers
	[GeneratedRegex(@"DISPLAY\.POWER:""?(?<Power>ON|OFF)""?", RegexOptions.Multiline)]
	private partial Regex PowerStatusResponseRegex();
	[GeneratedRegex(@"SOURCE\.SELECT:""?(?<Source>[\w\d\.]+)""?", RegexOptions.Multiline)]
	private partial Regex SourceStatusResponseRegex();
	[GeneratedRegex(@"AUDIO\.VOLUME:""?(?<Volume>\d{1,3})""?", RegexOptions.Multiline)]
	private partial Regex VolumeStatusResponseRegex();
	[GeneratedRegex(@"AUDIO\.MUTE:""?(?<Mute>ON|OFF)""?", RegexOptions.Multiline)]
	private partial Regex MuteStatusResponseRegex();
	#endregion Regex Matchers

	#region Regex Handlers
	private void PowerStatusResponseHandler(Match match)
	{
		ePowerState newPwr = match.Groups["Power"].Value switch
		{
			"ON" => ePowerState.On,
			"OFF" => ePowerState.Off,
			_ => ePowerState.Unknown
		};
		if (newPwr == PowerState) return;
		PowerState = newPwr;
		PowerStateChanged?.Invoke(this, new(PowerState));
	}

	private void SourceStatusResponseHandler(Match match)
	{
		eInputSource newInput = match.Groups["Source"].Value switch
		{
			"HDMI.1" => eInputSource.HDMI_1,
			"HDMI.2" => eInputSource.HDMI_2,
			_ => eInputSource.Unknown
		};
		if (newInput == Input) return;
		Input = newInput;
		InputChanged?.Invoke(this, new(Input));
	}

	private void VolumeStatusResponseHandler(Match match)
	{
		ushort newVolume = ushort.Parse(match.Groups["Volume"].Value);
		if (newVolume == Volume) return;
		Volume = newVolume;
		VolumeChanged?.Invoke(this, new(eVolumeChangeType.VolumeLevelChanged));
	}

	private void MuteStatusResponseHandler(Match match)
	{
		bool newMute = match.Groups["Mute"].Value == "ON";
		if (newMute == IsMuted) return;
		IsMuted = newMute;
		VolumeChanged?.Invoke(this, new(eVolumeChangeType.MuteStateChanged));
	}
	#endregion Regex Handlers

	private readonly Dictionary<Regex, Action<Match>> _responseHandlers;

	/// <summary>Initializes a new instance of the <see cref="PlanarDisplay"/> class.</summary>
	public PlanarDisplay()
	{
		_responseHandlers = new Dictionary<Regex, Action<Match>>
		{
			{ PowerStatusResponseRegex(), PowerStatusResponseHandler },
			{ SourceStatusResponseRegex(), SourceStatusResponseHandler },
			{ VolumeStatusResponseRegex(), VolumeStatusResponseHandler },
			{ MuteStatusResponseRegex(), MuteStatusResponseHandler }
		};
	}

	#region Polling
	private async Task SendPollingInfoAsync(CancellationToken ct)
	{
		if (ChannelTx is null) return;
		_ = ChannelTx.TryWrite("DISPLAY.POWER?\r");
		await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
		if (PowerState == ePowerState.Off) return;
		foreach (string command in new string[] { "SOURCE.SELECT?\r", "AUDIO.VOLUME?\r", "AUDIO.MUTE?\r" })
		{
			_ = ChannelTx.TryWrite(command);
			await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
		}
	}

	private async Task PollDeviceAsync(CancellationToken ct)
	{
		PeriodicTimer timer = new(TimeSpan.FromSeconds(5));
		try
		{
			while (await timer.WaitForNextTickAsync(ct))
			{
				await SendPollingInfoAsync(ct);
			}
		}
		catch (OperationCanceledException) { }
		finally { timer.Dispose(); }
	}
	#endregion Polling

	#region Response Parsing
	private string AnalyzeResponse(string rxData)
	{
		int farthestPosition = 0;
		foreach (KeyValuePair<Regex, Action<Match>> handler in _responseHandlers)
		{
			Match match = handler.Key.Match(rxData);
			if (!match.Success) continue;
			handler.Value(match);
			int endPosition = match.Index + match.Length;
			if (endPosition > farthestPosition) farthestPosition = endPosition;
		}
		return 0 < farthestPosition && farthestPosition < (rxData.Length) ? rxData[(farthestPosition)..] : string.Empty;
	}

	private async Task ProcessIncomingMessagesAsync(CancellationToken ct)
	{
		string _rxBuf = string.Empty;
		try
		{
			await foreach (string rxData in ChannelRx!.ReadAllAsync(ct))
			{
				_rxBuf += rxData;
				_rxBuf = AnalyzeResponse(_rxBuf);
			}
		}
		catch (OperationCanceledException) { }
	}
	#endregion Response Parsing

	/// <inheritdoc/>
	public async Task ConnectAsync(CancellationToken ct)
	{
		if (ChannelTx is null || ChannelRx is null) throw new InvalidOperationException("Communication channels are not linked.");
		Task pollTask = PollDeviceAsync(ct);
		Task rxTask = ProcessIncomingMessagesAsync(ct);
		await Task.WhenAll(pollTask, rxTask);
		_ = ChannelTx.TryComplete();
	}

	/// <inheritdoc/>
	public void LinkCommChannels(ChannelWriter<string> tx, ChannelReader<string> rx)
	{
		ChannelTx?.TryComplete();
		(ChannelTx, ChannelRx) = (tx, rx);
	}
	/// <inheritdoc/>
	public void SetInput(eInputSource input)
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel not linked.");
		if (PowerState == ePowerState.Off) return;
		string newInput = input switch
		{
			eInputSource.HDMI_1 => "HDMI.1",
			eInputSource.HDMI_2 => "HDMI.2",
			_ => throw new InvalidOperationException("That input source is not yet supported by this driver.")
		};
		_ = ChannelTx.TryWrite($"SOURCE.SELECT={newInput}\r");
	}
	/// <inheritdoc/>
	public void SetMute(bool mute)
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel not linked.");
		if (PowerState == ePowerState.Off) return;
		_ = ChannelTx.TryWrite($"AUDIO.MUTE={(mute ? "ON" : "OFF")}\r");
	}
	/// <inheritdoc/>
	public void SetPowerState(ePowerState powerState)
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel not linked.");
		string command = powerState switch
		{
			ePowerState.On => "ON",
			ePowerState.Off => "OFF",
			_ => throw new InvalidOperationException("Unknown power state.")
		};
		_ = ChannelTx.TryWrite($"DISPLAY.POWER={command}\r");
	}
	/// <inheritdoc/>
	public void SetVolume(ushort volume)
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel not linked.");
		if (volume < 0 || volume > 100) throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 0 and 100.");
		if (PowerState == ePowerState.Off) return;
		_ = ChannelTx.TryWrite($"AUDIO.VOLUME={volume}\r");
	}
	/// <inheritdoc/>
	public void ToggleMute()
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel not linked.");
		if (PowerState == ePowerState.Off) return;
		_ = ChannelTx.TryWrite($"AUDIO.MUTE={(IsMuted ? "OFF" : "ON")}\r");
	}
	/// <inheritdoc/>
	public void TogglePowerState()
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel not linked.");
		_ = ChannelTx.TryWrite($"DISPLAY.POWER={(PowerState == ePowerState.On ? "OFF" : "ON")}\r");
	}
}
