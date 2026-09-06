namespace VRMCast.Core.Tracking
{
    /// <summary>
    /// Single-slot, thread-safe "latest value wins" mailbox (PRD 28). Producers overwrite from any thread;
    /// the consumer reads a copy on the main thread. Older frames are dropped, never queued.
    /// </summary>
    public sealed class LatestFrameBuffer<T> where T : struct
    {
        private readonly object _gate = new object();
        private T _value;
        private bool _hasValue;
        private long _sequence;

        public long Sequence
        {
            get { lock (_gate) return _sequence; }
        }

        public void Publish(in T value)
        {
            lock (_gate)
            {
                _value = value;
                _hasValue = true;
                _sequence++;
            }
        }

        public bool TryRead(out T value)
        {
            lock (_gate)
            {
                value = _value;
                return _hasValue;
            }
        }

        /// <summary>Reads only if a newer frame than <paramref name="lastSequence"/> exists.</summary>
        public bool TryReadNewer(ref long lastSequence, out T value)
        {
            lock (_gate)
            {
                if (!_hasValue || _sequence == lastSequence)
                {
                    value = default;
                    return false;
                }
                lastSequence = _sequence;
                value = _value;
                return true;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _value = default;
                _hasValue = false;
            }
        }
    }
}
