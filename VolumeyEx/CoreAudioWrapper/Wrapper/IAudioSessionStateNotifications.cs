#nullable enable
using System;

namespace VolumeyEx.CoreAudioWrapper.Wrapper;

public interface IAudioSessionStateNotifications : IDisposable
{
	public event Action? SessionEnded;
	/// <summary>The session stopped playing (also when its app exits: Windows keeps the session, inactive)</summary>
	public event Action? Deactivated;
	/// <summary>The session started playing</summary>
	public event Action? Activated;
	public event Action<VolumeChangedEventArgs>? VolumeChanged;
	/// <summary>The app gave its session another display name (IAudioSessionControl::SetDisplayName), on a COM thread</summary>
	public event Action<string>? NameChanged;
	/// <summary>The app gave its session another icon (IAudioSessionControl::SetIconPath), on a COM thread</summary>
	public event Action<string>? IconPathChanged;
	public event Action<AudioSessionDisconnectReason>? Disconnected;
}
