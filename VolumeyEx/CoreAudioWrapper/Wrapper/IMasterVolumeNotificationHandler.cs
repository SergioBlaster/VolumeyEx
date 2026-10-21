#nullable enable
using System;

namespace VolumeyEx.CoreAudioWrapper.Wrapper;

public interface IMasterVolumeNotificationHandler : IDisposable
{
	event Action<VolumeChangedEventArgs>? VolumeChanged;
}
