namespace CubeHavoc.Shared;

/// <summary>
/// Силуэт оружия от первого лица. Клиент рисует по нему процедурную модель,
/// сервер использует только боевые параметры — поэтому таблица общая для обеих сторон.
/// </summary>
public enum WeaponStyle
{
    Pistol,
    Revolver,
    Smg,
    Rifle,
    Shotgun,
    Sniper,
    Lmg,
    Plasma,
    Launcher
}

public sealed class WeaponInfo
{
    public string Id = "";
    public string NameRu = "";
    public string NameEn = "";
    public WeaponStyle Style = WeaponStyle.Pistol;

    public int Damage = 10;
    public int Pellets = 1;
    public float SpreadDeg = 0.5f;
    public float FireInterval = 0.2f;
    public int MagSize = 12;
    public float ReloadTime = 1.5f;
    public float Range = 90f;
    public bool Automatic = true;
    public float Recoil = 1.0f;
    public float SplashRadius = 0f;

    public byte AccentR = 255;
    public byte AccentG = 170;
    public byte AccentB = 55;
}

/// <summary>
/// Единый список оружия Cube Havoc 5.0. Номера слотов (1..9) соответствуют индексам массива,
/// эти же индексы уходят по сети в пакете действия, чтобы сервер считал урон тем же оружием.
/// </summary>
public static class WeaponCatalog
{
    public const int MaxWeapons = 9;

    public static readonly WeaponInfo[] All =
    {
        new WeaponInfo
        {
            Id = "groza9", NameRu = "ГРОЗА-9", NameEn = "GROZA-9", Style = WeaponStyle.Pistol,
            Damage = 14, Pellets = 1, SpreadDeg = 0.45f, FireInterval = 0.26f,
            MagSize = 12, ReloadTime = 1.25f, Range = 80f, Automatic = false, Recoil = 0.7f,
            AccentR = 255, AccentG = 178, AccentB = 72
        },
        new WeaponInfo
        {
            Id = "magnum44", NameRu = "МАГНУМ 44", NameEn = "MAGNUM 44", Style = WeaponStyle.Revolver,
            Damage = 34, Pellets = 1, SpreadDeg = 0.35f, FireInterval = 0.62f,
            MagSize = 6, ReloadTime = 1.9f, Range = 100f, Automatic = false, Recoil = 1.7f,
            AccentR = 236, AccentG = 196, AccentB = 120
        },
        new WeaponInfo
        {
            Id = "wasp12", NameRu = "ОСА-12", NameEn = "WASP-12", Style = WeaponStyle.Smg,
            Damage = 8, Pellets = 1, SpreadDeg = 1.7f, FireInterval = 0.075f,
            MagSize = 32, ReloadTime = 1.5f, Range = 70f, Automatic = true, Recoil = 0.55f,
            AccentR = 120, AccentG = 210, AccentB = 160
        },
        new WeaponInfo
        {
            Id = "vortex74", NameRu = "ВИХРЬ-74", NameEn = "VORTEX-74", Style = WeaponStyle.Rifle,
            Damage = 12, Pellets = 1, SpreadDeg = 0.85f, FireInterval = 0.105f,
            MagSize = 30, ReloadTime = 1.85f, Range = 120f, Automatic = true, Recoil = 1.0f,
            AccentR = 255, AccentG = 150, AccentB = 60
        },
        new WeaponInfo
        {
            Id = "sledge12", NameRu = "КУВАЛДА-12", NameEn = "SLEDGE-12", Style = WeaponStyle.Shotgun,
            Damage = 7, Pellets = 9, SpreadDeg = 4.6f, FireInterval = 0.82f,
            MagSize = 6, ReloadTime = 2.35f, Range = 46f, Automatic = false, Recoil = 2.1f,
            AccentR = 230, AccentG = 120, AccentB = 80
        },
        new WeaponInfo
        {
            Id = "needleam", NameRu = "ИГЛА-АМ", NameEn = "NEEDLE-AM", Style = WeaponStyle.Sniper,
            Damage = 70, Pellets = 1, SpreadDeg = 0.12f, FireInterval = 1.15f,
            MagSize = 5, ReloadTime = 2.6f, Range = 220f, Automatic = false, Recoil = 2.4f,
            AccentR = 130, AccentG = 190, AccentB = 255
        },
        new WeaponInfo
        {
            Id = "goliath", NameRu = "ГОЛИАФ", NameEn = "GOLIATH", Style = WeaponStyle.Lmg,
            Damage = 11, Pellets = 1, SpreadDeg = 2.2f, FireInterval = 0.08f,
            MagSize = 90, ReloadTime = 3.4f, Range = 130f, Automatic = true, Recoil = 0.9f,
            AccentR = 200, AccentG = 200, AccentB = 90
        },
        new WeaponInfo
        {
            Id = "ionplasma", NameRu = "ИОН-ПЛАЗМА", NameEn = "ION-PLASMA", Style = WeaponStyle.Plasma,
            Damage = 28, Pellets = 1, SpreadDeg = 0.55f, FireInterval = 0.3f,
            MagSize = 18, ReloadTime = 1.8f, Range = 110f, Automatic = true, Recoil = 1.2f,
            AccentR = 120, AccentG = 220, AccentB = 255
        },
        new WeaponInfo
        {
            Id = "sledge2rpg", NameRu = "КУВАЛДА-2", NameEn = "SLEDGE-2 RPG", Style = WeaponStyle.Launcher,
            Damage = 45, Pellets = 1, SpreadDeg = 0.6f, FireInterval = 1.4f,
            MagSize = 2, ReloadTime = 3.0f, Range = 150f, Automatic = false, Recoil = 2.6f,
            SplashRadius = 6.5f,
            AccentR = 255, AccentG = 90, AccentB = 70
        }
    };

    public static WeaponInfo Get(int index)
    {
        if (index < 0 || index >= All.Length) return All[0];
        return All[index];
    }
}
