
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

using Rust;
using Facepunch;
using UnityEngine;

using Newtonsoft.Json;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries;

namespace Oxide.Plugins
{
    [Info("ServerMonitor", "DeltaDinizzz", "0.0.2")]
    public class ServerMonitor : CovalencePlugin
    {
        // ##StartModule - Global Variables & Config Properties
        private static ServerMonitor _instance;
        private FPSVisor ActiveVisor;
        private int MinimalFPS = 9999;
        
        // --- CONFIG ---
        // Altere a URL para o endereço de produção da sua API Next.js na Vercel
        private const string ApiUrl = "https://rustcenter.org/api/server-monitor/ingest";
        private string ServerToken => (string)Config["ServerToken"];
        
        // --- DYNAMIC INTERVALS (Provided by the Web API) ---
        private float _updateInterval = 2.0f;
        private float _sleepInterval = 60.0f;

        private Timer _loopTimer;
        private bool _isSleeping = false;
        private const float MinErrorRetrySeconds = 10.0f;
        // ##EndModule - Global Variables & Config Properties

        // ##StartModule - Oxide Hooks
        protected override void LoadDefaultConfig()
        {
            Config["ServerToken"] = "SM-" + Guid.NewGuid().ToString("N");
            SaveConfig();
        }

        private void Init()
        {
            _instance = this;
            if (Config["ServerToken"] == null || string.IsNullOrEmpty((string)Config["ServerToken"]))
            {
                LoadDefaultConfig();
            }

            Puts("======================================================");
            Puts(" RustCenter Server Monitor Initialized!");
            Puts($" Your Web Dashboard Token is: {ServerToken}");
            Puts(" Use this Token in the Website to control your server.");
            Puts("======================================================");
        }


        private void OnServerInitialized()
        {
            // Adiciona o script para monitorar o menor FPS
            ActiveVisor = Terrain.activeTerrain.gameObject.AddComponent<FPSVisor>();

            // Kickstart no loop
            SendServerStatsTick();
        }

        private void Unload()
        {
            if (ActiveVisor != null)
            {
                UnityEngine.Object.Destroy(ActiveVisor);
            }
            if (_loopTimer != null)
            {
                _loopTimer.Destroy();
            }
        }
        // ##EndModule - Oxide Hooks

        // ##StartModule - Core Server Tick Logic
        private void SendServerStatsTick()
        {
            // Coleta plugins
            var listPlugins = plugins.PluginManager.GetPlugins().ToArray();
            var pluginItems = new List<object>();

            for (var i = 0; i < listPlugins.Length; i++)
            {
                pluginItems.Add(new
                {
                    name = listPlugins[i].Name,
                    version = listPlugins[i].Version.ToString(),
                    author = listPlugins[i].Author,
                    hash = listPlugins[i].Name.GetHashCode(),
                    time = listPlugins[i].TotalHookTime.TotalSeconds
                });
            }

            int currentMinFps = MinimalFPS;
            MinimalFPS = 9999; // Reseta depois de ler

            // Monta o Payload principal
            var payload = new
            {
                method = "tick_server",
                serverName = server.Name,
                serverIp = server.Address + ":" + server.Port,
                token = ServerToken,
                fps = Performance.current.frameRate,
                minfps = currentMinFps,
                ent = BaseNetworkable.serverEntities.Count,
                online = players.Connected.Count(),
                maxPlayers = server.MaxPlayers,
                SleepPlayer = BasePlayer.sleepingPlayerList.Count,
                JoiningPlayer = ServerMgr.Instance.connectionQueue.Joining,
                QueuedPlayer = ServerMgr.Instance.connectionQueue.Queued,
                uptime = (int)UnityEngine.Time.realtimeSinceStartup,
                version = server.Version,
                map = ConVar.Server.level,
                listPlugins = pluginItems,
                isSleeping = _isSleeping
            };

            string jsonPayload = JsonConvert.SerializeObject(payload);
            
            // Cabeçalho básico
            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" }
            };

            // Envia o HTTP POST via WebRequest
            webrequest.Enqueue(ApiUrl, jsonPayload, (code, response) =>
            {
                float nextDelay = _isSleeping ? _sleepInterval : _updateInterval;

                if (code == 200 || code == 204)
                {
                    try
                    {
                        var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                        
                        // Captura novos tempos enviador pelo Admin Panel da Dashboard Web
                        if (jsonResponse != null)
                        {
                            if (jsonResponse.ContainsKey("updateInterval"))
                            {
                                float.TryParse(jsonResponse["updateInterval"].ToString(), out _updateInterval);
                            }
                            if (jsonResponse.ContainsKey("sleepInterval"))
                            {
                                float.TryParse(jsonResponse["sleepInterval"].ToString(), out _sleepInterval);
                            }

                            // Se a API informou state "sleep", ninguém está online no site.
                            if (jsonResponse.ContainsKey("state"))
                            {
                                string state = jsonResponse["state"].ToString();
                                if (state == "sleep")
                                {
                                    if (!_isSleeping)
                                        Puts($"[ServerMonitor] Ninguém na dashboard. Entrando em Sleep Mode ({_sleepInterval}s) para economizar recursos...");
                                    
                                    _isSleeping = true;
                                    nextDelay = _sleepInterval;
                                }
                                else if (state == "active")
                                {
                                    if (_isSleeping)
                                        Puts($"[ServerMonitor] Dashboard aberta/detectada. Saindo do Sleep Mode! Atualizando a cada {_updateInterval}s...");
                                    
                                    _isSleeping = false;
                                    nextDelay = _updateInterval;
                                }
                                else if (state == "degraded")
                                {
                                    _isSleeping = false;
                                    nextDelay = Math.Max(_updateInterval, MinErrorRetrySeconds);
                                }
                            }
                            // C# Native RCON-less execution 
                            if (jsonResponse.ContainsKey("pendingCommands"))
                            {
                                var commandsList = jsonResponse["pendingCommands"] as Newtonsoft.Json.Linq.JArray;
                                if (commandsList != null)
                                {
                                    foreach (var command in commandsList)
                                    {
                                        string cmdRaw = command.ToString();
                                        if (!string.IsNullOrEmpty(cmdRaw))
                                        {
                                            Puts($"[ServerMonitor] Executing remote command: {cmdRaw}");
                                            ConsoleSystem.Run(ConsoleSystem.Option.Server, cmdRaw);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    // Em erro transitório de rede/proxy, aplica backoff curto (não entra em sleep longo).
                    // Sleep real deve ser controlado pelo state "sleep" vindo da API.
                    float retryDelay = Mathf.Clamp(Math.Max(_updateInterval * 2.0f, MinErrorRetrySeconds), MinErrorRetrySeconds, _sleepInterval);
                    Puts($"[ServerMonitor] HTTP Error {code} - Response: {(response ?? "null")}. Retrying in {retryDelay:0.0}s...");
                    _isSleeping = false;
                    nextDelay = retryDelay;
                }

                // Agenda a próxima execução
                _loopTimer = timer.Once(nextDelay, SendServerStatsTick);

            }, this, RequestMethod.POST, headers);
        }
        // ##EndModule - Core Server Tick Logic

        // ##StartModule - FPSVisor Component
        public class FPSVisor : MonoBehaviour
        {
            private void Update()
            {
                if (_instance.MinimalFPS > (int)global::Performance.current.frameRate)
                {
                    _instance.MinimalFPS = (int)global::Performance.current.frameRate;
                }
            }
        }
        // ##EndModule - FPSVisor Component
    }
}