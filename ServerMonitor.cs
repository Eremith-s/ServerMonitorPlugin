
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

using Facepunch;
using Newtonsoft.Json;
using Oxide.Core.Plugins;
using Rust;

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ServerMonitor", "DeltaDinizzz", "0.0.1")]
    public class ServerStats : CovalencePlugin
    {
        // ##StartModule - Global Variables & Config Properties
        private static ServerStats _instance;
        private FPSVisor ActiveVisor;
        private int MinimalFPS = 9999;
        
        // --- CONFIG ---
        // Altere a URL para o endereço de produção da sua API Next.js na Vercel
        private string ApiUrl => (string)Config["ApiUrl"];
        private string Password => (string)Config["Password"];
        
        // --- DYNAMIC INTERVALS (Provided by the Web API) ---
        private float _updateInterval = 2.0f;
        private float _sleepInterval = 60.0f;

        private Timer _loopTimer;
        private bool _isSleeping = false;
        // ##EndModule - Global Variables & Config Properties

        // ##StartModule - Oxide Hooks
        protected override void LoadDefaultConfig()
        {
            Config["ApiUrl"] = "https://rustcenter.org/api/server-monitor/ingest";
            Config["Password"] = UnityEngine.Random.Range(1000, 999999).ToString();
            
            LogWarning("Config file ServerStats.json was generated. Your new server password is: " + Config["Password"]);
            
            Config.Save();
        }

        private void Init()
        {
            _instance = this;
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
                password = Password,
                fps = Performance.current.frameRate,
                minfps = currentMinFps,
                ent = BaseNetworkable.serverEntities.Count,
                online = players.Connected.Count(),
                SleepPlayer = BasePlayer.sleepingPlayerList.Count,
                JoiningPlayer = ServerMgr.Instance.connectionQueue.Joining,
                QueuedPlayer = ServerMgr.Instance.connectionQueue.Queued,
                listPlugins = pluginItems,
                // Avisamos a API sobre o nosso estado atual, para ele saber se acabamos de acordar
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
                float nextDelay = _updateInterval; // Padrão dinâmico na memória (inicialmente 2s)

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
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    // Em caso de erro na rede ou erro 500 na Vercel, entra em sleep forçado para não floodar
                    _isSleeping = true;
                    nextDelay = _sleepInterval;
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