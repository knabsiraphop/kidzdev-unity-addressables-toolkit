using System;
using UnityEngine;

namespace KidzDev.Unity.AddressablesToolkit
{
    /// <summary>
    /// EMA-smoothed bytes/sec estimator over a monotonically increasing byte count. Samples are
    /// throttled to <see cref="MinSampleInterval"/> to avoid per-frame noise from Addressables'
    /// polling cadence.
    /// </summary>
    internal sealed class DownloadRateTracker
    {
        private const double MinSampleInterval = 0.2;
        private const float SmoothingAlpha = 0.3f;

        private readonly Func<double> _now;
        private double _lastTime;
        private long _lastBytes;
        private float _smoothed;

        public DownloadRateTracker(Func<double> nowProvider = null)
            => _now = nowProvider ?? (() => Time.realtimeSinceStartupAsDouble);

        public void Reset(long initialBytes)
        {
            _lastTime = _now();
            _lastBytes = initialBytes;
            _smoothed = 0f;
        }

        /// <summary>Report the current cumulative byte count; returns the smoothed rate (bytes/sec).</summary>
        public float Sample(long currentBytes)
        {
            var now = _now();
            var dt = now - _lastTime;
            if (dt < MinSampleInterval)
                return _smoothed;

            var instant = (float)((currentBytes - _lastBytes) / dt);
            _smoothed = _smoothed <= 0f ? instant : Mathf.Lerp(_smoothed, instant, SmoothingAlpha);
            _lastTime = now;
            _lastBytes = currentBytes;
            return _smoothed;
        }
    }
}
