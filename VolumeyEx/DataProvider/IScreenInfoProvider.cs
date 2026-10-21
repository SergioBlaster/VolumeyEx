#nullable enable
using System.Collections.Generic;
using VolumeyEx.Model;

namespace VolumeyEx.DataProvider;

public interface IScreenInfoProvider
{
	IEnumerable<ScreenInfo> GetAllScreensInfo();
}
