using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SO_CustomSymbol : ScriptableObject
{
    [Serializable]
    public class CustomSymbolEntry
    {
        public string Name;
        public string Description;
        public bool IncludeInBuild = true;

        [Header("Platforms")]
        public bool AllPlatforms;
        public bool Window;
        public bool Mac;
        public bool Android;
        public bool Ios;
        
        public bool IsValidFor(BuildTarget target)
        {
            if (!IncludeInBuild || string.IsNullOrEmpty(Name)) return false;
            if (AllPlatforms) return true;

            return target switch
            {
                BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => Window,
                BuildTarget.StandaloneOSX => Mac,
                BuildTarget.Android => Android,
                BuildTarget.iOS => Ios,
                _ => false
            };
        }
    }

    public List<CustomSymbolEntry> Symbols = new();
    
    public string GetCombinedSymbols(BuildTarget target)
    {
        var filtered = Symbols
            .Where(e => e.IsValidFor(target))
            .Select(e => e.Name.Trim())
            .Distinct();

        return string.Join(";", filtered);
    }
}