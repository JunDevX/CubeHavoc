using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Numerics;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using CubeHavoc.Shared;
using Raylib_cs;

namespace CubeHavoc;

enum GameState
{
    MainMenu,
    PlaySelect,
    Singleplayer,
    LocalLobby,
    Settings,
    Developers
}

enum EntityType
{
    Block,
    GravityGun,
    Prop
}

class PhysicalObject3D
{
    public int NetId;
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 Size = new Vector3(1.5f, 1.5f, 1.5f);
    public Color Color = Color.Gray;
    public EntityType Type;
    public bool IsHeld;
    public int HeldByPlayerId = -1;

    public BoundingBox BoundingBox => new BoundingBox(
        Position - Size / 2.0f,
        Position + Size / 2.0f
    );
}

class HitParticle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public Color Color;
    public float Life = 0.4f;
    public float Size = 0.15f;

    // Bodycam v2: расширенные параметры частиц (дым, искры, гильзы)
    public float MaxLife = 0.0f;
    public float GravityScale = 1.0f;
    public float Grow = 0.0f;
    public float Drag = 0.0f;
    public bool Glow = false;
    public bool Round = false;
    public bool Bounce = false;
    public int Alpha = 255;
}

class BulletTracer
{
    public Vector3 From;
    public Vector3 To;
    public float Life = 0.10f;
}

class Player3D
{
    public int Id;
    public string Name = "Player";
    public Vector3 Position;
    public Vector3 Velocity;
    public float Speed = 8.0f;
    public int Health = 10;
    public int MaxHealth = 10;
    public int Kills = 0;
    public int Deaths = 0;
    public bool IsAdmin = false;
    
    public Color PlayerColor;

    public Vector3 BodySize = new Vector3(1.0f, 2.4f, 1.0f);

    public List<PhysicalObject3D> Inventory = new List<PhysicalObject3D>();
    public bool HasGravityGun => Inventory.Exists(x => x.Type == EntityType.GravityGun);

    public BoundingBox BoundingBox => new BoundingBox(
        Position - new Vector3(BodySize.X / 2.0f, 0.0f, BodySize.Z / 2.0f),
        Position + new Vector3(BodySize.X / 2.0f, BodySize.Y, BodySize.Z / 2.0f)
    );
}

class Zombie3D
{
    public int NetId;
    public Vector3 Position;
    public Vector3 Velocity;
    public float Speed = 4.5f;
    public int Health = 50; // Установлено 50 ХП
    public int MaxHealth = 50;
    public float Yaw = 0.0f; // Угол поворота лицом к игроку

    public Vector3 BodySize = new Vector3(1.0f, 2.4f, 1.0f);

    public BoundingBox BoundingBox => new BoundingBox(
        Position - new Vector3(BodySize.X / 2.0f, 0.0f, BodySize.Z / 2.0f),
        Position + new Vector3(BodySize.X / 2.0f, BodySize.Y, BodySize.Z / 2.0f)
    );
}

class ChatMessage
{
    public string Sender;
    public string Text;
    public Color SenderColor;
    public float TimeRemaining = 10.0f;
}

class Program
{
    static int screenWidth = 1280;
    static int screenHeight = 720;
    static GameState currentState = GameState.MainMenu;
    static GameState pendingState = GameState.MainMenu;
    static float menuTransition = 1.0f;
    static bool menuTransitioning = false;
    static float menuTime = 0.0f;

    class GameSettings
    {
        public float MouseSensitivity { get; set; } = 0.15f;
        public float Fov { get; set; } = 75.0f;
        public bool Fullscreen { get; set; } = false;
        public bool ShowHints { get; set; } = true;
        public bool ReducedEffects { get; set; } = false;
        public int TargetFps { get; set; } = 60;
        public int Theme { get; set; } = 0;
        public string PlayerName { get; set; } = "CubePlayer";
        // Адрес мастер-директории серверов (VPS). Список открывается по названию, а не по IP.
        public string DirectoryUrl { get; set; } = "http://94.232.46.58:8090";
    }

    static GameSettings settings = new GameSettings();
    static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "cube_havoc_settings.json");

    // Discord RPC
    static DiscordRPCManager discordRpc = new DiscordRPCManager();

    // Шрифт
    static Font customFont;

    // Фон меню
    static float bgScrollX = 0.0f;
    static float scrollSpeed = 60.0f;

    static Random rnd = new Random();

    // Выбранная вкладка в меню Выбора Режима (0 = Одиночная, 1 = Мультиплеер)
    static int playSelectTab = 0;

    // Вкладка главного меню в стиле CS2: 0 = Играть, 1 = Магазин, 2 = Новости
    static int mainMenuTab = 0;

    // 3D Игроки, зомби, предметы и частицы
    static Player3D localPlayer = new Player3D
    {
        Id = 1,
        Name = "CubePlayer",
        IsAdmin = true,
        PlayerColor = GetRandomColor()
    };

    static List<Player3D> players = new List<Player3D>();
    static List<Zombie3D> zombies = new List<Zombie3D>();
    static List<PhysicalObject3D> worldObjects = new List<PhysicalObject3D>();
    static List<HitParticle> particles = new List<HitParticle>();

    // Консоль и Админка
    static bool isConsoleOpen = false;
    static string consoleInput = "";
    static List<string> consoleLogs = new List<string> { "3D Cube Havoc Console initialized." };
    static bool isAdminPanelOpen = false;

    // Чат
    static bool isChatOpen = false;
    static string chatInput = "";
    static List<ChatMessage> chatMessages = new List<ChatMessage>();

    // 3D Камера
    static Camera3D camera = new Camera3D();
    static float cameraYaw = 0.0f;
    static float cameraPitch = 0.0f;
    static float mouseSensitivity = 0.15f;
    static bool cursorDisabled = false;

    // Bodycam-style camera/head presentation (original implementation, no external assets).
    static float headYaw = 0.0f;
    static float headPitch = 0.0f;
    static float bodyYaw = 0.0f;
    static float cameraRoll = 0.0f;
    static float headBobTime = 0.0f;
    static float recoil = 0.0f;
    static float damageFlash = 0.0f;
    static float muzzleFlash = 0.0f;
    static float cameraShake = 0.0f;
    static float movementFovKick = 0.0f;
    static float weaponSwayX = 0.0f;
    static float weaponSwayY = 0.0f;
    static float weaponRecoil = 0.0f;

    // Bodycam v2: сглаженный взгляд, пружинная отдача, свободный обзор (Alt), пост-обработка
    static float viewYaw = 0.0f;
    static float viewPitch = 0.0f;
    static float kickPitch = 0.0f, kickPitchVel = 0.0f;
    static float kickYaw = 0.0f, kickYawVel = 0.0f;
    static float kickRoll = 0.0f, kickRollVel = 0.0f;
    static bool freeLook = false;
    static float recTime = 0.0f;
    static int flashSeed = 0;
    static Vector3 aimForwardDir = new Vector3(0, 0, 1);
    static Vector3 aimRightDir = new Vector3(-1, 0, 0);
    static List<BulletTracer> tracers = new List<BulletTracer>();
    const float Deg2Rad = 0.0174532925f;
    const float Rad2Deg = 57.29578f;

    static Shader postShader;
    static bool postShaderReady = false;
    static int locTime = -1, locRes = -1, locKick = -1, locDamage = -1;
    static RenderTexture2D sceneTarget;
    static int sceneW = 0, sceneH = 0;
    static readonly Dictionary<int, float> btnAnim = new Dictionary<int, float>();

    // Карта 5.0 — увеличенный полигон (400 × 400 против прежних 200 × 200).
    const float MapHalf = 200.0f;      // половина стороны карты
    const float MapTile = 25.0f;       // размер одной плитки пола
    const int MapTiles = 16;           // 16 плиток по 25 = 400 единиц
    const float MapObjectLimit = 170f; // блоки не появляются ближе к краю
    const float MapZombieLimit = 160f;
    const int MapBlockCount = 200;
    const int MapZombieCount = 26;
    const int ParticleCap = 600;

    // Физика предметов
    const float ObjectGravity = 22.0f;
    const float ObjectBounce = 0.22f;
    const float ObjectGroundFriction = 0.82f;
    const float ObjectAirDrag = 0.995f;
    const float PhysicsRestVelocity = 0.08f;
    const float GroundY = 0.0f;

    // LAN / Radmin VPN multiplayer
    static NetworkManager networkManager = new NetworkManager();
    static string serverNameInput = "Cube Havoc Server";
    static string connectIpInput = "";
    static bool serverNameEditing = false;
    static bool ipEditing = false;
    static bool clientWorldPrepared = false;
    static string networkStatus = "";

    // ---------------------------------------------------------------
    // Оружие (5.0): общий каталог из Shared/WeaponCatalog.cs
    // ---------------------------------------------------------------
    static readonly WeaponInfo[] Weapons = WeaponCatalog.All;
    static int currentWeapon = 0;
    static readonly int[] weaponAmmo = new int[WeaponCatalog.MaxWeapons];
    static float weaponFireTimer = 0.0f;
    static float weaponReloadTimer = 0.0f;
    static float weaponSwitchAnim = 0.0f;

    static WeaponInfo ActiveWeapon => WeaponCatalog.Get(currentWeapon);

    static void ResetWeaponLoadout()
    {
        currentWeapon = 0;
        weaponFireTimer = 0.0f;
        weaponReloadTimer = 0.0f;
        weaponSwitchAnim = 0.0f;
        for (int i = 0; i < weaponAmmo.Length; i++) weaponAmmo[i] = WeaponCatalog.Get(i).MagSize;
    }

    static void SelectWeapon(int index)
    {
        if (index == currentWeapon) return;
        currentWeapon = Math.Clamp(index, 0, WeaponCatalog.MaxWeapons - 1);
        weaponReloadTimer = 0.0f;
        weaponSwitchAnim = 1.0f;
        weaponFireTimer = Math.Max(weaponFireTimer, 0.18f);
    }

    static void StartReload()
    {
        var w = ActiveWeapon;
        if (weaponReloadTimer > 0.0f) return;
        if (weaponAmmo[currentWeapon] >= w.MagSize) return;
        weaponReloadTimer = w.ReloadTime;
    }

    static void UpdateWeaponTimers(float dt, bool canControl)
    {
        weaponFireTimer = MathF.Max(0.0f, weaponFireTimer - dt);
        weaponSwitchAnim = MathF.Max(0.0f, weaponSwitchAnim - dt * 4.0f);

        if (weaponReloadTimer > 0.0f)
        {
            weaponReloadTimer -= dt;
            if (weaponReloadTimer <= 0.0f)
            {
                weaponReloadTimer = 0.0f;
                weaponAmmo[currentWeapon] = ActiveWeapon.MagSize;
            }
        }

        if (!canControl) return;

        for (int i = 0; i < WeaponCatalog.MaxWeapons; i++)
            if (Raylib.IsKeyPressed((KeyboardKey)((int)KeyboardKey.One + i))) SelectWeapon(i);

        float wheel = Raylib.GetMouseWheelMove();
        if (wheel > 0.01f) SelectWeapon((currentWeapon + WeaponCatalog.MaxWeapons - 1) % WeaponCatalog.MaxWeapons);
        else if (wheel < -0.01f) SelectWeapon((currentWeapon + 1) % WeaponCatalog.MaxWeapons);

        if (Raylib.IsKeyPressed(KeyboardKey.R)) StartReload();
    }

    /// <summary>Разброс ствола: поворот направления на случайные углы в конусе.</summary>
    static Vector3 ApplySpread(Vector3 dir, Vector3 right, Vector3 up, float spreadDeg)
    {
        if (spreadDeg <= 0.001f) return dir;
        float yaw = ((float)rnd.NextDouble() - 0.5f) * spreadDeg * Deg2Rad;
        float pitch = ((float)rnd.NextDouble() - 0.5f) * spreadDeg * Deg2Rad;
        return Vector3.Normalize(dir + right * MathF.Tan(yaw) + up * MathF.Tan(pitch));
    }

    /// <summary>
    /// Единая обработка попадания: используется и локально, и хостом для действий клиентов.
    /// Возвращает true, если кто-то умер (для счётчиков).
    /// </summary>
    static bool ResolveWeaponHit(int weaponIndex, Ray ray, Player3D shooter)
    {
        var w = WeaponCatalog.Get(weaponIndex);
        bool killed = false;

        for (int i = zombies.Count - 1; i >= 0; i--)
        {
            var z = zombies[i];
            RayCollision hit = Raylib.GetRayCollisionBox(ray, z.BoundingBox);
            if (!hit.Hit || hit.Distance > w.Range) continue;

            z.Health -= w.Damage;
            SpawnHitParticles(hit.Point);

            if (w.SplashRadius > 0.0f) ApplySplash(hit.Point, w.SplashRadius, w.Damage / 2, shooter);

            if (z.Health <= 0)
            {
                zombies.RemoveAt(i);
                if (shooter != null) shooter.Kills++;
                killed = true;
            }
            break;
        }

        return killed;
    }

    static void ApplySplash(Vector3 center, float radius, int damage, Player3D shooter)
    {
        for (int i = zombies.Count - 1; i >= 0; i--)
        {
            var z = zombies[i];
            Vector3 zc = z.Position + new Vector3(0, z.BodySize.Y * 0.5f, 0);
            if (Vector3.Distance(center, zc) > radius) continue;
            z.Health -= damage;
            SpawnHitParticles(zc);
            if (z.Health <= 0)
            {
                zombies.RemoveAt(i);
                if (shooter != null) shooter.Kills++;
            }
        }
    }

    /// <summary>
    /// Выстрел из текущего оружия. applyDamage = false используется клиентом: он показывает
    /// эффекты и отправляет действие хосту, а урон считает хост.
    /// </summary>
    static void FireWeaponLocal(Vector3 origin, Vector3 forward, Vector3 right, bool visuals, bool applyDamage)
    {
        var w = ActiveWeapon;
        weaponFireTimer = w.FireInterval;
        weaponAmmo[currentWeapon] = Math.Max(0, weaponAmmo[currentWeapon] - 1);
        weaponRecoil = 1.0f;

        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));

        for (int pellet = 0; pellet < Math.Max(1, w.Pellets); pellet++)
        {
            Vector3 dir = ApplySpread(forward, right, up, w.SpreadDeg);
            Ray ray = new Ray(origin, dir);
            if (visuals) TriggerShotEffects(ray, w);
            if (applyDamage) ResolveWeaponHit(currentWeapon, ray, localPlayer);
        }

        // Отдача тем сильнее, чем тяжелее ствол.
        recoil = MathF.Max(recoil, Math.Clamp(w.Recoil * 0.55f, 0.2f, 1.0f));
        cameraShake = MathF.Max(cameraShake, Math.Clamp(w.Recoil * 0.42f, 0.15f, 0.9f));

        if (weaponAmmo[currentWeapon] <= 0) StartReload();
    }

    static Color GetRandomColor()
    {
        return new Color((byte)rnd.Next(60, 255), (byte)rnd.Next(60, 255), (byte)rnd.Next(60, 255), (byte)255);
    }

    static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(screenWidth, screenHeight, "Cube Havoc | version 5.0");
        LoadSettings();
        Raylib.SetTargetFPS(settings.TargetFps);
        InitPostProcess();

        discordRpc.Initialize();

        SetApplicationIcon();
        LoadCyrillicFont();
        networkManager.Configure(players, worldObjects, zombies, chatMessages, localPlayer);

        camera.Position = new Vector3(0.0f, 2.0f, 0.0f);
        camera.Target = new Vector3(0.0f, 2.0f, -1.0f);
        camera.Up = new Vector3(0.0f, 1.0f, 0.0f);
        camera.FovY = settings.Fov;
        camera.Projection = CameraProjection.Perspective;

        while (!Raylib.WindowShouldClose())
        {
            float deltaTime = Raylib.GetFrameTime();
            menuTime += deltaTime;
            UpdateMenuTransition(deltaTime);
            screenWidth = Raylib.GetScreenWidth();
            screenHeight = Raylib.GetScreenHeight();

            discordRpc.Update();
            networkManager.PollEvents(deltaTime);
            ProcessNetworkActions();
            if (networkManager.Mode == NetworkManager.NetMode.Client && networkManager.IsConnected && !clientWorldPrepared)
                PrepareClientWorld();

            bgScrollX += scrollSpeed * deltaTime;
            if (bgScrollX >= screenWidth) bgScrollX -= screenWidth;

            if (Raylib.IsKeyPressed(KeyboardKey.Grave))
            {
                isConsoleOpen = !isConsoleOpen;
                if (isConsoleOpen) EnableCursor();
            }

            if ((currentState == GameState.Singleplayer || currentState == GameState.LocalLobby) 
                && !isConsoleOpen && !isAdminPanelOpen)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.T) && !isChatOpen)
                {
                    isChatOpen = true;
                    chatInput = "";
                    EnableCursor();
                }
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                if (isChatOpen)
                {
                    isChatOpen = false;
                }
                else if (isConsoleOpen)
                {
                    isConsoleOpen = false;
                }
                else if (isAdminPanelOpen)
                {
                    isAdminPanelOpen = false;
                }
                else if (currentState == GameState.Singleplayer || currentState == GameState.LocalLobby)
                {
                    if (cursorDisabled) EnableCursor();
                    else DisableCursor();
                }
            }

            for (int i = chatMessages.Count - 1; i >= 0; i--)
            {
                chatMessages[i].TimeRemaining -= deltaTime;
            }

            if (currentState == GameState.Singleplayer || currentState == GameState.LocalLobby)
            {
                UpdateGame3D(deltaTime);
            }
            else
            {
                if (cursorDisabled) EnableCursor();
            }

            Raylib.BeginDrawing();
            
            Raylib.ClearBackground(new Color(135, 185, 235, 255));

            if (currentState == GameState.Singleplayer || currentState == GameState.LocalLobby)
            {
                DrawGameScene();
                DrawGameUI();
                DrawChatUI();
                DrawMissionOverlays();
            }
            else
            {
                DrawMovingPixelForest();

                switch (currentState)
                {
                    case GameState.MainMenu:
                        DrawMainMenu();
                        break;
                    case GameState.PlaySelect:
                        DrawPlaySelectMenu();
                        break;
                    case GameState.Settings:
                        DrawSettingsMenu();
                        break;
                    case GameState.Developers:
                        DrawDevelopersMenu();
                        break;
                }
            }

            if (isConsoleOpen) DrawConsole();
            if (isAdminPanelOpen) DrawAdminPanel();
            DrawTransitionOverlay();

            Raylib.EndDrawing();
        }

        networkManager.Stop();
        discordRpc.Deinitialize();

        Raylib.UnloadFont(customFont);
        ShutdownPostProcess();
        Raylib.CloseWindow();
    }

    static void DisableCursor()
    {
        Raylib.DisableCursor();
        cursorDisabled = true;
    }

    static void EnableCursor()
    {
        Raylib.EnableCursor();
        cursorDisabled = false;
    }

    static void SpawnHitParticles(Vector3 pos)
    {
        int count = settings.ReducedEffects ? 6 : 15;
        for (int i = 0; i < count; i++)
        {
            particles.Add(new HitParticle
            {
                Position = pos,
                Velocity = new Vector3(
                    (float)(rnd.NextDouble() * 8.0 - 4.0),
                    (float)(rnd.NextDouble() * 6.0 + 1.0),
                    (float)(rnd.NextDouble() * 8.0 - 4.0)
                ),
                Color = rnd.Next(0, 2) == 0 ? Color.Red : Color.Gold,
                Life = 0.35f,
                Size = (float)(rnd.NextDouble() * 0.15 + 0.1)
            });
        }
    }

    static void InitGameWorld3D(bool enableZombies)
    {
        players.Clear();
        zombies.Clear();
        worldObjects.Clear();
        chatMessages.Clear();
        particles.Clear();

        localPlayer.Position = new Vector3(0, 0.0f, 0);
        localPlayer.Health = 10;
        localPlayer.Kills = 0;
        localPlayer.Deaths = 0;
        localPlayer.Inventory.Clear();
        localPlayer.PlayerColor = GetRandomColor();
        players.Add(localPlayer);

        cameraYaw = 0.0f;
        cameraPitch = 0.0f;
        headYaw = 0.0f;
        headPitch = 0.0f;
        bodyYaw = 0.0f;
        cameraRoll = 0.0f;
        headBobTime = 0.0f;
        recoil = 0.0f;
        damageFlash = 0.0f;
        muzzleFlash = 0.0f;
        cameraShake = 0.0f;
        movementFovKick = 0.0f;
        weaponSwayX = 0.0f;
        weaponSwayY = 0.0f;
        weaponRecoil = 0.0f;
        viewYaw = 0.0f;
        viewPitch = 0.0f;
        kickPitch = kickPitchVel = 0.0f;
        kickYaw = kickYawVel = 0.0f;
        kickRoll = kickRollVel = 0.0f;
        freeLook = false;
        recTime = 0.0f;
        tracers.Clear();
        DisableCursor();

        chatMessages.Add(new ChatMessage
        {
            Sender = "СЕРВЕР",
            Text = "Добро пожаловать в игру! Бросайте предметы в зомби для урона.",
            SenderColor = Color.Gold,
            TimeRemaining = 15.0f
        });

        int limit = (int)MapObjectLimit;
        for (int i = 0; i < MapBlockCount; i++)
        {
            worldObjects.Add(new PhysicalObject3D
            {
                NetId = 1000 + i,
                Position = new Vector3(rnd.Next(-limit, limit), 0.8f, rnd.Next(-limit, limit)),
                Size = new Vector3(1.6f, 1.6f, 1.6f),
                Color = new Color(110, 120, 135, 255),
                Type = EntityType.Block
            });
        }

        for (int i = 0; i < 8; i++)
        {
            worldObjects.Add(new PhysicalObject3D
            {
                NetId = 1100 + i,
                Position = new Vector3(rnd.Next(-limit, limit), 0.5f, rnd.Next(-limit, limit)),
                Size = new Vector3(0.8f, 0.8f, 0.8f),
                Color = Color.Gold,
                Type = EntityType.GravityGun
            });
        }

        if (enableZombies)
        {
            for (int i = 0; i < MapZombieCount; i++)
            {
                zombies.Add(new Zombie3D
                {
                    NetId = 2000 + i,
                    Position = new Vector3(rnd.Next(-(int)MapZombieLimit, (int)MapZombieLimit), 0.0f, rnd.Next(-(int)MapZombieLimit, (int)MapZombieLimit)),
                    Health = 50,
                    MaxHealth = 50
                });
            }
        }

        ResetWeaponLoadout();
    }

    static void UpdateGame3D(float dt)
    {
        if (missionBriefingOpen || missionComplete)
        {
            if (cursorDisabled) EnableCursor();
            return;
        }

        if (isChatOpen || isConsoleOpen || isAdminPanelOpen)
        {
            if (cursorDisabled) EnableCursor();
            return;
        }

        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !cursorDisabled)
        {
            DisableCursor();
        }

        // --- 1. Голова/камера в стиле Bodycam ---
        // Голова вращается на полные 360° (без ограничения), взгляд слегка сглажен,
        // отдача и тряска работают как пружины. Alt = свободный обзор (корпус не поворачивается).
        recTime += dt;
        freeLook = cursorDisabled && Raylib.IsKeyDown(KeyboardKey.LeftAlt);

        if (cursorDisabled)
        {
            Vector2 mouseDelta = Raylib.GetMouseDelta();
            headYaw -= mouseDelta.X * mouseSensitivity;
            headPitch -= mouseDelta.Y * mouseSensitivity;
            headPitch = Math.Clamp(headPitch, -88.0f, 88.0f);
            headYaw = WrapAngle(headYaw); // раньше было Clamp(-180..180) — из-за этого нельзя было крутиться
        }

        // Корпус догоняет голову с задержкой (кроме режима свободного обзора).
        if (!freeLook)
        {
            float yawDifference = MathF.IEEERemainder(headYaw - bodyYaw, 360.0f);
            float bodyFollow = Math.Clamp(dt * 7.0f, 0.0f, 1.0f);
            if (MathF.Abs(yawDifference) > 55.0f)
                bodyFollow = Math.Clamp(dt * 13.0f, 0.0f, 1.0f);
            bodyYaw = WrapAngle(bodyYaw + yawDifference * bodyFollow);
        }

        // Лёгкая инерция взгляда — камера «тяжёлая», как на теле.
        float lookFollow = 1.0f - MathF.Exp(-dt * 30.0f);
        viewYaw = WrapAngle(viewYaw + MathF.IEEERemainder(headYaw - viewYaw, 360.0f) * lookFollow);
        viewPitch += (headPitch - viewPitch) * lookFollow;

        // Пружины отдачи (градусы).
        float sdt = MathF.Min(dt, 0.033f);
        kickPitchVel += (-110.0f * kickPitch - 15.0f * kickPitchVel) * sdt;
        kickPitch += kickPitchVel * sdt;
        kickYawVel += (-110.0f * kickYaw - 15.0f * kickYawVel) * sdt;
        kickYaw += kickYawVel * sdt;
        kickRollVel += (-110.0f * kickRoll - 15.0f * kickRollVel) * sdt;
        kickRoll += kickRollVel * sdt;

        cameraYaw = headYaw;
        cameraPitch = headPitch;

        // Направление прицела = туда, куда реально смотрит центр камеры (без шума тряски).
        float aimYawRad = (viewYaw + kickYaw) * Deg2Rad;
        float aimPitchRad = Math.Clamp(viewPitch + kickPitch, -89.0f, 89.0f) * Deg2Rad;

        Vector3 forward = Vector3.Normalize(new Vector3(
            MathF.Sin(aimYawRad) * MathF.Cos(aimPitchRad),
            MathF.Sin(aimPitchRad),
            MathF.Cos(aimYawRad) * MathF.Cos(aimPitchRad)));

        // Клавиша D идет вправо, A — влево
        Vector3 right = new Vector3(-MathF.Cos(aimYawRad), 0, MathF.Sin(aimYawRad));
        Vector3 forwardFlat = Vector3.Normalize(new Vector3(forward.X, 0, forward.Z));
        aimForwardDir = forward;
        aimRightDir = right;

        // --- 2. Движение игрока от 1-го лица ---
        // В обычном режиме идём туда, куда смотрим (как раньше). С Alt — по направлению корпуса.
        float moveYawRad = (freeLook ? bodyYaw : headYaw) * Deg2Rad;
        Vector3 moveFwd = new Vector3(MathF.Sin(moveYawRad), 0, MathF.Cos(moveYawRad));
        Vector3 moveRight = new Vector3(-MathF.Cos(moveYawRad), 0, MathF.Sin(moveYawRad));

        Vector3 moveDir = Vector3.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.W)) moveDir += moveFwd;
        if (Raylib.IsKeyDown(KeyboardKey.S)) moveDir -= moveFwd;
        if (Raylib.IsKeyDown(KeyboardKey.D)) moveDir += moveRight;
        if (Raylib.IsKeyDown(KeyboardKey.A)) moveDir -= moveRight;

        if (moveDir != Vector3.Zero)
            moveDir = Vector3.Normalize(moveDir);

        localPlayer.Velocity = moveDir * localPlayer.Speed;
        localPlayer.Position += localPlayer.Velocity * dt;

        // Расширенный игровой сектор: примерно 200x200 метров.
        localPlayer.Position.X = Math.Clamp(localPlayer.Position.X, -95.0f, 95.0f);
        localPlayer.Position.Z = Math.Clamp(localPlayer.Position.Z, -95.0f, 95.0f);

        float horizontalSpeed = new Vector3(localPlayer.Velocity.X, 0, localPlayer.Velocity.Z).Length();
        float moveAmt = Math.Clamp(horizontalSpeed / Math.Max(1.0f, localPlayer.Speed), 0.0f, 1.0f);
        if (horizontalSpeed > 0.15f)
            headBobTime += dt * (7.0f + horizontalSpeed * 0.75f);
        else
            headBobTime += dt * 1.5f;

        float bobX = horizontalSpeed > 0.15f ? MathF.Sin(headBobTime * 0.5f) * 0.05f : 0.0f;
        float bobY = horizontalSpeed > 0.15f ? MathF.Abs(MathF.Sin(headBobTime)) * 0.07f : 0.0f;
        float breathe = MathF.Sin(menuTime * 1.7f) * 0.012f;

        // Наклон при стрейфе считается от бокового движения (раньше зависел от направления взгляда).
        float strafe = Vector3.Dot(localPlayer.Velocity, right) / Math.Max(1.0f, localPlayer.Speed);
        float targetRoll = -strafe * 2.0f + MathF.Sin(headBobTime * 0.5f) * 0.8f * moveAmt;
        cameraRoll += (targetRoll - cameraRoll) * Math.Clamp(dt * 8.0f, 0, 1);

        recoil = MathF.Max(0.0f, recoil - dt * 5.5f);
        cameraShake = MathF.Max(0.0f, cameraShake - dt * 4.0f);
        damageFlash = MathF.Max(0.0f, damageFlash - dt * 2.8f);
        muzzleFlash = MathF.Max(0.0f, muzzleFlash - dt * 14.0f);
        movementFovKick += (((horizontalSpeed > 0.2f) ? 2.5f : 0.0f) - movementFovKick) * Math.Clamp(dt * 5.0f, 0, 1);

        // Тряска (trauma-модель): плавный шум, амплитуда ~ trauma².
        float trauma2 = cameraShake * cameraShake;
        float nt = menuTime * 24.0f;
        float shakeYawDeg = Noise1(nt, 1.0f) * trauma2 * 4.0f;
        float shakePitchDeg = Noise1(nt, 2.0f) * trauma2 * 3.0f;
        float shakeRollDeg = Noise1(nt, 3.0f) * trauma2 * 3.5f;

        // Дыхание и шаги.
        float idleSwayYaw = MathF.Sin(menuTime * 0.8f) * 0.10f + MathF.Sin(menuTime * 2.1f) * 0.04f;
        float idleSwayPitch = MathF.Sin(menuTime * 1.1f + 1.3f) * 0.08f;
        float stepPitch = -MathF.Abs(MathF.Sin(headBobTime)) * 0.9f * moveAmt;
        float stepYaw = MathF.Sin(headBobTime * 0.5f) * 0.55f * moveAmt;

        float finalYaw = (viewYaw + kickYaw + shakeYawDeg + idleSwayYaw + stepYaw) * Deg2Rad;
        float finalPitch = Math.Clamp(viewPitch + kickPitch + shakePitchDeg + idleSwayPitch + stepPitch, -89.0f, 89.0f) * Deg2Rad;
        Vector3 viewDir = Vector3.Normalize(new Vector3(
            MathF.Sin(finalYaw) * MathF.Cos(finalPitch),
            MathF.Sin(finalPitch),
            MathF.Cos(finalYaw) * MathF.Cos(finalPitch)));
        Vector3 camRight = new Vector3(-MathF.Cos(finalYaw), 0, MathF.Sin(finalYaw));

        camera.Position = localPlayer.Position + camRight * bobX + new Vector3(0.0f, 2.0f + bobY + breathe, 0.0f);
        camera.Target = camera.Position + viewDir;
        // Крен вокруг оси взгляда (раньше наклон шёл вокруг мировой оси Z и «ломал» картинку при повороте).
        float rollRad = (cameraRoll + kickRoll + shakeRollDeg) * Deg2Rad;
        camera.Up = Vector3.Transform(Vector3.UnitY, Quaternion.CreateFromAxisAngle(viewDir, rollRad));
        camera.FovY = settings.Fov + movementFovKick + recoil * 2.5f;

        // Частицы, трассеры и покачивание оружия обновляются и у клиента тоже.
        UpdateEffects(dt);
        UpdateWeaponTimers(dt, cursorDisabled && localPlayer.Inventory.Count == 0);
        if (missionActive) UpdateMission(dt);

        bool triggerDown = ActiveWeapon.Automatic ? Raylib.IsMouseButtonDown(MouseButton.Left) : Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool canFire = cursorDisabled && localPlayer.Inventory.Count == 0 && weaponFireTimer <= 0.0f &&
                       weaponReloadTimer <= 0.0f && weaponAmmo[currentWeapon] > 0;

        if (networkManager.Mode == NetworkManager.NetMode.Client)
        {
            networkManager.SetLocalInputState(localPlayer.Position, forward, Raylib.IsMouseButtonDown(MouseButton.Left));
            if (triggerDown && canFire)
            {
                // Локальные camera/weapon effects показываем сразу, а урон подтверждает хост.
                FireWeaponLocal(camera.Position, forward, right, true, false);
                networkManager.SendAction(NetworkManager.ActAttack, camera.Position, forward, (byte)currentWeapon);
            }
            return; // Client only predicts own movement; host is authoritative.
        }

        // --- 3. Стрельба и Взаимодействие ---
        if (triggerDown && canFire)
        {
            FireWeaponLocal(camera.Position, forward, right, true, true);
        }
        else if (triggerDown && canFire == false && weaponAmmo[currentWeapon] <= 0)
        {
            StartReload();
        }

        if (Raylib.IsMouseButtonDown(MouseButton.Left) && cursorDisabled)
        {
            foreach (var obj in worldObjects)
            {
                if (!obj.IsHeld && localPlayer.Inventory.Count < 3)
                {
                    if (obj.Type == EntityType.GravityGun && localPlayer.HasGravityGun) continue;

                    float dist = Vector3.Distance(camera.Position, obj.Position);
                    if (dist < 5.0f)
                    {
                        obj.IsHeld = true;
                        obj.HeldByPlayerId = localPlayer.Id;
                        localPlayer.Inventory.Add(obj);

                        // Сюжет: подобранное энергоядро засчитывается один раз.
                        if (missionActive && missionStage == 2 && obj.Type == EntityType.GravityGun && missionCoreIds.Add(obj.NetId))
                        {
                            missionCores++;
                            PushRadio(missionCores < 3
                                ? $"Ядро {missionCores} из 3 — есть."
                                : "Третье ядро взято. Двигай к ретранслятору.");
                        }
                        break;
                    }
                }
            }
        }

        for (int i = 0; i < localPlayer.Inventory.Count; i++)
        {
            var item = localPlayer.Inventory[i];
            Vector3 holdPos = camera.Position + forward * 2.5f + right * 0.8f + new Vector3(0, -0.4f, 0);

            // Даже предмет в руках не может оказаться под землёй.
            float minHeldY = GroundY + item.Size.Y * 0.5f;
            if (holdPos.Y < minHeldY) holdPos.Y = minHeldY;

            item.Position = holdPos;
            item.Velocity = Vector3.Zero;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Q) && localPlayer.Inventory.Count > 0)
        {
            var item = localPlayer.Inventory[localPlayer.Inventory.Count - 1];
            item.IsHeld = false;
            item.HeldByPlayerId = -1;
            item.Velocity = forward * 12.0f + new Vector3(0, 2.0f, 0);
            localPlayer.Inventory.Remove(item);
        }

        if (Raylib.IsKeyPressed(KeyboardKey.E))
        {
            if (localPlayer.Inventory.Count > 0)
            {
                var item = localPlayer.Inventory[localPlayer.Inventory.Count - 1];
                item.IsHeld = false;
                item.HeldByPlayerId = -1;
                item.Velocity = forward * 28.0f + new Vector3(0, 3.0f, 0);
                localPlayer.Inventory.Remove(item);
            }

            foreach (var z in zombies)
            {
                Vector3 zCenter = z.Position + new Vector3(0, z.BodySize.Y / 2.0f, 0);
                if (Vector3.Distance(camera.Position, zCenter) < 4.5f)
                {
                    z.Position += forwardFlat * 6.0f;
                }
            }
        }

        // --- 4. Полная физика предметов ---
        // Гравитация + столкновение с землёй + отскок + трение.
        // Это выполняется на хосте, поэтому в LAN/Radmin все игроки получают
        // одинаковое авторитетное положение предметов.
        foreach (var obj in worldObjects)
        {
            if (!obj.IsHeld)
            {
                // Спящий предмет (лежит на земле и не движется) не участвует в физике:
                // это убирает основной расход CPU — раньше 200 объектов считались каждый кадр.
                bool sleeping = obj.Velocity.LengthSquared() < 0.0004f &&
                                obj.Position.Y <= GroundY + obj.Size.Y * 0.5f + 0.002f;
                if (sleeping)
                {
                    obj.Velocity = Vector3.Zero;
                    continue;
                }

                // Гравитация
                obj.Velocity.Y -= ObjectGravity * dt;

                // Интеграция движения
                obj.Position += obj.Velocity * dt;

                // Предмет не может провалиться под поверхность земли.
                ResolveGroundCollision(obj);

                // Столкновение предметов между собой.
                ResolveObjectCollisions(obj);

                // После столкновения ещё раз гарантируем, что предмет не ушёл под землю.
                ResolveGroundCollision(obj);

                // Столкновение предмета с зомби
                if (obj.Velocity.Length() > 2.0f)
                {
                    for (int i = zombies.Count - 1; i >= 0; i--)
                    {
                        var z = zombies[i];
                        if (Raylib.CheckCollisionBoxes(obj.BoundingBox, z.BoundingBox))
                        {
                            z.Health -= 10; // -10 ХП за удар предметом
                            SpawnHitParticles(obj.Position);

                            obj.Velocity = -obj.Velocity * 0.3f; // Отскок предмета

                            if (z.Health <= 0)
                            {
                                zombies.RemoveAt(i);
                                localPlayer.Kills++;
                            }
                            break;
                        }
                    }
                }

                // Воздушное сопротивление. На земле горизонтальное трение сильнее.
                obj.Velocity.X *= ObjectAirDrag;
                obj.Velocity.Z *= ObjectAirDrag;
                if (MathF.Abs(obj.Velocity.Y) < PhysicsRestVelocity &&
                    obj.Position.Y <= GroundY + obj.Size.Y * 0.5f + 0.001f)
                {
                    obj.Velocity.Y = 0.0f;
                }

                if (MathF.Abs(obj.Velocity.Y) < PhysicsRestVelocity &&
                    obj.Position.Y <= GroundY + obj.Size.Y * 0.5f + 0.001f)
                {
                    obj.Velocity.X *= ObjectGroundFriction;
                    obj.Velocity.Z *= ObjectGroundFriction;
                }

                if (obj.Velocity.Length() < PhysicsRestVelocity)
                    obj.Velocity = Vector3.Zero;

                if (obj.Velocity.Length() > 6.0f && Raylib.CheckCollisionBoxes(obj.BoundingBox, localPlayer.BoundingBox))
                {
                    int damage = Math.Min(9, (int)(obj.Velocity.Length() / 3.0f));
                    localPlayer.Health -= damage;
                    damageFlash = 1.0f;
                    cameraShake = MathF.Max(cameraShake, 0.65f);
                    obj.Velocity = Vector3.Zero;
                    if (localPlayer.Health <= 0)
                    {
                        localPlayer.Deaths++;
                        localPlayer.Health = 10;
                        localPlayer.Position = new Vector3(0, 0.0f, 0);
                    }
                }
            }
        }

        // --- 5. ИИ Зомби (Поворот в сторону игрока и движение) ---
        foreach (var z in zombies)
        {
            Vector3 toPlayer = localPlayer.Position - z.Position;
            toPlayer.Y = 0;

            if (toPlayer != Vector3.Zero)
            {
                // Поворот зомби лицом к игроку
                z.Yaw = MathF.Atan2(toPlayer.X, toPlayer.Z) * (180.0f / MathF.PI);

                Vector3 dir = Vector3.Normalize(toPlayer);
                z.Velocity = dir * z.Speed;
                z.Position += z.Velocity * dt;
            }

            if (Raylib.CheckCollisionBoxes(z.BoundingBox, localPlayer.BoundingBox))
            {
                localPlayer.Health -= 1;
                damageFlash = 1.0f;
                cameraShake = MathF.Max(cameraShake, 0.45f);
                z.Position -= Vector3.Normalize(toPlayer) * 3.5f;
                if (localPlayer.Health <= 0)
                {
                    localPlayer.Deaths++;
                    localPlayer.Health = 10;
                    localPlayer.Position = new Vector3(0, 0.0f, 0);
                }
            }
        }

    }

    // ================================================================
    // Bodycam v2: общие хелперы
    // ================================================================
    static float WrapAngle(float a) => MathF.IEEERemainder(a, 360.0f);

    static float Noise1(float t, float seed)
        => MathF.Sin(t + seed * 12.9898f) * 0.6f + MathF.Sin(t * 2.37f + seed * 78.233f) * 0.4f;

    static Color Col(int r, int g, int b, int a = 255)
        => new Color((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255), (byte)Math.Clamp(a, 0, 255));

    static Color LerpColor(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        return Col((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t), (int)(a.A + (b.A - a.A) * t));
    }

    static Color Tint(Color c, float f) => Col((int)(c.R * f), (int)(c.G * f), (int)(c.B * f), c.A);

    // Скруглённые прямоугольники (радиус в пикселях).
    static float RoundRatio(float w, float h, float radiusPx) => Math.Clamp(radiusPx * 2.0f / Math.Max(1.0f, Math.Min(w, h)), 0.0f, 1.0f);

    static void RR(float x, float y, float w, float h, float radius, Color c)
        => Raylib.DrawRectangleRounded(new Rectangle(x, y, w, h), RoundRatio(w, h, radius), 10, c);

    static void RRL(float x, float y, float w, float h, float radius, float thick, Color c)
        => Raylib.DrawRectangleRoundedLinesEx(new Rectangle(x, y, w, h), RoundRatio(w, h, radius), 10, thick, c);

    // Круг в локальных координатах viewmodel (барабан револьвера, линза оптики, магазин пулемёта).
    static void FCylView(float cx, float cy, float radius, int sides, Color c)
        => Raylib.DrawPoly(new Vector2(cx, cy), sides, radius, 0.0f, c);

    // ================================================================
    // Сюжет: операция «КУБ-9»
    // ================================================================

    const string MissionTitle = "ОПЕРАЦИЯ «КУБ-9»";
    static readonly Vector3 RelayPoint = new Vector3(0, 0, -96);
    const float RelayRadius = 14.0f;
    const int MissionWaveKills = 8;
    const int MissionFinalKills = 20;

    static bool missionActive = false;
    static bool missionBriefingOpen = false;
    static bool missionComplete = false;
    static int missionStage = 0;
    static float missionElapsed = 0.0f;
    static int missionCores = 0;
    static readonly HashSet<int> missionCoreIds = new HashSet<int>();
    static string missionRadio = "";
    static float missionRadioTimer = 0.0f;

    static void StartMission()
    {
        missionActive = true;
        missionBriefingOpen = true;
        missionComplete = false;
        missionStage = 1;
        missionElapsed = 0.0f;
        missionCores = 0;
        missionCoreIds.Clear();
        missionRadio = "";
        missionRadioTimer = 0.0f;
        EnableCursor();
        discordRpc.SetStatus("Операция «КУБ-9»");
    }

    static void AbortMission()
    {
        missionActive = false;
        missionBriefingOpen = false;
        missionComplete = false;
        missionStage = 0;
        missionRadio = "";
        missionRadioTimer = 0.0f;
    }

    static void PushRadio(string text, float seconds = 7.0f)
    {
        missionRadio = text;
        missionRadioTimer = seconds;
        chatMessages.Add(new ChatMessage
        {
            Sender = "РАДИО",
            Text = text,
            SenderColor = Color.Gold,
            TimeRemaining = 14.0f
        });
        if (chatMessages.Count > 8) chatMessages.RemoveAt(0);
    }

    static float DistanceToRelay()
    {
        Vector3 d = new Vector3(localPlayer.Position.X, 0, localPlayer.Position.Z) - RelayPoint;
        return d.Length();
    }

    static string MissionObjectiveText()
    {
        switch (missionStage)
        {
            case 1: return $"Уничтожить заражённых: {Math.Min(localPlayer.Kills, MissionWaveKills)}/{MissionWaveKills}";
            case 2: return $"Собрать энергоядра: {missionCores}/3";
            case 3: return $"Дойти до ретранслятора: {MathF.Round(DistanceToRelay())} м";
            case 4: return $"Удержать сектор: {Math.Min(localPlayer.Kills, MissionFinalKills)}/{MissionFinalKills}";
            default: return "Сектор зачищен";
        }
    }

    static string MissionHintText()
    {
        switch (missionStage)
        {
            case 1: return "Держи дистанцию: автомат и дробовик решают вблизи.";
            case 2: return "Ядра светятся золотом — подбирай ЛКМ.";
            case 3: return "Маяк отмечен столбом света к северу от моста.";
            case 4: return "Не подпускай заражённых к ретранслятору.";
            default: return "";
        }
    }

    static float MissionProgress()
    {
        switch (missionStage)
        {
            case 1: return Math.Clamp(localPlayer.Kills / (float)MissionWaveKills, 0.0f, 1.0f);
            case 2: return Math.Clamp(missionCores / 3.0f, 0.0f, 1.0f);
            case 3: return Math.Clamp(1.0f - DistanceToRelay() / (MapHalf * 1.5f), 0.0f, 1.0f);
            case 4: return Math.Clamp(localPlayer.Kills / (float)MissionFinalKills, 0.0f, 1.0f);
            default: return 1.0f;
        }
    }

    static void UpdateMission(float dt)
    {
        if (!missionActive || missionComplete || missionBriefingOpen) return;

        missionElapsed += dt;
        missionRadioTimer = MathF.Max(0.0f, missionRadioTimer - dt);

        switch (missionStage)
        {
            case 1:
                if (localPlayer.Kills >= MissionWaveKills)
                    AdvanceMissionStage(2, "Восемь целей — чисто. Теперь ядра: три штуки разбросаны по полигону.");
                break;

            case 2:
                if (missionCores >= 3)
                    AdvanceMissionStage(3, "Ядра у тебя. Ретранслятор севернее — маяк за мостом.");
                break;

            case 3:
                if (DistanceToRelay() <= RelayRadius)
                    AdvanceMissionStage(4, "Ты у ретранслятора. Ставь ядра и держи сектор — волна идёт!");
                break;

            case 4:
                if (localPlayer.Kills >= MissionFinalKills)
                    FinishMission();
                break;
        }
    }

    static void AdvanceMissionStage(int stage, string radio)
    {
        missionStage = stage;
        PushRadio(radio);
        if (stage == 4)
        {
            SpawnMissionWave();
            discordRpc.SetStatus("Оборона ретранслятора");
        }
    }

    static void SpawnMissionWave()
    {
        for (int i = 0; i < 8; i++)
        {
            float ang = i * (MathF.PI * 2.0f / 8.0f);
            zombies.Add(new Zombie3D
            {
                NetId = GetNextZombieNetId(),
                Position = RelayPoint + new Vector3(MathF.Cos(ang) * 26.0f, 0, MathF.Sin(ang) * 26.0f),
                Health = 50,
                MaxHealth = 50
            });
        }
    }

    static void FinishMission()
    {
        missionComplete = true;
        missionStage = 5;
        EnableCursor();
        PushRadio("Сектор чист, ретранслятор в сети. Возвращайся, Куб.", 12.0f);
        discordRpc.SetStatus("Операция завершена");
    }

    static void DrawMissionHud()
    {
        if (!missionActive || missionComplete || missionBriefingOpen) return;

        int w = 330, x = screenWidth - w - 20, y = 46;
        RR(x, y, w, 104, 14, new Color(10, 14, 22, 205));
        RRL(x, y, w, 104, 14, 1.5f, new Color(255, 180, 70, 150));
        DrawTextCustom(MissionTitle, x + 12, y + 8, 12, Color.Gold);
        DrawTextCustom(MissionObjectiveText(), x + 12, y + 28, 14, Color.White);

        float p = MissionProgress();
        RR(x + 12, y + 54, w - 24, 8, 4, new Color(32, 38, 48, 230));
        RR(x + 12, y + 54, Math.Max(8.0f, (w - 24) * p), 8, 4, new Color(255, 184, 70, 255));
        DrawTextCustom(MissionHintText(), x + 12, y + 70, 11, Color.LightGray);

        if (missionRadioTimer > 0.0f && !string.IsNullOrEmpty(missionRadio))
        {
            int bw = Math.Min(screenWidth - 160, 780);
            int bx = (screenWidth - bw) / 2;
            int by = screenHeight - 196;
            RR(bx, by, bw, 56, 14, new Color(12, 16, 26, 220));
            RRL(bx, by, bw, 56, 14, 1.5f, new Color(255, 190, 90, 190));
            DrawTextCustom("РАДИО", bx + 14, by + 8, 10, Color.Gold);
            DrawTextCustom(missionRadio, bx + 14, by + 26, 14, Color.White);
        }
    }

    static void DrawMissionOverlays()
    {
        if (!missionActive) return;
        if (missionBriefingOpen) { DrawMissionBriefing(); return; }
        if (missionComplete) DrawMissionComplete();
    }

    static void DrawMissionBriefing()
    {
        Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, new Color(4, 7, 12, 232));

        int pw = Math.Min(880, screenWidth - 100);
        int ph = 470;
        int px = (screenWidth - pw) / 2;
        int py = (screenHeight - ph) / 2;

        RR(px, py, pw, ph, 20, new Color(12, 17, 26, 250));
        RRL(px, py, pw, ph, 20, 2, Color.Orange);
        DrawTextCustom("ПАМЯТКА ОПЕРАТОРА", px + 40, py + 22, 12, Color.Gold);
        DrawTextCustom(MissionTitle, px + 40, py + 44, 26, Color.White);
        Raylib.DrawLine(px + 40, py + 84, px + pw - 40, py + 84, new Color(70, 84, 110, 180));

        string[] lines =
        {
            "«Куб-9» — заброшенный полигон корпорации «Хавок». Три часа назад ИИ",
            "снял протокол сдерживания, и биомасса вышла из-под контроля.",
            "",
            "Ты — оператор «Куб», последний, кто остался на частоте базы.",
            "Задача: зачистить сектор, собрать три энергоядра и запустить ретранслятор.",
            "",
            "Всё, что шевелится и не отвечает по связи — враждебно."
        };

        for (int i = 0; i < lines.Length; i++)
        {
            Color c = i == 3 || i == 4 ? Color.White : new Color(190, 198, 210, 255);
            DrawTextCustom(lines[i], px + 40, py + 104 + i * 24, 15, c);
        }

        DrawTextCustom("1-9/колесо — оружие   R — перезарядка   ЛКМ — огонь   T — чат   Alt — обзор",
            px + 40, py + ph - 104, 12, Color.Gray);

        if (DrawButtonCustom("НАЧАТЬ ОПЕРАЦИЮ", px + pw / 2 - 150, py + ph - 74, 300, 46, Color.Orange))
        {
            missionBriefingOpen = false;
            DisableCursor();
            PushRadio("База, это Куб. Вышел на полигон. Слышу вас?", 8.0f);
        }
    }

    static void DrawMissionComplete()
    {
        Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, new Color(4, 7, 12, 215));

        int pw = Math.Min(640, screenWidth - 120);
        int ph = 340;
        int px = (screenWidth - pw) / 2;
        int py = (screenHeight - ph) / 2;

        RR(px, py, pw, ph, 20, new Color(12, 17, 26, 250));
        RRL(px, py, pw, ph, 20, 2, new Color(120, 220, 150, 230));
        DrawTextCustom("ОПЕРАЦИЯ ЗАВЕРШЕНА", px + 34, py + 26, 24, new Color(140, 230, 170, 255));

        int t = (int)missionElapsed;
        DrawTextCustom($"Время: {t / 60:00}:{t % 60:00}", px + 34, py + 78, 16, Color.White);
        DrawTextCustom($"Убийств: {localPlayer.Kills}", px + 34, py + 104, 16, Color.White);
        DrawTextCustom($"Смертей: {localPlayer.Deaths}", px + 34, py + 130, 16, Color.White);
        DrawTextCustom($"Ядер собрано: {missionCores}/3", px + 34, py + 156, 16, Color.White);
        DrawTextCustom("Ретранслятор в сети. Сектор «Куб-9» под контролем.", px + 34, py + 192, 13, Color.LightGray);

        if (DrawButtonCustom("В МЕНЮ", px + 34, py + ph - 76, 220, 44))
        {
            AbortMission();
            networkManager.Stop();
            NavigateTo(GameState.MainMenu);
        }
        if (DrawButtonCustom("ПОВТОРИТЬ", px + pw - 254, py + ph - 76, 220, 44, Color.Orange))
        {
            InitGameWorld3D(true);
            StartMission();
        }
    }

    // ================================================================
    // Эффекты: частицы, трассеры, покачивание оружия
    // ================================================================
    static void UpdateEffects(float dt)
    {
        weaponRecoil = MathF.Max(0.0f, weaponRecoil - dt * 7.0f);
        Vector2 md = cursorDisabled ? Raylib.GetMouseDelta() : Vector2.Zero;
        float swayK = Math.Clamp(dt * 12.0f, 0, 1);
        weaponSwayX += (Math.Clamp(md.X * 0.015f, -3.0f, 3.0f) - weaponSwayX) * swayK;
        weaponSwayY += (Math.Clamp(md.Y * 0.015f, -3.0f, 3.0f) - weaponSwayY) * swayK;

        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var p = particles[i];
            if (p.MaxLife <= 0.0f) p.MaxLife = p.Life;
            p.Life -= dt;
            p.Position += p.Velocity * dt;
            p.Velocity.Y -= 9.8f * dt * p.GravityScale; // Гравитация для частиц
            if (p.Drag > 0.0f) p.Velocity *= MathF.Max(0.0f, 1.0f - p.Drag * dt);
            p.Size += p.Grow * dt;

            if (p.Bounce && p.Position.Y < 0.04f)
            {
                p.Position.Y = 0.04f;
                if (p.Velocity.Y < 0.0f) p.Velocity.Y = -p.Velocity.Y * 0.35f;
                p.Velocity.X *= 0.6f;
                p.Velocity.Z *= 0.6f;
            }

            if (p.Life <= 0)
            {
                particles.RemoveAt(i);
            }
        }

        for (int i = tracers.Count - 1; i >= 0; i--)
        {
            tracers[i].Life -= dt;
            if (tracers[i].Life <= 0.0f) tracers.RemoveAt(i);
        }

        // Жёсткий потолок частиц: длинные перестрелки не должны просаживать кадры.
        if (particles.Count > ParticleCap)
            particles.RemoveRange(0, particles.Count - ParticleCap);
    }

    // Весь «эффект выстрела» в стиле Bodycam: пружинная отдача камеры, вспышка, дым, гильза, трассер, засветка.
    static void TriggerShotEffects(Ray ray, WeaponInfo weapon)
    {
        Vector3 f = ray.Direction;
        float weaponKick = Math.Clamp(weapon.Recoil, 0.4f, 2.8f);

        recoil = 1.0f;
        muzzleFlash = 1.0f;
        weaponRecoil = 1.0f;
        cameraShake = MathF.Max(cameraShake, Math.Clamp(0.5f * weaponKick, 0.3f, 1.0f));
        flashSeed = rnd.Next(100000);

        // Рывок камеры вверх + случайный увод вбок и крен (потом пружина возвращает назад).
        kickPitchVel += (40.0f + (float)rnd.NextDouble() * 16.0f) * weaponKick;
        kickYawVel += ((float)rnd.NextDouble() - 0.5f) * 30.0f * weaponKick;
        kickRollVel += ((float)rnd.NextDouble() - 0.5f) * 34.0f * weaponKick;

        Vector3 muzzle = ray.Position + f * 0.9f + aimRightDir * 0.28f + new Vector3(0.0f, -0.22f, 0.0f);
        SpawnMuzzleParticles(muzzle, f);

        // Гильза вылетает вправо-вверх и отскакивает от земли.
        particles.Add(new HitParticle
        {
            Position = ray.Position + f * 0.45f + aimRightDir * 0.25f + new Vector3(0.0f, -0.18f, 0.0f),
            Velocity = aimRightDir * (2.2f + (float)rnd.NextDouble() * 1.2f) + new Vector3(0.0f, 2.6f + (float)rnd.NextDouble(), 0.0f) - f * 0.4f,
            Color = Color.Gold,
            Life = 1.3f,
            Size = 0.05f,
            Bounce = true
        });

        // Трассер: до первого зомби или до земли (на экране — только визуал, урон считается отдельно).
        float dist = Math.Min(weapon.Range, 260.0f);
        if (f.Y < -0.0001f)
        {
            float tg = -ray.Position.Y / f.Y;
            if (tg > 0.0f && tg < dist)
            {
                dist = tg;
                SpawnDust(ray.Position + f * tg);
            }
        }
        foreach (var z in zombies)
        {
            RayCollision h = Raylib.GetRayCollisionBox(ray, z.BoundingBox);
            if (h.Hit && h.Distance < dist && h.Distance <= weapon.Range) dist = h.Distance;
        }
        tracers.Add(new BulletTracer { From = muzzle, To = ray.Position + f * dist });
    }

    static void SpawnDust(Vector3 pos)
    {
        int count = settings.ReducedEffects ? 2 : 5;
        for (int i = 0; i < count; i++)
        {
            particles.Add(new HitParticle
            {
                Position = pos + new Vector3(0, 0.05f, 0),
                Velocity = new Vector3((float)(rnd.NextDouble() - 0.5) * 2.5f, 0.8f + (float)rnd.NextDouble() * 1.5f, (float)(rnd.NextDouble() - 0.5) * 2.5f),
                Color = Col(150, 145, 135),
                Life = 0.5f + (float)rnd.NextDouble() * 0.3f,
                Size = 0.14f,
                Grow = 0.5f,
                GravityScale = 0.15f,
                Drag = 2.0f,
                Round = true,
                Alpha = 110
            });
        }
    }

    static void SpawnMuzzleParticles(Vector3 pos, Vector3 forward)
    {
        // Яркие искры (additive)
        int sparks = settings.ReducedEffects ? 4 : 12;
        for (int i = 0; i < sparks; i++)
        {
            Vector3 jitter = new Vector3((float)(rnd.NextDouble() - 0.5) * 2.0f, (float)(rnd.NextDouble() - 0.5) * 2.0f, (float)(rnd.NextDouble() - 0.5) * 2.0f);
            particles.Add(new HitParticle
            {
                Position = pos,
                Velocity = Vector3.Normalize(forward + jitter * 0.35f) * (6.0f + (float)rnd.NextDouble() * 10.0f),
                Color = rnd.Next(0, 2) == 0 ? Color.Gold : Color.Orange,
                Life = 0.10f + (float)rnd.NextDouble() * 0.14f,
                Size = 0.05f + (float)rnd.NextDouble() * 0.07f,
                GravityScale = 0.3f,
                Glow = true,
                Round = true
            });
        }

        // Дым из ствола
        int smoke = settings.ReducedEffects ? 1 : 4;
        for (int i = 0; i < smoke; i++)
        {
            particles.Add(new HitParticle
            {
                Position = pos,
                Velocity = forward * (1.5f + (float)rnd.NextDouble() * 1.5f) + new Vector3((float)(rnd.NextDouble() - 0.5) * 0.6f, 0.5f + (float)rnd.NextDouble() * 0.5f, (float)(rnd.NextDouble() - 0.5) * 0.6f),
                Color = Col(185, 185, 190),
                Life = 0.6f + (float)rnd.NextDouble() * 0.4f,
                Size = 0.10f,
                Grow = 0.7f,
                GravityScale = -0.05f,
                Drag = 1.8f,
                Round = true,
                Alpha = 95
            });
        }
    }

    // ================================================================
    // Оружие от первого лица (процедурное, скруглённое, смотрит в прицел)
    // ================================================================
    static void DrawWeaponViewModel()
    {
        int w = screenWidth, h = screenHeight;
        float k = h / 720.0f * 0.72f;
        WeaponInfo wd = ActiveWeapon;
        float moveAmt = Math.Clamp(new Vector2(localPlayer.Velocity.X, localPlayer.Velocity.Z).Length() / Math.Max(1.0f, localPlayer.Speed), 0.0f, 1.0f);
        float bobX = MathF.Sin(headBobTime * 0.5f) * 10.0f * moveAmt;
        float bobY = MathF.Abs(MathF.Sin(headBobTime)) * 9.0f * moveAmt + MathF.Sin(menuTime * 1.7f) * 1.5f;

        // Перезарядка: ствол опускается и покачивается, смена оружия — короткий «провал» вниз.
        float reloadProgress = weaponReloadTimer > 0.0f ? 1.0f - weaponReloadTimer / MathF.Max(0.01f, wd.ReloadTime) : 0.0f;
        float reloadDip = weaponReloadTimer > 0.0f ? MathF.Sin(reloadProgress * MathF.PI) : 0.0f;
        float switchDip = weaponSwitchAnim;

        // Точка опоры оружия (внизу справа) и угол так, чтобы ствол смотрел в центр экрана.
        float ax = w * 0.80f + weaponSwayX * 18.0f + bobX + weaponRecoil * 20.0f * k;
        float ay = h * 0.93f + bobY + weaponSwayY * 12.0f + weaponRecoil * 22.0f * k + (reloadDip * 120.0f + switchDip * 200.0f) * k;
        float aim = MathF.Atan2(h * 0.53f - ay, w * 0.52f - ax) * Rad2Deg;
        float rot = WrapAngle(aim - 180.0f) + weaponRecoil * 4.5f - weaponSwayX * 0.6f + reloadDip * 16.0f - switchDip * 10.0f;

        Color glove = Col(46, 48, 54), gloveHi = Col(72, 75, 82);
        Color steel = Col(28, 30, 35), steelHi = Col(84, 88, 96), dark = Col(14, 15, 18);
        Color accent = Col(wd.AccentR, wd.AccentG, wd.AccentB);
        Color accentDim = Col(wd.AccentR / 2, wd.AccentG / 2, wd.AccentB / 2);

        Rlgl.PushMatrix();
        Rlgl.Translatef(ax, ay, 0.0f);
        Rlgl.Rotatef(rot, 0.0f, 0.0f, 1.0f);

        // Предплечья (уходят за край экрана)
        RR(-40 * k, 44 * k, 360 * k, 96 * k, 40 * k, Col(36, 38, 44));
        RR(-300 * k, 36 * k, 440 * k, 70 * k, 30 * k, Col(40, 42, 48));

        // Мушка (куда смотрит вспышка) зависит от длины ствола выбранного оружия.
        float muzzleX = -600.0f * k;
        float muzzleY = -12.0f * k;

        switch (wd.Style)
        {
            case WeaponStyle.Pistol:
            {
                RR(0, -22 * k, 110 * k, 44 * k, 14 * k, steel);                    // затвор
                RR(-150 * k, -26 * k, 170 * k, 52 * k, 12 * k, steel);             // корпус
                RR(-140 * k, -24 * k, 150 * k, 8 * k, 4 * k, Col(steelHi.R, steelHi.G, steelHi.B, 140));
                RR(-250 * k, -14 * k, 110 * k, 30 * k, 10 * k, dark);              // ствол
                RR(-120 * k, 18 * k, 44 * k, 120 * k, 14 * k, Col(20, 22, 26));    // магазин
                RR(-70 * k, 14 * k, 42 * k, 96 * k, 16 * k, Col(24, 26, 30));      // рукоять
                RR(-70 * k, 12 * k, 42 * k, 10 * k, 4 * k, accent);                // акцентная накладка
                RR(-250 * k, -34 * k, 18 * k, 12 * k, 3 * k, dark);                // мушка
                RR(6 * k, -28 * k, 16 * k, 8 * k, 3 * k, steel);                   // целик
                muzzleX = -258.0f * k; muzzleY = 0.0f * k;
                break;
            }
            case WeaponStyle.Revolver:
            {
                RR(0, -24 * k, 96 * k, 46 * k, 14 * k, steel);
                RR(-160 * k, -30 * k, 170 * k, 56 * k, 14 * k, steel);
                RR(-150 * k, -28 * k, 150 * k, 8 * k, 4 * k, Col(steelHi.R, steelHi.G, steelHi.B, 140));
                RR(-250 * k, -18 * k, 96 * k, 34 * k, 10 * k, Col(34, 36, 42));    // ствол
                RR(-250 * k, -18 * k, 96 * k, 6 * k, 3 * k, accentDim);            // вент. планка
                FCylView(-118 * k, 8 * k, 30 * k, 34, Col(38, 40, 46));        // барабан
                RR(-66 * k, 16 * k, 46 * k, 104 * k, 18 * k, Col(24, 26, 30));
                RR(-66 * k, 12 * k, 46 * k, 10 * k, 4 * k, accent);
                RR(-252 * k, -32 * k, 16 * k, 12 * k, 3 * k, dark);
                muzzleX = -258.0f * k; muzzleY = -2.0f * k;
                break;
            }
            case WeaponStyle.Smg:
            {
                RR(0, -26 * k, 120 * k, 48 * k, 14 * k, steel);
                RR(-230 * k, -34 * k, 250 * k, 56 * k, 12 * k, steel);
                RR(-220 * k, -32 * k, 230 * k, 8 * k, 4 * k, Col(steelHi.R, steelHi.G, steelHi.B, 140));
                RR(-330 * k, -30 * k, 120 * k, 44 * k, 12 * k, Col(26, 28, 34));   // цевьё
                RR(-440 * k, -18 * k, 130 * k, 22 * k, 6 * k, dark);               // ствол
                RR(-460 * k, -24 * k, 26 * k, 32 * k, 6 * k, Col(10, 10, 12));     // дульный срез
                RR(-150 * k, 20 * k, 44 * k, 150 * k, 10 * k, Col(20, 22, 26));    // длинный магазин
                RR(-146 * k, 26 * k, 8 * k, 130 * k, 3 * k, Col(60, 63, 70, 120));
                RR(-64 * k, 16 * k, 40 * k, 92 * k, 14 * k, Col(22, 24, 28));
                RR(-336 * k, -50 * k, 26 * k, 18 * k, 4 * k, accent);              // акцент на цевье
                RR(-160 * k, -58 * k, 22 * k, 20 * k, 5 * k, steel);               // целик
                muzzleX = -468.0f * k; muzzleY = -8.0f * k;
                break;
            }
            case WeaponStyle.Rifle:
            {
                RR(0, -28 * k, 150 * k, 50 * k, 16 * k, steel);                    // приклад
                RR(-250 * k, -36 * k, 260 * k, 58 * k, 14 * k, steel);             // ресивер
                RR(-240 * k, -34 * k, 240 * k, 8 * k, 4 * k, Col(steelHi.R, steelHi.G, steelHi.B, 150));
                RR(-340 * k, -48 * k, 330 * k, 12 * k, 4 * k, Col(52, 55, 62));    // планка
                RR(-440 * k, -32 * k, 210 * k, 48 * k, 16 * k, Col(24, 26, 31));   // цевьё
                for (int i = 0; i < 5; i++)
                    RR((-420 + i * 38) * k, -22 * k, 22 * k, 8 * k, 3 * k, dark);
                RR(-560 * k, -20 * k, 140 * k, 16 * k, 6 * k, dark);               // ствол
                RR(-596 * k, -25 * k, 42 * k, 26 * k, 6 * k, Col(10, 10, 12));     // дульный тормоз
                RR(-500 * k, -62 * k, 12 * k, 42 * k, 4 * k, steel);               // мушка
                RR(-80 * k, -66 * k, 24 * k, 22 * k, 5 * k, steel);                // целик
                RR(-190 * k, 20 * k, 52 * k, 100 * k, 10 * k, Col(20, 22, 26));    // магазин
                RR(-186 * k, 24 * k, 8 * k, 90 * k, 3 * k, Col(60, 63, 70, 120));
                RR(-70 * k, 16 * k, 40 * k, 86 * k, 14 * k, Col(22, 24, 28));      // рукоять
                RR(-252 * k, -14 * k, 60 * k, 12 * k, 4 * k, accent);              // акцент
                muzzleX = -604.0f * k; muzzleY = -12.0f * k;
                break;
            }
            case WeaponStyle.Shotgun:
            {
                RR(0, -30 * k, 150 * k, 54 * k, 16 * k, Col(58, 40, 30));          // деревянный приклад
                RR(-230 * k, -34 * k, 240 * k, 56 * k, 14 * k, steel);
                RR(-220 * k, -32 * k, 220 * k, 8 * k, 4 * k, Col(steelHi.R, steelHi.G, steelHi.B, 120));
                RR(-420 * k, -24 * k, 200 * k, 32 * k, 8 * k, dark);               // ствол
                RR(-420 * k, 8 * k, 180 * k, 20 * k, 8 * k, Col(30, 32, 38));      // подствольный магазин
                RR(-300 * k, 2 * k, 90 * k, 34 * k, 12 * k, Col(64, 46, 34));      // цевьё-помпа
                for (int i = 0; i < 4; i++)
                    RR((-292 + i * 22) * k, 8 * k, 8 * k, 22 * k, 3 * k, Col(38, 28, 22));
                RR(-600 * k, -30 * k, 34 * k, 44 * k, 8 * k, Col(12, 12, 14));     // срез ствола
                RR(-60 * k, 16 * k, 40 * k, 88 * k, 14 * k, Col(56, 40, 30));
                RR(-236 * k, -50 * k, 20 * k, 18 * k, 4 * k, accent);
                muzzleX = -606.0f * k; muzzleY = -6.0f * k;
                break;
            }
            case WeaponStyle.Sniper:
            {
                RR(0, -26 * k, 170 * k, 52 * k, 16 * k, steel);
                RR(-260 * k, -34 * k, 280 * k, 54 * k, 12 * k, steel);
                RR(-250 * k, -32 * k, 260 * k, 8 * k, 4 * k, Col(steelHi.R, steelHi.G, steelHi.B, 130));
                RR(-560 * k, -16 * k, 320 * k, 14 * k, 5 * k, dark);               // длинный ствол
                RR(-650 * k, -22 * k, 50 * k, 26 * k, 6 * k, Col(10, 10, 12));     // дульный тормоз
                RR(-300 * k, -96 * k, 210 * k, 34 * k, 14 * k, Col(18, 20, 24));   // оптика
                FCylView(-330 * k, -78 * k, 26 * k, 30, Col(40, 60, 90));      // линза
                RR(-250 * k, -66 * k, 14 * k, 24 * k, 4 * k, steel);               // крепление
                RR(-260 * k, 18 * k, 50 * k, 96 * k, 10 * k, Col(20, 22, 26));     // магазин
                RR(-70 * k, 14 * k, 44 * k, 92 * k, 16 * k, Col(22, 24, 28));
                RR(-420 * k, 6 * k, 70 * k, 12 * k, 4 * k, accentDim);             // сошки/акцент
                muzzleX = -656.0f * k; muzzleY = -8.0f * k;
                break;
            }
            case WeaponStyle.Lmg:
            {
                RR(0, -30 * k, 160 * k, 56 * k, 16 * k, steel);
                RR(-280 * k, -40 * k, 300 * k, 66 * k, 14 * k, steel);
                RR(-270 * k, -38 * k, 280 * k, 8 * k, 4 * k, Col(steelHi.R, steelHi.G, steelHi.B, 150));
                RR(-470 * k, -32 * k, 210 * k, 52 * k, 14 * k, Col(24, 26, 31));   // цевьё
                RR(-600 * k, -20 * k, 150 * k, 22 * k, 6 * k, dark);               // ствол
                RR(-520 * k, -14 * k, 10 * k, 12 * k, 3 * k, accent);              // кожух
                FCylView(-230 * k, 34 * k, 54 * k, 46, Col(26, 28, 34));       // барабанный магазин
                RR(-70 * k, 16 * k, 44 * k, 92 * k, 14 * k, Col(22, 24, 28));
                RR(-360 * k, -22 * k, 90 * k, 12 * k, 4 * k, accentDim);
                RR(-120 * k, -70 * k, 26 * k, 22 * k, 5 * k, steel);
                muzzleX = -606.0f * k; muzzleY = -8.0f * k;
                break;
            }
            case WeaponStyle.Plasma:
            {
                RR(0, -28 * k, 140 * k, 54 * k, 16 * k, Col(32, 40, 48));
                RR(-240 * k, -38 * k, 250 * k, 62 * k, 14 * k, Col(28, 34, 42));
                RR(-330 * k, -28 * k, 120 * k, 46 * k, 12 * k, Col(22, 28, 36));   // корпус
                RR(-420 * k, -18 * k, 110 * k, 30 * k, 10 * k, Col(16, 22, 30));   // эмиттер
                for (int i = 0; i < 4; i++)
                    RR((-250 - i * 34) * k, -26 * k, 14 * k, 34 * k, 6 * k, accentDim); // катушки
                RR(-120 * k, -8 * k, 74 * k, 26 * k, 8 * k, accent);               // энергоядро
                RR(-60 * k, 16 * k, 42 * k, 92 * k, 14 * k, Col(22, 26, 32));
                RR(-150 * k, 20 * k, 46 * k, 96 * k, 10 * k, Col(20, 24, 30));
                RR(-424 * k, -30 * k, 18 * k, 20 * k, 5 * k, accent);
                muzzleX = -430.0f * k; muzzleY = -4.0f * k;
                break;
            }
            case WeaponStyle.Launcher:
            {
                RR(0, -32 * k, 150 * k, 56 * k, 16 * k, steel);
                RR(-300 * k, -46 * k, 330 * k, 78 * k, 22 * k, Col(30, 34, 40));   // толстая труба
                RR(-300 * k, -40 * k, 320 * k, 10 * k, 5 * k, Col(steelHi.R, steelHi.G, steelHi.B, 110));
                RR(-620 * k, -54 * k, 70 * k, 94 * k, 22 * k, Col(16, 18, 22));    // раструб
                RR(-620 * k, -40 * k, 70 * k, 12 * k, 5 * k, accent);              // акцентная полоса
                RR(-210 * k, 26 * k, 56 * k, 64 * k, 12 * k, Col(20, 22, 26));     // рукоять/спуск
                RR(-70 * k, 18 * k, 44 * k, 92 * k, 16 * k, Col(22, 24, 28));
                RR(-420 * k, -72 * k, 26 * k, 24 * k, 5 * k, accent);              // прицел
                muzzleX = -628.0f * k; muzzleY = 0.0f * k;
                break;
            }
        }

        // Руки в перчатках
        RR(-410 * k, 8 * k, 130 * k, 64 * k, 28 * k, glove);                  // левая под цевьём
        for (int i = 0; i < 4; i++)
            RR((-405 + i * 30) * k, -14 * k, 22 * k, 34 * k, 10 * k, gloveHi);
        RR(-100 * k, 38 * k, 124 * k, 92 * k, 34 * k, glove);                 // правая на рукояти
        for (int i = 0; i < 3; i++)
            RR(-104 * k, (26 + i * 20) * k, 74 * k, 16 * k, 8 * k, gloveHi);

        // Вспышка выстрела (additive)
        if (muzzleFlash > 0.02f)
        {
            float m = muzzleFlash;
            Vector2 tip = new Vector2(muzzleX, muzzleY);
            Color flashCol = wd.Style == WeaponStyle.Plasma ? Col(150, 230, 255, (int)(150 * m)) : Col(255, 170, 60, (int)(150 * m));
            Color flashCore = wd.Style == WeaponStyle.Plasma ? Col(220, 245, 255, (int)(230 * m)) : Col(255, 245, 200, (int)(230 * m));
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawCircleGradient((int)tip.X, (int)tip.Y, (150.0f + 90.0f * m) * k, flashCol, Col(255, 110, 20, 0));
            Raylib.DrawCircleGradient((int)tip.X, (int)tip.Y, (60.0f + 40.0f * m) * k, flashCore, Col(255, 200, 90, 0));
            var fr = new Random(flashSeed);
            for (int i = 0; i < 9; i++)
            {
                float ang = (float)(Math.PI + (fr.NextDouble() - 0.5) * 1.5);
                float len = (60.0f + (float)fr.NextDouble() * 150.0f) * m * k + 20.0f * k;
                Vector2 d = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                Raylib.DrawLineEx(tip, tip + d * len, (3.0f + (float)fr.NextDouble() * 4.0f) * k, Col(255, 205, 100, (int)(200 * m)));
            }
            Raylib.EndBlendMode();
        }

        Rlgl.PopMatrix();
    }

    // Запасной вариант (если шейдер недоступен или включены «упрощённые эффекты»).
    static void DrawCameraEffects(bool shaderActive)
    {
        if (shaderActive) return; // всё это делает пост-шейдер

        int w = screenWidth, h = screenHeight;
        int edge = Math.Max(30, (int)(Math.Min(w, h) * 0.08f));
        Raylib.DrawRectangle(0, 0, w, edge, new Color(0, 0, 0, 34));
        Raylib.DrawRectangle(0, h - edge, w, edge, new Color(0, 0, 0, 48));
        Raylib.DrawRectangle(0, 0, edge, h, new Color(0, 0, 0, 30));
        Raylib.DrawRectangle(w - edge, 0, edge, h, new Color(0, 0, 0, 30));

        if (!settings.ReducedEffects)
        {
            for (int y = 0; y < h; y += 6)
                Raylib.DrawLine(0, y, w, y, new Color(255, 255, 255, 5));
        }

        if (damageFlash > 0.0f)
            Raylib.DrawRectangle(0, 0, w, h, new Color(180, 25, 20, (int)(damageFlash * 70.0f)));
    }

    // ================================================================
    // Пост-обработка «камера на теле»: рыбий глаз, хроматика, зерно, виньетка, засветка при выстреле
    // ================================================================
    const string PostVertexSrc = @"#version 330
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;
out vec2 fragTexCoord;
out vec4 fragColor;
uniform mat4 mvp;
void main()
{
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
";

    const string PostFragmentSrc = @"#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
uniform sampler2D texture0;
uniform float time;
uniform vec2 resolution;
uniform float kick;
uniform float damage;
out vec4 finalColor;

float hash(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

void main()
{
    vec2 uv = fragTexCoord;
    vec2 d = uv - 0.5;
    float aspect = resolution.x / resolution.y;
    vec2 da = d * vec2(aspect, 1.0);
    float r2 = dot(da, da);

    // Бочкообразное искажение (широкоугольный объектив bodycam)
    vec2 duv = d * (1.0 + 0.20 * r2) * 0.86;

    // Лёгкий горизонтальный «срыв строк» при выстреле
    float row = floor(uv.y * resolution.y / 3.0);
    duv.x += (hash(vec2(row, floor(time * 60.0))) - 0.5) * kick * 0.006;

    // Хроматическая аберрация (сильнее к краям и при выстреле)
    float ca = 0.0016 + 0.006 * r2 + kick * 0.004;
    vec3 col;
    col.r = texture(texture0, 0.5 + duv * (1.0 + ca)).r;
    col.g = texture(texture0, 0.5 + duv).g;
    col.b = texture(texture0, 0.5 + duv * (1.0 - ca)).b;

    // Дешёвое свечение ярких областей
    vec2 px = 3.0 / resolution;
    vec3 bloom = (texture(texture0, 0.5 + duv + vec2(px.x, 0.0)).rgb
                + texture(texture0, 0.5 + duv - vec2(px.x, 0.0)).rgb
                + texture(texture0, 0.5 + duv + vec2(0.0, px.y)).rgb
                + texture(texture0, 0.5 + duv - vec2(0.0, px.y)).rgb) * 0.25;
    col += max(bloom - 0.72, 0.0) * 0.6;

    // Цветокоррекция: слегка выцветшая, прохладная картинка
    float luma = dot(col, vec3(0.299, 0.587, 0.114));
    col = mix(vec3(luma), col, 0.88 - damage * 0.4);
    col = (col - 0.5) * 1.08 + 0.5;
    col *= vec3(0.97, 1.0, 1.03);
    col = mix(col, col * vec3(1.4, 0.55, 0.5), damage * 0.5);
    col += kick * 0.10 * vec3(1.0, 0.9, 0.7);

    // Плёночное зерно, строки, мерцание
    col += (hash(uv * resolution + fract(time) * 100.0) - 0.5) * 0.07;
    col *= 0.97 + 0.03 * sin(uv.y * resolution.y * 3.14159);
    col *= 0.985 + 0.015 * sin(time * 60.0);

    // Виньетка
    float vig = 1.0 - smoothstep(0.35, 1.15, sqrt(r2));
    col *= mix(0.55, 1.0, vig);

    // За краем искажённого кадра — чёрный
    vec2 sp = 0.5 + duv;
    if (sp.x < 0.0 || sp.x > 1.0 || sp.y < 0.0 || sp.y > 1.0) col = vec3(0.0);

    finalColor = vec4(col, 1.0);
}
";

    static void InitPostProcess()
    {
        try
        {
            postShader = Raylib.LoadShaderFromMemory(PostVertexSrc, PostFragmentSrc);
            locTime = Raylib.GetShaderLocation(postShader, "time");
            locRes = Raylib.GetShaderLocation(postShader, "resolution");
            locKick = Raylib.GetShaderLocation(postShader, "kick");
            locDamage = Raylib.GetShaderLocation(postShader, "damage");
            // Если шейдер не скомпилировался, raylib подставляет стандартный — тогда uniform'ов не будет.
            postShaderReady = locTime >= 0 && locRes >= 0 && locKick >= 0 && locDamage >= 0;
        }
        catch
        {
            postShaderReady = false;
        }
    }

    static void ShutdownPostProcess()
    {
        if (sceneW > 0) Raylib.UnloadRenderTexture(sceneTarget);
        if (postShaderReady) Raylib.UnloadShader(postShader);
        sceneW = 0;
        postShaderReady = false;
    }

    static void EnsureSceneTarget()
    {
        if (sceneW == screenWidth && sceneH == screenHeight) return;
        if (sceneW > 0) Raylib.UnloadRenderTexture(sceneTarget);
        sceneW = screenWidth;
        sceneH = screenHeight;
        sceneTarget = Raylib.LoadRenderTexture(sceneW, sceneH);
        Raylib.SetTextureFilter(sceneTarget.Texture, TextureFilter.Bilinear);
    }

    static void DrawGameScene()
    {
        bool usePost = postShaderReady && !settings.ReducedEffects && screenWidth > 0 && screenHeight > 0;
        if (!usePost)
        {
            DrawWorld3D();
            if (localPlayer.Inventory.Count == 0) DrawWeaponViewModel();
            DrawCameraEffects(false);
            return;
        }

        EnsureSceneTarget();

        Raylib.BeginTextureMode(sceneTarget);
        DrawWorld3D();
        if (localPlayer.Inventory.Count == 0) DrawWeaponViewModel();
        Raylib.EndTextureMode();

        Raylib.SetShaderValue(postShader, locTime, (float)(Raylib.GetTime() % 1000.0), ShaderUniformDataType.Float);
        Raylib.SetShaderValue(postShader, locRes, new Vector2(sceneW, sceneH), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(postShader, locKick, muzzleFlash, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(postShader, locDamage, damageFlash, ShaderUniformDataType.Float);

        Raylib.BeginShaderMode(postShader);
        Raylib.DrawTextureRec(sceneTarget.Texture, new Rectangle(0, 0, sceneW, -sceneH), Vector2.Zero, Color.White);
        Raylib.EndShaderMode();
    }

    // Оверлей камеры: REC, время, батарея (рисуется поверх картинки, не искажается).
    static void DrawRecOverlay()
    {
        int w = screenWidth, h = screenHeight;
        bool blink = ((int)(recTime * 1.5f)) % 2 == 0;
        int t = (int)recTime;
        string rec = $"REC {t / 3600:00}:{(t / 60) % 60:00}:{t % 60:00}";

        int px = 20, py = settings.ShowHints ? 48 : 20;
        RR(px, py, 158, 28, 14, new Color(0, 0, 0, 120));
        Raylib.DrawCircle(px + 16, py + 14, 5, blink ? new Color(235, 40, 40, 255) : new Color(110, 25, 25, 255));
        DrawTextCustom(rec, px + 30, py + 6, 15, Color.White);

        string stamp = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
        Vector2 sz = Raylib.MeasureTextEx(customFont, stamp, 14, 1);
        RR(w - sz.X - 44, 44, sz.X + 24, 26, 13, new Color(0, 0, 0, 120));
        DrawTextCustom(stamp, (int)(w - sz.X - 32), 49, 14, Color.White);

        // Батарея
        RRL(w - 70, h - 42, 40, 20, 5, 2, new Color(255, 255, 255, 190));
        RR(w - 30, h - 36, 4, 8, 2, new Color(255, 255, 255, 190));
        RR(w - 67, h - 39, 30, 14, 3, new Color(110, 220, 130, 220));

        if (freeLook)
            DrawTextCentered("СВОБОДНЫЙ ОБЗОР", h - 120, 16, new Color(255, 200, 90, 230));
    }

    // Удерживает предметы над землёй и даёт небольшой физический отскок.
    static void ResolveGroundCollision(PhysicalObject3D obj)
    {
        float halfY = obj.Size.Y * 0.5f;
        float minY = GroundY + halfY;

        if (obj.Position.Y < minY)
        {
            obj.Position.Y = minY;

            if (obj.Velocity.Y < 0.0f)
            {
                obj.Velocity.Y = -obj.Velocity.Y * ObjectBounce;

                // Слабый удар почти сразу превращаем в покой, чтобы предметы
                // не дрожали бесконечно на полу.
                if (MathF.Abs(obj.Velocity.Y) < PhysicsRestVelocity)
                    obj.Velocity.Y = 0.0f;
            }
        }
        else if (obj.Position.Y <= minY + 0.002f && obj.Velocity.Y < 0.0f &&
                 MathF.Abs(obj.Velocity.Y) < PhysicsRestVelocity)
        {
            obj.Position.Y = minY;
            obj.Velocity.Y = 0.0f;
        }
    }

    // Простое AABB-столкновение динамических предметов.
    // Если предметы пересеклись, раздвигаем их по минимальной оси проникновения.
    static void ResolveObjectCollisions(PhysicalObject3D obj)
    {
        foreach (var other in worldObjects)
        {
            if (ReferenceEquals(obj, other) || other.IsHeld) continue;

            // Дешёвый отсев по XZ до расчёта боксов: разница в скорости несопоставима с ценой
            // 40 000 проверок AABB на кадр.
            float dx = obj.Position.X - other.Position.X;
            float dz = obj.Position.Z - other.Position.Z;
            float reachX = (obj.Size.X + other.Size.X) * 0.5f + 0.06f;
            float reachZ = (obj.Size.Z + other.Size.Z) * 0.5f + 0.06f;
            if (MathF.Abs(dx) > reachX || MathF.Abs(dz) > reachZ) continue;
            if (MathF.Abs(obj.Position.Y - other.Position.Y) > (obj.Size.Y + other.Size.Y) * 0.5f + 0.06f) continue;

            BoundingBox a = obj.BoundingBox;
            BoundingBox b = other.BoundingBox;

            if (!Raylib.CheckCollisionBoxes(a, b)) continue;

            float overlapX = MathF.Min(a.Max.X, b.Max.X) - MathF.Max(a.Min.X, b.Min.X);
            float overlapY = MathF.Min(a.Max.Y, b.Max.Y) - MathF.Max(a.Min.Y, b.Min.Y);
            float overlapZ = MathF.Min(a.Max.Z, b.Max.Z) - MathF.Max(a.Min.Z, b.Min.Z);

            if (overlapX <= 0 || overlapY <= 0 || overlapZ <= 0) continue;

            // Для предметов, стоящих друг на друге, в первую очередь решаем Y.
            if (overlapY <= overlapX && overlapY <= overlapZ)
            {
                float objCenter = obj.Position.Y;
                float otherCenter = other.Position.Y;
                float correction = overlapY + 0.001f;

                if (objCenter >= otherCenter)
                {
                    obj.Position.Y += correction;
                    if (obj.Velocity.Y < 0) obj.Velocity.Y = -obj.Velocity.Y * ObjectBounce;
                }
                else
                {
                    obj.Position.Y -= correction;
                    if (obj.Velocity.Y > 0) obj.Velocity.Y = -obj.Velocity.Y * ObjectBounce;
                }

                ResolveGroundCollision(obj);
            }
            else if (overlapX <= overlapZ)
            {
                float direction = obj.Position.X >= other.Position.X ? 1.0f : -1.0f;
                obj.Position.X += overlapX * direction;
                obj.Velocity.X *= -0.25f;
            }
            else
            {
                float direction = obj.Position.Z >= other.Position.Z ? 1.0f : -1.0f;
                obj.Position.Z += overlapZ * direction;
                obj.Velocity.Z *= -0.25f;
            }
        }
    }

    // ================================================================
    // Туман / затенение: всё, что далеко, плавно уходит в дымку
    // ================================================================
    static readonly Color FogColor = new Color(158, 184, 208, 255);
    static readonly Color SkyTop = new Color(66, 124, 200, 255);

    static float FogAmount(Vector3 p)
    {
        // Дальность тумана растянута под карту 400×400, иначе полигон упирался бы в «стену».
        float d = Vector3.Distance(camera.Position, p);
        float t = Math.Clamp((d - 70.0f) / 250.0f, 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    static Color Fogged(Color c, Vector3 p) => LerpColor(c, FogColor, FogAmount(p));

    static void FCube(Vector3 p, float w, float h, float l, Color c) => Raylib.DrawCube(p, w, h, l, Fogged(c, p));
    static void FWires(Vector3 p, float w, float h, float l, Color c) => Raylib.DrawCubeWires(p, w, h, l, Fogged(c, p));
    static void FSphere(Vector3 p, float r, Color c) => Raylib.DrawSphere(p, r, Fogged(c, p));
    static void FCyl(Vector3 p, float rt, float rb, float h, int slices, Color c) => Raylib.DrawCylinder(p, rt, rb, h, slices, Fogged(c, p));
    static void FCapsule(Vector3 a, Vector3 b, float r, Color c) => Raylib.DrawCapsule(a, b, r, 10, 5, Fogged(c, (a + b) * 0.5f));

    // Куб с «освещённой» верхней гранью — выглядит объёмнее, чем плоский цвет.
    static void LitCube(Vector3 p, Vector3 size, Color c)
    {
        FCube(p, size.X, size.Y, size.Z, Tint(c, 0.88f));
        FCube(p + new Vector3(0, size.Y * 0.5f - 0.012f, 0), size.X * 0.996f, 0.026f, size.Z * 0.996f, Tint(c, 1.22f));
    }

    static void DrawBlobShadow(float x, float z, float radius, float height)
    {
        float k = 1.0f / (1.0f + MathF.Max(0.0f, height) * 0.35f);
        float fog = 1.0f - FogAmount(new Vector3(x, 0, z));
        int alpha = (int)(95 * k * fog);
        if (alpha < 3) return;
        Raylib.DrawCylinder(new Vector3(x, 0.105f, z), radius * k, radius * k, 0.01f, 14, Col(0, 0, 0, alpha));
    }

    static void DrawBuildingDetail(Vector3 center, Vector3 size, Color body, Color trim)
    {
        FCube(center, size.X, size.Y, size.Z, body);
        FWires(center, size.X + 0.08f, size.Y + 0.08f, size.Z + 0.08f, trim);

        int panels = Math.Max(2, (int)(size.X / 4.0f));
        float panelStep = size.X / panels;
        for (int i = 0; i < panels; i++)
        {
            float px = center.X - size.X * 0.5f + panelStep * (i + 0.5f);
            FCube(new Vector3(px, center.Y, center.Z - size.Z * 0.505f), 0.045f, size.Y * 0.9f, 0.05f, trim);
        }

        int rows = Math.Max(1, (int)(size.Y / 3.2f));
        for (int row = 0; row < rows; row++)
        {
            float py = center.Y - size.Y * 0.5f + 1.3f + row * 3.0f;
            if (py > center.Y + size.Y * 0.45f) break;
            for (int col = 0; col < panels; col++)
            {
                float px = center.X - size.X * 0.5f + 1.1f + col * panelStep;
                if (px > center.X + size.X * 0.45f) break;
                FCube(new Vector3(px, py, center.Z - size.Z * 0.515f), 0.9f, 0.75f, 0.035f, new Color(75, 125, 145, 210));
                FWires(new Vector3(px, py, center.Z - size.Z * 0.54f), 1.0f, 0.85f, 0.04f, new Color(30, 38, 45, 220));
            }
        }
    }

    static void DrawStreetLight(Vector3 pos, float height)
    {
        FCyl(pos + new Vector3(0, height * 0.5f, 0), 0.10f, 0.10f, height, 8, new Color(45, 48, 52, 255));
        FCube(pos + new Vector3(0.55f, height - 0.15f, 0), 1.2f, 0.10f, 0.10f, new Color(45, 48, 52, 255));
        FCube(pos + new Vector3(1.05f, height - 0.18f, 0), 0.65f, 0.16f, 0.42f, new Color(65, 70, 75, 255));
        FSphere(pos + new Vector3(1.05f, height - 0.29f, 0), 0.16f, new Color(255, 205, 95, 220));
    }

    static void DrawContainer(Vector3 pos, Color body)
    {
        FCube(pos, 7.5f, 3.2f, 3.0f, body);
        FWires(pos, 7.65f, 3.35f, 3.15f, new Color(25, 29, 33, 255));
        for (int i = -3; i <= 3; i++)
        {
            float x = pos.X + i * 0.9f;
            FCube(new Vector3(x, pos.Y, pos.Z - 1.53f), 0.055f, 3.0f, 0.04f, new Color(35, 40, 44, 180));
        }
        FCube(new Vector3(pos.X, pos.Y + 0.55f, pos.Z - 1.56f), 5.8f, 0.06f, 0.04f, new Color(120, 125, 128, 150));
    }

    static void DrawWorld3D()
    {
        Raylib.ClearBackground(FogColor);

        // Небо: градиент до линии горизонта (горизонт смещается при наклоне головы).
        {
            float halfH = screenHeight * 0.5f;
            float tanHalf = MathF.Tan(camera.FovY * 0.5f * Deg2Rad);
            float hz = halfH + halfH / MathF.Max(0.1f, tanHalf) * MathF.Tan(viewPitch * Deg2Rad);
            int hzi = (int)Math.Clamp(hz, 0.0f, (float)screenHeight);
            if (hzi > 0) Raylib.DrawRectangleGradientV(0, 0, screenWidth, hzi, SkyTop, FogColor);
        }

        Raylib.BeginMode3D(camera);

        // Солнце с мягким ореолом.
        Raylib.DrawSphere(new Vector3(-150, 230, -180), 42.0f, Col(255, 235, 165, 55));
        Raylib.DrawSphere(new Vector3(-150, 230, -180), 24.0f, new Color(255, 240, 180, 255));
        Raylib.DrawSphere(new Vector3(130, 160, 150), 12.0f, new Color(210, 225, 245, 210));

        // Горы по кругу (уходят в дымку) — кольцо вынесено за границу увеличенной карты.
        for (int m = 0; m < 32; m++)
        {
            float ang = m * (MathF.PI * 2.0f / 32.0f);
            float rad = 300.0f + (m % 3) * 22.0f;
            float height = 34.0f + (m % 5) * 12.0f;
            float baseR = 26.0f + (m % 4) * 6.0f;
            Vector3 bp = new Vector3(MathF.Cos(ang) * rad, 0, MathF.Sin(ang) * rad);
            FCyl(bp + new Vector3(0, height * 0.5f - 1.0f, 0), 0.8f, baseR, height, 7, new Color(64, 78, 95, 255));
            FCyl(bp + new Vector3(0, height * 0.82f - 1.0f, 0), 0.3f, baseR * 0.30f, height * 0.36f, 7, new Color(205, 214, 224, 255));
        }

        // Земля: шахматные плитки 400×400 + дальняя плоскость до горизонта.
        Raylib.DrawPlane(new Vector3(0, -0.03f, 0), new Vector2(3600, 3600), FogColor);
        for (int ix = -MapTiles / 2; ix < MapTiles / 2; ix++)
        {
            for (int iz = -MapTiles / 2; iz < MapTiles / 2; iz++)
            {
                Vector3 c = new Vector3(ix * MapTile + MapTile * 0.5f, 0, iz * MapTile + MapTile * 0.5f);

                // Плитки дальше 240 м полностью в тумане — не тратим на них вызовы отрисовки.
                float tdx = c.X - camera.Position.X;
                float tdz = c.Z - camera.Position.Z;
                if (tdx * tdx + tdz * tdz > 57600.0f) continue;

                bool alt = ((ix + iz) & 1) == 0;
                Raylib.DrawPlane(c, new Vector2(MapTile, MapTile), Fogged(alt ? new Color(82, 88, 86, 255) : new Color(74, 80, 79, 255), c));
            }
        }

        // Магистрали крестом через весь полигон.
        for (int r = -MapTiles / 2; r < MapTiles / 2; r++)
        {
            float seg = r * MapTile + MapTile * 0.5f;
            FCube(new Vector3(0, 0.03f, seg), 16, 0.05f, MapTile + 0.02f, new Color(43, 46, 48, 255));
            FCube(new Vector3(seg, 0.04f, 0), MapTile + 0.02f, 0.05f, 16, new Color(43, 46, 48, 255));
        }
        for (int z = -190; z <= 190; z += 12)
            FCube(new Vector3(0, 0.08f, z), 0.45f, 0.03f, 6.0f, new Color(220, 210, 165, 220));
        for (int x = -190; x <= 190; x += 12)
            FCube(new Vector3(x, 0.08f, 0), 6.0f, 0.03f, 0.45f, new Color(220, 210, 165, 220));

        // Промышленная линия по краям карты: вышки, прожекторы, энергощиты.
        for (int i = -8; i <= 8; i++)
        {
            float z = i * 22.0f;
            FCube(new Vector3(-186, 6.0f, z), 5.0f, 12.0f, 5.0f, new Color(38, 45, 55, 255));
            FWires(new Vector3(-186, 6.0f, z), 5.1f, 12.1f, 5.1f, new Color(110, 120, 130, 180));
            FSphere(new Vector3(-186, 12.7f, z), 0.55f, Color.Orange);
            FCube(new Vector3(186, 8.0f, z + 7), 6.0f, 16.0f, 6.0f, new Color(35, 42, 51, 255));
            FWires(new Vector3(186, 8.0f, z + 7), 6.1f, 16.1f, 6.1f, new Color(90, 170, 235, 170));
            FSphere(new Vector3(186, 16.8f, z + 7), 0.6f, new Color(90, 190, 255, 255));
        }

        // Склады и ангары: два ряда вдоль дальних краёв.
        for (int i = -4; i <= 4; i++)
        {
            float x = i * 48.0f;
            DrawBuildingDetail(new Vector3(x, 6.0f, -178), new Vector3(36, 12, 14), new Color(55, 58, 62, 255), new Color(115, 120, 125, 255));
            FCube(new Vector3(x, 10.0f, -198), 26.0f, 0.7f, 2.4f, new Color(80, 85, 90, 255));
            FCube(new Vector3(x, 2.8f, -199.0f), 13.0f, 0.12f, 0.08f, new Color(210, 190, 120, 170));
            FCyl(new Vector3(x + 12.0f, 10.5f, -178), 1.2f, 0.9f, 8.0f, 12, new Color(70, 74, 78, 255));

            float x2 = i * 44.0f + 20.0f;
            DrawBuildingDetail(new Vector3(x2, 5.0f, 178), new Vector3(30, 10, 12), new Color(52, 56, 61, 255), new Color(105, 112, 120, 255));
            FCube(new Vector3(x2, 8.6f, 190), 20.0f, 0.6f, 2.0f, new Color(76, 82, 88, 255));
        }

        // Городские кварталы: плотная застройка даёт укрытия и ориентиры.
        for (int block = 0; block < 2; block++)
        {
            float bcx = block == 0 ? -120.0f : 118.0f;
            float bcz = block == 0 ? -110.0f : 96.0f;
            for (int gx = -1; gx <= 1; gx++)
            {
                for (int gz = -1; gz <= 1; gz++)
                {
                    if (block == 0 && gx == 1 && gz == 1) continue; // площадь
                    float x = bcx + gx * 30.0f;
                    float z = bcz + gz * 30.0f;
                    float h = 14.0f + ((gx + gz + 4) % 3) * 6.0f;
                    DrawBuildingDetail(new Vector3(x, h * 0.5f, z), new Vector3(18, h, 18),
                        new Color(50, 55, 62, 255), new Color(96, 104, 112, 255));
                    FCube(new Vector3(x, h + 0.5f, z), 19.0f, 0.8f, 19.0f, new Color(64, 70, 78, 255));
                    for (int wy = 2; wy < (int)h - 2; wy += 4)
                        FCube(new Vector3(x - 9.2f, wy, z), 0.12f, 1.6f, 11.0f, new Color(150, 190, 220, 170));
                }
            }
        }

        // Центральный мост с фермами и поперечными балками (теперь шире и длиннее).
        FCube(new Vector3(0, 12.0f, 96), 150.0f, 1.5f, 5.0f, new Color(55, 60, 66, 255));
        for (int x = -72; x <= 72; x += 12)
        {
            FCube(new Vector3(x, 6.0f, 96), 1.0f, 12.0f, 1.0f, new Color(70, 75, 80, 255));
            FCube(new Vector3(x, 12.9f, 94.0f), 1.2f, 1.0f, 8.0f, new Color(78, 83, 88, 255));
        }
        for (int x = -72; x < 72; x += 12)
            FCube(new Vector3(x + 6, 9.0f, 96), 0.45f, 6.0f, 0.45f, new Color(90, 95, 100, 255));

        // Контейнерные дворы по четырём квадрантам карты.
        DrawContainer(new Vector3(-92, 1.7f, -88), new Color(58, 92, 105, 255));
        DrawContainer(new Vector3(-82, 1.7f, -88), new Color(102, 73, 54, 255));
        DrawContainer(new Vector3(-87, 4.9f, -88), new Color(72, 82, 89, 255));
        DrawContainer(new Vector3(96, 1.7f, -74), new Color(78, 98, 67, 255));
        DrawContainer(new Vector3(106, 1.7f, -74), new Color(91, 70, 62, 255));
        DrawContainer(new Vector3(-118, 1.7f, 104), new Color(74, 96, 92, 255));
        DrawContainer(new Vector3(-108, 1.7f, 104), new Color(96, 84, 62, 255));
        DrawContainer(new Vector3(-113, 4.9f, 104), new Color(66, 76, 86, 255));
        DrawContainer(new Vector3(120, 1.7f, 132), new Color(62, 88, 100, 255));
        DrawContainer(new Vector3(130, 1.7f, 132), new Color(92, 74, 58, 255));

        // Опоры, прожекторы и уличные фонари добавляют масштаб.
        for (int lx = -3; lx <= 3; lx++)
        {
            for (int lz = -2; lz <= 2; lz++)
            {
                if (lx == 0 && lz == 0) continue;
                if (((lx + lz) & 1) != 0) continue;
                DrawStreetLight(new Vector3(lx * 46.0f, 0, lz * 52.0f), 9.5f);
            }
        }
        FCube(new Vector3(0, 7.0f, -96), 2.0f, 14.0f, 2.0f, new Color(42, 46, 50, 255));
        FCube(new Vector3(0, 13.5f, -96), 8.0f, 0.7f, 2.0f, new Color(60, 64, 68, 255));
        for (int x = -2; x <= 2; x++)
            FSphere(new Vector3(x * 1.35f, 13.0f, -97.1f), 0.28f, new Color(255, 214, 115, 220));

        // Предметы: тени, корпуса и вайрфрейм рисуем по дистанции.
        // Дальние объекты полностью съедены туманом, поэтому их можно не отправлять в GPU.
        foreach (var obj in worldObjects)
        {
            float dx = obj.Position.X - camera.Position.X;
            float dz = obj.Position.Z - camera.Position.Z;
            float distSq = dx * dx + dz * dz;

            if (distSq > 52900.0f) continue; // > 230 м — за пределами видимости

            if (distSq < 4900.0f)
                DrawBlobShadow(obj.Position.X, obj.Position.Z, obj.Size.X * 0.78f, obj.Position.Y - obj.Size.Y * 0.5f);

            if (obj.Type == EntityType.GravityGun)
            {
                float pulse = 1.0f + MathF.Sin(menuTime * 4.0f + obj.NetId) * 0.10f;
                LitCube(obj.Position, obj.Size, Color.Gold);
                if (distSq < 9025.0f) // вайрфрейм только вблизи
                    FWires(obj.Position, obj.Size.X * pulse, obj.Size.Y * pulse, obj.Size.Z * pulse, Color.Orange);
            }
            else
            {
                LitCube(obj.Position, obj.Size, obj.Color);
                if (distSq < 9025.0f)
                    FWires(obj.Position, obj.Size.X, obj.Size.Y, obj.Size.Z, Color.DarkGray);
            }
        }

        // Zombies: округлое тело из капсул, руки вперёд, шаткая походка.
        foreach (var z in zombies)
        {
            float zdx = z.Position.X - camera.Position.X;
            float zdz = z.Position.Z - camera.Position.Z;
            if (zdx * zdx + zdz * zdz > 57600.0f) continue; // дальше 240 м не рисуем

            float hpRatio = Math.Clamp((float)z.Health / Math.Max(1, z.MaxHealth), 0.0f, 1.0f);
            Color skin = LerpColor(new Color(128, 150, 58, 255), new Color(45, 140, 65, 255), hpRatio);
            Color cloth = new Color(52, 62, 84, 255);
            float rad = z.Yaw * Deg2Rad;
            Vector3 fwdZ = new Vector3(MathF.Sin(rad), 0, MathF.Cos(rad));
            Vector3 rgtZ = new Vector3(MathF.Cos(rad), 0, -MathF.Sin(rad));
            float walk = MathF.Sin(menuTime * 7.0f + z.NetId * 1.7f);

            DrawBlobShadow(z.Position.X, z.Position.Z, 0.8f, 0.0f);

            // Ноги
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 hip = z.Position + rgtZ * (0.23f * side) + new Vector3(0, 1.0f, 0);
                Vector3 foot = z.Position + rgtZ * (0.23f * side) + fwdZ * (walk * side * 0.28f) + new Vector3(0, 0.2f, 0);
                FCapsule(foot, hip, 0.19f, cloth);
            }
            // Тело
            FCapsule(z.Position + new Vector3(0, 1.05f, 0), z.Position + new Vector3(0, 1.6f, 0), 0.40f, Tint(skin, 0.85f));
            // Руки вперёд
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 shoulder = z.Position + rgtZ * (0.5f * side) + new Vector3(0, 1.75f, 0);
                Vector3 hand = shoulder + fwdZ * 0.95f + new Vector3(0, -0.15f + walk * side * 0.08f, 0);
                FCapsule(shoulder, hand, 0.13f, skin);
            }
            // Голова
            Vector3 headCenter = z.Position + new Vector3(0, 2.05f, 0);
            FSphere(headCenter, 0.46f, Tint(skin, 1.1f));
            FSphere(headCenter + fwdZ * 0.40f - rgtZ * 0.18f + new Vector3(0, 0.05f, 0), 0.075f, Color.Red);
            FSphere(headCenter + fwdZ * 0.40f + rgtZ * 0.18f + new Vector3(0, 0.05f, 0), 0.075f, Color.Red);
        }

        // Other players with visible heads (голова поворачивается за взглядом игрока).
        foreach (var p in players)
        {
            if (p.Id == localPlayer.Id) continue;

            float headYawDeg = 0.0f;
            if (networkManager.RemoteInputs.TryGetValue(p.Id, out var ri))
                headYawDeg = MathF.Atan2(ri.Forward.X, ri.Forward.Z) * Rad2Deg;
            float rad = headYawDeg * Deg2Rad;
            Vector3 fwdP = new Vector3(MathF.Sin(rad), 0, MathF.Cos(rad));

            DrawBlobShadow(p.Position.X, p.Position.Z, 0.8f, 0.0f);
            FCapsule(p.Position + new Vector3(0, 0.55f, 0), p.Position + new Vector3(0, 1.9f, 0), 0.48f, p.PlayerColor);
            Vector3 headPos = p.Position + new Vector3(0, 2.6f, 0);
            FSphere(headPos, 0.42f, new Color(205, 170, 140, 255));
            FSphere(headPos + fwdP * 0.30f + new Vector3(0, 0.03f, 0), 0.17f, new Color(30, 35, 45, 255));
        }

        // Маяк ретранслятора: световой столб виден из любой точки карты.
        if (missionActive && !missionComplete && missionStage >= 3)
        {
            Raylib.BeginBlendMode(BlendMode.Additive);
            for (int i = 0; i < 7; i++)
            {
                float y0 = 4.0f + i * 7.0f;
                int a = 95 - i * 11;
                Raylib.DrawCylinderEx(RelayPoint + new Vector3(0, y0, 0), RelayPoint + new Vector3(0, y0 + 7.0f, 0),
                    1.7f, 1.3f, 8, Col(255, 205, 90, a));
            }
            Raylib.DrawCylinderEx(RelayPoint, RelayPoint + new Vector3(0, 4.0f, 0), 2.4f, 1.9f, 10, Col(90, 210, 255, 120));
            Raylib.EndBlendMode();
        }

        // Частицы: дым/пыль/гильзы обычным смешиванием
        foreach (var p in particles)
        {
            if (p.Glow) continue;
            float fade = p.MaxLife > 0.0f ? Math.Clamp(p.Life / p.MaxLife, 0.0f, 1.0f) : 1.0f;
            Color c = Col(p.Color.R, p.Color.G, p.Color.B, (int)(p.Alpha * fade));
            if (p.Round) Raylib.DrawSphereEx(p.Position, p.Size * 0.5f, 5, 5, c);
            else Raylib.DrawCube(p.Position, p.Size, p.Size, p.Size, c);
        }

        // Искры и трассеры — additive
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var p in particles)
        {
            if (!p.Glow) continue;
            float fade = p.MaxLife > 0.0f ? Math.Clamp(p.Life / p.MaxLife, 0.0f, 1.0f) : 1.0f;
            Raylib.DrawSphereEx(p.Position, p.Size * 0.5f, 4, 4, Col(p.Color.R, p.Color.G, p.Color.B, (int)(255 * fade)));
        }
        foreach (var t in tracers)
        {
            float fade = Math.Clamp(t.Life / 0.10f, 0.0f, 1.0f);
            Raylib.DrawCylinderEx(t.From, t.To, 0.012f, 0.012f, 4, Col(255, 225, 150, (int)(220 * fade)));
        }
        Raylib.EndBlendMode();

        Raylib.EndMode3D();
    }

    static void DrawGameUI()
    {
        int cx = screenWidth / 2;
        int cy = screenHeight / 2;
        // Прицел: разбегается при отдаче
        int gap = 5 + (int)(recoil * 9.0f);
        Color chCol = new Color(255, 255, 255, 235);
        Raylib.DrawLineEx(new Vector2(cx - gap - 9, cy), new Vector2(cx - gap, cy), 2, chCol);
        Raylib.DrawLineEx(new Vector2(cx + gap, cy), new Vector2(cx + gap + 9, cy), 2, chCol);
        Raylib.DrawLineEx(new Vector2(cx, cy - gap - 9), new Vector2(cx, cy - gap), 2, chCol);
        Raylib.DrawLineEx(new Vector2(cx, cy + gap), new Vector2(cx, cy + gap + 9), 2, chCol);
        Raylib.DrawCircle(cx, cy, 2, Color.Red);

        // HP игрока
        int barW = 200, barH = 22, barX = 20, barY = screenHeight - 40;
        RR(barX - 4, barY - 4, barW + 8, barH + 8, 14, new Color(0, 0, 0, 110));
        RR(barX, barY, barW, barH, 10, new Color(30, 30, 30, 200));
        float hpPercent = Math.Clamp((float)localPlayer.Health / localPlayer.MaxHealth, 0.0f, 1.0f);
        Color hpCol = LerpColor(new Color(235, 50, 45, 255), new Color(80, 215, 110, 255), hpPercent);
        if (hpPercent > 0.001f)
        {
            RR(barX, barY, Math.Max(barH, barW * hpPercent), barH, 10, hpCol);
            RR(barX + 3, barY + 2, Math.Max(barH - 6, barW * hpPercent - 6), 6, 3, new Color(255, 255, 255, 60));
        }
        RRL(barX, barY, barW, barH, 10, 2, new Color(255, 255, 255, 200));
        DrawTextCustom($"HP: {localPlayer.Health} / {localPlayer.MaxHealth}", barX + 50, barY + 3, 16, Color.White);

        // Оружие: имя, патроны, перезарядка и слоты 1..9.
        DrawWeaponHud();

        if (settings.ShowHints) DrawTextCustom("WASD: Движение | ЛКМ: Огонь/Взять | 1-9/Колесо: Оружие | R: Перезарядка | Q/E: Бросить | T: Чат | Alt: Обзор", 20, 20, 14, Color.White);
        DrawTextCustom($"CUBE HAVOC 5.0 // {Raylib.GetFPS()} FPS", screenWidth - 300, 20, 12, Color.White);

        DrawRecOverlay();

        if (missionActive) DrawMissionHud();

        if (Raylib.IsKeyDown(KeyboardKey.Tab))
        {
            int panelW = 380, panelH = 200, px = (screenWidth - panelW) / 2, py = (screenHeight - panelH) / 2;
            RR(px - 4, py - 2, panelW + 8, panelH + 10, 22, new Color(0, 0, 0, 90));
            RR(px, py, panelW, panelH, 18, new Color(20, 25, 35, 240));
            RRL(px, py, panelW, panelH, 18, 2, Color.Orange);

            DrawTextCustom("СПИСОК ИГРОКОВ (3D FPS)", px + 80, py + 15, 18, Color.Gold);
            DrawTextCustom(localPlayer.Name, px + 30, py + 70, 16, Color.White);
            DrawTextCustom($"Убийств: {localPlayer.Kills}", px + 180, py + 70, 16, Color.Green);
            DrawTextCustom($"Смертей: {localPlayer.Deaths}", px + 280, py + 70, 16, Color.Red);
        }
    }

    /// <summary>Панель оружия: имя, патроны, перезарядка и быстрые слоты.</summary>
    static void DrawWeaponHud()
    {
        WeaponInfo wd = ActiveWeapon;
        int ammo = weaponAmmo[currentWeapon];
        int x = 20, y = screenHeight - 104;

        RR(x - 4, y - 6, 250, 56, 14, new Color(0, 0, 0, 130));
        RRL(x - 4, y - 6, 250, 56, 14, 1.5f, new Color(wd.AccentR, wd.AccentG, wd.AccentB, (byte)150));
        DrawTextCustom(wd.NameRu, x + 8, y - 2, 17, Col(wd.AccentR, wd.AccentG, wd.AccentB));
        DrawTextCustom($"{(ammo > 0 ? ammo : 0)} / {wd.MagSize}", x + 8, y + 20, 16, ammo > 0 ? Color.White : Color.Red);

        if (weaponReloadTimer > 0.0f)
        {
            float p = Math.Clamp(1.0f - weaponReloadTimer / MathF.Max(0.01f, wd.ReloadTime), 0.0f, 1.0f);
            RR(x + 96, y + 24, 120, 10, 5, new Color(35, 38, 44, 220));
            RR(x + 96, y + 24, Math.Max(10.0f, 120 * p), 10, 5, Col(wd.AccentR, wd.AccentG, wd.AccentB));
            DrawTextCustom("ПЕРЕЗАРЯДКА", x + 96, y + 38, 11, Color.Gray);
        }
        else
        {
            DrawTextCustom(wd.NameEn, x + 96, y + 22, 12, Color.Gray);
        }

        // Быстрые слоты 1..9 внизу по центру.
        int n = WeaponCatalog.MaxWeapons;
        int slotW = 44, slotH = 30, gap = 4;
        int total = n * slotW + (n - 1) * gap;
        int sx = (screenWidth - total) / 2;
        int sy = screenHeight - 44;
        for (int i = 0; i < n; i++)
        {
            WeaponInfo info = WeaponCatalog.Get(i);
            bool active = i == currentWeapon;
            int bx = sx + i * (slotW + gap);
            RR(bx, sy, slotW, slotH, 8, active ? new Color(38, 44, 56, 240) : new Color(16, 20, 28, 200));
            if (active) RRL(bx, sy, slotW, slotH, 8, 2, Col(info.AccentR, info.AccentG, info.AccentB));
            DrawTextCustom((i + 1).ToString(), bx + 5, sy + 6, 13, active ? Col(info.AccentR, info.AccentG, info.AccentB) : Color.Gray);
            string shortName = info.NameRu.Length > 6 ? info.NameRu.Substring(0, 6) : info.NameRu;
            DrawTextCustom(shortName, bx + 18, sy + 7, 11, active ? Color.White : new Color(150, 155, 165, 255));
            if (weaponAmmo[i] <= 0)
                DrawTextCustom("0", bx + slotW - 12, sy + 18, 10, Color.Red);
        }
    }

    static void DrawChatUI()
    {
        int chatX = 20;
        int chatY = screenHeight - 220;
        int chatWidth = 420;

        int startY = chatY;
        foreach (var msg in chatMessages)
        {
            if (msg.TimeRemaining > 0 || isChatOpen)
            {
                RR(chatX, startY, chatWidth, 24, 8, new Color(0, 0, 0, 150));
                DrawTextCustom($"[{msg.Sender}]: ", chatX + 6, startY + 4, 15, msg.SenderColor);
                
                int senderWidth = (int)Raylib.MeasureTextEx(customFont, $"[{msg.Sender}]: ", 15, 1).X;
                DrawTextCustom(msg.Text, chatX + 8 + senderWidth, startY + 4, 15, Color.White);
                
                startY += 26;
            }
        }

        if (isChatOpen)
        {
            int inputY = screenHeight - 60;
            RR(chatX, inputY, chatWidth, 32, 12, new Color(20, 25, 35, 230));
            RRL(chatX, inputY, chatWidth, 32, 12, 2, Color.Orange);

            DrawTextCustom("Чат: " + chatInput + "_", chatX + 10, inputY + 7, 16, Color.Yellow);

            int key = Raylib.GetCharPressed();
            while (key > 0)
            {
                if (key >= 32 && key <= 125) chatInput += (char)key;
                key = Raylib.GetCharPressed();
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && chatInput.Length > 0)
            {
                chatInput = chatInput.Substring(0, chatInput.Length - 1);
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Enter))
            {
                if (!string.IsNullOrWhiteSpace(chatInput))
                    networkManager.SendChat(chatInput.Trim());

                chatInput = "";
                isChatOpen = false;
                DisableCursor();
            }
        }
    }

    static void DrawConsole()
    {
        int conH = screenHeight / 2;
        Raylib.DrawRectangle(0, 0, screenWidth, conH, new Color(12, 16, 24, 245));
        Raylib.DrawLine(0, conH, screenWidth, conH, Color.Orange);

        int logY = 10;
        int startIndex = Math.Max(0, consoleLogs.Count - 10);
        for (int i = startIndex; i < consoleLogs.Count; i++)
        {
            Color c = consoleLogs[i].StartsWith("[ERROR]") ? Color.Red : Color.Green;
            DrawTextCustom(consoleLogs[i], 15, logY, 14, c);
            logY += 20;
        }

        RR(10, conH - 35, screenWidth - 20, 25, 8, new Color(30, 35, 45, 255));
        DrawTextCustom("> " + consoleInput + "_", 15, conH - 30, 14, Color.White);

        int key = Raylib.GetCharPressed();
        while (key > 0)
        {
            if (key >= 32 && key <= 125) consoleInput += (char)key;
            key = Raylib.GetCharPressed();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && consoleInput.Length > 0)
            consoleInput = consoleInput.Substring(0, consoleInput.Length - 1);

        if (Raylib.IsKeyPressed(KeyboardKey.Enter) && !string.IsNullOrWhiteSpace(consoleInput))
        {
            ExecuteConsoleCommand(consoleInput.Trim());
            consoleInput = "";
        }
    }

    static void ExecuteConsoleCommand(string cmd)
    {
        consoleLogs.Add("> " + cmd);
        string[] args = cmd.Split(':');
        string baseCmd = args[0].ToLower();

        switch (baseCmd)
        {
            case "disconnect":
                networkManager.Stop();
                clientWorldPrepared = false;
                NavigateTo(GameState.MainMenu);
                discordRpc.SetStatus("В меню");
                EnableCursor();
                break;
            case "adminpanel":
                isAdminPanelOpen = !isAdminPanelOpen;
                if (isAdminPanelOpen) EnableCursor();
                break;
            case "exit":
                Environment.Exit(0);
                break;
            case "help":
                consoleLogs.Add("Commands: disconnect, adminpanel, exit, help");
                break;
            default:
                consoleLogs.Add("[ERROR] Unknown command!");
                break;
        }
    }

    static void DrawAdminPanel()
    {
        int pw = 440, ph = 300, px = (screenWidth - pw) / 2, py = (screenHeight - ph) / 2;
        RR(px - 4, py - 2, pw + 8, ph + 10, 22, new Color(0, 0, 0, 90));
        RR(px, py, pw, ph, 18, new Color(25, 30, 40, 250));
        RRL(px, py, pw, ph, 18, 2, Color.Red);

        DrawTextCustom("АДМИН-ПАНЕЛЬ (3D)", px + 120, py + 15, 20, Color.Red);

        if (DrawButtonCustom("Заспавнить Блок", px + 30, py + 60, 180, 40))
        {
            worldObjects.Add(new PhysicalObject3D
            {
                NetId = GetNextObjectNetId(),
                Position = localPlayer.Position + new Vector3(0, 1.0f, 3.0f),
                Size = new Vector3(1.6f, 1.6f, 1.6f),
                Color = Color.Blue
            });
        }

        if (DrawButtonCustom("Заспавнить Зомби", px + 230, py + 60, 180, 40))
        {
            zombies.Add(new Zombie3D { NetId = GetNextZombieNetId(), Position = localPlayer.Position + new Vector3(3.0f, 0, 3.0f), Health = 50 });
        }

        if (DrawButtonCustom("Сменить свой цвет", px + 30, py + 120, 180, 40))
        {
            localPlayer.PlayerColor = GetRandomColor();
        }

        if (DrawButtonCustom("Закрыть", px + 130, py + 230, 180, 40))
        {
            isAdminPanelOpen = false;
            DisableCursor();
        }
    }

    static unsafe void LoadCyrillicFont()
    {
        string fontPath = @"C:\Windows\Fonts\arial.ttf";
        if (!File.Exists(fontPath)) fontPath = @"C:\Windows\Fonts\consola.ttf";

        if (File.Exists(fontPath))
        {
            List<int> codepoints = new List<int>();
            for (int i = 32; i <= 126; i++) codepoints.Add(i);
            for (int i = 0x0400; i <= 0x04FF; i++) codepoints.Add(i);

            int[] codePointsArray = codepoints.ToArray();
            byte[] fontPathBytes = Encoding.UTF8.GetBytes(fontPath + "\0");

            fixed (byte* pFontPath = fontPathBytes)
            fixed (int* pCodePoints = codePointsArray)
            {
                customFont = Raylib.LoadFontEx((sbyte*)pFontPath, 48, pCodePoints, codePointsArray.Length);
            }
            Raylib.SetTextureFilter(customFont.Texture, TextureFilter.Bilinear);
        }
        else customFont = Raylib.GetFontDefault();
    }

    static unsafe void SetApplicationIcon()
    {
        string iconPath = File.Exists("icon.png") ? "icon.png" : (File.Exists("icon.ico") ? "icon.ico" : "");
        if (!string.IsNullOrEmpty(iconPath))
        {
            Image icon = Raylib.LoadImage(iconPath);
            if (icon.Data != null)
            {
                Raylib.SetWindowIcon(icon);
                Raylib.UnloadImage(icon);
            }
        }
    }

    static void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(SettingsPath));
                if (loaded != null) settings = loaded;
            }
        }
        catch { settings = new GameSettings(); }

        mouseSensitivity = settings.MouseSensitivity;
        camera.FovY = settings.Fov;
        if (settings.Fullscreen && !Raylib.IsWindowFullscreen()) Raylib.ToggleFullscreen();
    }

    static void SaveSettings()
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    static void NavigateTo(GameState state)
    {
        if (currentState == state || menuTransitioning) return;
        pendingState = state;
        menuTransition = 0.0f;
        menuTransitioning = true;
    }

    static void UpdateMenuTransition(float dt)
    {
        if (!menuTransitioning) return;
        menuTransition += dt * 4.5f;
        if (menuTransition >= 0.5f && currentState != pendingState)
            currentState = pendingState;
        if (menuTransition >= 1.0f)
        {
            menuTransition = 0.0f;
            menuTransitioning = false;
        }
    }

    static void DrawTransitionOverlay()
    {
        if (!menuTransitioning) return;
        float a = menuTransition < 0.5f ? menuTransition * 2.0f : (1.0f - menuTransition) * 2.0f;
        a = Math.Clamp(a, 0.0f, 1.0f);
        Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, new Color((byte)6, (byte)9, (byte)15, (byte)(a * 235)));
        DrawTextCentered("CUBE HAVOC 5.0", screenHeight / 2 - 18, 24, new Color((byte)255, (byte)190, (byte)60, (byte)(a * 255)));
    }

    static void DrawMenuPanel(int x, int y, int w, int h, float alpha = 1.0f)
    {
        byte a = (byte)Math.Clamp((int)(230 * alpha), 0, 255);
        for (int i = 3; i >= 1; i--)
            RR(x - i * 4, y - i * 4 + 6, w + i * 8, h + i * 8, 22 + i * 4, Col(0, 0, 0, (int)(16 * alpha)));
        RR(x, y, w, h, 20, new Color((byte)9, (byte)13, (byte)22, a));
        RRL(x, y, w, h, 20, 2, new Color((byte)255, (byte)170, (byte)55, (byte)Math.Clamp((int)(150 * alpha), 0, 255)));
        Raylib.DrawLine(x + 22, y + 52, x + w - 22, y + 52, new Color((byte)75, (byte)90, (byte)120, (byte)Math.Clamp((int)(150 * alpha), 0, 255)));
    }

    static void DrawCubeHavocLogo(float cx, float cy, float scale)
    {
        float pulse = 1.0f + MathF.Sin(menuTime * 2.2f) * 0.035f;
        scale *= pulse;
        float s = 48 * scale;
        Raylib.DrawCircleGradient((int)cx, (int)cy, s * 2.4f, Col(255, 160, 40, 70), Col(255, 160, 40, 0));
        RR(cx - s, cy - s, s * 2, s * 2, s * 0.38f, new Color(255, 172, 48, 255));
        RR(cx - s + 7, cy - s + 7, s * 2 - 14, s * 2 - 14, s * 0.30f, new Color(25, 31, 45, 255));
        RRL(cx - s, cy - s, s * 2, s * 2, s * 0.38f, 3, Color.Orange);
        RR(cx - s * 0.42f, cy - s * 0.42f, s * 0.84f, s * 0.84f, s * 0.18f, new Color(255, 172, 48, 255));
        RR(cx - s * 0.30f, cy - s * 0.30f, s * 0.60f, s * 0.60f, s * 0.12f, new Color(25, 31, 45, 255));
        Raylib.DrawLineEx(new Vector2(cx - s + 12, cy), new Vector2(cx - s * 0.5f, cy), 3, Color.Orange);
        Raylib.DrawLineEx(new Vector2(cx + s * 0.5f, cy), new Vector2(cx + s - 12, cy), 3, Color.Orange);
        Raylib.DrawLineEx(new Vector2(cx, cy - s + 12), new Vector2(cx, cy - s * 0.5f), 3, Color.Orange);
        Raylib.DrawLineEx(new Vector2(cx, cy + s * 0.5f), new Vector2(cx, cy + s - 12), 3, Color.Orange);
    }

    static void DrawMovingPixelForest()
    {
        // Stylized procedural background: no external assets required.
        Raylib.DrawRectangleGradientV(0, 0, screenWidth, screenHeight, new Color(8, 13, 24, 255), new Color(25, 40, 58, 255));
        Raylib.DrawRectangleGradientV(0, screenHeight / 2 - 140, screenWidth, 140, Col(255, 150, 50, 0), Col(255, 150, 50, 28));
        float grid = 64.0f;
        float offset = (menuTime * 18.0f) % grid;
        for (float x = -grid + offset; x < screenWidth + grid; x += grid)
            Raylib.DrawLine((int)x, screenHeight / 2, (int)x, screenHeight, new Color((byte)35, (byte)58, (byte)82, (byte)120));
        for (float y = screenHeight / 2; y < screenHeight; y += grid * 0.65f)
            Raylib.DrawLine(0, (int)y, screenWidth, (int)y, new Color((byte)35, (byte)58, (byte)82, (byte)110));

        for (int i = 0; i < 18; i++)
        {
            float x = (i * 113 + bgScrollX * 0.15f) % (screenWidth + 140) - 70;
            float y = 90 + (i * 47) % Math.Max(180, screenHeight / 2);
            int r = 2 + (i % 4);
            Raylib.DrawCircleGradient((int)x, (int)y, r * 6.0f, Col(255, 181, 70, 55 + (i % 4) * 15), Col(255, 181, 70, 0));
            Raylib.DrawCircle((int)x, (int)y, r, new Color((byte)255, (byte)181, (byte)70, (byte)(80 + (i % 4) * 25)));
        }

        Raylib.DrawRectangle(0, screenHeight - 90, screenWidth, 90, new Color((byte)5, (byte)8, (byte)14, (byte)220));
        Raylib.DrawLine(0, screenHeight - 90, screenWidth, screenHeight - 90, new Color((byte)255, (byte)165, (byte)45, (byte)130));
    }

    static void DrawMainMenu()
    {
        discordRpc.SetStatus("В меню");

        const int barH = 64;   // верхняя панель навигации
        const int footH = 52;  // нижняя полоса
        int contentTop = barH + 22;
        int panelW = 330;
        int sideX = screenWidth - 48 - panelW;
        int sideH = screenHeight - contentTop - footH - 18;
        int leftW = sideX - 48 - 20;

        DrawMainMenuHeader(barH);

        // Правая колонка — карточка профиля и состояния сети.
        DrawMenuSidePanel(sideX, contentTop, panelW, sideH);

        string heading = mainMenuTab == 0 ? "ИГРАТЬ" : mainMenuTab == 1 ? "МАГАЗИН" : "НОВОСТИ";
        string subtitle = mainMenuTab == 0 ? "Выберите режим"
            : mainMenuTab == 1 ? "Скины, наборы и эмблемы"
            : "Что нового на полигоне «Куб-9»";

        DrawTextCustom(heading, 48, contentTop, 32, Color.White);
        DrawTextCustom(subtitle, 48, contentTop + 42, 14, Color.LightGray);

        int cardsY = contentTop + 84;

        if (mainMenuTab == 0)
        {
            int cardW = (leftW - 20) / 2;
            int cardH = 236;

            // Главные кнопки меню: одиночная и сетевая игра.
            if (DrawModeCard("ОДИНОЧНАЯ ИГРА", "ОПЕРАЦИЯ «КУБ-9»",
                    new[] { "4 этапа: зачистка, ядра, ретранслятор, оборона", "26 зомби · карта 400×400 · 9 стволов" },
                    "НАЧАТЬ ОПЕРАЦИЮ", 48, cardsY, cardW, cardH, Color.Orange))
            {
                playSelectTab = 0;
                NavigateTo(GameState.PlaySelect);
            }

            if (DrawModeCard("СЕТЕВАЯ ИГРА", "ОНЛАЙН ПО НАЗВАНИЮ",
                    new[] { "Список серверов по имени, без IP", "VPS-серверы · до 16 игроков · чат" },
                    "ВЫБРАТЬ СЕРВЕР", 48 + cardW + 20, cardsY, cardW, cardH, new Color(90, 180, 255, 255)))
            {
                playSelectTab = 1;
                if (serverList.Count == 0) RefreshServerList();
                NavigateTo(GameState.PlaySelect);
            }

            int stripY = cardsY + cardH + 22;
            RR(48, stripY, leftW, 92, 16, new Color(11, 16, 25, 215));
            RRL(48, stripY, leftW, 92, 16, 1.5f, new Color(58, 70, 92, 255));
            DrawTextCustom("ЧТО ВНУТРИ", 72, stripY + 16, 11, Color.Gold);
            DrawTextCustom("Сюжетная операция · 9 стволов с разной отдачей · карта 400×400", 72, stripY + 38, 14, Color.White);
            DrawTextCustom("Сервер «Кубляндия Прайм» уже в сети: сетевая игра → ОБНОВИТЬ", 72, stripY + 60, 12, Color.LightGray);
        }
        else if (mainMenuTab == 1)
        {
            string[] names = { "ГРОЗА-9", "ВИХРЬ-74", "КУВАЛДА-12", "ИГЛА-АМ" };
            string[] skins = { "«Закат»", "«Полигон»", "«Ржавчина»", "«Северный»" };
            Color[] tints = { Col(230, 120, 60), Col(90, 170, 235), Col(170, 90, 70), Col(140, 200, 220) };

            int cardW = (leftW - 3 * 16) / 4;
            for (int i = 0; i < 4; i++)
                DrawShopCard(names[i], skins[i], tints[i], 48 + i * (cardW + 16), cardsY, cardW, 220);

            RR(48, cardsY + 244, leftW, 96, 16, new Color(11, 16, 25, 215));
            RRL(48, cardsY + 244, leftW, 96, 16, 1.5f, new Color(58, 70, 92, 255));
            DrawTextCustom("МАГАЗИН ГОТОВИТСЯ", 72, cardsY + 262, 14, Color.Gold);
            DrawTextCustom("Покупки, наборы и обмен появятся в следующем обновлении — оружие и прогресс уже на месте.",
                72, cardsY + 288, 13, Color.LightGray);
        }
        else
        {
            DrawNewsRow("31.10.2026", "Операция «КУБ-9»",
                "Сюжетный режим: четыре этапа, радиообмен и финальная сводка.", 48, cardsY, leftW, Color.Orange);
            DrawNewsRow("31.10.2026", "Онлайн по названию",
                "Серверы выбираются по имени: директория на VPS, поиск и счётчики игроков.", 48, cardsY + 104, leftW, new Color(90, 180, 255, 255));
            DrawNewsRow("30.10.2026", "Карта 400×400 и оптимизация",
                "Полигон вырос вчетверо, физика спящих предметов и куллинг ускорили игру.", 48, cardsY + 208, leftW, new Color(120, 210, 150, 255));
        }

        DrawMainMenuFooter(footH);
    }

    static void DrawMainMenuHeader(int barH)
    {
        Raylib.DrawRectangle(0, 0, screenWidth, barH, new Color(8, 12, 20, 242));
        Raylib.DrawLine(0, barH, screenWidth, barH, new Color(255, 165, 45, 120));

        DrawCubeHavocLogo(46, barH / 2, 0.26f);
        DrawTextCustom("CUBE HAVOC", 76, 12, 19, Color.White);
        DrawTextCustom("VERSION 5.0", 77, 36, 10, Color.Gold);

        int navX = 230;
        if (DrawNavTab("ИГРАТЬ", navX, barH / 2, mainMenuTab == 0)) mainMenuTab = 0;
        navX += (int)NavTabWidth("ИГРАТЬ");
        if (DrawNavTab("МАГАЗИН", navX, barH / 2, mainMenuTab == 1)) mainMenuTab = 1;
        navX += (int)NavTabWidth("МАГАЗИН");
        if (DrawNavTab("НОВОСТИ", navX, barH / 2, mainMenuTab == 2)) mainMenuTab = 2;

        int rightX = screenWidth - 24;
        if (DrawHeaderButton("ВЫХОД", ref rightX, barH / 2 - 8, 14, new Color(230, 110, 100, 255))) Environment.Exit(0);
        if (DrawHeaderButton("РАЗРАБОТЧИКИ", ref rightX, barH / 2 - 8, 14, Color.LightGray)) NavigateTo(GameState.Developers);
        if (DrawHeaderButton("НАСТРОЙКИ", ref rightX, barH / 2 - 8, 14, Color.LightGray)) NavigateTo(GameState.Settings);
    }

    static void DrawMainMenuFooter(int footH)
    {
        int y = screenHeight - footH;
        Raylib.DrawRectangle(0, y, screenWidth, footH, new Color(8, 12, 20, 235));
        Raylib.DrawLine(0, y, screenWidth, y, new Color(70, 84, 108, 140));

        DrawTextCustom("CUBE HAVOC 5.0", 48, y + 18, 13, Color.White);
        DrawTextCustom("© 2026 · процедурные ассеты", 170, y + 19, 11, Color.Gray);

        string right = "Сюжетная операция · 9 стволов · карта 400×400 · онлайн по названию";
        Vector2 sz = Raylib.MeasureTextEx(customFont, right, 11, 1);
        DrawTextCustom(right, (int)(screenWidth - 48 - sz.X), y + 19, 11, Color.Gray);
    }

    static float NavTabWidth(string text) => Raylib.MeasureTextEx(customFont, text, 15, 1).X + 34;

    static bool DrawNavTab(string text, int x, int y, bool active)
    {
        Vector2 sz = Raylib.MeasureTextEx(customFont, text, 15, 1);
        Rectangle rect = new Rectangle(x, y - 15, sz.X + 20, 30);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);

        Color color = active || hover ? Color.White : new Color(150, 158, 172, 255);
        Raylib.DrawTextEx(customFont, text, new Vector2(x + 10, y - sz.Y / 2), 15, 1, color);

        if (active)
            RR(x + 10, y + 13, sz.X, 3, 2, Color.Orange);
        else if (hover)
            RR(x + 10, y + 13, sz.X, 3, 2, new Color(255, 165, 45, 90));

        return hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    static bool DrawHeaderButton(string text, ref int rightX, int y, int fontSize, Color tint)
    {
        Vector2 sz = Raylib.MeasureTextEx(customFont, text, fontSize, 1);
        int x = (int)(rightX - sz.X);
        Rectangle rect = new Rectangle(x - 8, y - 6, sz.X + 16, sz.Y + 12);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);

        Raylib.DrawTextEx(customFont, text, new Vector2(x, y), fontSize, 1, hover ? Color.White : tint);
        rightX = x - 24;

        return hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    static bool DrawModeCard(string title, string kicker, string[] lines, string footer, int x, int y, int w, int h, Color accent)
    {
        Rectangle rect = new Rectangle(x, y, w, h);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);

        int key = HashCode.Combine(x, y, title);
        btnAnim.TryGetValue(key, out float anim);
        anim += ((hover ? 1.0f : 0.0f) - anim) * Math.Clamp(Raylib.GetFrameTime() * 12.0f, 0.0f, 1.0f);
        btnAnim[key] = anim;
        int lift = (int)(anim * 4.0f);

        RR(x + 3, y + 8, w, h, 18, new Color(0, 0, 0, 85));
        RR(x, y - lift, w, h, 18, LerpColor(new Color(12, 18, 28, 242), new Color(20, 29, 43, 250), anim));
        RRL(x, y - lift, w, h, 18, 1.5f + anim, LerpColor(new Color(58, 72, 96, 255), accent, anim));

        // Превью режима: плашка с мини-иконкой.
        RR(x + 18, y + 18 - lift, w - 36, 84, 12, LerpColor(new Color(16, 22, 33, 255), new Color(26, 36, 52, 255), anim));
        RR(x + 18, y + 18 - lift, w - 36, 3, 2, LerpColor(accent, Color.White, 0.2f));

        int bx = x + 38, by = y + 44 - lift;
        RR(bx, by, 16, 16, 4, Col(accent.R, accent.G, accent.B, (int)(120 + 100 * anim)));
        RR(bx + 22, by - 6, 12, 12, 3, new Color((byte)255, (byte)255, (byte)255, (byte)(60 + 60 * anim)));
        RR(bx + 40, by + 4, 20, 20, 5, Col(accent.R, accent.G, accent.B, (int)(70 + 80 * anim)));

        DrawTextCustom(kicker, x + 112, y + 34 - lift, 10, Col(accent.R, accent.G, accent.B));

        DrawTextCustom(title, x + 24, y + 118 - lift, 23, Color.White);
        int lineY = y + 156 - lift;
        foreach (string line in lines)
        {
            DrawTextCustom(line, x + 24, lineY, 12, new Color(168, 176, 190, 255));
            lineY += 20;
        }

        DrawTextCustom(footer, x + 24, y + h - 34 - lift, 12, LerpColor(new Color(140, 148, 162, 255), accent, anim));
        Raylib.DrawTextEx(customFont, ">", new Vector2(x + w - 34, y + h - 38 - lift), 18, 1,
            LerpColor(new Color(90, 100, 116, 200), accent, anim));

        return hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    static void DrawShopCard(string weapon, string skin, Color tint, int x, int y, int w, int h)
    {
        RR(x, y, w, h, 16, new Color(12, 18, 28, 238));
        RRL(x, y, w, h, 16, 1.5f, new Color(56, 68, 90, 255));

        RR(x + 14, y + 14, w - 28, h - 92, 10, new Color(18, 25, 37, 255));
        Raylib.DrawRectangleGradientV(x + 20, y + 20, w - 40, h - 104,
            Col(tint.R, tint.G, tint.B, 130), Col(tint.R / 3, tint.G / 3, tint.B / 3, 40));

        DrawTextCustom(weapon, x + 16, y + h - 66, 14, Color.White);
        DrawTextCustom(skin, x + 16, y + h - 46, 12, Col(tint.R, tint.G, tint.B));

        RR(x + w - 72, y + h - 72, 56, 20, 10, new Color(48, 56, 70, 230));
        DrawTextCustom("СКОРО", x + w - 64, y + h - 69, 9, Color.LightGray);
    }

    static void DrawNewsRow(string date, string title, string text, int x, int y, int w, Color accent)
    {
        RR(x, y, w, 92, 16, new Color(12, 18, 28, 238));
        RRL(x, y, w, 92, 16, 1.5f, new Color(56, 68, 90, 255));
        RR(x, y + 16, 4, 60, 2, accent);

        DrawTextCustom(date, x + 22, y + 14, 10, Color.Gray);
        DrawTextCustom(title, x + 22, y + 32, 17, Color.White);
        DrawTextCustom(text, x + 22, y + 58, 12, new Color(168, 176, 190, 255));
    }

    static void DrawMenuSidePanel(int x, int y, int w, int h)
    {
        RR(x, y, w, h, 18, new Color(11, 16, 25, 230));
        RRL(x, y, w, h, 18, 1.5f, new Color(58, 70, 92, 255));

        DrawTextCustom("ПРОФИЛЬ", x + 20, y + 16, 11, Color.Gray);

        // Аватар — цвет игрока и первая буква имени.
        RR(x + 20, y + 40, 56, 56, 12, localPlayer.PlayerColor);
        string initial = string.IsNullOrEmpty(settings.PlayerName) ? "C" : settings.PlayerName.Substring(0, 1).ToUpperInvariant();
        Vector2 iSz = Raylib.MeasureTextEx(customFont, initial, 26, 1);
        Raylib.DrawTextEx(customFont, initial, new Vector2(x + 20 + (56 - iSz.X) / 2, y + 40 + (56 - iSz.Y) / 2), 26, 1,
            new Color((byte)20, (byte)24, (byte)32, (byte)255));

        DrawTextCustom(string.IsNullOrEmpty(settings.PlayerName) ? "CubePlayer" : settings.PlayerName, x + 88, y + 44, 15, Color.White);
        DrawTextCustom("УРОВЕНЬ 01", x + 88, y + 68, 10, Color.Gold);

        Raylib.DrawLine(x + 20, y + 116, x + w - 20, y + 116, new Color(58, 70, 92, 160));

        DrawTextCustom("СОСТОЯНИЕ СЕТИ", x + 20, y + 132, 11, Color.Gray);
        DrawTextCustom("Директория серверов", x + 20, y + 158, 12, new Color(168, 176, 190, 255));
        string dir = string.IsNullOrWhiteSpace(settings.DirectoryUrl) ? "не задана" : settings.DirectoryUrl;
        DrawTextCustom(dir, x + 20, y + 178, 12, Color.White);
        DrawTextCustom("«Кубляндия Прайм» · 94.232.46.58:7777", x + 20, y + 198, 12, new Color(120, 210, 150, 255));

        Raylib.DrawLine(x + 20, y + 228, x + w - 20, y + 228, new Color(58, 70, 92, 160));

        DrawTextCustom("УПРАВЛЕНИЕ", x + 20, y + 244, 11, Color.Gray);
        DrawTextCustom("WASD · ЛКМ · 1-9 · R · T · Tab · Alt", x + 20, y + 268, 11, Color.LightGray);

        DrawTextCustom("CUBE HAVOC 5.0", x + 20, y + h - 30, 10, Color.Gray);
    }

    // ---------------------------------------------------------------
    // Браузер серверов 5.0: список по названию, а не по IP.
    // Клиент забирает список с мастер-директории, локальные серверы
    // подмешиваются из LAN-обнаружения.
    // ---------------------------------------------------------------
    class ServerEntry
    {
        public string Name = "";
        public string Host = "";
        public int Port = NetworkManager.GamePort;
        public int Players;
        public int MaxPlayers;
        public string Version = "";
        public string Map = "";
        public bool Lan;
    }

    static readonly List<ServerEntry> serverList = new List<ServerEntry>();
    static string serverSearch = "";
    static bool serverSearchEditing = false;
    static bool serverShowIp = false;
    static Task<List<ServerEntry>> serverFetchTask = null;
    static string directoryStatus = "Список серверов ещё не загружен";
    static int serverSelected = -1;
    static float directoryTimer = 999.0f;

    static void PollServerDirectory()
    {
        if (serverFetchTask != null && serverFetchTask.IsCompleted)
        {
            if (serverFetchTask.IsCompletedSuccessfully)
            {
                serverList.RemoveAll(s => !s.Lan);
                serverList.AddRange(serverFetchTask.Result);
                directoryStatus = serverList.Count == 0
                    ? "Серверов нет: включите сервер на VPS или создайте LAN-игру"
                    : $"Доступно серверов: {serverList.Count}";
                if (serverSelected >= serverList.Count) serverSelected = -1;
            }
            else
            {
                directoryStatus = "Директория недоступна — проверьте соединение";
            }
            serverFetchTask = null;
        }

        // LAN-серверы живут отдельно, добавляем их в общий список без дублей.
        foreach (var lan in networkManager.DiscoveredServers)
        {
            ServerEntry exists = serverList.Find(s => s.Lan && s.Host == lan.Ip && s.Port == lan.Port);
            if (exists == null)
            {
                serverList.Add(new ServerEntry
                {
                    Name = lan.Name,
                    Host = lan.Ip,
                    Port = lan.Port,
                    Players = lan.CurrentPlayers,
                    MaxPlayers = lan.MaxPlayers,
                    Version = "LAN",
                    Map = "полигон",
                    Lan = true
                });
            }
            else
            {
                exists.Players = lan.CurrentPlayers;
                exists.MaxPlayers = lan.MaxPlayers;
            }
        }
        serverList.RemoveAll(s => s.Lan && !networkManager.IsScanning && networkManager.DiscoveredServers.Count == 0);
    }

    static void RefreshServerList()
    {
        if (serverFetchTask != null && !serverFetchTask.IsCompleted) return;
        string url = string.IsNullOrWhiteSpace(settings.DirectoryUrl) ? "http://94.232.46.58:8090" : settings.DirectoryUrl.Trim();
        directoryStatus = "Обновляю список серверов...";
        serverFetchTask = Task.Run(() => FetchDirectory(url));
        networkManager.StartScan();
    }

    static List<ServerEntry> FetchDirectory(string baseUrl)
    {
        var result = new List<ServerEntry>();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            string json = http.GetStringAsync(baseUrl.TrimEnd('/') + "/api/servers").GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("servers", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in arr.EnumerateArray())
                {
                    result.Add(new ServerEntry
                    {
                        Name = GetJsonString(s, "name", "Сервер"),
                        Host = GetJsonString(s, "host", ""),
                        Port = GetJsonInt(s, "port", NetworkManager.GamePort),
                        Players = GetJsonInt(s, "players", 0),
                        MaxPlayers = GetJsonInt(s, "maxPlayers", 16),
                        Version = GetJsonString(s, "version", "5.0"),
                        Map = GetJsonString(s, "map", "полигон")
                    });
                }
            }
        }
        catch
        {
            // директория выключена или нет сети — список просто останется пустым
        }
        return result;
    }

    static string GetJsonString(JsonElement el, string name, string fallback)
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString() ?? fallback;
        return fallback;
    }

    static int GetJsonInt(JsonElement el, string name, int fallback)
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)) return i;
        return fallback;
    }

    // Имена серверов: только латиница, кириллица, цифры, пробел, дефис, подчёркивание и точка.
    static bool IsAllowedNameChar(char c)
    {
        if (c >= 'A' && c <= 'Z') return true;
        if (c >= 'a' && c <= 'z') return true;
        if (c >= 'А' && c <= 'я') return true;
        if (c == 'Ё' || c == 'ё') return true;
        if (c >= '0' && c <= '9') return true;
        return c == ' ' || c == '-' || c == '_' || c == '.';
    }

    static bool HasLetter(string s)
    {
        foreach (char c in s)
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= 'А' && c <= 'я') || c == 'Ё' || c == 'ё') return true;
        return false;
    }

    static string ValidateServerName(string name)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length < 3) return "Минимум 3 символа";
        if (trimmed.Length > 24) return "Максимум 24 символа";
        foreach (char c in trimmed)
            if (!IsAllowedNameChar(c)) return "Только английские и русские буквы, цифры, пробел, - _ .";
        if (!HasLetter(trimmed)) return "В названии должна быть хотя бы одна буква";
        return null;
    }

    static void DrawPlaySelectMenu()
    {
        PollServerDirectory();
        directoryTimer += Raylib.GetFrameTime();
        if (playSelectTab == 1 && directoryTimer > 20.0f && (serverFetchTask == null || serverFetchTask.IsCompleted))
        {
            directoryTimer = 0.0f;
            RefreshServerList();
        }

        DrawMenuPanel(40, 34, screenWidth - 80, screenHeight - 95);
        DrawTextCustom("PLAY / GAME MODES", 70, 60, 28, Color.White);
        DrawTextCustom("Выбери режим и устраивай хаос", 70, 94, 14, Color.LightGray);

        int tabW = 260, tabH = 45;
        int startX = (screenWidth - (tabW * 2 + 20)) / 2;
        int tabY = 120;
        if (DrawButtonCustom("ОДИНОЧНАЯ ИГРА", startX, tabY, tabW, tabH, playSelectTab == 0 ? Color.Orange : Color.Gray)) playSelectTab = 0;
        if (DrawButtonCustom("МУЛЬТИПЛЕЕР [ОНЛАЙН]", startX + tabW + 20, tabY, tabW, tabH, playSelectTab == 1 ? Color.Orange : Color.Gray))
        {
            playSelectTab = 1;
            if (serverList.Count == 0) RefreshServerList();
            else networkManager.StartScan();
        }

        int listX = 72, listY = 182, listW = screenWidth - 144, listH = screenHeight - 268;
        RR(listX, listY, listW, listH, 18, new Color(13, 18, 29, 235));
        RRL(listX, listY, listW, listH, 18, 2, new Color(65, 78, 105, 255));

        if (playSelectTab == 0)
        {
            DrawTextCustom("SOLO SURVIVAL", listX + 30, listY + 28, 24, Color.Gold);
            DrawTextCustom("Зомби, физика предметов, гравипушка и процедурный полигон.", listX + 30, listY + 66, 15, Color.LightGray);
            DrawTextCustom($"{MapZombieCount} зомби • {MapBlockCount} объектов • карта 400×400 • 9 стволов", listX + 30, listY + 94, 14, Color.White);
            if (DrawButtonCustom("+ НАЧАТЬ ОПЕРАЦИЮ", listX + 30, listY + 135, 250, 48, Color.Orange))
            {
                networkManager.Stop(); clientWorldPrepared = false; NavigateTo(GameState.Singleplayer);
                InitGameWorld3D(true); StartMission();
            }
            DrawTextCustom("ЛКМ — огонь, 1-9 — смена оружия, R — перезарядка", listX + 30, listY + 200, 13, Color.Gray);
        }
        else
        {
            // --- Поиск и обновление ---
            DrawTextCustom("СПИСОК СЕРВЕРОВ", listX + 30, listY + 20, 22, Color.Gold);
            DrawTextCustom("Подключение по названию сервера", listX + 30, listY + 48, 12, Color.Gray);

            DrawInputBox("Поиск по названию", ref serverSearch, listX + 30, listY + 66, 320, 38, ref serverSearchEditing,
                c => IsAllowedNameChar(c) || c == 'ё' || c == 'Ё');
            if (DrawButtonCustom("ОБНОВИТЬ", listX + 362, listY + 66, 140, 38, Color.Orange)) { directoryTimer = 0.0f; RefreshServerList(); }
            DrawTextCustom(directoryStatus, listX + 516, listY + 76, 12, Color.LightGray);
            if (DrawButtonCustom(serverShowIp ? "СКРЫТЬ IP" : "IP ДЛЯ АДМИНОВ", listX + listW - 190, listY + 66, 160, 38, Color.Gray)) serverShowIp = !serverShowIp;

            // --- Список ---
            int rowY = listY + 118;
            int rowH = 54;
            string query = (serverSearch ?? "").Trim().ToLowerInvariant();
            var visible = new List<ServerEntry>();
            foreach (var s in serverList)
            {
                if (query.Length > 0 && !s.Name.ToLowerInvariant().Contains(query)) continue;
                visible.Add(s);
            }

            int maxRows = Math.Max(1, (listH - 132) / (rowH + 6));
            if (visible.Count == 0)
            {
                DrawTextCustom(serverFetchTask != null ? "Ищу серверы..." : "Серверов не найдено. Проверьте название или обновите список.",
                    listX + 34, rowY + 8, 15, Color.Gray);
            }
            for (int i = 0; i < visible.Count && i < maxRows; i++)
            {
                var server = visible[i];
                bool selected = server == (serverSelected >= 0 && serverSelected < serverList.Count ? serverList[serverSelected] : null);
                RR(listX + 26, rowY, listW - 52, rowH, 12, selected ? new Color(34, 44, 60, 250) : new Color(22, 28, 40, 245));
                if (selected) RRL(listX + 26, rowY, listW - 52, rowH, 12, 2, Color.Orange);

                Color nameCol = server.Players > 0 ? Color.White : Color.LightGray;
                DrawTextCustom(server.Name, listX + 42, rowY + 8, 17, nameCol);

                string info = $"{server.Players}/{server.MaxPlayers} игроков • {server.Version} • {server.Map}";
                if (server.Lan) info += " • LAN";
                if (serverShowIp) info += $" • {server.Host}:{server.Port}";
                DrawTextCustom(info, listX + 42, rowY + 30, 12, Color.LightGray);

                if (DrawButtonCustom("ВОЙТИ", listX + listW - 148, rowY + 9, 96, 36, Color.Orange))
                {
                    serverSelected = serverList.IndexOf(server);
                    JoinServerByName(server);
                }
                rowY += rowH + 6;
            }

            // --- Создание сервера и продвинутое подключение ---
            int bottomY = listY + listH - 96;
            DrawTextCustom("СОЗДАТЬ СВОЙ СЕРВЕР", listX + 30, bottomY, 15, Color.Gold);
            DrawInputBox("Название (RU/EN)", ref serverNameInput, listX + 30, bottomY + 24, 320, 38, ref serverNameEditing, IsAllowedNameChar);
            string nameError = ValidateServerName(serverNameInput);
            if (nameError != null)
                DrawTextCustom(nameError, listX + 30, bottomY + 68, 11, new Color(235, 140, 90, 255));
            else
                DrawTextCustom("Название пройдёт проверку", listX + 30, bottomY + 68, 11, new Color(120, 200, 140, 255));

            if (DrawButtonCustom("ЗАПУСТИТЬ LAN", listX + 362, bottomY + 24, 170, 38, Color.Orange)) CreateLocalServer();
            DrawTextCustom("Публичные серверы круглосуточно живут на VPS", listX + 362, bottomY + 68, 11, Color.Gray);

            if (serverShowIp)
            {
                DrawTextCustom("ПРЯМОЕ ПОДКЛЮЧЕНИЕ", listX + 560, bottomY, 15, Color.Gold);
                DrawInputBox("IP:порт", ref connectIpInput, listX + 560, bottomY + 24, 230, 38, ref ipEditing, c => char.IsDigit(c) || c == '.' || c == ':');
                if (DrawButtonCustom("ВОЙТИ", listX + 800, bottomY + 24, 100, 38, Color.Orange)) ConnectToServer(connectIpInput, NetworkManager.GamePort);
                DrawTextCustom("Резервный способ для админов", listX + 560, bottomY + 68, 11, Color.Gray);
            }

            if (!string.IsNullOrWhiteSpace(networkStatus)) DrawTextCustom(networkStatus, listX + 30, listY + listH - 18, 12, Color.Yellow);
        }
        if (DrawButtonCustom("НАЗАД", screenWidth / 2 - 120, screenHeight - 75, 240, 40)) { networkManager.Stop(); NavigateTo(GameState.MainMenu); }
    }

    static void JoinServerByName(ServerEntry server)
    {
        if (server == null || string.IsNullOrWhiteSpace(server.Host))
        {
            networkStatus = "У этого сервера пока нет адреса — попробуйте обновить список.";
            return;
        }
        networkStatus = $"Подключение к «{server.Name}»...";
        ConnectToServer(server.Host, server.Port);
    }

    static void DrawInputBox(string label, ref string value, int x, int y, int w, int h, ref bool editing, Func<char, bool> filter = null)
    {
        Rectangle rect = new Rectangle(x, y, w, h);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
        if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left)) editing = true;
        else if (!hover && Raylib.IsMouseButtonPressed(MouseButton.Left) && editing && !Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect))
        {
            // клик мимо поля закрывает ввод, чтобы фильтр не «съедал» нажатия в меню
            editing = false;
        }

        if (editing)
        {
            int key = Raylib.GetCharPressed();
            while (key > 0)
            {
                char c = (char)key;
                bool allowed = filter != null ? filter(c) : (c >= 32 && c <= 126);
                if (allowed && value.Length < 48) value += c;
                key = Raylib.GetCharPressed();
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && value.Length > 0) value = value.Substring(0, value.Length - 1);
            if (Raylib.IsKeyPressed(KeyboardKey.Enter)) editing = false;
        }
        RR(x, y, w, h, 10, new Color(18, 22, 30, 245));
        RRL(x, y, w, h, 10, 2, editing ? Color.Orange : Color.DarkGray);
        DrawTextCustom(string.IsNullOrEmpty(value) ? label : value, x + 10, y + 9, 16, string.IsNullOrEmpty(value) ? Color.Gray : Color.White);
    }

    static void CreateLocalServer()
    {
        string error = ValidateServerName(serverNameInput);
        if (error != null) { networkStatus = "Название сервера: " + error; return; }
        string name = serverNameInput.Trim();
        if (!networkManager.StartHost(name, 8)) { networkStatus = "Не удалось открыть порт 7777. Возможно, он занят."; return; }
        AbortMission();
        InitGameWorld3D(true); clientWorldPrepared = false; NavigateTo(GameState.LocalLobby);
        discordRpc.SetStatus("Локальный сервер: " + name); networkStatus = "Сервер запущен на порту 7777"; EnableCursor();
    }

    static void ConnectToServer(string address, int defaultPort)
    {
        if (string.IsNullOrWhiteSpace(address)) { networkStatus = "Введите адрес сервера."; return; }

        string host = address.Trim();
        int port = defaultPort;
        int colon = host.LastIndexOf(':');
        if (colon > 0 && colon < host.Length - 1 && int.TryParse(host.Substring(colon + 1), out int parsedPort))
        {
            port = parsedPort;
            host = host.Substring(0, colon);
        }
        if (port < 1 || port > 65535) { networkStatus = "Неверный порт сервера."; return; }

        if (!networkManager.StartClient(host, port)) { networkStatus = "Не удалось начать подключение."; return; }
        InitGameWorld3D(false); clientWorldPrepared = false; NavigateTo(GameState.LocalLobby);
        discordRpc.SetStatus("Подключение к серверу"); networkStatus = $"Подключение к {host}:{port}..."; EnableCursor();
    }

    static void PrepareClientWorld()
    {
        players.Clear(); zombies.Clear(); worldObjects.Clear(); particles.Clear();
        localPlayer.Position = Vector3.Zero; localPlayer.Health = localPlayer.MaxHealth; localPlayer.Inventory.Clear(); players.Add(localPlayer);
        clientWorldPrepared = true; networkStatus = "Подключено к серверу."; discordRpc.SetStatus("В локальной игре");
    }

    static void ProcessNetworkActions()
    {
        if (networkManager.Mode != NetworkManager.NetMode.Host) return;
        foreach (var action in networkManager.DrainPendingActions())
        {
            var player = players.Find(p => p.Id == action.PlayerId); if (player == null) continue;
            if (action.ActionType != NetworkManager.ActAttack) continue;
            // Хост — источник истины: урон считается по тому же каталогу оружия, что и у клиента.
            Ray ray = new Ray(action.Position, Vector3.Normalize(action.Forward));
            ResolveWeaponHit(action.WeaponIndex, ray, player);
        }
    }

    static int GetNextObjectNetId() { int max = 1100; foreach (var o in worldObjects) if (o.NetId >= max) max = o.NetId + 1; return max; }
    static int GetNextZombieNetId() { int max = 2000; foreach (var z in zombies) if (z.NetId >= max) max = z.NetId + 1; return max; }

    static void DrawSettingsMenu()
    {
        DrawMenuPanel(70, 45, screenWidth - 140, screenHeight - 110);
        DrawTextCustom("НАСТРОЙКИ", 100, 72, 30, Color.White);
        DrawTextCustom("Сохраняются автоматически в cube_havoc_settings.json", 100, 108, 13, Color.Gray);

        int x1 = 110, x2 = screenWidth / 2 + 30, y = 155;
        DrawTextCustom($"Чувствительность мыши: {settings.MouseSensitivity:0.00}", x1, y, 16, Color.White);
        if (DrawButtonCustom("−", x1, y + 28, 52, 38)) { settings.MouseSensitivity = Math.Max(0.05f, settings.MouseSensitivity - 0.01f); mouseSensitivity = settings.MouseSensitivity; SaveSettings(); }
        if (DrawButtonCustom("+", x1 + 62, y + 28, 52, 38)) { settings.MouseSensitivity = Math.Min(0.50f, settings.MouseSensitivity + 0.01f); mouseSensitivity = settings.MouseSensitivity; SaveSettings(); }

        DrawTextCustom($"FOV: {settings.Fov:0}", x1, y + 88, 16, Color.White);
        if (DrawButtonCustom("−", x1, y + 116, 52, 38)) { settings.Fov = Math.Max(55, settings.Fov - 5); camera.FovY = settings.Fov; SaveSettings(); }
        if (DrawButtonCustom("+", x1 + 62, y + 116, 52, 38)) { settings.Fov = Math.Min(110, settings.Fov + 5); camera.FovY = settings.Fov; SaveSettings(); }

        DrawTextCustom($"FPS limit: {settings.TargetFps}", x1, y + 176, 16, Color.White);
        if (DrawButtonCustom("60", x1, y + 204, 70, 38)) { settings.TargetFps = 60; Raylib.SetTargetFPS(60); SaveSettings(); }
        if (DrawButtonCustom("120", x1 + 80, y + 204, 70, 38)) { settings.TargetFps = 120; Raylib.SetTargetFPS(120); SaveSettings(); }
        if (DrawButtonCustom("144", x1 + 160, y + 204, 70, 38)) { settings.TargetFps = 144; Raylib.SetTargetFPS(144); SaveSettings(); }

        DrawTextCustom($"Полный экран: {(settings.Fullscreen ? "ВКЛ" : "ВЫКЛ")}", x2, y, 16, Color.White);
        if (DrawButtonCustom(settings.Fullscreen ? "ВЫКЛЮЧИТЬ" : "ВКЛЮЧИТЬ", x2, y + 28, 180, 38)) { Raylib.ToggleFullscreen(); settings.Fullscreen = !settings.Fullscreen; SaveSettings(); }

        DrawTextCustom($"Подсказки HUD: {(settings.ShowHints ? "ВКЛ" : "ВЫКЛ")}", x2, y + 88, 16, Color.White);
        if (DrawButtonCustom("ПЕРЕКЛЮЧИТЬ", x2, y + 116, 180, 38)) { settings.ShowHints = !settings.ShowHints; SaveSettings(); }

        DrawTextCustom($"Эффекты: {(settings.ReducedEffects ? "УПРОЩЕНЫ" : "ПОЛНЫЕ")}", x2, y + 176, 16, Color.White);
        if (DrawButtonCustom("ПЕРЕКЛЮЧИТЬ", x2, y + 204, 180, 38)) { settings.ReducedEffects = !settings.ReducedEffects; SaveSettings(); }

        DrawTextCustom("Профиль игрока", x2, y + 264, 16, Color.Gold);
        if (DrawButtonCustom("СЛУЧАЙНЫЙ ЦВЕТ", x2, y + 292, 180, 38, Color.Orange)) { localPlayer.PlayerColor = GetRandomColor(); SaveSettings(); }

        if (DrawButtonCustom("НАЗАД", screenWidth / 2 - 120, screenHeight - 70, 240, 40)) NavigateTo(GameState.MainMenu);
    }

    static void DrawDevelopersMenu()
    {
        DrawTextCentered("РАЗРАБОТЧИКИ", 90, 26, Color.Orange);
        DrawTextCentered("GitHub: JunDevX", 145, 20, Color.Yellow);
        DrawTextCentered("br br patapim", 178, 20, Color.White);
        DrawTextCentered("Discord: karbyurator7", 211, 20, Color.White);
        DrawTextCentered("Авторская заметка", 270, 18, Color.Gold);
        DrawTextCentered("Некоторые визуальные приёмы и механики вдохновлены современными", 302, 15, Color.LightGray);
        DrawTextCentered("FPS-играми, включая Bodycam. Мы не используем код, модели, текстуры", 325, 15, Color.LightGray);
        DrawTextCentered("или аудио Bodycam и не заявляем о связи с её разработчиками.", 348, 15, Color.LightGray);
        DrawTextCentered("Все ассеты и реализация Cube Havoc должны оставаться оригинальными.", 371, 15, Color.LightGray);
        DrawTextCentered("Эта надпись является информационной и сама по себе не гарантирует", 405, 13, Color.Gray);
        DrawTextCentered("отсутствие юридических претензий — для релиза проверьте права на ассеты.", 424, 13, Color.Gray);
        if (DrawButton("Назад", screenHeight - 90)) NavigateTo(GameState.MainMenu);
    }

    static bool DrawButton(string text, int y) => DrawButtonCustom(text, (screenWidth - 360) / 2, y, 360, 50);

    static bool DrawButtonCustom(string text, int x, int y, int w, int h, Color? borderOverride = null)
    {
        Rectangle rect = new Rectangle(x, y, w, h);
        bool isHovered = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);

        // Плавная анимация наведения (отдельное значение для каждой кнопки).
        int key = HashCode.Combine(x, y, text);
        btnAnim.TryGetValue(key, out float anim);
        anim += ((isHovered ? 1.0f : 0.0f) - anim) * Math.Clamp(Raylib.GetFrameTime() * 14.0f, 0.0f, 1.0f);
        btnAnim[key] = anim;

        float r = Math.Min(h * 0.32f, 16.0f);
        float lift = anim * 2.0f;

        RR(x + 2, y + 5 - lift, w, h, r, new Color(0, 0, 0, 90));
        RR(x, y - lift, w, h, r, LerpColor(new Color(16, 22, 33, 235), new Color(38, 50, 72, 245), anim));
        RR(x + 3, y + 3 - lift, w - 6, h * 0.42f, r * 0.8f, Col(255, 255, 255, (int)(10 + 14 * anim)));

        Color borderColor = borderOverride ?? LerpColor(new Color(65, 78, 100, 255), Color.Orange, anim);
        RRL(x, y - lift, w, h, r, 2.0f + anim, borderColor);
        if (anim > 0.02f)
            RR(x + 8, y + h * 0.25f - lift, 4, h * 0.5f, 2, Col(255, 170, 55, (int)(255 * anim)));

        Vector2 textSize = Raylib.MeasureTextEx(customFont, text, 20, 1);
        Raylib.DrawTextEx(customFont, text, new Vector2(x + (w - textSize.X) / 2, y + (h - textSize.Y) / 2 - lift), 20, 1, LerpColor(Color.LightGray, Color.White, anim));

        return isHovered && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    static void DrawTextCustom(string text, int x, int y, int fontSize, Color color)
    {
        Raylib.DrawTextEx(customFont, text, new Vector2(x, y), fontSize, 1, color);
    }

    static void DrawTextCentered(string text, int y, int fontSize, Color color)
    {
        Vector2 textSize = Raylib.MeasureTextEx(customFont, text, fontSize, 1);
        Raylib.DrawTextEx(customFont, text, new Vector2((screenWidth - textSize.X) / 2, y), fontSize, 1, color);
    }
}