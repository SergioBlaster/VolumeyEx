#nullable enable
using log4net.Core;
using log4net.Filter;

namespace VolumeyEx.Helper;

public sealed class DuplicateLogMessageFilter : FilterSkeleton
{
	private string? lastMsg;
	public override FilterDecision Decide(LoggingEvent loggingEvent)
	{
		string? newMessage = loggingEvent?.MessageObject?.ToString();
		if(newMessage != null && newMessage.Equals(lastMsg))
			return FilterDecision.Deny;
		lastMsg = newMessage;
		return FilterDecision.Accept;
	}
}
