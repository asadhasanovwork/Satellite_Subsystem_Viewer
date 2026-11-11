using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Academy.OrbitSim
{
    /// <summary>One burn planned in the Orbit Simulation, in the terms the Academy satellite understands.</summary>
    [Serializable]
    public class PlannedBurn
    {
        /// <summary>Seconds after the start of the orbit simulation (the satellite starts at the orbit's reference point).</summary>
        public double tSec;
        /// <summary>PROGRADE, RETROGRADE, NORMAL, ANTINORMAL, RADIAL_OUT or RADIAL_IN (physical, Earth-centred inertial).</summary>
        public string direction;
        public double dvMps;
        public double durationS;
        public double fuelKg;
    }

    /// <summary>Manoeuvre plan handed from the Orbit Simulation to Mission Operations / OpenC3.</summary>
    [Serializable]
    public class ManeuverPlan
    {
        public string designName;
        public string templateId;
        public string savedUtc;
        public double altitudeKm;
        public double inclinationDeg;
        /// <summary>Period of the starting circular orbit, s: burns are matched to the same point of the orbit.</summary>
        public double periodS;
        public List<PlannedBurn> burns = new List<PlannedBurn>();

        public double TotalDvMps { get { double s = 0; foreach (PlannedBurn b in burns) s += b.dvMps; return s; } }
    }

    public static class ManeuverPlanStore
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "OrbitPlans");

        public static string PathFor(string designName)
        {
            string safe = string.IsNullOrEmpty(designName) ? "satellite" : designName;
            foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            return Path.Combine(Folder, safe + ".plan.json");
        }

        public static void Save(ManeuverPlan plan)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(PathFor(plan.designName), JsonUtility.ToJson(plan, true));
        }

        public static ManeuverPlan Load(string designName)
        {
            try
            {
                string p = PathFor(designName);
                if (!File.Exists(p)) return null;
                var plan = JsonUtility.FromJson<ManeuverPlan>(File.ReadAllText(p));
                return plan != null && plan.burns != null && plan.burns.Count > 0 ? plan : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OrbitSim] Could not read the manoeuvre plan: " + e.Message);
                return null;
            }
        }

        public static void Delete(string designName)
        {
            string p = PathFor(designName);
            if (File.Exists(p)) File.WriteAllText(p, "{}"); // emptied (no burns) rather than deleted
        }
    }

    /// <summary>
    /// Writes the plan as an OpenC3 Script Runner procedure. The procedure reads the satellite's MET, finds the next
    /// time the satellite is at the planned point of its orbit and stores time-tagged commands on board
    /// (OBSW SCHEDULE): engine on 30 s before, then BURN &lt;direction&gt; &lt;m/s&gt; for every node.
    /// </summary>
    public static class ManeuverPlanProcedure
    {
        public const string FileName = "maneuver_plan.rb";

        public static string Ruby(ManeuverPlan plan, string target, string engineId)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("# Manoeuvre plan for " + plan.designName + ", made in the Academy Orbit Simulation (" + plan.savedUtc + ").");
            sb.AppendLine("# Starting orbit: " + plan.altitudeKm.ToString("0", ci) + " km, inclination " + plan.inclinationDeg.ToString("0.##", ci) +
                          " deg, period " + plan.periodS.ToString("0", ci) + " s. Total delta-v " + plan.TotalDvMps.ToString("0.###", ci) + " m/s.");
            sb.AppendLine("#");
            sb.AppendLine("# The burns run at the same point of the orbit as in the Orbit Simulation (next pass of that point),");
            sb.AppendLine("# so fly this plan before any other manoeuvre changes the orbit.");
            sb.AppendLine("# Each burn is stored on board as a time-tagged command (OBSW SCHEDULE); see OBSW telemetry for the queue.");
            sb.AppendLine();
            sb.AppendLine("TARGET = \"" + target + "\"");
            sb.AppendLine("ENGINE = \"" + engineId + "\"");
            sb.AppendLine("PERIOD = " + plan.periodS.ToString("0.###", ci));
            sb.AppendLine("LEAD = 120.0   # s of margin before the first burn (uplink + engine warm-up)");
            sb.AppendLine("# [seconds after orbit-sim start, direction, delta-v m/s]");
            sb.AppendLine("PLAN = [");
            foreach (PlannedBurn b in plan.burns)
                sb.AppendLine("  [" + b.tSec.ToString("0.###", ci) + ", \"" + b.direction + "\", " + b.dvMps.ToString("0.####", ci) + "],");
            sb.AppendLine("]");
            sb.AppendLine();
            sb.AppendLine("def run_plan");
            sb.AppendLine("  met = tlm(\"#{TARGET} SIM_STATUS MET\").to_f");
            sb.AppendLine("  t0 = PLAN[0][0]");
            sb.AppendLine("  k = ((met + LEAD - t0) / PERIOD).ceil");
            sb.AppendLine("  k = 0 if k < 0");
            sb.AppendLine("  start = t0 + k * PERIOD");
            sb.AppendLine("  puts \"MET now #{met.round} s. First burn at MET #{start.round(1)} s (in #{(start - met).round} s), #{PLAN.length} burn(s).\"");
            sb.AppendLine();
            sb.AppendLine("  PLAN.each_with_index do |(t, dir, dv), i|");
            sb.AppendLine("    at = start + (t - t0)");
            sb.AppendLine("    cmd(\"#{TARGET} RAW_LINE with LINE 'OBSW SCHEDULE #{format('%.1f', at - 30)} #{ENGINE} ON'\")");
            sb.AppendLine("    cmd(\"#{TARGET} RAW_LINE with LINE 'OBSW SCHEDULE #{format('%.1f', at)} #{ENGINE} BURN #{dir} #{format('%.4f', dv)}'\")");
            sb.AppendLine("    puts \"Burn #{i + 1}: #{dir} #{dv} m/s at MET #{at.round(1)} s\"");
            sb.AppendLine("  end");
            sb.AppendLine("  puts \"Plan stored on board. Watch the burns on the Orbit and propulsion screens.\"");
            sb.AppendLine("end");
            sb.AppendLine();
            sb.AppendLine("if wait(\"#{TARGET} SIM_STATUS CONTACT == 1\", 900)");
            sb.AppendLine("  run_plan");
            sb.AppendLine("else");
            sb.AppendLine("  puts \"No contact with the satellite: run this plan again during a pass.\"");
            sb.AppendLine("end");
            return sb.ToString();
        }
    }
}
