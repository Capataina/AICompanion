#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AICompanion.Tools.SessionReport;

/// <summary>Bounded inspection of recorded actors and causal events. Source files remain
/// authoritative; absent terrain, omitted samples and malformed evidence are never invented.</summary>
public static class WritePlaytestHtml
{
    private const int MaximumRuns = 4, MaximumPoints = 4000, MaximumKinds = 32, EventsPerKind = 256;
    private sealed record Sample(float Tick, float Wall, float? PlayerX, float? PlayerY,
        float? CompanionX, float? CompanionY, string[] Values);
    private sealed class Coverage
    {
        public int Seen { get; set; }
        public int Kept { get; set; }
        public int Malformed { get; set; }
        public int Oversized { get; set; }
        public int Omitted => Seen - Kept;
        public bool SidecarPresent { get; set; }
        public bool NormalClose { get; set; }
    }
    private sealed record Run(string Source, string[] Columns, List<Sample> Samples,
        Dictionary<string, List<JsonElement>> Events, Coverage Coverage, int Rows, int RaggedRows);

    public static void Write(string output, IEnumerable<string> paths)
    {
        string[] selected = paths.Distinct().ToArray();
        var runs = new List<Run>();
        foreach (string path in selected.TakeLast(MaximumRuns))
        {
            Session session = Session.Load(path);
            string[] columns = session.Names.ToArray();
            var samples = new List<Sample>();
            int stride = Math.Max(1, (int)Math.Ceiling(session.Count / (double)MaximumPoints));
            for (int i = 0; i < session.Count; i += stride)
            {
                float px = 0, py = 0;
                bool player = session.Has("player_px") && Session.TryPair(session["player_px"].Text[i], out px, out py);
                float? nx = Number(session, "observed_left", i), width = Number(session, "npc_width", i);
                if (nx.HasValue && width.HasValue) nx += width / 2;
                samples.Add(new Sample(session.Tick(i), Number(session, "wall_elapsed_ms", i) ?? 0,
                    player ? px : null, player ? py : null, nx, Number(session, "observed_bottom", i),
                    columns.Select(column => session[column].Text[i]).ToArray()));
            }
            var coverage = new Coverage();
            var events = ReadEvents(path, coverage);
            runs.Add(new Run(Path.GetFullPath(path), columns, samples, events, coverage, session.Count, session.Ragged));
        }
        string data = JsonSerializer.Serialize(new { Runs = runs, OmittedRuns = Math.Max(0, selected.Length - MaximumRuns) });
        File.WriteAllText(output, Page.Replace("__DATA__", data, StringComparison.Ordinal), Encoding.UTF8);
    }

    private static float? Number(Session session, string column, int row) => session.Has(column)
        && float.IsFinite(session[column].Number[row]) ? session[column].Number[row] : null;

    private static Dictionary<string, List<JsonElement>> ReadEvents(string tsv, Coverage coverage)
    {
        string path = Path.ChangeExtension(tsv, null) + "-events.jsonl";
        coverage.SidecarPresent = File.Exists(path);
        var result = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var random = new Random(1);
        if (!coverage.SidecarPresent) return result;
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length > 32768) { coverage.Oversized++; continue; }
            try
            {
                using JsonDocument json = JsonDocument.Parse(line);
                JsonElement root = json.RootElement;
                string kind = root.GetProperty("kind").GetString() ?? "unknown";
                if (kind == "session-end") coverage.NormalClose = true;
                if (kind is "session" or "session-end" or "terrain-snapshot") continue;
                double wall = root.GetProperty("wall_elapsed_ms").GetDouble();
                if (!double.IsFinite(wall) || !root.GetProperty("tick").TryGetInt64(out _)) throw new JsonException();
                coverage.Seen++;
                if (!result.TryGetValue(kind, out var bucket))
                {
                    if (result.Count >= MaximumKinds) continue;
                    result[kind] = bucket = new List<JsonElement>(); counts[kind] = 0;
                }
                int seen = ++counts[kind];
                // Separate reservoirs preserve rare hits despite abundant cosmetic contacts.
                if (bucket.Count < EventsPerKind) bucket.Add(root.Clone());
                else
                {
                    int replace = random.Next(seen);
                    if (replace < EventsPerKind) bucket[replace] = root.Clone();
                }
            }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            { coverage.Malformed++; }
        }
        foreach (var bucket in result.Values)
            bucket.Sort((a, b) => a.GetProperty("tick").GetInt64().CompareTo(b.GetProperty("tick").GetInt64()));
        coverage.Kept = result.Values.Sum(bucket => bucket.Count);
        return result;
    }

    private const string Page = """
<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>AICompanion recorded playtest</title>
<style>
*{box-sizing:border-box}body{margin:0;background:#111820;color:#e8edf2;font:14px 'Avenir Next','Segoe UI',sans-serif}header{padding:20px 24px;border-bottom:1px solid #334250}h1{margin:0 0 8px;font-size:24px}p{margin:6px 0;color:#b4c5d4}nav{display:flex;flex-wrap:wrap;gap:12px;align-items:center;padding:16px 24px}select,input,button{background:#203140;color:#fff;border:1px solid #526778;border-radius:4px;padding:7px}select{max-width:40vw}input[type=number]{width:110px}input[type=range]{width:100%;padding:0}main{padding:0 24px 24px;display:grid;grid-template-columns:minmax(0,3fr) minmax(280px,2fr);gap:18px}canvas{background:#17222e;border:1px solid #334250;width:100%;height:460px}pre{margin:8px 0;white-space:pre-wrap;overflow-wrap:anywhere;max-height:400px;overflow:auto;background:#17222e;padding:12px;font:12px 'SFMono-Regular',Consolas,monospace}#coverage{font-size:12px;margin:0 24px 16px;color:#e9c783;white-space:pre-wrap}#position{margin:12px 0}summary{cursor:pointer;font-weight:600}@media(max-width:800px){main{grid-template-columns:1fr}select{max-width:90vw}canvas{height:320px}}
</style>
<header><h1>Recorded actor timeline</h1><p>Blue: player · Coral: companion. Blank/unrecorded terrain and gaps are unknown.</p><p>Inspect sampled state and retained events; the TSV and JSONL files contain the full recorded evidence.</p></header>
<nav><label>Run <select id="run"></select></label><label>Events <select id="kind"></select></label><label>Tick <input id="jump" type="number" value="0"></label><button id="go">Go</button><button id="previous">Previous event</button><button id="next">Next event</button></nav>
<div id="coverage"></div><main><section><input id="time" type="range" min="0" value="0"><div id="position"></div><canvas id="map" width="1000" height="600"></canvas><details><summary>Recorded sample fields</summary><pre id="state"></pre></details></section><section><strong>Events at or before the selected tick</strong><pre id="events"></pre></section></main>
<script id="data" type="application/json">__DATA__</script><script>
'use strict';
const data=JSON.parse(document.getElementById('data').textContent),q=id=>document.getElementById(id),run=q('run'),kind=q('kind'),time=q('time'),canvas=q('map'),ctx=canvas.getContext('2d');
let current=null,indexed=[],selectedTick=0;
const upper=(a,t,key)=>{let lo=0,hi=a.length;while(lo<hi){let mid=(lo+hi)>>>1;if(key(a[mid])<=t)lo=mid+1;else hi=mid}return lo};
for(let i=0;i<data.Runs.length;i++){let option=document.createElement('option');option.value=i;option.textContent=data.Runs[i].Source.split('/').pop();run.append(option)}
function selectRun(){current=data.Runs[+run.value];if(!current)return;kind.replaceChildren();for(const name of ['all',...Object.keys(current.Events)]){let option=document.createElement('option');option.value=name;option.textContent=name;kind.append(option)}time.max=Math.max(0,current.Samples.length-1);time.value=0;selectKind();let c=current.Coverage;q('coverage').textContent=current.Source+'\n'+current.Samples.length+'/'+current.Rows+' samples; '+c.Kept+'/'+c.Seen+' events retained ('+c.Omitted+' omitted); '+c.Malformed+' malformed events; '+c.Oversized+' oversized records not embedded; '+current.RaggedRows+' malformed TSV rows. Sidecar '+(c.SidecarPresent?'present':'MISSING')+'; closure '+(c.NormalClose?'normal':'not observed')+'. '+data.OmittedRuns+' earlier selected runs omitted by the four-run viewer limit.';scrub()}
function selectKind(){indexed=(kind.value==='all'?Object.values(current.Events).flat():current.Events[kind.value]||[]).slice().sort((a,b)=>a.tick-b.tick);showEvents()}
function showEvents(){let end=upper(indexed,selectedTick,e=>e.tick);q('events').textContent=indexed.slice(Math.max(0,end-6),end).map(e=>JSON.stringify(e,null,2)).join('\n\n')||'No retained events before this tick. This is not evidence that nothing happened.'}
function show(tick){if(!Number.isFinite(tick))return;let i=Math.max(0,upper(current.Samples,tick,s=>s.Tick)-1);time.value=i;selectedTick=tick;q('jump').value=tick;draw(i);showEvents()}
function scrub(){let sample=current?.Samples[+time.value];if(sample)show(sample.Tick)}
function draw(i){let sample=current.Samples[i];ctx.clearRect(0,0,canvas.width,canvas.height);if(!sample){q('position').textContent='No valid samples';return}q('position').textContent='Tick '+selectedTick+' · displayed sample '+sample.Tick+' · '+(sample.Wall/1000).toFixed(2)+' seconds from session start';q('state').textContent=JSON.stringify(Object.fromEntries(current.Columns.map((name,n)=>[name,sample.Values[n]])),null,2);let ox=sample.PlayerX??sample.CompanionX??0,oy=sample.PlayerY??sample.CompanionY??0;const point=(x,y,color,r)=>{if(x===null||y===null)return;ctx.fillStyle=color;ctx.beginPath();ctx.arc(500+(x-ox)*.3,300+(y-oy)*.3,r,0,Math.PI*2);ctx.fill()};for(let n=Math.max(0,i-600);n<=i;n++){let s=current.Samples[n];point(s.PlayerX,s.PlayerY,'#76b6f0',1.5);point(s.CompanionX,s.CompanionY,'#f49283',1.5)}point(sample.PlayerX,sample.PlayerY,'#76b6f0',6);point(sample.CompanionX,sample.CompanionY,'#f49283',6)}
run.onchange=selectRun;kind.onchange=selectKind;time.oninput=scrub;q('go').onclick=()=>show(Number(q('jump').value));q('jump').onkeydown=e=>{if(e.key==='Enter')show(Number(e.target.value))};q('previous').onclick=()=>{let i=upper(indexed,selectedTick-1,e=>e.tick)-1;if(i>=0)show(indexed[i].tick)};q('next').onclick=()=>{let i=upper(indexed,selectedTick,e=>e.tick);if(i<indexed.length)show(indexed[i].tick)};selectRun();
</script></html>
""";
}
