using System.Text.Json.Nodes;

namespace MpcConverter.Core.Conversion;

/// <summary>
/// The superset field-merge that upgrades a 1.3 object to 3.10. Because the 3.10
/// schema is a strict superset of 1.3, we start from a 3.10 default
/// <c>template</c> and overlay the <c>source</c> value for every key present in
/// both (recursing into nested objects). Template-only keys keep their 3.10
/// defaults; source-only keys are dropped.
/// </summary>
public static class JsonMerge
{
    public static JsonObject UpgradeOnto(JsonObject template, JsonObject source)
    {
        var result = (JsonObject)template.DeepClone();
        Overlay(result, source);
        return result;
    }

    private static void Overlay(JsonObject target, JsonObject source)
    {
        foreach (var key in System.Linq.Enumerable.ToList(GetKeys(target)))
        {
            if (!source.ContainsKey(key)) continue; // template-only key: keep default

            // "version" is always a schema-version number: keep the TEMPLATE's
            // (target-format) value, never the source's. Otherwise the upgraded
            // element carries the old format's version (e.g. layer v11, synthSection
            // v28), which MPC rejects for some features (warp-enabled pads) — it
            // loads a blank default. The template values match what MPC itself writes.
            if (key == "version" && target[key] is JsonValue) continue;

            var srcVal = source[key];
            var tgtVal = target[key];

            if (tgtVal is JsonObject tgtObj && srcVal is JsonObject srcObj)
            {
                Overlay(tgtObj, srcObj); // recurse into shared object
            }
            else
            {
                // Shared scalar/array/type-mismatch: take source value verbatim.
                target[key] = srcVal?.DeepClone();
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<string> GetKeys(JsonObject obj)
    {
        foreach (var kvp in obj) yield return kvp.Key;
    }
}
