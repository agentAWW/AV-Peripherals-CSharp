using System.Text;
using System.Threading.Channels;

namespace AVPeripherals.Displays;

/// <summary>Represents a Samsung display that can be controlled using the MDC protocol.</summary>
public class SamsungMdcDisplay : ICommunicationChannel<string>, IPowerControls, IVolumeControls, IInputSwitching
{
	/// <summary>The ID of the display. Useful for daisy-chaining multiple displays off one serial port.</summary>
	public byte UnitId { get; init; }
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

	#region Parsing and Data Manipulation
	private static byte CalculateChecksum(byte[] data)
	{
		int sumInt = 0;
		foreach (byte b in data[1..^1]) { sumInt += b; }
		return (byte)sumInt;
	}
	private static string HexFrom8Bit(string str)
	{
		StringBuilder sb = new();
		foreach (char c in str)
		{ sb.AppendFormat("{0:X2}", (int)c); }
		return sb.ToString();
	}
	private string CreateTxMessage(byte command, byte? data = null)
	{
		byte[] txData =
		[
			0xAA,
			command,
			UnitId,
			(byte)(data != null ? 0x01 : 0x00),
			data ?? 0x00,
			0x00
		];
		txData[data != null ? ^1 : ^2] = CalculateChecksum(txData);

		var tx = Encoding.Latin1.GetString(data != null ? txData : txData[..^1]);
		//CrestronConsole.PrintLine($"New message to send: {HexFrom8Bit(tx)}");
		return tx;
	}
	#endregion Parsing and Data Manipulation

	#region Incoming Data Handlers
	private bool MessageIsValid(byte[] msg)
	{
		bool checksumValid = msg[^1] == CalculateChecksum(msg);
		bool idValid = msg[2] == UnitId;
		bool lengthValid = msg[3] == 3;
		bool ackValid = msg[4] == (byte)'A';
		//CrestronConsole.PrintLine($"Checksum: {checksumValid}, ID: {idValid}, Length: {lengthValid}, Ack: {ackValid}");
		return checksumValid && idValid && lengthValid && ackValid;
	}
	private void CheckNewPower(byte pwrVal)
	{
		ePowerState newPwr = pwrVal switch
		{
			0 => ePowerState.Off,
			1 => ePowerState.On,
			_ => ePowerState.Unknown
		};
		if (newPwr == PowerState) return;
		PowerState = newPwr;
		PowerStateChanged?.Invoke(this, new(PowerState));
	}
	private void CheckNewInput(byte inVal)
	{
		eInputSource newSrc = inVal switch
		{
			33 or 34 => eInputSource.HDMI_1,
			35 or 36 => eInputSource.HDMI_2,
			49 or 50 => eInputSource.HDMI_3,
			51 or 52 => eInputSource.HDMI_4,
			24 or 31 => eInputSource.DVI_1,
			37 => eInputSource.DisplayPort_1,
			38 => eInputSource.DisplayPort_2,
			39 => eInputSource.DisplayPort_3,
			64 => eInputSource.DTV,
			85 => eInputSource.HDBaseT_1,
			20 => eInputSource.VGA_1,
			99 => eInputSource.Signage,
			_ => eInputSource.Unknown
		};
		if (Input == newSrc) return;
		Input = newSrc;
		InputChanged?.Invoke(this, new(Input));
	}
	private void CheckNewMute(byte muteVal)
	{
		bool newMute = muteVal == 1;
		if (IsMuted == newMute) return;
		IsMuted = newMute;
		VolumeChanged?.Invoke(this, new(eVolumeChangeType.MuteStateChanged));
	}
	private void CheckNewVolume(byte newVol)
	{
		if (Volume == newVol) return;
		Volume = newVol;
		VolumeChanged?.Invoke(this, new(eVolumeChangeType.VolumeLevelChanged));
	}
	private async Task ProcessIncomingMessagesAsync(CancellationToken ct)
	{
		try
		{
			await foreach (string rxData in ChannelRx!.ReadAllAsync(ct))
			{
				if (!rxData.StartsWith('\xAA')) continue;
				//CrestronConsole.PrintLine($"New data incoming: {HexFrom8Bit(rxData)}");
				byte[] msgBytes = Encoding.Latin1.GetBytes(rxData);
				if (!MessageIsValid(msgBytes)) continue;
				//CrestronConsole.PrintLine($"Data is valid");
				Action<byte>? parser = msgBytes[5] switch
				{
					0x11 => CheckNewPower,
					0x12 => CheckNewVolume,
					19 => CheckNewMute,
					0x14 => CheckNewInput,
					_ => null
				};
				if (parser is null) continue;
				parser(msgBytes[6]);
			}
		}
		catch (OperationCanceledException) { }
		finally { /*CrestronConsole.PrintLine("Exiting response processing loop...");*/ }
	}
	#endregion Incoming Data Handlers

	private readonly Dictionary<byte, Action<byte>> _responseHandlers;

	/// <summary>Creates a new instance of a Samsung display using the MDC protocol.</summary>
	/// <param name="unitId">The ID to address commands/queries to. Useful for display daisy-chaining.</param>
	public SamsungMdcDisplay(byte unitId = 0)
	{
		UnitId = unitId;
		_responseHandlers = new Dictionary<byte, Action<byte>>
		{
			{ 17, CheckNewPower },
			{ 18, CheckNewVolume },
			{ 19, CheckNewMute },
			{ 20, CheckNewInput }
		};
	}

	#region Polling
	private async Task SendPollingInfoAsync(CancellationToken ct)
	{
		if (ChannelTx is null) return;
		_ = ChannelTx.TryWrite(CreateTxMessage(0x11));
		await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
		if (PowerState == ePowerState.Off) return;
		foreach (string command in new string[] { CreateTxMessage(0x14), CreateTxMessage(0x12), CreateTxMessage(19) })
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
		_ = ChannelTx?.TryComplete();
		(ChannelTx, ChannelRx) = (tx, rx);
	}
	/// <inheritdoc/>
	public void SetInput(eInputSource input)
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel is not linked.");
		byte newInput = input switch
		{
			eInputSource.HDMI_1 => 33,
			eInputSource.HDMI_2 => 35,
			eInputSource.HDMI_3 => 49,
			eInputSource.HDMI_4 => 51,
			eInputSource.DisplayPort_1 => 37,
			eInputSource.DisplayPort_2 => 38,
			eInputSource.DisplayPort_3 => 39,
			eInputSource.DTV => 64,
			eInputSource.DVI_1 => 24,
			eInputSource.HDBaseT_1 => 85,
			eInputSource.VGA_1 => 20,
			eInputSource.Signage => 99,
			_ => throw new ArgumentException("This input is not valid.", nameof(input))
		};
		_ = ChannelTx.TryWrite(CreateTxMessage(0x14, newInput));
	}
	/// <inheritdoc/>
	public void SetMute(bool mute)
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel is not linked.");
		_ = ChannelTx.TryWrite(CreateTxMessage(19, (byte)(mute ? 1 : 0)));
	}
	/// <inheritdoc/>
	public void SetPowerState(ePowerState powerState)
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel is not linked.");
		byte newPwr = powerState switch
		{
			ePowerState.On => 1,
			ePowerState.Off => 0,
			_ => throw new ArgumentException("Invalid power state", nameof(powerState))
		};
		_ = ChannelTx.TryWrite(CreateTxMessage(0x11, newPwr));
	}
	/// <inheritdoc/>
	public void SetVolume(ushort volume)
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel is not linked.");
		if (volume > 100) throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 0 and 100.");
		_ = ChannelTx.TryWrite(CreateTxMessage(0x12, (byte)volume));
	}
	/// <inheritdoc/>
	public void ToggleMute()
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel is not linked.");
		_ = ChannelTx.TryWrite(CreateTxMessage(19, (byte)(IsMuted ? 0 : 1)));
	}
	/// <inheritdoc/>
	public void TogglePowerState()
	{
		if (ChannelTx is null) throw new InvalidOperationException("Communication channel is not linked.");
		_ = ChannelTx.TryWrite(CreateTxMessage(0x11, (byte)(PowerState == ePowerState.On ? 0 : 1)));
	}
}
