using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EchoKey.Core;

namespace EchoKey.Audio;

/// <summary>
/// Keeps decoded sounds in RAM (so a hotkey press starts instantly) under a fixed memory budget.
/// Least-recently-used sounds are evicted and decoded again on demand. UI thread only.
/// </summary>
public sealed class SoundCache
{
    const long BudgetBytes = 96L * 1024 * 1024;

    sealed class Entry
    {
        public Task<short[]> Task = null!;
        public long Bytes;
        public long LastUse;
    }

    readonly Dictionary<Guid, Entry> _map = new();
    long _total;
    long _tick;

    public Task<short[]> GetAsync(SoundItem item)
    {
        if (_map.TryGetValue(item.Id, out var existing))
        {
            existing.LastUse = ++_tick;
            return existing.Task;
        }
        var e = new Entry { LastUse = ++_tick };
        e.Task = LoadAsync(item, e);
        _map[item.Id] = e;
        return e.Task;
    }

    async Task<short[]> LoadAsync(SoundItem item, Entry e)
    {
        string path = Path.Combine(AppPaths.SoundsDir, item.FileName);
        short[] data;
        try
        {
            data = await Task.Run(() => SoundDecoder.Decode(path));
        }
        catch
        {
            if (_map.TryGetValue(item.Id, out var cur) && ReferenceEquals(cur, e))
                _map.Remove(item.Id);
            throw;
        }
        e.Bytes = data.Length * 2L;
        _total += e.Bytes;
        Evict(item.Id);
        return data;
    }

    void Evict(Guid keep)
    {
        while (_total > BudgetBytes)
        {
            Guid victim = Guid.Empty;
            long best = long.MaxValue;
            foreach (var kv in _map)
            {
                if (kv.Key == keep || kv.Value.Bytes == 0) continue;
                if (kv.Value.LastUse < best) { best = kv.Value.LastUse; victim = kv.Key; }
            }
            if (victim == Guid.Empty) break;
            _total -= _map[victim].Bytes;
            _map.Remove(victim);
        }
    }

    public void Remove(Guid id)
    {
        if (_map.TryGetValue(id, out var e))
        {
            _total -= e.Bytes;
            _map.Remove(id);
        }
    }

    /// <summary>Decodes sounds in the background until about 75% of the budget is used.</summary>
    public async Task PreloadAsync(IEnumerable<SoundItem> items)
    {
        foreach (var item in items.ToList())
        {
            if (_total > BudgetBytes * 3 / 4) break;
            try { await GetAsync(item); }
            catch { /* broken/missing file: reported when the user tries to play it */ }
        }
    }
}
