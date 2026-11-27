using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace BehindTheGear.HideOut
{
    // Real-time Selena lines (SLM). A local server from the Selena checkpoint project
    //     python Training/generative/serve_selena.py --adapter models/selena-repair7d-17b --realtime
    // generates candidates for the given state, validates them and answers with one line (one of its own reviewed
    // pool lines when every candidate failed). This class only asks for lines and keeps them ready:
    //     Tick     base view, every frame: prefetches the next clinic-visit line and the leaving line.
    //     (own)    treatment screen: prefetches the line of the next likely event (after a heal, heal pressed
    //              during the cooldown, cooldown over), so those usually show at once as well.
    //     TryTake  base lines (SelenaBaseDialogue): uses a prefetched line when one is ready. Never waits.
    //     Request  treatment screen: uses a prefetched line, or waits up to MaxWaitSeconds for a fresh one.
    // Every request is one generation batch (about two seconds on the GPU). A prefetched line that turned out to
    // be the server's pool line is asked for again in the background until the model's own line arrives.
    // Server off, too slow or failing: the caller gets nothing and uses SelenaLinePool exactly as before.
    // Start the server before pressing Play; a server started later is noticed within about 20 seconds.
    // "continued" and the treatment upgrade events are not server situations; they stay on the pool / rule lines.
    // Switch: Tools > HideOut > Selena realtime lines (SLM server). On by default in the Editor, off in builds.
    public static class SelenaRealtime
    {
        public const string LineUrl = "http://127.0.0.1:8765/line";
        // Treatment screen: longest wait for a fresh line before the pool line is shown instead.
        public const float MaxWaitSeconds = 3.5f;

        private const float RetrySeconds = 20f;   // Server not reachable: do not ask again for this long.
        private const float QuietSeconds = 60f;   // Server had no line for a state: do not ask for that state for this long.
        private const float ReadyLifetime = 600f; // A prefetched line older than this is not used.
        private const int RequestTimeoutSeconds = 45, MaxReady = 12, MaxQueued = 5, MaxRecent = 2, MaxGearNameLength = 20;
        private const int MaxUpgrades = 2; // Extra tries to replace a prefetched pool line with a generated one.
        private const string PrefKey = "Selena.RealtimeLines";

        // The eight situations the model was trained on (situation_v7.EVENTS).
        private static readonly string[] Events =
        {
            SelenaEvent.Returned, SelenaEvent.ClinicReady, SelenaEvent.ClinicCooldown, SelenaEvent.AfterHeal,
            SelenaEvent.PotionRefilled, SelenaEvent.NewEquipment, SelenaEvent.Leaving, SelenaEvent.Tired,
        };

        // POST /line body and answer (docs/REALTIME_LINE_DESIGN_20261126.md section 8 in the checkpoint project).
        // rounds 1 = one generation batch. fast = answer as soon as one finished candidate passes (someone is waiting).
        [Serializable] private sealed class RequestBody { public StateBody state; public int rounds; public bool fast; }
        [Serializable] private sealed class StateBody
        {
            public string @event, apparentHealth, apparentStamina, clinicStatus, potionDelivery, lastMap, gear, newEquipment;
            public string[] recentLines;
        }
#pragma warning disable 0649 // Filled by JsonUtility.
        [Serializable] private sealed class Reply { public string text, source; public int attempts; }
#pragma warning restore 0649

        private sealed class Job
        {
            public string key, json, text;
            public bool waited; // A caller is waiting for this answer and takes the line itself.
            public bool done;
        }
        private sealed class ReadyLine
        {
            public string text;
            public float at;
            public bool generated; // false: the server's own pool line (its candidates failed).
            public int upgrades;   // Background tries already spent on getting a generated line instead.
        }
        private sealed class Runner : MonoBehaviour { private void Update() { RoomTick(); } }

        private static readonly List<Job> queue = new();
        private static readonly Dictionary<string, ReadyLine> ready = new();
        private static readonly Dictionary<string, float> quietUntil = new();
        private static readonly List<string> recent = new(); // Last lines Selena actually said (any source).
        private static Runner runner;
        private static HideOutPlayerBridge player; // Last bridge seen; the treatment screen prefetch uses it.
        private static HttpClient http;
        private static Job current;
        private static bool pumping, down;
        private static bool alive; // The server has answered in this play session (and has not failed since).
        private static bool? enabled;
        private static float retryAt, nextTick, nextRoomTick;
        private static string postError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            Clear();
            recent.Clear();
            // The runner (and any request coroutine on it) of an earlier play session is gone.
            runner = null; player = null; current = null; pumping = false;
            enabled = null; down = false; alive = false; retryAt = 0f; nextTick = 0f; nextRoomTick = 0f;
        }

        public static bool Enabled
        {
            get
            {
                if (!enabled.HasValue) enabled = PlayerPrefs.GetInt(PrefKey, Application.isEditor ? 1 : 0) != 0;
                return enabled.Value;
            }
            set
            {
                enabled = value;
                PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
                if (!value) Clear();
            }
        }

        private static bool Usable => Application.isPlaying && Enabled && Time.realtimeSinceStartup >= retryAt;

        private static void Clear()
        {
            foreach (var job in queue) job.done = true; // Waiting callers fall back to the pool.
            if (current != null) current.done = true;   // The request on its way is ignored when it returns.
            queue.Clear(); ready.Clear(); quietUntil.Clear();
        }

        // ---------- Used by the game ----------

        // Base view, every frame (throttled here): keeps the next clinic-visit line and the leaving line ready for
        // the current state, so the base can show them without waiting.
        public static void Tick(HideOutPlayerBridge bridge)
        {
            if (!Usable || Time.realtimeSinceStartup < nextTick) return;
            nextTick = Time.realtimeSinceStartup + .25f;
            if (!SelenaGameState.SelenaPresent(bridge)) return;
            player = bridge;
            // Inside the treatment screen the visit line was just used; RoomTick asks for that screen's lines.
            if (!TreatmentRoomScene.IsActive) Prefetch(bridge, SelenaGameState.PeekClinicVisit(bridge));
            Prefetch(bridge, SelenaGameState.Collect(bridge, SelenaEvent.Leaving));
        }

        // Treatment screen (runs on this class's own hidden object): asks in advance for the line of the next
        // likely event, with the state that event will have.
        //   booth can heal   -> after_heal with the health band the heal leads to
        //   cooldown running -> clinic_cooldown (heal pressed too early) and clinic_ready (cooldown ends in the room)
        private static void RoomTick()
        {
            if (!TreatmentRoomScene.IsActive || !alive || !Usable || Time.realtimeSinceStartup < nextRoomTick) return;
            nextRoomTick = Time.realtimeSinceStartup + .25f;
            if (player == null || !SelenaGameState.SelenaPresent(player)) return;
            var healed = SelenaGameState.PeekAfterHeal(player);
            if (healed.HasValue) Prefetch(player, healed.Value);
            else if (!SelenaGameState.ClinicReady(player))
            {
                Prefetch(player, SelenaGameState.Collect(player, SelenaEvent.ClinicCooldown));
                var reopened = SelenaGameState.Collect(player, SelenaEvent.ClinicReady);
                reopened.clinicReady = true;
                Prefetch(player, reopened);
            }
            Prefetch(player, SelenaGameState.Collect(player, SelenaEvent.Leaving));
        }

        // Asks for a line in advance. Nothing happens when a generated line is already ready, or a request is on its
        // way, for the same state. A ready pool line is asked for again (up to MaxUpgrades times, only while the
        // server has nothing else to do) so the model's own line can replace it.
        public static void Prefetch(HideOutPlayerBridge bridge, SelenaLineState state)
        {
            if (!Usable || !TryKey(bridge, state, out string key, out string gearName) || Quiet(key)) return;
            if (Pending(key) != null) return;
            if (ready.TryGetValue(key, out var line) && Time.realtimeSinceStartup - line.at <= ReadyLifetime)
            {
                if (line.generated || line.upgrades >= MaxUpgrades || current != null || queue.Count > 0) return;
                line.upgrades++;
            }
            // The state moved on: the oldest queued prefetch nobody waits for makes room.
            for (int i = 0; queue.Count >= MaxQueued && i < queue.Count; i++)
                if (!queue[i].waited) { queue[i].done = true; queue.RemoveAt(i); break; }
            Enqueue(new Job { key = key, json = Body(state, gearName, false) }, false);
        }

        // True with a prefetched line for exactly this state. Never waits and never sends a request.
        public static bool TryTake(HideOutPlayerBridge bridge, SelenaLineState state, out string text)
        {
            text = null;
            if (!Application.isPlaying || !Enabled || !TryKey(bridge, state, out string key, out _)) return false;
            return Take(key, out text);
        }

        // Treatment screen. deliver runs exactly once on the main thread: at once with a prefetched line or with null
        // (server off / not a server situation), otherwise within MaxWaitSeconds. null = use the line pool.
        // It only waits once the server has answered before (Tick finds that out in the background), so a project
        // without the server never waits.
        public static void Request(HideOutPlayerBridge bridge, SelenaLineState state, Action<string> deliver)
        {
            if (deliver == null) return;
            if (!Application.isPlaying || !Enabled || !TryKey(bridge, state, out string key, out string gearName)) { deliver(null); return; }
            player = bridge;
            if (Take(key, out string text)) { deliver(text); return; }
            if (!alive)
            {
                Prefetch(bridge, state); // Finds out in the background whether the server is there (no base view yet).
                deliver(null);
                return;
            }
            if (Time.realtimeSinceStartup < retryAt || Quiet(key)) { deliver(null); return; }
            var job = Pending(key);
            if (job != null && job != current && !job.waited)
            {
                // Its prefetch has not been sent yet: it is replaced by the hurried request below.
                job.done = true;
                queue.Remove(job);
                job = null;
            }
            if (job == null)
            {
                // Someone is watching the screen: the server answers as soon as one candidate passes (its own pool
                // line when the batch fails), and this request goes ahead of queued prefetches.
                job = new Job { key = key, json = Body(state, gearName, true) };
                Enqueue(job, true);
            }
            job.waited = true;
            Host().StartCoroutine(Wait(job, deliver));
        }

        // Call for every Selena situation line actually shown (generated or pool). The server is told the last two
        // lines so it does not repeat them.
        public static void NoteSpoken(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            recent.Remove(text);
            recent.Add(text);
            while (recent.Count > MaxRecent) recent.RemoveAt(0);
            // A prefetched copy of the same sentence would now be a repeat: drop it so a new one is fetched.
            var stale = new List<string>();
            foreach (var pair in ready) if (pair.Value.text == text) stale.Add(pair.Key);
            foreach (string key in stale) ready.Remove(key);
        }

        // ---------- State -> request ----------

        // Key of the server's model input: the pool slot plus the exact bands (one slot can bundle tired/exhausted or
        // badly hurt/critical) and the gear name. Same key = the server would be asked the same question.
        private static bool TryKey(HideOutPlayerBridge bridge, SelenaLineState state, out string key, out string gearName)
        {
            key = null; gearName = null;
            if (Array.IndexOf(Events, state.eventId) < 0) return false;
            var slot = SelenaLinePool.Shared.Resolve(state);
            if (slot == null) return false;
            var n = SelenaLinePool.Normalize(state);
            gearName = n.gear.Length > 0 ? CleanName(SelenaGameState.GearName(bridge, n.gear)) : string.Empty;
            key = string.Join("|", slot.id, n.health, n.stamina, gearName);
            return true;
        }

        // Only plain Korean names are sent (the model and its validator work in Hangul). Anything else is left out
        // and the server uses its own names for the current gear.
        private static string CleanName(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > MaxGearNameLength) return string.Empty;
            foreach (char c in name)
                if (c != ' ' && (c < '가' || c > '힣')) return string.Empty;
            return name;
        }

        private static string Body(SelenaLineState state, string gearName, bool fast)
        {
            var n = SelenaLinePool.Normalize(state);
            var body = new RequestBody
            {
                rounds = 1, fast = fast,
                state = new StateBody
                {
                    @event = n.eventId, apparentHealth = n.health, apparentStamina = n.stamina,
                    clinicStatus = n.clinicReady ? "ready" : "cooldown_wait",
                    potionDelivery = n.potionReady ? "ready" : "cooldown_wait",
                    lastMap = n.lastMap, gear = n.gear, newEquipment = gearName ?? string.Empty,
                    recentLines = recent.ToArray(),
                },
            };
            return JsonUtility.ToJson(body);
        }

        // ---------- Ready lines ----------

        private static bool Take(string key, out string text)
        {
            text = null;
            if (!ready.TryGetValue(key, out var line)) return false;
            ready.Remove(key);
            if (Time.realtimeSinceStartup - line.at > ReadyLifetime || recent.Contains(line.text)) return false;
            text = line.text;
            return true;
        }

        private static bool Quiet(string key) => quietUntil.TryGetValue(key, out float until) && Time.realtimeSinceStartup < until;

        private static Job Pending(string key)
        {
            if (current != null && !current.done && current.key == key) return current;
            foreach (var job in queue) if (job.key == key) return job;
            return null;
        }

        private static void Finish(Job job, string text, bool generated)
        {
            job.text = text; job.done = true;
            if (text == null) { quietUntil[job.key] = Time.realtimeSinceStartup + QuietSeconds; return; }
            if (job.waited || recent.Contains(text)) return; // A waiting caller shows it itself; a repeat is not kept.
            if (ready.TryGetValue(job.key, out var kept) && Time.realtimeSinceStartup - kept.at <= ReadyLifetime)
            {
                // Answer to a background retry: a generated line replaces the pool line, anything else changes nothing.
                if (generated && !kept.generated) { kept.text = text; kept.generated = true; kept.at = Time.realtimeSinceStartup; }
                return;
            }
            ready[job.key] = new ReadyLine { text = text, at = Time.realtimeSinceStartup, generated = generated };
            if (ready.Count <= MaxReady) return;
            string oldest = null;
            foreach (var pair in ready) if (oldest == null || pair.Value.at < ready[oldest].at) oldest = pair.Key;
            if (oldest != null) ready.Remove(oldest);
        }

        // ---------- Requests (one at a time; the server answers them one after another anyway) ----------

        private static Runner Host()
        {
            if (runner != null) return runner;
            var go = new GameObject("SelenaRealtime");
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
            return runner;
        }

        private static void Enqueue(Job job, bool first)
        {
            if (first) queue.Insert(0, job); else queue.Add(job);
            if (pumping) return;
            pumping = true;
            Host().StartCoroutine(Pump());
        }

        private static IEnumerator Pump()
        {
            while (queue.Count > 0)
            {
                var job = queue[0];
                queue.RemoveAt(0);
                current = job;
                yield return Send(job);
                if (current == job) current = null;
            }
            pumping = false;
        }

        private static IEnumerator Wait(Job job, Action<string> deliver)
        {
            float until = Time.realtimeSinceStartup + MaxWaitSeconds;
            while (!job.done && Time.realtimeSinceStartup < until) yield return null;
            if (job.done) { deliver(job.text); yield break; }
            job.waited = false; // Too slow: the answer, when it arrives, is kept ready for the next time.
            deliver(null);
        }

        private static Task<HttpResponseMessage> Post(string json)
        {
            try
            {
                if (http == null)
                {
                    http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds) };
                    http.DefaultRequestHeaders.ExpectContinue = false; // The body goes out with the headers.
                }
                return http.PostAsync(LineUrl, new StringContent(json, Encoding.UTF8, "application/json"));
            }
            catch (Exception e)
            {
                postError = e.Message;
                return null;
            }
        }

        private static Reply Parse(string payload)
        {
            try { return string.IsNullOrEmpty(payload) ? null : JsonUtility.FromJson<Reply>(payload); }
            catch (Exception) { return null; }
        }

        private static IEnumerator Send(Job job)
        {
            float started = Time.realtimeSinceStartup;
            postError = null;
            Task<HttpResponseMessage> post = Post(job.json);
            while (post != null && !post.IsCompleted) yield return null;
            if (job.done) yield break; // Real-time lines were switched off meanwhile.
            if (post == null || post.IsFaulted || post.IsCanceled)
            {
                string reason = post == null ? postError : post.IsCanceled ? "no answer in time" : post.Exception?.GetBaseException().Message;
                ServerDown(job, reason);
                yield break;
            }

            HttpResponseMessage response = post.Result;
            Task<string> read = response.Content.ReadAsStringAsync();
            while (!read.IsCompleted) yield return null;
            int status = (int)response.StatusCode;
            bool ok = response.IsSuccessStatusCode && !read.IsFaulted && !read.IsCanceled;
            Reply reply = ok ? Parse(read.Result) : null;
            response.Dispose();
            if (job.done) yield break;

            if (down) Debug.Log("[Selena] Real-time line server is answering again.");
            down = false; alive = true;
            // "generated" = made by the model just now, "pool" = the server's reviewed line after its candidates failed.
            // Anything else (e.g. a server started without --realtime) is not used.
            bool usable = reply != null && (reply.source == "generated" || reply.source == "pool") && !string.IsNullOrWhiteSpace(reply.text);
            string text = usable ? reply.text.Trim() : null;
            float seconds = Time.realtimeSinceStartup - started;
            if (text == null) Debug.Log($"[Selena] realtime: no line for {job.key} (HTTP {status}); the line pool is used.");
            else Debug.Log($"[Selena] realtime {reply.source} in {seconds:0.0}s ({reply.attempts} candidates) {job.key}: {text}");
            Finish(job, text, usable && reply.source == "generated");
        }

        private static void ServerDown(Job job, string reason)
        {
            retryAt = Time.realtimeSinceStartup + RetrySeconds;
            if (!down)
                Debug.Log($"[Selena] Real-time line server not reachable ({reason}). Using the reviewed line pool. " +
                          "Start serve_selena.py --realtime in the Selena checkpoint project to get generated lines.");
            down = true; alive = false;
            job.done = true;
            foreach (var waiting in queue) waiting.done = true; // Their callers fall back to the pool right away.
            queue.Clear();
        }

#if UNITY_EDITOR
        private const string MenuPath = "Tools/HideOut/Selena realtime lines (SLM server)";

        [UnityEditor.MenuItem(MenuPath)]
        private static void ToggleFromMenu()
        {
            Enabled = !Enabled;
            Debug.Log(Enabled
                ? "[Selena] Real-time lines ON: lines come from the local SLM server when it is running (line pool otherwise)."
                : "[Selena] Real-time lines OFF: reviewed line pool only.");
        }

        [UnityEditor.MenuItem(MenuPath, true)]
        private static bool ToggleFromMenuValidate()
        {
            UnityEditor.Menu.SetChecked(MenuPath, Enabled);
            return true;
        }
#endif
    }
}
