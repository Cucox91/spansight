using Microsoft.Extensions.Options;

using SpanSight.Core.Ai;

namespace SpanSight.Api.Ai;

/// <summary>
/// The ADR-008 §4 cost governor: a hard daily request ceiling that trips /api/ai/* to
/// "temporarily unavailable" instead of letting spend run. In-memory (resets on restart and on
/// UTC date change) — deliberately simple for the single-replica demo tier; a shared store can
/// take over if the API ever scales out.
/// </summary>
public sealed class AiRequestBudget(IOptions<AiOptions> options, TimeProvider timeProvider)
{
    private readonly Lock _lock = new();
    private DateOnly _day;
    private int _used;

    public bool TryConsume()
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        lock (_lock)
        {
            if (today != _day)
            {
                _day = today;
                _used = 0;
            }

            if (_used >= options.Value.MaxRequestsPerDay)
            {
                return false;
            }

            _used++;
            return true;
        }
    }
}
