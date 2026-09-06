using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace AVPeripherals.TvTuners;

/// <summary>Connects to a Contemporary Research ATSC tuner, such as the ATSC-mini.</summary>
public partial class ContemporaryResearchATSC : ICommunicationChannel<string>, IPowerControls, ITvTuner
{
	/// <summary>The tuner's unit ID.</summary>
	public byte UnitId { get; init; }
	/// <summary>Whether closed captions are currently active on the set top box.</summary>
	public bool ClosedCaptionsEnabled { get; private set; }

	/// <summary>
	/// The currently tuned channel, or <c>null</c> when no channel is known.
	/// </summary>
	public Channel? CurrentChannel { get; private set; }
	/// <summary>
	/// The catalog of known channels used for tuning and lookups.
	/// </summary>
	public ChannelCatalog? ChannelList { get; private set; }

	/// <summary>
	/// Writer channel used to transmit commands to the tuner device.
	/// </summary>
	public ChannelWriter<string>? ChannelTx { get; private set; }

	/// <summary>
	/// Reader channel used to receive responses from the tuner device.
	/// </summary>
	public ChannelReader<string>? ChannelRx { get; private set; }

	/// <summary>
	/// The current power state of the tuner.
	/// </summary>
	public ePowerState PowerState { get; private set; } = ePowerState.Unknown;

	/// <summary>
	/// Raised when the tuner's power state changes.
	/// </summary>
	public event EventHandler<PowerChangedEventArgs>? PowerStateChanged;

	/// <summary>
	/// Raised when the currently selected channel changes.
	/// </summary>
	public event EventHandler<ChannelChangedEventArgs>? ChannelChanged;

	/// <summary>
	/// Raised when the closed captions enabled state changes.
	/// </summary>
	public event EventHandler<CaptionsChangedEventArgs>? CaptionsChanged;

	#region Response Handlers
	private void ChannelSourceStatusResponseHandler(Match match)
	{
		if (match.Groups["UnitId"].Value != UnitId.ToString()) return;

		ePowerState newPwr = match.Groups["Power"].Value switch
		{
			"U" => ePowerState.On,
			"M" => ePowerState.Off,
			_ => ePowerState.Unknown
		};
		if (newPwr != PowerState)
		{
			PowerState = newPwr;
			PowerStateChanged?.Invoke(this, new(PowerState));
		}

		if (PowerState != ePowerState.On || ChannelList is null) return;
		int majChn = match.Groups["ChannelMajor"].Value switch
		{
			"xxx" => -1,
			string s => Convert.ToInt32(s, 10)
		};
		int minChn = match.Groups["ChannelMinor"].Value switch
		{
			"xxx" => -1,
			string s => Convert.ToInt32(s, 10)
		};
		if (CurrentChannel is not null && CurrentChannel.DtvMajor == majChn && CurrentChannel.DtvMinor == minChn) return;
		IEnumerable<Channel> possibleChannels =
			from channel in ChannelList.Channels
			where channel.DtvMajor == majChn && channel.DtvMinor == minChn
			select channel;
		CurrentChannel = possibleChannels.FirstOrDefault() ?? new Channel { DtvMajor = majChn, DtvMinor = minChn };
		ChannelChanged?.Invoke(this, new(CurrentChannel));
	}

	private void FrontPanelModeStatusResponseHandler(Match match)
	{
		if (match.Groups["UnitId"].Value != UnitId.ToString()) return;
	}

	private void QModeResponseHandler(Match match)
	{
		if (match.Groups["UnitId"].Value != UnitId.ToString()) return;
		bool newCCMode = match.Groups["CCMode"].Value == "1";
		if (newCCMode == ClosedCaptionsEnabled) return;
		ClosedCaptionsEnabled = newCCMode;
		CaptionsChanged?.Invoke(this, new(newCCMode));
	}
	#endregion Response Handlers

	#region Regex Compilation
	[GeneratedRegex(@"<(?<UnitId>\d)T(?<Power>U|M)(?<ChannelMajor>[\dx]{3})U0(?<RfMode>[AC])(?<RxRes>[0-4N])(?<ChannelMinor>[\dx]{3})x0", RegexOptions.Multiline)]
	private static partial Regex ChannelSourceStatusResponseRegex();
	[GeneratedRegex(@"<(?<UnitId>\d)Sx(?<TuneMode>[0-4])(?<Lockout>\d)084(?<OutColor>[02])(?<OutResCurrent>[0-57])(?<OutResSetting>[0-7])x{4}", RegexOptions.Multiline)]
	private static partial Regex FrontPanelModeStatusResponseRegex();
	[GeneratedRegex(@"<(?<UnitId>\d)Q(?<CCMode>[01])(?<CCType>[1-8])302(?<IRControl>[09])0(?<DigitalCC>[01])(?<DigitalCCService>[1-6])x{2}", RegexOptions.Multiline)]
	private static partial Regex QModeResponseRegex();
	[GeneratedRegex(@"^(?<ChannelMajor>\d{1,3})[-:](?<ChannelMinor>\d{1,3})$")]
	private static partial Regex ChannelTuningParser();
	#endregion Regex Compilation
	private readonly Dictionary<Regex, Action<Match>> _responseHandlers;

	/// <summary>
	/// Initializes a new instance of the <see cref="ContemporaryResearchATSC"/> class.
	/// </summary>
	/// <param name="channelList">Optional channel catalog used for tuning lookups.</param>
	/// <param name="unitId">The unit identifier of the tuner device.</param>
	public ContemporaryResearchATSC(ChannelCatalog? channelList = default, byte unitId = 1)
	{
		UnitId = unitId;
		ChannelList = channelList;
		_responseHandlers = new()
		{
			{ ChannelSourceStatusResponseRegex(), ChannelSourceStatusResponseHandler },
			{ FrontPanelModeStatusResponseRegex(), FrontPanelModeStatusResponseHandler },
			{ QModeResponseRegex(), QModeResponseHandler }
		};
	}

	#region Polling
	private void SendPollingInfoAsync()
	{
		if (ChannelTx is null) return;
		_ = ChannelTx.TryWrite($">{UnitId}ST\r\n>{UnitId}SQ\r\n");
	}

	private async Task PollDeviceAsync(CancellationToken ct)
	{
		PeriodicTimer timer = new(TimeSpan.FromSeconds(5));
		try
		{
			while (await timer.WaitForNextTickAsync(ct))
			{ SendPollingInfoAsync(); }
		}
		catch (OperationCanceledException) { }
		finally { timer.Dispose(); }
	}
	#endregion Polling

	private string AnalyzeResponse(string rxData)
	{
		foreach (KeyValuePair<Regex, Action<Match>> handler in _responseHandlers)
		{
			Match match = handler.Key.Match(rxData);
			if (match.Success) handler.Value(match);
		}
		int lastBreak = rxData.LastIndexOfAny(['\r', '\n']);
		return 0 < lastBreak && lastBreak < (rxData.Length - 1) ? rxData[(lastBreak + 1)..] : string.Empty;
	}

	private async Task ReceiveRxMessagesAsync(CancellationToken ct)
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

	/// <summary>
	/// Listens for responses from the device and dispatches parsed events until canceled.
	/// </summary>
	/// <param name="ct">Cancellation token used to stop processing.</param>
	/// <exception cref="InvalidOperationException">Thrown when communication channels are not linked.</exception>
	public async Task ConnectAsync(CancellationToken ct)
	{
		if (ChannelTx is null || ChannelRx is null) throw new InvalidOperationException("Communication channels are not linked.");
		//ChannelTx.TryWrite($">{UnitId}ST\r\n>{UnitId}SS\r\n>{UnitId}SQ\r\n");
		//ChannelTx.TryWrite($">{UnitId}ST\r\n>{UnitId}SQ\r\n");
		Task pollTask = PollDeviceAsync(ct);
		Task rxTask = ReceiveRxMessagesAsync(ct);
		await Task.WhenAll(pollTask, rxTask);
		_ = ChannelTx.TryComplete();
	}

	/// <summary>
	/// Links the communication channels used to send commands and receive responses from the device.
	/// Any previously linked transmit channel will be completed prior to linking the new channels.
	/// </summary>
	/// <param name="tx">Channel writer used to transmit strings to the device.</param>
	/// <param name="rx">Channel reader used to receive strings from the device.</param>
	public void LinkCommChannels(ChannelWriter<string> tx, ChannelReader<string> rx)
	{
		ChannelTx?.TryComplete();
		(ChannelTx, ChannelRx) = (tx, rx);
	}

	/// <summary>
	/// Sets or replaces the channel catalog used for tuning operations.
	/// </summary>
	/// <param name="catalog">The channel catalog to use.</param>
	public void SetCatalog(ChannelCatalog catalog) => ChannelList = catalog;

	/// <summary>
	/// Sends a command to tune the tuner to the specified channel.
	/// </summary>
	/// <param name="channel">The channel to tune to. Must contain valid DTV major/minor values.</param>
	/// <exception cref="InvalidOperationException">Thrown when communication channel is not linked.</exception>
	public void SetChannel(Channel channel)
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel not linked.");
		if (channel.DtvMajor < 1 || channel.DtvMinor < 1) return;
		string txData = $">{UnitId}TC={channel.DtvMajor}-{channel.DtvMinor}\r\n";
		_ = ChannelTx.TryWrite(txData);
	}
	/// <summary>
	/// Tunes to the first channel in the catalog that matches the supplied channel number.
	/// </summary>
	/// <param name="channelNumber">The source channel number to tune to.</param>
	/// <exception cref="InvalidOperationException">Thrown when the channel catalog is not linked.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when the channel number is not listed in the catalog.</exception>
	public void SetChannel(int channelNumber)
	{
		if (ChannelList is null) throw new InvalidOperationException("Channel database is not linked.");
		IEnumerable<Channel> newChannel =
			from channel in ChannelList.Channels
			where channel.ChNum == channelNumber
			select channel;
		SetChannel(newChannel.FirstOrDefault() ?? throw new ArgumentOutOfRangeException(nameof(channelNumber), "That channel isn't listed in the database."));
	}
	/// <summary>
	/// Tunes to a channel specified by a tuning string in the format "major-minor" or "major:minor".
	/// </summary>
	/// <param name="channelTuning">A tuning string formatted as "major-minor" or "major:minor".</param>
	/// <exception cref="InvalidOperationException">Thrown when the channel catalog is not linked.</exception>
	/// <exception cref="ArgumentException">Thrown when the tuning string is not in the correct format.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when the parsed channel is not listed in the catalog.</exception>
	public void SetChannel(string channelTuning)
	{
		if (ChannelList is null) throw new InvalidOperationException("Channel database is not linked.");
		Match chMatch = ChannelTuningParser().Match(channelTuning);
		if (!chMatch.Success) throw new ArgumentException("Channel tuning string is not in the correct format.", nameof(channelTuning));
		(int majChn, int minChn) = (Convert.ToInt32(chMatch.Groups["ChannelMajor"].Value, 10), Convert.ToInt32(chMatch.Groups["ChannelMinor"].Value, 10));
		IEnumerable<Channel> newChannel =
			from channel in ChannelList.Channels
			where channel.DtvMajor == majChn && channel.DtvMinor == minChn
			select channel;
		SetChannel(newChannel.FirstOrDefault() ?? throw new ArgumentOutOfRangeException(nameof(channelTuning), "That channel isn't listed in the database."));
	}

	/// <summary>
	/// Sends a power command to the tuner to set its power state to the specified value.
	/// </summary>
	/// <param name="powerState">The desired power state; only <see cref="ePowerState.On"/> and <see cref="ePowerState.Off"/> are supported.</param>
	/// <exception cref="InvalidOperationException">Thrown when the communication channel is not linked.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="powerState"/> is not a supported value for this operation.</exception>
	public void SetPowerState(ePowerState powerState)
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel not linked.");
		string txData = powerState switch
		{
			ePowerState.On => $">{UnitId}P1\r\n",
			ePowerState.Off => $">{UnitId}P0\r\n",
			_ => throw new ArgumentOutOfRangeException(nameof(powerState), "Power state must be On or Off.")
		};
		_ = ChannelTx.TryWrite(txData);
	}
	/// <summary>
	/// Toggles the current power state of the tuner: turns it off if it is on, or turns it on if it is off.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when the communication channel is not linked, or when the current power state is unknown.</exception>
	public void TogglePowerState()
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel not linked.");
		string txData = PowerState switch
		{
			ePowerState.On => $">{UnitId}P0\r\n",
			ePowerState.Off => $">{UnitId}P1\r\n",
			_ => throw new InvalidOperationException("Power state is unknown, cannot toggle.")
		};
		_ = ChannelTx.TryWrite(txData);
	}
}
