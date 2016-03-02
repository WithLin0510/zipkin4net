﻿using System;
using System.Threading;
using Criteo.Profiling.Tracing.Annotation;
using Criteo.Profiling.Tracing.Dispatcher;
using Criteo.Profiling.Tracing.Sampling;
using Criteo.Profiling.Tracing.Utils;

namespace Criteo.Profiling.Tracing
{

    /// <summary>
    /// Represents a trace. It records the annotations to the globally registered tracers.
    /// </summary>
    public sealed class Trace : IEquatable<Trace>
    {

        internal SpanId CurrentId { get; private set; }

        private static readonly ISampler Sampler = new DefaultSampler(salt: RandomUtils.NextLong(), samplingRate: 0f);
        private static IRecordDispatcher _dispatcher = new VoidDispatcher();
        private static int _status = (int)Status.Stopped;

        internal static Configuration Configuration = new Configuration();

        /// <summary>
        /// Returns true if tracing is currently running and forwarding records to the registered tracers.
        /// </summary>
        /// <returns></returns>
        public static bool TracingRunning
        {
            get { return _status == (int)Status.Started; }
        }

        /// <summary>
        /// Start tracing, records will be forwarded to the registered tracers.
        /// </summary>
        /// <returns>True if successfully started, false if error or the service was already running.</returns>
        public static bool Start(Configuration configuration)
        {
            return Start(configuration, new InOrderAsyncDispatcher(Tracer.Push));
        }

        internal static bool Start(Configuration configuration, IRecordDispatcher dispatcher)
        {
            if (Interlocked.CompareExchange(ref _status, (int)Status.Started, (int)Status.Stopped) ==
                      (int)Status.Stopped)
            {
                Configuration = configuration;
                _dispatcher = dispatcher;
                Configuration.Logger.LogInformation("Tracing dispatcher started");
                Configuration.Logger.LogInformation("HighResolutionDateTime is " + (HighResolutionDateTime.IsAvailable ? "available" : "not available"));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Stop tracing, records will be ignored.
        /// </summary>
        /// <returns></returns>
        public static bool Stop()
        {
            if (Interlocked.CompareExchange(ref _status, (int)Status.Stopped, (int)Status.Started) ==
                   (int)Status.Started)
            {
                _dispatcher.Stop(); // InOrderAsyncDispatcher
                _dispatcher = new VoidDispatcher();
                Configuration.Logger.LogInformation("Tracing dispatcher stopped");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Sampling of the tracing. Between 0.0 (not tracing) and 1.0 (full tracing). Default 0.0
        /// </summary>
        public static float SamplingRate
        {
            get { return Sampler.SamplingRate; }
            set { Sampler.SamplingRate = value; }
        }

        /// <summary>
        /// Starts a new trace with a random id, no parent and empty flags.
        /// </summary>
        /// <returns></returns>
        public static Trace CreateIfSampled()
        {
            var traceId = RandomUtils.NextLong();
            return Sampler.Sample(traceId) ? new Trace(traceId) : null;
        }

        /// <summary>
        /// Creates a trace from an existing span id.
        /// </summary>
        /// <param name="spanId"></param>
        /// <returns></returns>
        public static Trace CreateFromId(SpanId spanId)
        {
            return new Trace(spanId);
        }

        private Trace(SpanId spanId)
        {
            CurrentId = new SpanId(spanId.TraceId, spanId.ParentSpanId, spanId.Id, spanId.Flags);
        }

        private Trace(long traceId)
        {
            CurrentId = CreateRootSpanId(traceId);
        }

        private static SpanId CreateRootSpanId(long traceId)
        {
            return new SpanId(traceId: traceId, parentSpanId: null, id: RandomUtils.NextLong(), flags: Flags.Empty());
        }

        /// <summary>
        /// Creates a derived trace which inherits from
        /// the trace id and flags.
        /// It has a new span id and the parent id set to the current span id.
        /// </summary>
        /// <returns></returns>
        public Trace Child()
        {
            return new Trace(CreateChildSpanId());
        }

        private SpanId CreateChildSpanId()
        {
            return new SpanId(traceId: CurrentId.TraceId, parentSpanId: CurrentId.Id, id: RandomUtils.NextLong(), flags: CurrentId.Flags);
        }

        internal void RecordAnnotation(IAnnotation annotation)
        {
            var record = new Record(CurrentId, TimeUtils.UtcNow, annotation);
            _dispatcher.Dispatch(record);
        }

        public bool Equals(Trace other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(CurrentId, other.CurrentId);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            var objTrace = obj as Trace;
            return objTrace != null && Equals(objTrace);
        }

        public override int GetHashCode()
        {
            return (CurrentId != null ? CurrentId.GetHashCode() : 0);
        }

        public override string ToString()
        {
            return String.Format("Trace [{0}]", CurrentId);
        }

        private enum Status
        {
            Started,
            Stopped
        }
    }

    /**
     * Traces are sampled for performance management. Therefore trace can be null
     * and you probably don't want to check for nullity every time in your code.
     */
    public static class TraceExtensions
    {
        public static void Record(this Trace trace, IAnnotation annotation)
        {
            if (trace != null)
            {
                trace.RecordAnnotation(annotation);
            }
        }
    }


}
