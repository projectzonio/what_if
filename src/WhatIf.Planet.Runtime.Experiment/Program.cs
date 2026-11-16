using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WhatIf.Planet;
using WhatIf.Planet.FarLayer;
using WhatIf.Planet.Runtime;
using WhatIf.Planet.Runtime.Experiment;
using WhatIf.Planet.WorldGen;

// What If planet streaming runtime experiment, anchor content (variant B) only; the region-first path (variant A: modes drive/verify/cold/flight/profile/report) was removed in RG-185.// Modes (the runner starts each in its own process, one after another, under the measurement lock):
//   adrive <out> [--seconds s]   RG-178 row s6: runtime drive along a corridor chain of the RG-177 areas -> rows-adrive-B.txt
//   cpath|cdrive|cverify|ccold|cflight|ct9 <out>   RG-184 B5 city-to-city runtime rows (AnchorRun)
//   creport <out> <md> <csv> --sha <sha> --hw "<..>" --wall <s>
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length < 2) { Console.Error.WriteLine("usage: adrive|cpath|cdrive|cverify|ccold|cflight|ct9|creport <outdir> ..."); return 2; }
string mode = args[0], dir = args[1]; Directory.CreateDirectory(dir);
string Opt(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
Scenarios.HighResTimer();
System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;   // lever 1 for main-thread pauses while region builds churn the heap

switch (mode)
{
    case "adrive": return AnchorDrive.Run(dir, double.Parse(Opt("--seconds", "300")), 120.0);
    case "cpath": case "cdrive": case "cverify": case "ccold": case "cflight": case "ct9": return AnchorRun.Run(mode, dir, Opt);   // RG-184 B5
    case "creport": return AnchorRun.Report(dir, args[2], args[3], Opt("--sha", "?"), Opt("--hw", "?"), Opt("--wall", "?"));
}
Console.Error.WriteLine("unknown mode " + mode); return 2;
