#nullable enable
using VolumeyEx.Model;

namespace VolumeyEx.ViewModel.Settings;

public interface IAudioProcessStateMediator
{
	void NotifyAudioStateChange(IManagedMasterAudioSession sender);
	void NotifyOfDisposing(IManagedMasterAudioSession sender);
}
