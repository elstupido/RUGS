using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BAModAPI;
using Buildings;   // ClosestBuildingFromPlayer
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// Spawns the dealer roster on city load. The SHIPPED roster is embedded in the DLL
    /// (Roster/dealers.csv) so every downloader gets a populated city. On a Dev build, a local
    /// F7-capture file (RUGS_dealer_spots.txt) overrides it for in-game authoring; bake that file
    /// into Roster/dealers.csv to publish it. After spawning, the dealer nearest the player's home
    /// is promoted to the "anchor" (trades all rugs, stable prices).
    ///
    /// Line format (InvariantCulture): x,y,z,yRotation,neighborhoodKey,neighborhoodName
    /// (blank lines and lines starting with '#' are ignored.)
    /// </summary>
    internal static class RugDealers
    {
        /// <summary>Distinct neighborhood keys that have dealers — RugEvents targets these.</summary>
        internal static List<string> Districts = new List<string>();

        /// <summary>Every dealer spawned this session (for dev seed-cycling of the shared look).</summary>
        internal static List<RugDealerController> Spawned = new List<RugDealerController>();

        /// <summary>Re-apply the shared dealer look to every live dealer (dev F5 seed-cycling).</summary>
        internal static void ReskinAll() { foreach (RugDealerController d in Spawned) if (d != null) d.Reskin(); }

        internal static int SpawnFromRoster(IModLogger log)
        {
            Districts = new List<string>();
            string[] lines;
            string source;
            string capture = RugSpotCapture.FilePath;
            if (RugsConfig.Dev && File.Exists(capture))
            {
                lines = File.ReadAllLines(capture);
                source = "local capture (dev override)";
            }
            else
            {
                lines = ReadEmbeddedRoster(log);
                source = "embedded roster";
            }

            var spawned = new List<RugDealerController>();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                try
                {
                    string[] p = line.Split(',');
                    if (p.Length < 4) { log.Warn("RUGS! skipping malformed spot: " + line); continue; }

                    float x = float.Parse(p[0], CultureInfo.InvariantCulture);
                    float y = float.Parse(p[1], CultureInfo.InvariantCulture);
                    float z = float.Parse(p[2], CultureInfo.InvariantCulture);
                    float yRot = float.Parse(p[3], CultureInfo.InvariantCulture);
                    string hood = p.Length > 4 ? p[4] : "";
                    if (!string.IsNullOrEmpty(hood) && !Districts.Contains(hood)) Districts.Add(hood);

                    RugDealerController ctrl = RugDealerController.Spawn(new Vector3(x, y, z), Quaternion.Euler(0f, yRot, 0f), hood, log);
                    if (ctrl != null) spawned.Add(ctrl);
                }
                catch (Exception e) { log.Error("RUGS! bad dealer spot line: " + line); log.Error(e); }
            }

            Spawned = spawned;
            log.Info($"RUGS! spawned {spawned.Count} dealer(s) from {source}.");
            if (spawned.Count == 0) log.Warn("RUGS! NO dealers spawned — the city will be empty. Check the roster.");
            else ComputeAnchor(spawned, log);
            return spawned.Count;
        }

        // The dealer nearest the player's home (oldest rented residence) becomes the anchor.
        private static void ComputeAnchor(List<RugDealerController> dealers, IModLogger log)
        {
            try
            {
                var gi = SaveGameManager.Current;
                if (gi == null || gi.BuildingRegistrations == null) return;

                BuildingRegistration home = null;
                int bestDay = int.MaxValue;
                foreach (BuildingRegistration reg in gi.BuildingRegistrations)
                {
                    if (reg == null || !reg.RentedByPlayer) continue;
                    string t = reg.GetBuildingType();
                    if (string.IsNullOrEmpty(t) || t.IndexOf("residential", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int day = reg.creationDay < 0 ? int.MaxValue : reg.creationDay;
                    if (day <= bestDay) { bestDay = day; home = reg; } // oldest residence = "home"
                }
                if (home == null) { log.Info("RUGS! no rented residence — no anchor dealer this session."); return; }

                Vector3 homePos = GameManager.GetPlayerPositionBasedOnAddress(home.Address);
                if (float.IsInfinity(homePos.x) || float.IsInfinity(homePos.z))
                {
                    log.Warn("RUGS! couldn't resolve home position; no anchor dealer.");
                    return;
                }

                RugDealerController nearest = null;
                float best = float.MaxValue;
                foreach (RugDealerController d in dealers)
                {
                    if (d == null) continue;
                    float dist = Vector3.Distance(d.transform.position, homePos);
                    if (dist < best) { best = dist; nearest = d; }
                }
                if (nearest != null)
                {
                    // The home district's ONLY special rule: the anchor buys every rug, so the player always has
                    // a guaranteed place to offload a haul. Otherwise home is a normal district — no price haven
                    // and no sell restrictions (its dealers sell their usual subset, like anywhere else).
                    nearest.MakeAnchor();
                    log.Info($"RUGS! anchor '{nearest.Name}' near home ({best:0}m) — buys all 9 rugs (guaranteed cash-out).");
                }
            }
            catch (Exception e) { log.Error("RUGS! anchor compute failed."); log.Error(e); }
        }

        private static string[] ReadEmbeddedRoster(IModLogger log)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string name = null;
                foreach (string n in asm.GetManifestResourceNames())
                    if (n.EndsWith("dealers.csv", StringComparison.OrdinalIgnoreCase)) { name = n; break; }
                if (name == null) { log.Warn("RUGS! embedded roster resource not found."); return new string[0]; }

                using (Stream s = asm.GetManifestResourceStream(name))
                using (var r = new StreamReader(s))
                    return r.ReadToEnd().Replace("\r\n", "\n").Split('\n');
            }
            catch (Exception e) { log.Error("RUGS! embedded roster read failed."); log.Error(e); return new string[0]; }
        }

        /// <summary>Neighborhood key at the player's current position (for F7 capture / F8 test).</summary>
        internal static string CurrentNeighborhood()
        {
            try { var b = ClosestBuildingFromPlayer.Get(); return b != null ? b.Neighbourhood : ""; }
            catch { return ""; }
        }
    }
}
