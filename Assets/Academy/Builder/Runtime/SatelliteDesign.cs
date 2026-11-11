using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Academy.Prototype;
using UnityEngine;

namespace Academy.Builder
{
    public enum SlotRequirement { Mandatory, Conditional, Optional }

    public enum SlotStatus
    {
        /// <summary>Hardware selected and built.</summary>
        Added,
        /// <summary>Required (mandatory, or conditional and triggered) but nothing selected.</summary>
        Missing,
        /// <summary>Optional and nothing selected.</summary>
        Empty,
        /// <summary>Conditional and not triggered by the current design.</summary>
        NotNeeded
    }

    public sealed class SlotSelection
    {
        public string variantId;
        public readonly Dictionary<string, float> parameters = new Dictionary<string, float>();
    }

    public struct DesignTotals
    {
        public float massKg;
        public float powerW;
        public float powerGenW;
        public int partCount;
        public float PowerMarginW { get { return powerGenW - powerW; } }
    }

    /// <summary>
    /// The learner's satellite design: one selection per hardware slot, plus the rules
    /// (mandatory / conditional / optional) and budget totals derived from the template.
    /// </summary>
    public sealed class SatelliteDesign
    {
        public BuilderTemplate Template { get; private set; }
        public string DesignName;

        /// <summary>Orbit chosen in the Builder (always set; starts at the template's default orbit).</summary>
        public DesignOrbit Orbit;

        /// <summary>Raised after any selection or parameter change.</summary>
        public event Action Changed;

        private readonly Dictionary<string, BuilderSlot> _slots = new Dictionary<string, BuilderSlot>();
        private readonly Dictionary<string, SlotSelection> _selections = new Dictionary<string, SlotSelection>();
        private static readonly Dictionary<string, FieldInfo> FieldCache = new Dictionary<string, FieldInfo>();

        public SatelliteDesign(BuilderTemplate template)
        {
            Template = template;
            DesignName = string.IsNullOrEmpty(template.designName) ? template.displayName : template.designName;
            Orbit = DesignOrbit.DefaultFor(template.orbitClass);
            foreach (BuilderSubsystem subsystem in template.subsystems)
                foreach (BuilderSlot slot in subsystem.slots)
                    if (!string.IsNullOrEmpty(slot.id))
                        _slots[slot.id] = slot;
        }

        public IEnumerable<BuilderSlot> AllSlots { get { return _slots.Values; } }

        public BuilderSlot GetSlot(string slotId)
        {
            BuilderSlot slot;
            return slotId != null && _slots.TryGetValue(slotId, out slot) ? slot : null;
        }

        // ------------------------------------------------------------------ rules

        public static SlotRequirement RequirementOf(BuilderSlot slot)
        {
            string r = (slot.requirement ?? "mandatory").Trim().ToLowerInvariant();
            if (r.StartsWith("cond")) return SlotRequirement.Conditional;
            if (r.StartsWith("opt")) return SlotRequirement.Optional;
            return SlotRequirement.Mandatory;
        }

        /// <summary>Conditional slots are active when any of their conditions is met; others always are.</summary>
        public bool IsActive(BuilderSlot slot)
        {
            if (RequirementOf(slot) != SlotRequirement.Conditional)
                return true;
            if (slot.conditions == null)
                return false;

            foreach (BuilderCondition condition in slot.conditions)
            {
                BuilderVariant selected = GetSelectedVariant(condition.slot);
                if (selected != null && condition.variants != null && condition.variants.Contains(selected.id))
                    return true;
            }
            return false;
        }

        public bool IsBuilt(BuilderSlot slot)
        {
            return IsActive(slot) && GetSelectedVariant(slot.id) != null;
        }

        public SlotStatus StatusOf(BuilderSlot slot)
        {
            if (!IsActive(slot))
                return SlotStatus.NotNeeded;
            if (GetSelectedVariant(slot.id) != null)
                return SlotStatus.Added;
            return RequirementOf(slot) == SlotRequirement.Optional ? SlotStatus.Empty : SlotStatus.Missing;
        }

        public int MissingCount(BuilderSubsystem subsystem)
        {
            int missing = 0;
            foreach (BuilderSlot slot in subsystem.slots)
                if (StatusOf(slot) == SlotStatus.Missing)
                    missing++;
            return missing;
        }

        public int MissingCount()
        {
            int missing = 0;
            foreach (BuilderSubsystem subsystem in Template.subsystems)
                missing += MissingCount(subsystem);
            return missing;
        }

        public bool IsComplete { get { return MissingCount() == 0; } }

        // ------------------------------------------------------------------ selections

        public SlotSelection GetSelection(string slotId)
        {
            SlotSelection selection;
            return slotId != null && _selections.TryGetValue(slotId, out selection) ? selection : null;
        }

        public BuilderVariant GetSelectedVariant(string slotId)
        {
            SlotSelection selection = GetSelection(slotId);
            BuilderSlot slot = GetSlot(slotId);
            if (selection == null || slot == null || string.IsNullOrEmpty(selection.variantId))
                return null;
            foreach (BuilderVariant variant in slot.variants)
                if (variant.id == selection.variantId)
                    return variant;
            return null;
        }

        /// <summary>Select a variant (null or empty removes the hardware). Sliders reset to their defaults.</summary>
        public void SelectVariant(string slotId, string variantId)
        {
            BuilderSlot slot = GetSlot(slotId);
            if (slot == null)
                return;

            if (string.IsNullOrEmpty(variantId))
            {
                _selections.Remove(slotId);
            }
            else
            {
                var selection = new SlotSelection { variantId = variantId };
                BuilderVariant variant = null;
                foreach (BuilderVariant v in slot.variants)
                    if (v.id == variantId) variant = v;
                if (variant == null)
                    return;
                foreach (BuilderParameter p in variant.parameters)
                    selection.parameters[p.id] = p.defaultValue;
                _selections[slotId] = selection;
            }

            RaiseChanged();
        }

        public float GetParameter(string slotId, BuilderParameter parameter)
        {
            SlotSelection selection = GetSelection(slotId);
            float value;
            if (selection != null && selection.parameters.TryGetValue(parameter.id, out value))
                return value;
            return parameter.defaultValue;
        }

        public void SetParameter(string slotId, BuilderParameter parameter, float value)
        {
            SlotSelection selection = GetSelection(slotId);
            if (selection == null)
                return;
            value = Mathf.Clamp(value, parameter.min, parameter.max);
            if (parameter.wholeNumbers)
                value = Mathf.Round(value);
            float old;
            if (selection.parameters.TryGetValue(parameter.id, out old) && Mathf.Approximately(old, value))
                return;
            selection.parameters[parameter.id] = value;
            RaiseChanged();
        }

        /// <summary>Fill every empty mandatory / triggered slot of a subsystem with its first (recommended) variant.</summary>
        public void FillRecommended(BuilderSubsystem subsystem)
        {
            bool changedAny = false;
            // Repeat: filling one slot can trigger a conditional slot in the same subsystem
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (BuilderSlot slot in subsystem.slots)
                {
                    if (StatusOf(slot) == SlotStatus.Missing && slot.variants.Count > 0)
                    {
                        var selection = new SlotSelection { variantId = slot.variants[0].id };
                        foreach (BuilderParameter p in slot.variants[0].parameters)
                            selection.parameters[p.id] = p.defaultValue;
                        _selections[slot.id] = selection;
                        changedAny = true;
                    }
                }
            }
            if (changedAny)
                RaiseChanged();
        }

        // ------------------------------------------------------------------ geometry + budgets

        /// <summary>Signature of what a slot currently builds; changes whenever its 3D model must be rebuilt.</summary>
        public string BuildKey(BuilderSlot slot)
        {
            if (!IsBuilt(slot))
                return string.Empty;
            SlotSelection selection = GetSelection(slot.id);
            var key = selection.variantId;
            foreach (KeyValuePair<string, float> kv in selection.parameters)
                key += "|" + kv.Key + "=" + kv.Value.ToString("R", CultureInfo.InvariantCulture);
            return key;
        }

        /// <summary>Copies of the selected variant's parts with slider values applied.</summary>
        public List<PartSpec> BuildParts(BuilderSlot slot)
        {
            var result = new List<PartSpec>();
            BuilderVariant variant = IsBuilt(slot) ? GetSelectedVariant(slot.id) : null;
            if (variant == null)
                return result;

            foreach (PartSpec original in variant.parts)
            {
                PartSpec copy = JsonUtility.FromJson<PartSpec>(JsonUtility.ToJson(original));
                if (string.IsNullOrEmpty(copy.id))
                    copy.id = slot.id + "-" + result.Count;
                if (string.IsNullOrEmpty(copy.label))
                    copy.label = variant.name;
                if (string.IsNullOrEmpty(copy.description))
                    copy.description = variant.description;
                result.Add(copy);
            }

            foreach (BuilderParameter parameter in variant.parameters)
            {
                float value = GetParameter(slot.id, parameter) * (Mathf.Approximately(parameter.fieldMultiplier, 0f) ? 1f : parameter.fieldMultiplier);
                for (int i = 0; i < result.Count; i++)
                {
                    if (parameter.partIndices != null && parameter.partIndices.Count > 0 && !parameter.partIndices.Contains(i))
                        continue;
                    foreach (string field in parameter.fields)
                        SetField(result[i], field, value);
                }
            }
            return result;
        }

        public DesignTotals ComputeTotals()
        {
            var totals = new DesignTotals();
            foreach (BuilderSlot slot in _slots.Values)
            {
                if (!IsBuilt(slot))
                    continue;
                BuilderVariant variant = GetSelectedVariant(slot.id);
                float mass = variant.massKg, power = variant.powerW, gen = variant.powerGenW;
                foreach (BuilderParameter p in variant.parameters)
                {
                    float delta = GetParameter(slot.id, p) - p.defaultValue;
                    mass += delta * p.massPerUnit;
                    power += delta * p.powerPerUnit;
                    gen += delta * p.powerGenPerUnit;
                }
                totals.massKg += Mathf.Max(0f, mass);
                totals.powerW += Mathf.Max(0f, power);
                totals.powerGenW += Mathf.Max(0f, gen);
                totals.partCount += variant.parts.Count;
            }
            return totals;
        }

        /// <summary>The whole design as an assembly file the procedural builder / simulator can load.</summary>
        public SatelliteAssemblyData ToAssembly()
        {
            var data = new SatelliteAssemblyData
            {
                name = DesignName,
                orbitClass = Template.orbitClass,
                orbitSummary = Template.orbitSummary,
                description = "Designed in the Satellite Builder from template '" + Template.displayName + "'.",
                cameraDistance = Template.cameraDistance,
                explodeDistance = Template.explodeDistance
            };
            foreach (BuilderSubsystem subsystem in Template.subsystems)
                foreach (BuilderSlot slot in subsystem.slots)
                    data.parts.AddRange(BuildParts(slot));
            return data;
        }

        // ------------------------------------------------------------------ save / load

        public DesignSave ToSave()
        {
            var save = new DesignSave { templateId = Template.id, designName = DesignName, orbit = Orbit != null ? Orbit.Clone() : null };
            foreach (KeyValuePair<string, SlotSelection> kv in _selections)
            {
                var saved = new SavedSlot { slot = kv.Key, variant = kv.Value.variantId };
                foreach (KeyValuePair<string, float> p in kv.Value.parameters)
                    saved.parameters.Add(new SavedParameter { id = p.Key, value = p.Value });
                save.slots.Add(saved);
            }
            return save;
        }

        public void LoadSave(DesignSave save)
        {
            _selections.Clear();
            if (save == null)
            {
                RaiseChanged();
                return;
            }
            if (!string.IsNullOrEmpty(save.designName))
                DesignName = save.designName;
            Orbit = save.orbit != null && save.orbit.altitudeKm > 0 ? save.orbit.Clone() : DesignOrbit.DefaultFor(Template.orbitClass);
            Orbit.ClampTo(Template.orbitClass);
            foreach (SavedSlot saved in save.slots)
            {
                if (GetSlot(saved.slot) == null)
                    continue;
                var selection = new SlotSelection { variantId = saved.variant };
                foreach (SavedParameter p in saved.parameters)
                    selection.parameters[p.id] = p.value;
                _selections[saved.slot] = selection;
            }
            RaiseChanged();
        }

        // ------------------------------------------------------------------ internals

        private void RaiseChanged()
        {
            if (Changed != null)
                Changed();
        }

        private static void SetField(PartSpec spec, string fieldName, float value)
        {
            if (string.IsNullOrEmpty(fieldName))
                return;

            FieldInfo field;
            if (!FieldCache.TryGetValue(fieldName, out field))
            {
                field = typeof(PartSpec).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
                FieldCache[fieldName] = field;
            }
            if (field == null)
            {
                Debug.LogWarning("[SatelliteDesign] PartSpec has no field '" + fieldName + "'.");
                return;
            }

            if (field.FieldType == typeof(float))
                field.SetValue(spec, value);
            else if (field.FieldType == typeof(int))
                field.SetValue(spec, Mathf.RoundToInt(value));
            else if (field.FieldType == typeof(bool))
                field.SetValue(spec, value > 0.5f);
        }
    }

    [Serializable]
    public class DesignSave
    {
        public string templateId;
        public string designName;
        public List<SavedSlot> slots = new List<SavedSlot>();
        /// <summary>Circular orbit chosen in the Builder; null in designs saved before orbits were selectable.</summary>
        public DesignOrbit orbit;
    }

    /// <summary>
    /// Circular orbit the learner picks in the Builder (km and degrees). The orbit class (LEO / MEO / GEO)
    /// is fixed by the satellite type; these values choose where inside that class it flies.
    /// </summary>
    [Serializable]
    public class DesignOrbit
    {
        public float altitudeKm;
        public float inclinationDeg;
        public float raanDeg;
        /// <summary>GEO only: longitude of the orbital slot (east positive). -999 = not used.</summary>
        public float geoLongitudeDeg = -999f;

        public const float EarthRadiusKm = 6378.137f;
        public const float MuEarth = 398600.4418f;
        public const float GeoAltitudeKm = 35786f;

        public DesignOrbit Clone() { return (DesignOrbit)MemberwiseClone(); }

        public static bool IsGeo(string orbitClass) { return string.Equals(orbitClass, "GEO", StringComparison.OrdinalIgnoreCase); }
        public static bool IsMeo(string orbitClass) { return string.Equals(orbitClass, "MEO", StringComparison.OrdinalIgnoreCase); }

        public static DesignOrbit DefaultFor(string orbitClass)
        {
            if (IsGeo(orbitClass)) return new DesignOrbit { altitudeKm = GeoAltitudeKm, inclinationDeg = 0.05f, geoLongitudeDeg = 46f };
            if (IsMeo(orbitClass)) return new DesignOrbit { altitudeKm = 23222f, inclinationDeg = 56f };
            return new DesignOrbit { altitudeKm = 500f, inclinationDeg = 97.4f };
        }

        /// <summary>Allowed altitude range for the class (GEO is fixed by physics).</summary>
        public static Vector2 AltitudeRange(string orbitClass)
        {
            if (IsGeo(orbitClass)) return new Vector2(GeoAltitudeKm, GeoAltitudeKm);
            if (IsMeo(orbitClass)) return new Vector2(2000f, 30000f);
            return new Vector2(300f, 2000f);
        }

        public static Vector2 InclinationRange(string orbitClass)
        {
            if (IsGeo(orbitClass)) return new Vector2(0f, 10f);
            if (IsMeo(orbitClass)) return new Vector2(0f, 90f);
            return new Vector2(0f, 110f);
        }

        public void ClampTo(string orbitClass)
        {
            Vector2 a = AltitudeRange(orbitClass), i = InclinationRange(orbitClass);
            altitudeKm = Mathf.Clamp(altitudeKm, a.x, a.y);
            inclinationDeg = Mathf.Clamp(inclinationDeg, i.x, i.y);
            if (IsGeo(orbitClass))
            {
                if (geoLongitudeDeg < -180f || geoLongitudeDeg > 180f) geoLongitudeDeg = 46f;
            }
            else geoLongitudeDeg = -999f;
        }

        public float SemiMajorAxisKm { get { return EarthRadiusKm + altitudeKm; } }

        /// <summary>Orbital period in minutes (circular two-body).</summary>
        public float PeriodMinutes
        {
            get { double a = SemiMajorAxisKm; return (float)(2.0 * Math.PI * Math.Sqrt(a * a * a / MuEarth) / 60.0); }
        }

        /// <summary>Circular orbital speed, km/s.</summary>
        public float SpeedKmS { get { return (float)Math.Sqrt(MuEarth / SemiMajorAxisKm); } }

        /// <summary>Inclination that makes a circular orbit at this altitude Sun-synchronous (J2), or NaN.</summary>
        public float SunSynchronousInclinationDeg
        {
            get
            {
                double c = -Math.Pow(SemiMajorAxisKm / 12352.0, 3.5);
                return c < -1 ? float.NaN : (float)(Math.Acos(c) * 180.0 / Math.PI);
            }
        }
    }

    [Serializable]
    public class SavedSlot
    {
        public string slot;
        public string variant;
        public List<SavedParameter> parameters = new List<SavedParameter>();
    }

    [Serializable]
    public class SavedParameter
    {
        public string id;
        public float value;
    }
}
