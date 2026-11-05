using System;
using DiscordRPC;
using DiscordRPC.Logging;

namespace CubeHavoc
{
    public class DiscordRPCManager : IDisposable
    {
        private DiscordRpcClient client;
        private const string ApplicationId = "1548651703048605757"; // Замени на свой Client ID из Discord Developer Portal

        public void Initialize()
        {
            client = new DiscordRpcClient(ApplicationId)
            {
                Logger = new ConsoleLogger() { Level = LogLevel.Warning }
            };

            // Подключаемся к Discord Client
            client.Initialize();

            // Устанавливаем стартовый статус (например, "В меню")
            SetStatus("В меню");
        }

        /// <summary>
        /// Обновляет статус игрока в Discord (например: "В меню", "В одиночной игре", "В локальной игре")
        /// </summary>
        /// <param name="stateText">Текст статуса (В меню / В одиночной игре / В локальной игре)</param>
        public void SetStatus(string stateText)
        {
            if (client == null || client.IsDisposed) return;

            client.SetPresence(new RichPresence()
            {
                Details = "Cube Havoc",
                State = stateText,
                Assets = new Assets()
                {
                    LargeImageKey = "logo", // Имя картинки в Discord Developer Portal (если загружена)
                    LargeImageText = "Cube Havoc"
                },
                Buttons = new Button[]
                {
                    new Button()
                    {
                        Label = "Discord",
                        Url = "https://discord.gg/zm5p6eGu56"
                    }
                },
                Timestamps = Timestamps.Now // Показывает плашку "Играет 00:05"
            });
        }

        public void Update()
        {
            // Необходимый вызов для обработки событий Discord
            client?.Invoke();
        }

        public void Deinitialize()
        {
            if (client != null && !client.IsDisposed)
            {
                client.ClearPresence();
                client.Dispose();
            }
        }

        public void Dispose()
        {
            Deinitialize();
        }
    }
}