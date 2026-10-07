using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FastDM
{
    public enum TrafficMode { Low, Medium, High }

    // একটা মোডের সীমা (FDM-এর Low / Medium / High কলামের মতো)
    public class TrafficProfile
    {
        public int SpeedKBps { get; set; }        // মোট ডাউনলোড গতি, 0 = আনলিমিটেড
        public int MaxConnections { get; set; }   // সব ডাউনলোড মিলিয়ে মোট কানেকশন
        public int PerServer { get; set; }        // প্রতি সার্ভারে কানেকশন (একটা ফাইলের সেগমেন্টও এর বেশি হয় না)
        public int Simultaneous { get; set; }     // একসাথে কয়টা ডাউনলোড

        public TrafficProfile() { }

        public TrafficProfile(int speedKBps, int maxConnections, int perServer, int simultaneous)
        {
            SpeedKBps = speedKBps;
            MaxConnections = maxConnections;
            PerServer = perServer;
            Simultaneous = simultaneous;
        }

        // FDM-এর ডিফল্ট মান
        public static TrafficProfile DefaultFor(TrafficMode m) =>
            m == TrafficMode.Low ? new TrafficProfile(256, 15, 5, 2)
            : m == TrafficMode.Medium ? new TrafficProfile(2048, 50, 8, 3)
            : new TrafficProfile(0, 200, 15, 4);

        // সংরক্ষিত ফাইল বা ইউজারের ইনপুট যেমনই হোক, সীমার ভেতরে রাখে
        public void Clamp()
        {
            SpeedKBps = Math.Clamp(SpeedKBps, 0, 10_485_760);
            PerServer = Math.Clamp(PerServer, 1, 16);
            MaxConnections = Math.Clamp(MaxConnections, PerServer, 500);   // মোট কখনো প্রতি-সার্ভারের কম নয়
            Simultaneous = Math.Clamp(Simultaneous, 1, 10);
        }

        public static string SpeedText(int kbps) =>
            kbps <= 0 ? "unlimited"
            : kbps % 1024 == 0 ? (kbps / 1024) + " MB/s"
            : kbps + " KB/s";

        public string Describe() =>
            SpeedText(SpeedKBps) + ", " + Simultaneous + " download" + (Simultaneous == 1 ? "" : "s");
    }

    // ছোট async গেট: একসাথে সর্বোচ্চ Limit জন ভেতরে ঢুকতে পারে, বাকিরা লাইনে (FIFO)।
    // Limit চলতে চলতে বদলানো যায়: বাড়ালে অপেক্ষাকারীরা সঙ্গে সঙ্গে ঢোকে, কমালে চলমানরা শেষ পর্যন্ত চলে।
    public sealed class AsyncGate
    {
        readonly object lk = new object();
        readonly LinkedList<TaskCompletionSource<bool>> waiters = new LinkedList<TaskCompletionSource<bool>>();
        int limit;
        int inUse;

        public AsyncGate(int limit) { this.limit = Math.Max(1, limit); }

        public int Limit
        {
            get { lock (lk) return limit; }
            set { lock (lk) { limit = Math.Max(1, value); Pump(); } }
        }

        public int InUse { get { lock (lk) return inUse; } }
        public int Waiting { get { lock (lk) return waiters.Count; } }

        public Task<IDisposable> WaitAsync(CancellationToken ct)
        {
            TaskCompletionSource<bool> tcs;

            lock (lk)
            {
                if (ct.IsCancellationRequested)
                    return Task.FromCanceled<IDisposable>(ct);

                if (inUse < limit && waiters.Count == 0)
                {
                    inUse++;
                    return Task.FromResult<IDisposable>(new Releaser(this));
                }

                tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                waiters.AddLast(tcs);
            }

            return WaitSlowAsync(tcs, ct);
        }

        async Task<IDisposable> WaitSlowAsync(TaskCompletionSource<bool> tcs, CancellationToken ct)
        {
            using (ct.Register(() =>
            {
                lock (lk) waiters.Remove(tcs);
                tcs.TrySetCanceled(ct);
            }))
            {
                await tcs.Task.ConfigureAwait(false);
            }

            return new Releaser(this);
        }

        void Pump()
        {
            while (inUse < limit && waiters.Count > 0)
            {
                var w = waiters.First!.Value;   // Count > 0 হওয়ায় নিশ্চিত আছে
                waiters.RemoveFirst();

                if (w.TrySetResult(true))   // বাতিল হয়ে যাওয়াটা গণনায় ধরা হয় না
                    inUse++;
            }
        }

        void Release()
        {
            lock (lk)
            {
                inUse--;
                Pump();
            }
        }

        sealed class Releaser : IDisposable
        {
            AsyncGate? gate;
            public Releaser(AsyncGate g) { gate = g; }

            public void Dispose()
            {
                var g = Interlocked.Exchange(ref gate, null);
                g?.Release();       // দুবার Dispose করলেও একবারই ছাড়ে
            }
        }
    }

    // অ্যাপজুড়ে ট্রাফিক সীমা: মোট কানেকশন + প্রতি সার্ভারে কানেকশন
    public static class Traffic
    {
        public static readonly AsyncGate Total = new AsyncGate(200);

        static readonly ConcurrentDictionary<string, AsyncGate> hosts =
            new ConcurrentDictionary<string, AsyncGate>(StringComparer.OrdinalIgnoreCase);

        static int perServer = 8;

        public static void Configure(int maxConnections, int perServerLimit)
        {
            perServer = Math.Max(1, perServerLimit);
            Total.Limit = Math.Max(1, maxConnections);

            foreach (var g in hosts.Values)
                g.Limit = perServer;
        }

        static AsyncGate Host(string? host) =>
            hosts.GetOrAdd(host ?? "", _ => new AsyncGate(perServer));

        // একটা সেগমেন্ট চালানোর অনুমতি: আগে সার্ভারের স্লট, তারপর মোট স্লট। শেষে Dispose করলে দুটোই ছাড়ে।
        public static async Task<IDisposable> EnterAsync(string? host, CancellationToken ct)
        {
            var h = await Host(host).WaitAsync(ct).ConfigureAwait(false);

            try
            {
                var t = await Total.WaitAsync(ct).ConfigureAwait(false);
                return new Both(h, t);
            }
            catch
            {
                h.Dispose();
                throw;
            }
        }

        sealed class Both : IDisposable
        {
            readonly IDisposable a, b;
            public Both(IDisposable a, IDisposable b) { this.a = a; this.b = b; }
            public void Dispose() { b.Dispose(); a.Dispose(); }
        }
    }
}
