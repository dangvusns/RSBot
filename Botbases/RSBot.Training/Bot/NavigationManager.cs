using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Objects;

namespace RSBot.Training.Bot;

/// <summary>
///     Builds a walk script to the training area from the community navigation graph (Silkroad-NavLink).
///     The graph holds waypoints joined by walk edges (both ways) and teleport edges (one way, via an NPC).
/// </summary>
internal static class NavigationManager
{
    private const string DefaultLinkageUrl =
        "https://github.com/Silkroad-Developer-Community/Silkroad-NavLink/releases/latest/download/navigation_linkage.json.gz";

    private const float TeleportCost = 100f;
    private const float MaxEntryDistance = 1000f;
    private const float MoveStep = 50f;

    private static readonly string DataDirectory = Path.Combine(Kernel.BasePath, "Data");
    private static readonly string DownloadedPath = Path.Combine(DataDirectory, "navigation_linkage.json");

    // A graph the user built for their own server always wins and is never overwritten by updates.
    private static readonly string CustomPath = Path.Combine(DataDirectory, "navigation_linkage.custom.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly object _lock = new();
    private static readonly SemaphoreSlim _downloadLock = new(1, 1);

    // Several bot processes can share one Data folder; the file swap is guarded across processes.
    private static readonly string FileMutexName =
        @"Local\RSBot.NavLink." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DataDirectory.ToLowerInvariant())))[..16];

    private const int FileRetries = 5;
    private const int FileRetryDelayMs = 200;
    private static Graph _graph;

    private static string LinkageUrl => GlobalConfig.Get("RSBot.Navigation.LinkageUrl", DefaultLinkageUrl);

    /// <summary>
    ///     Builds a walk script from <paramref name="from" /> to <paramref name="target" />.
    ///     Runs on the bot thread; may wait for the first download of the graph.
    /// </summary>
    /// <returns>The script path, or <c>null</c> if no path could be built.</returns>
    public static string TryBuildWalkScript(Position from, Position target)
    {
        try
        {
            Log.Notify(Lang("AutoPathGenerating", "No walkscript set. Calculating a path to the training area..."));

            var graph = GetGraph();
            if (graph == null && !File.Exists(CustomPath))
            {
                Log.Notify(Lang("AutoPathDownloading", "Downloading navigation data..."));
                UpdateLinkageAsync(onlyIfMissing: true).GetAwaiter().GetResult();

                // Another bot process may have written the file meanwhile
                graph = GetGraph();
            }

            if (graph == null)
            {
                Log.Warn(Lang("AutoPathDataMissing", "Navigation data is not available. Record a walkscript instead."));
                return null;
            }

            var path = graph.FindPath(from, target);
            if (path == null)
            {
                Log.Warn(Lang("AutoPathNotFound", "No path found to the training area. Record a walkscript instead."));
                return null;
            }

            return WriteScript(path, from, target);
        }
        catch (Exception e)
        {
            Log.Error($"[Navigation] {e.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Downloads the latest graph and replaces the local copy. The custom graph is never touched.
    /// </summary>
    /// <param name="onlyIfMissing">Skip the update if a valid local copy exists, e.g. written by another bot process.</param>
    public static async Task<bool> UpdateLinkageAsync(bool onlyIfMissing = false)
    {
        if (!await _downloadLock.WaitAsync(0).ConfigureAwait(false))
            return false;

        try
        {
            if (onlyIfMissing && HasValidDownloadedCopy())
                return true;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await client.GetAsync(LinkageUrl).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var json = await ReadJsonAsync(stream).ConfigureAwait(false);

            // Validate before replacing the working copy.
            var linkage = Parse(json);
            if (linkage == null)
            {
                Log.Error(Lang("NavLinkUpdateFailed", "Failed to update navigation data: {0}", "malformed file"));
                return false;
            }

            if (!ReplaceDownloadedCopy(json, onlyIfMissing))
                return true;

            lock (_lock)
                _graph = null;

            Log.Notify(
                Lang("NavLinkUpdated", "Navigation data updated: {0} nodes, {1} edges (version {2}).",
                    linkage.Nodes.Count, linkage.Edges.Count, linkage.Version)
            );

            if (File.Exists(CustomPath))
                Log.Notify(Lang("NavLinkCustomInUse", "Note: navigation_linkage.custom.json is present and is used instead."));

            return true;
        }
        catch (Exception e)
        {
            Log.Error(Lang("NavLinkUpdateFailed", "Failed to update navigation data: {0}", e.Message));
            return false;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    /// <summary>
    ///     Writes the graph to the local copy. Runs synchronously, the mutex belongs to the calling thread.
    /// </summary>
    /// <returns><c>false</c> if another bot process wrote a valid copy meanwhile and <paramref name="onlyIfMissing" /> is set.</returns>
    private static bool ReplaceDownloadedCopy(string json, bool onlyIfMissing)
    {
        Directory.CreateDirectory(DataDirectory);

        using var mutex = new Mutex(false, FileMutexName);
        var owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                // The other process died while holding it; the file swap below is still safe
                owned = true;
            }

            if (onlyIfMissing && HasValidDownloadedCopy())
                return false;

            // A unique name, another process may be writing its own temp file
            var tempPath = $"{DownloadedPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tempPath, json);
                WithFileRetries(() => File.Move(tempPath, DownloadedPath, true));
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }

            return true;
        }
        finally
        {
            if (owned)
                mutex.ReleaseMutex();
        }
    }

    private static bool HasValidDownloadedCopy()
    {
        try
        {
            return File.Exists(DownloadedPath) && Parse(WithFileRetries(() => File.ReadAllText(DownloadedPath))) != null;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Retries a file access that can collide with another bot process replacing the same file.
    /// </summary>
    private static void WithFileRetries(Action action)
    {
        WithFileRetries(() =>
        {
            action();
            return true;
        });
    }

    private static T WithFileRetries<T>(Func<T> func)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return func();
            }
            catch (IOException) when (attempt < FileRetries)
            {
                Thread.Sleep(FileRetryDelayMs * attempt);
            }
        }
    }

    private static async Task<string> ReadJsonAsync(Stream stream)
    {
        // The release asset is gzipped; a self-hosted URL may serve plain JSON.
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        buffer.Position = 0;

        var isGzip = buffer.Length > 2 && buffer.GetBuffer()[0] == 0x1F && buffer.GetBuffer()[1] == 0x8B;
        using var reader = isGzip
            ? new StreamReader(new GZipStream(buffer, CompressionMode.Decompress))
            : new StreamReader(buffer);

        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static NavigationLinkage Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var linkage = JsonSerializer.Deserialize<NavigationLinkage>(json, JsonOptions);

        return linkage?.Nodes == null || linkage.Edges == null || linkage.Nodes.Count == 0 ? null : linkage;
    }

    /// <summary>
    ///     Returns the cached graph, loading it from disk on first use.
    /// </summary>
    private static Graph GetGraph()
    {
        lock (_lock)
        {
            if (_graph != null)
                return _graph;

            var path = File.Exists(CustomPath) ? CustomPath : DownloadedPath;
            if (!File.Exists(path))
                return null;

            var linkage = Parse(WithFileRetries(() => File.ReadAllText(path)));
            if (linkage == null)
            {
                Log.Warn($"[Navigation] {Path.GetFileName(path)} is malformed.");
                return null;
            }

            _graph = new Graph(linkage);

            Log.Notify(
                $"[Navigation] Loaded {Path.GetFileName(path)}: {linkage.Nodes.Count} nodes, {linkage.Edges.Count} edges, "
                    + $"version {linkage.Version} ({linkage.Date}) by {linkage.Maintainer}."
            );

            return _graph;
        }
    }

    private static string WriteScript(List<PathStep> path, Position from, Position target)
    {
        var lines = new List<string>();
        var last = from;

        foreach (var step in path)
        {
            if (step.Teleport != null)
                lines.Add($"teleport {step.Teleport.Npc} {step.Teleport.Dest}");
            else
                AddMoveCommands(lines, last, step.Position);

            last = step.Position;
        }

        AddMoveCommands(lines, last, target);

        // One file per character: overwritten on each walkback instead of piling up.
        var directory = Path.Combine(ScriptManager.InitialDirectory, "Dynamic");
        Directory.CreateDirectory(directory);

        var invalid = Path.GetInvalidFileNameChars();
        var name = string.Concat((Game.Player?.Name ?? "player").Select(c => invalid.Contains(c) ? '_' : c));
        var filePath = Path.Combine(directory, $"auto_walk_{name}.rbs");
        File.WriteAllLines(filePath, lines);

        Log.Notify(Lang("AutoPathGenerated", "Generated a walk path with {0} steps: {1}", lines.Count, Path.GetFileName(filePath)));

        return filePath;
    }

    /// <summary>
    ///     Splits a straight walk into steps of at most <see cref="MoveStep" /> units.
    /// </summary>
    private static void AddMoveCommands(List<string> lines, Position start, Position end)
    {
        var distance = start.DistanceTo(end);
        if (distance < 1)
            return;

        var segments = Math.Max(1, (int)Math.Ceiling(distance / MoveStep));
        for (var i = 1; i <= segments; i++)
        {
            Position point;
            if (i == segments)
            {
                point = end;
            }
            else
            {
                var t = (float)i / segments;
                point = new Position(
                    start.X + (end.X - start.X) * t,
                    start.Y + (end.Y - start.Y) * t,
                    t < 0.5f ? start.Region : end.Region
                )
                {
                    ZOffset = start.ZOffset + (end.ZOffset - start.ZOffset) * t,
                };
            }

            lines.Add($"move {point.XOffset} {point.YOffset} {point.ZOffset} {point.Region.X} {point.Region.Y}");
        }
    }

    private static string Lang(string key, string fallback, params object[] args)
    {
        var text = LanguageManager.GetLangBySpecificKey("RSBot.Training", key, fallback);

        return args.Length == 0 ? text : string.Format(text, args);
    }

    private sealed class PathStep
    {
        public Position Position { get; init; }

        /// <summary>The teleport taken to reach this step, or <c>null</c> for a walk.</summary>
        public LinkageEdge Teleport { get; init; }
    }

    /// <summary>
    ///     The parsed graph. Nodes at the same whole-unit position are merged, since contributors record the
    ///     same spot under different ids.
    /// </summary>
    private sealed class Graph
    {
        private readonly List<Position> _positions = new();
        private readonly List<List<(int To, float Cost, LinkageEdge Teleport)>> _edges = new();

        public Graph(NavigationLinkage linkage)
        {
            var nodeIndex = new Dictionary<string, int>();
            var positionIndex = new Dictionary<(int, int, ushort), int>();

            foreach (var (id, node) in linkage.Nodes)
            {
                if (node == null)
                    continue;

                var key = ((int)Math.Floor(node.X), (int)Math.Floor(node.Y), node.Region);
                if (!positionIndex.TryGetValue(key, out var index))
                {
                    index = _positions.Count;
                    positionIndex[key] = index;
                    _positions.Add(new Position(node.X, node.Y, node.Region));
                    _edges.Add(new List<(int, float, LinkageEdge)>());
                }

                nodeIndex[id] = index;
            }

            foreach (var edge in linkage.Edges.Values)
            {
                if (edge?.From == null || edge.To == null)
                    continue;

                if (!nodeIndex.TryGetValue(edge.From, out var from) || !nodeIndex.TryGetValue(edge.To, out var to))
                    continue;

                if (edge.Type == "teleport")
                {
                    if (!string.IsNullOrEmpty(edge.Npc) && edge.Dest != null)
                        _edges[from].Add((to, TeleportCost, edge));

                    continue;
                }

                if (from == to)
                    continue;

                var cost = (float)_positions[from].DistanceTo(_positions[to]);
                _edges[from].Add((to, cost, null));
                _edges[to].Add((from, cost, null));
            }
        }

        public List<PathStep> FindPath(Position from, Position target)
        {
            var start = Nearest(from);
            var goal = Nearest(target);
            if (start < 0 || goal < 0)
                return null;

            var cost = new Dictionary<int, float> { [start] = 0 };
            var cameFrom = new Dictionary<int, (int Prev, LinkageEdge Teleport)>();
            var open = new PriorityQueue<int, float>();
            open.Enqueue(start, Heuristic(start, goal));

            while (open.TryDequeue(out var current, out _))
            {
                if (current == goal)
                    return Reconstruct(cameFrom, start, goal);

                foreach (var (to, edgeCost, teleport) in _edges[current])
                {
                    var tentative = cost[current] + edgeCost;
                    if (cost.TryGetValue(to, out var known) && tentative >= known)
                        continue;

                    cost[to] = tentative;
                    cameFrom[to] = (current, teleport);
                    open.Enqueue(to, tentative + Heuristic(to, goal));
                }
            }

            return null;
        }

        // Straight-line distance can overestimate when a teleport is shorter, so across regions
        // fall back to Dijkstra (heuristic 0), as OasisBot does.
        private float Heuristic(int a, int b) =>
            (ushort)_positions[a].Region == (ushort)_positions[b].Region ?(float)_positions[a].DistanceTo(_positions[b]) : 0f;

        private int Nearest(Position position)
        {
            var nearest = -1;
            var best = double.MaxValue;

            for (var i = 0; i < _positions.Count; i++)
            {
                var distance = position.DistanceTo(_positions[i]);
                if (distance < best)
                {
                    best = distance;
                    nearest = i;
                }
            }

            return best < MaxEntryDistance ? nearest : -1;
        }

        private List<PathStep> Reconstruct(Dictionary<int, (int Prev, LinkageEdge Teleport)> cameFrom, int start, int goal)
        {
            var path = new List<PathStep>();
            for (var current = goal; current != start; current = cameFrom[current].Prev)
                path.Add(new PathStep { Position = _positions[current], Teleport = cameFrom[current].Teleport });

            path.Add(new PathStep { Position = _positions[start] });
            path.Reverse();

            return path;
        }
    }
}
