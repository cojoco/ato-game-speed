using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace GameSpeed
{
    // Speeds the game up with Time.timeScale, in single player and co-op.
    // - Single player: the local [Speed] GameSpeed, plus the game's own Ultrafast mode if [Speed] Ultrafast is on.
    // - Co-op room: the host's GameSpeed, published as room property "gs_speed"; every player with this mod runs at it. It must
    //   match on every client: co-op combat waits at sync points for every player, so one fast client only waits for the slow
    //   ones. A host without the mod means 1x. Ultrafast is not used in co-op (the game itself ignores it there).
    // - Connected to Photon but not in a room (region/room list): 1x, so NetworkManager's 15 s connection timeout (scaled
    //   time) keeps its length.
    // The game never writes Time.timeScale itself. Works in any room; MorePlayers is not needed.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.cojoco.gamespeed";
        public const string PluginName = "Game Speed";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<float> Speed;
        internal static ConfigEntry<bool> Ultrafast;

        private void Awake()
        {
            Log = Logger;

            Speed = Config.Bind("Speed", "GameSpeed", 1.7f,
                new ConfigDescription("How fast the game runs (1 = normal). Speeds up animations, enemy turns and the game's own pauses. In co-op only " +
                                      "the host's value counts: every player with Game Speed runs at it while in the room. Can be changed in game " +
                                      "(F5 config manager).", new AcceptableValueRange<float>(1f, 4f)));
            Ultrafast = Config.Bind("Speed", "Ultrafast", false,
                "Single player only: also use the game's hidden Ultrafast mode (skips some effects and pauses entirely). Overrides the game's " +
                "Fast mode option while on. Co-op never uses it.");

            var go = new GameObject("GameSpeed.Runner");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Runner>();

            Log.LogInfo($"{PluginName} {PluginVersion} loaded. GameSpeed={Speed.Value:0.##}x, Ultrafast={Ultrafast.Value}.");
        }

        internal static float Clamp(float f)
        {
            return f < 1f ? 1f : f > 4f ? 4f : f;
        }
    }

    // Plugin objects do not get Update() in this game, so this runs on its own object. Polling every frame covers joining,
    // leaving, disconnects, host changes and config changes without a callback for each.
    internal class Runner : MonoBehaviour
    {
        private const string KeySpeed = "gs_speed";
        private const string KeyVersion = "gs_ver";

        private float applied = -1f;
        private string mode = "";
        private bool ultrafastSet;

        // Co-op bookkeeping, reset on leaving the room.
        private string room;
        private float published = -1f;
        private readonly HashSet<int> checkedPlayers = new HashSet<int>();
        private readonly Dictionary<int, float> firstSeen = new Dictionary<int, float>();

        private void Update()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Game speed: {e}");
                enabled = false;
            }
        }

        private void Tick()
        {
            float want;
            string now;
            bool inRoom = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null;
            if (inRoom)
            {
                Coop();
                object v = PhotonNetwork.CurrentRoom.CustomProperties?[KeySpeed];
                want = v is float f ? Plugin.Clamp(f) : 1f;
                now = v is float ? "co-op, the host's setting" : "co-op, the host has no Game Speed";
            }
            else
            {
                LeftRoom();
                ClientState state = PhotonNetwork.NetworkClientState;
                bool online = state != ClientState.PeerCreated && state != ClientState.Disconnected;
                want = online ? 1f : Plugin.Clamp(Plugin.Speed.Value);
                now = online ? "online, not in a room" : "single player";
            }

            if (want != applied || now != mode || Time.timeScale != want)
            {
                if (want != applied || now != mode)
                {
                    Plugin.Log.LogInfo($"Game speed: {want:0.##}x ({now}).");
                }
                Time.timeScale = want;
                applied = want;
                mode = now;
            }

            SetUltrafast(!inRoom && now == "single player" && Plugin.Ultrafast.Value && GameManager.Instance != null && !GameManager.Instance.IsMultiplayer());
        }

        private void SetUltrafast(bool on)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null)
            {
                return;
            }
            if (on)
            {
                // Re-applied every frame: the settings screen resets configGameSpeed from the Fast mode option when it loads.
                if (gm.configGameSpeed != Enums.ConfigSpeed.Ultrafast)
                {
                    gm.configGameSpeed = Enums.ConfigSpeed.Ultrafast;
                    if (!ultrafastSet)
                    {
                        Plugin.Log.LogInfo("Ultrafast mode on (single player).");
                    }
                }
                ultrafastSet = true;
            }
            else if (ultrafastSet)
            {
                // Back to the game's own Fast mode option, as SettingsManager loads it.
                SaveManager save = SaveManager.Instance;
                bool fast = save != null && save.PrefsHasKey("fastMode") ? save.LoadPrefsBool("fastMode") : gm.configGameSpeed == Enums.ConfigSpeed.Fast;
                gm.configGameSpeed = fast ? Enums.ConfigSpeed.Fast : Enums.ConfigSpeed.Slow;
                ultrafastSet = false;
                Plugin.Log.LogInfo($"Ultrafast mode off: game speed option back to {gm.configGameSpeed}.");
            }
        }

        private void Coop()
        {
            Photon.Realtime.Room r = PhotonNetwork.CurrentRoom;
            if (room != r.Name)
            {
                room = r.Name;
                published = -1f;
                checkedPlayers.Clear();
                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { KeyVersion, Plugin.PluginVersion } });
            }
            if (!PhotonNetwork.IsMasterClient)
            {
                return;
            }
            // The host (also a new host after the old one left) publishes its own speed, again whenever the config changes.
            // Photon applies room properties locally only when the server echoes them, so compare with what we sent, not the room.
            float speed = Plugin.Clamp(Plugin.Speed.Value);
            if (speed != published)
            {
                r.SetCustomProperties(new Hashtable { { KeySpeed, speed } });
                published = speed;
                Plugin.Log.LogInfo($"Co-op game speed set to {speed:0.##}x for every player with Game Speed.");
            }
            // Tell the host (in the log) about players who would hold everyone back.
            foreach (Player p in PhotonNetwork.PlayerList)
            {
                if (p.IsLocal || checkedPlayers.Contains(p.ActorNumber))
                {
                    continue;
                }
                object v = p.CustomProperties?[KeyVersion];
                if (v != null)
                {
                    checkedPlayers.Add(p.ActorNumber);
                    Plugin.Log.LogInfo($"{Nick(p)} has Game Speed {v}.");
                }
                else if (GraceOver(p))
                {
                    checkedPlayers.Add(p.ActorNumber);
                    Plugin.Log.LogWarning($"{Nick(p)} has no Game Speed: they play at 1x and everyone waits for them in combat.");
                }
            }
        }

        // Players announce their version as soon as they join; give them 10 seconds before reporting them as without the mod.
        private bool GraceOver(Player p)
        {
            if (!firstSeen.TryGetValue(p.ActorNumber, out float t))
            {
                t = Time.unscaledTime;
                firstSeen[p.ActorNumber] = t;
            }
            return Time.unscaledTime > t + 10f;
        }

        private static string Nick(Player p)
        {
            return NetworkManager.Instance != null ? NetworkManager.Instance.GetPlayerNickReal(p.NickName) : p.NickName;
        }

        private void LeftRoom()
        {
            if (room == null)
            {
                return;
            }
            room = null;
            published = -1f;
            checkedPlayers.Clear();
            firstSeen.Clear();
        }
    }
}
