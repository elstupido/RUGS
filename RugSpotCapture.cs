using System;
using System.Globalization;
using System.IO;
using BAModAPI;     // IModLogger
using Buildings;    // ClosestBuildingFromPlayer
using Localizor;    // GetLocalization
using UnityEngine;

namespace Rugs
{
    /// <summary>
    /// Dev tool for authoring dealer locations. Stand where a dealer should be (facing the
    /// way it should face) and press F7: the spot (position, facing, neighborhood) is
    /// appended to RUGS_dealer_spots.txt in the game's save folder and a preview dealer is
    /// spawned there. F6 clears the file. The captured file is then baked into the roster.
    ///
    /// File line format (InvariantCulture): x,y,z,yRotation,neighborhoodKey,neighborhoodName
    /// </summary>
    internal static class RugSpotCapture
    {
        internal static string FilePath => Path.Combine(Application.persistentDataPath, "RUGS_dealer_spots.txt");
        private static int _count;

        internal static void Capture(IModLogger log)
        {
            var pc = InstanceBehavior<GameManager>.Instance?.playerController;
            if (pc == null) { log?.Warn("RUGS! capture: no player controller."); return; }

            Vector3 p = pc.transform.position;
            float yRot = pc.transform.eulerAngles.y;

            string key = "unknown", name = "Unknown";
            try
            {
                var b = ClosestBuildingFromPlayer.Get();
                if (b != null && !string.IsNullOrEmpty(b.Neighbourhood)) { key = b.Neighbourhood; name = key.GetLocalization(); }
            }
            catch (Exception e) { log?.Error(e); }

            string line = string.Format(CultureInfo.InvariantCulture,
                "{0:F3},{1:F3},{2:F3},{3:F1},{4},{5}", p.x, p.y, p.z, yRot, key, name);

            try { File.AppendAllText(FilePath, line + "\n"); }
            catch (Exception e) { log?.Error("RUGS! capture write failed."); log?.Error(e); }

            _count++;
            log?.Info($"[RUGS! SPOT] #{_count}  {line}");

            // Visual confirmation — drop a preview dealer right on the spot (same deterministic
            // identity it'll have when loaded from the roster next session).
            RugDealerController.Spawn(p, Quaternion.Euler(0f, yRot, 0f), key, log);
        }

        internal static void Clear(IModLogger log)
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch (Exception e) { log?.Error(e); }
            _count = 0;
            log?.Info("RUGS! dealer spots cleared: " + FilePath);
        }
    }
}
