using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System.Linq;

[InitializeOnLoad]
public class Editor_AddressableAutoName
{
    static Editor_AddressableAutoName()
    {
        EditorApplication.delayCall += RegisterEvent;
    }
    
    private static void RegisterEvent()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings != null)
        {
            settings.OnModification -= OnSettingsModification;
            settings.OnModification += OnSettingsModification;
        }
    }

    private static void OnSettingsModification(AddressableAssetSettings settings, AddressableAssetSettings.ModificationEvent ev, object data)
    {
        if (ev == AddressableAssetSettings.ModificationEvent.EntryAdded)
        {
            var entries = data as System.Collections.Generic.List<AddressableAssetEntry>;
            if (entries == null) return;

            foreach (var entry in entries)
            {
                string assetName = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entry.AssetPath).name;
                string newAddress = assetName;

                int counter = 1;
                while (settings.FindAssetEntry(GetGuidFromAddress(settings, newAddress)) != null &&
                       settings.FindAssetEntry(GetGuidFromAddress(settings, newAddress)).address != entry.address)
                {
                    newAddress = $"{assetName}_{counter}";
                    counter++;
                }

                entry.address = newAddress;
            }
        }
    }

    private static string GetGuidFromAddress(AddressableAssetSettings settings, string address)
    {
        var entry = settings.groups.SelectMany(g => g.entries).FirstOrDefault(e => e.address == address);
        return entry?.guid ?? string.Empty;
    }
}