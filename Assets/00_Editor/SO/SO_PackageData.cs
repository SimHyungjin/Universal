using System.Collections.Generic;
using UnityEngine;

public enum PackageType
{
    UPM,             
    OpenUPM,         
    UnityPackage,    
}

public enum FavoriteRegistryType
{
    UnityRegistry,
    MyAssets,
}

[System.Serializable]
public class PackageEntry
{
    public string     name;
    public string     category;
    public PackageType type;
    [Tooltip("UPM: Git URL / OpenUPM: com.xxx.xxx / UnityPackage: https://...unitypackage")]
    public string     url;
    public string     description;
    [Tooltip("UPM의 경우 비워두면 최신, #v1.2.3 형태로 입력 가능")]
    public string     version;
}

[System.Serializable]
public class FavoriteItem
{
    public string name = "";
    public FavoriteRegistryType registryType = FavoriteRegistryType.UnityRegistry;
    public string description = "";
    public bool   done;
}

public class SO_PackageData : ScriptableObject
{
    public List<PackageEntry> packages = new List<PackageEntry>();
    public List<FavoriteItem> favorites = new List<FavoriteItem>();
}