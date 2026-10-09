using System;
using System.Collections.Generic;
using System.Text;
using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Backpacking.Net
{
    public enum CoopRole
    {
        None,
        Host,
        Guest,
    }

    /// <summary>A friend on Steam who's hosting a trip you could join.</summary>
    public struct JoinableFriend
    {
        public string name;
        public ulong lobby;
    }

    /// <summary>
    /// Co-op: up to four hikers on one trip. The host's game runs the world (the clock, the weather, the doors, the
    /// truck when it's parked); each player's game runs their own hiker (vitals, pack, money) and shows everyone
    /// else's (<see cref="CoopHiker"/>, <see cref="CoopWorld"/>). Friends join through Steam: the host opens the trip
    /// to friends (a friends-only lobby) and invites them, or they pick the host from Join a friend; Steam carries the
    /// game's traffic, so no one forwards ports. Direct connection by address is there too, for testing on one PC.
    /// Lives for the whole run (it survives scene reloads); made on first use.
    /// </summary>
    public class CoopSession : MonoBehaviour
    {
        public const int MaxPlayers = 4;
        /// <summary>Valve's shared test app (Spacewar) until the game has its own Steam app ID.</summary>
        public const uint SteamAppId = 480;
        public const ushort DefaultPort = 7777;
        const string GameKey = "backpacking";
        const string HikerPrefabPath = "Net/CoopHiker";
        const string WorldPrefabPath = "Net/CoopWorld";

        static CoopSession instance;

        NetworkManager network;
        FacepunchTransport steamTransport;
        UnityTransport directTransport;
        GameObject worldPrefab;
        Lobby? lobby;
        bool steamReady;
        bool leaving;
        Action afterLeaving;

        /// <summary>The session, made (and Steam started) the first time anything asks.</summary>
        public static CoopSession Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("Co-op");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<CoopSession>();
                }
                return instance;
            }
        }

        /// <summary>Playing with others right now (hosting, or joined to a host).</summary>
        public static bool Active => instance != null && instance.network != null && instance.network.IsListening && !instance.leaving
                                     && !instance.network.ShutdownInProgress;
        public static CoopRole Role => !Active ? CoopRole.None : instance.network.IsServer ? CoopRole.Host : CoopRole.Guest;
        public static bool IsHost => Role == CoopRole.Host;
        public static bool IsGuest => Role == CoopRole.Guest;
        /// <summary>This player's id in the session (0 for the host).</summary>
        public static ulong LocalId => Active ? instance.network.LocalClientId : 0;
        public static int PlayerCount => !Active ? 1 : instance.network.IsServer ? instance.network.ConnectedClientsIds.Count : CoopHiker.All.Count;

        /// <summary>Steam is running and signed in, so friends can be invited.</summary>
        public bool SteamReady => steamReady && SteamClient.IsValid;
        public string SteamName => SteamReady ? SteamClient.Name : null;
        /// <summary>What's happening, for the menus ("Hosting · 2 of 4 hikers", "Couldn't reach the host").</summary>
        public string Status { get; private set; } = "";
        public bool HasLobby => lobby.HasValue;

        /// <summary>Someone accepted an invite or chose Join from Steam: the lobby to join, once the menus are ready.</summary>
        public static ulong PendingJoin { get; set; }
        /// <summary>Raised when an invite is accepted (from Steam's friends list or overlay) while the game is running.</summary>
        public static event Action<ulong> JoinRequested;
        /// <summary>Raised on the guest when the session ends under them (the host left, or the connection dropped), with why.</summary>
        public static event Action<string> Ended;
        public static event Action StatusChanged;
        /// <summary>Raised on a guest just before it leaves the trip, while there's still time to send the host something.</summary>
        public static event Action Leaving;

        /// <summary>
        /// Who this player is, the same every time they play (made once and kept in the settings): the host keeps a
        /// guest's hiker and gear under it. "-coop-key name" on the command line overrides it, for testing.
        /// </summary>
        public static string HikerKey
        {
            get
            {
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-coop-key");
                if (at >= 0 && at + 1 < args.Length)
                    return args[at + 1];
                string key = PlayerPrefs.GetString("coop.hikerKey", "");
                if (string.IsNullOrEmpty(key))
                {
                    key = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString("coop.hikerKey", key);
                    PlayerPrefs.Save();
                }
                return key;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
            PendingJoin = 0;
            JoinRequested = null;
            Ended = null;
            StatusChanged = null;
            Leaving = null;
        }

        /// <summary>Starts Steam with the game, so invites reach it, and picks up "+connect_lobby" from a launch by invite.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            _ = Instance;
            Camp.CampOwner.LocalKey = HikerKey;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong id))
                    PendingJoin = id;
        }

        void Awake()
        {
            StartSteam();
            BuildNetwork();
        }

        void StartSteam()
        {
            if (SteamClient.IsValid)
            {
                steamReady = true;
                return;
            }
            try
            {
                SteamClient.Init(SteamAppId, asyncCallbacks: false);
                steamReady = SteamClient.IsValid;
            }
            catch (Exception exception)
            {
                // Steam isn't running (or isn't installed): co-op by address still works.
                steamReady = false;
                Debug.Log($"Steam isn't available, so no Steam invites this run: {exception.Message}");
            }
            if (!steamReady)
                return;
            SteamFriends.OnGameLobbyJoinRequested -= OnLobbyJoinRequested;
            SteamFriends.OnGameLobbyJoinRequested += OnLobbyJoinRequested;
            SteamFriends.OnGameRichPresenceJoinRequested -= OnRichPresenceJoinRequested;
            SteamFriends.OnGameRichPresenceJoinRequested += OnRichPresenceJoinRequested;
        }

        void BuildNetwork()
        {
            var hikerPrefab = Resources.Load<GameObject>(HikerPrefabPath);
            worldPrefab = Resources.Load<GameObject>(WorldPrefabPath);
            if (hikerPrefab == null || worldPrefab == null)
            {
                Debug.LogWarning($"Co-op prefabs are missing from Resources/{HikerPrefabPath} and {WorldPrefabPath}: run Backpacking > Build Prototype Scene.");
                return;
            }
            steamTransport = gameObject.AddComponent<FacepunchTransport>();
            directTransport = gameObject.AddComponent<UnityTransport>();
            // Someone joining gets the whole camp in one message (every patch of brush cut, say): more than the default 6 KB.
            directTransport.MaxPayloadSize = 512 * 1024;
            network = gameObject.AddComponent<NetworkManager>();
            network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = steamTransport,
                PlayerPrefab = hikerPrefab,
                // Everyone has the same scene already; nothing is loaded over the network.
                EnableSceneManagement = false,
                ConnectionApproval = true,
                TickRate = 30,
                ConnectionData = Encoding.UTF8.GetBytes($"{GameKey}|{Application.version}"),
            };
            network.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = hikerPrefab });
            network.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = worldPrefab });
            network.ConnectionApprovalCallback = Approve;
            network.OnClientConnectedCallback += OnClientConnected;
            network.OnClientDisconnectCallback += OnClientDisconnected;
            network.OnTransportFailure += () => EndAsGuest("The connection failed.");
        }

        void Update()
        {
            bool running = network != null && network.IsListening;
            // Shutting down takes a frame or two; until it's done, nothing counts as the session ending under us.
            if (leaving && !running)
                leaving = false;
            if (running)
                return;
            if (afterLeaving != null)
            {
                Action then = afterLeaving;
                afterLeaving = null;
                then();
            }
            // The Steam transport shuts Steam down with it: start it again for the next invite.
            if (steamReady && !SteamClient.IsValid)
                StartSteam();
            // The transport pumps Steam while a session runs; between sessions, this does (invites, lobbies).
            if (SteamClient.IsValid)
                SteamClient.RunCallbacks();
        }

        void OnApplicationQuit()
        {
            Leave();
            if (SteamClient.IsValid)
                SteamClient.Shutdown();
        }

        // ---------- Hosting ----------

        /// <summary>Opens this trip to Steam friends: a friends-only lobby they can join from an invite or their friends list.</summary>
        public async void HostWithSteam(Action<bool> done = null)
        {
            if (Active)
            {
                done?.Invoke(true);
                return;
            }
            if (!SteamReady)
            {
                SetStatus("Steam isn't running. Start Steam and restart the game to invite friends.");
                done?.Invoke(false);
                return;
            }
            if (!StartHost(steamTransport))
            {
                done?.Invoke(false);
                return;
            }
            SetStatus("Opening your trip to friends…");
            Lobby? made = await SteamMatchmaking.CreateLobbyAsync(MaxPlayers);
            if (!made.HasValue)
            {
                SetStatus("Steam couldn't make a lobby. You're still hosting: friends can't find you until it works, so try again.");
                done?.Invoke(true);
                return;
            }
            Lobby open = made.Value;
            open.SetFriendsOnly();
            open.SetJoinable(true);
            open.SetData("game", GameKey);
            open.SetData("version", Application.version);
            open.SetData("host", SteamClient.Name);
            lobby = open;
            // "Join game" in the Steam friends list launches (or tells) the friend's game with this.
            SteamFriends.SetRichPresence("connect", $"+connect_lobby {open.Id.Value}");
            SteamFriends.SetRichPresence("status", "Hiking with friends");
            UpdateHostStatus();
            done?.Invoke(true);
        }

        /// <summary>Hosts by address (LAN, or one PC for testing): others join with this PC's IP and the port.</summary>
        public bool HostDirect(ushort port = DefaultPort)
        {
            if (Active || directTransport == null)
                return Active;
            directTransport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            return StartHost(directTransport);
        }

        bool StartHost(NetworkTransport transport)
        {
            if (network == null)
            {
                SetStatus("Co-op isn't set up in this build.");
                return false;
            }
            leaving = false;
            network.NetworkConfig.NetworkTransport = transport;
            if (!network.StartHost())
            {
                SetStatus("Couldn't start hosting.");
                return false;
            }
            Application.runInBackground = true;
            Instantiate(worldPrefab).GetComponent<NetworkObject>().Spawn();
            UpdateHostStatus();
            return true;
        }

        /// <summary>Opens Steam's invite window for the lobby (only when the overlay works: the game launched from Steam).</summary>
        public bool OpenInviteOverlay()
        {
            if (!lobby.HasValue || !SteamReady)
                return false;
            SteamFriends.OpenGameInviteOverlay(lobby.Value.Id);
            return SteamUtils.IsOverlayEnabled;
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            string payload = request.Payload != null ? Encoding.UTF8.GetString(request.Payload) : "";
            string expected = $"{GameKey}|{Application.version}";
            response.CreatePlayerObject = true;
            if (request.ClientNetworkId != NetworkManager.ServerClientId && network.ConnectedClientsIds.Count >= MaxPlayers)
            {
                response.Approved = false;
                response.Reason = $"The trip is full ({MaxPlayers} hikers).";
            }
            else if (request.ClientNetworkId != NetworkManager.ServerClientId && payload != expected)
            {
                response.Approved = false;
                response.Reason = "The host is playing a different version of the game.";
            }
            else
                response.Approved = true;
        }

        void OnClientConnected(ulong clientId)
        {
            if (network.IsServer)
            {
                UpdateHostStatus();
                if (lobby.HasValue)
                    lobby.Value.SetJoinable(network.ConnectedClientsIds.Count < MaxPlayers);
            }
            else if (clientId == network.LocalClientId)
                SetStatus("Joined the trip.");
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (network.IsServer)
            {
                UpdateHostStatus();
                if (lobby.HasValue)
                    lobby.Value.SetJoinable(network.ConnectedClientsIds.Count < MaxPlayers);
                return;
            }
            // On a guest, this is our own connection going (refused, dropped, or the host leaving).
            string reason = string.IsNullOrEmpty(network.DisconnectReason) ? "The host ended the trip, or the connection dropped." : network.DisconnectReason;
            EndAsGuest(reason);
        }

        void UpdateHostStatus()
        {
            if (network == null || !network.IsServer)
                return;
            int count = network.ConnectedClientsIds.Count;
            string how = lobby.HasValue ? "open to Steam friends" : steamTransport != null && network.NetworkConfig.NetworkTransport == steamTransport
                ? "on Steam" : $"by address, port {directTransport.ConnectionData.Port}";
            SetStatus($"Hosting, {how} · {count} of {MaxPlayers} hikers");
        }

        // ---------- Joining ----------

        /// <summary>The friends on Steam who are hosting a trip right now.</summary>
        public List<JoinableFriend> JoinableFriends()
        {
            var found = new List<JoinableFriend>();
            if (!SteamReady)
                return found;
            foreach (Friend friend in SteamFriends.GetFriends())
            {
                if (!friend.IsPlayingThisGame)
                    continue;
                Friend.FriendGameInfo? game = friend.GameInfo;
                if (game.HasValue && game.Value.Lobby.HasValue)
                    found.Add(new JoinableFriend { name = friend.Name, lobby = game.Value.Lobby.Value.Id.Value });
            }
            return found;
        }

        /// <summary>Joins a friend's trip through their Steam lobby.</summary>
        public async void JoinSteamLobby(ulong lobbyId, Action<bool> done = null)
        {
            if (Active || network == null || !SteamReady)
            {
                SetStatus(!SteamReady ? "Steam isn't running." : Active ? "Already in a co-op trip." : "Co-op isn't set up in this build.");
                done?.Invoke(false);
                return;
            }
            SetStatus("Finding your friend's trip…");
            Lobby? joined = await SteamMatchmaking.JoinLobbyAsync(lobbyId);
            if (!joined.HasValue)
            {
                SetStatus("Couldn't join: the trip is gone or full.");
                done?.Invoke(false);
                return;
            }
            Lobby found = joined.Value;
            if (found.GetData("game") != GameKey)
            {
                found.Leave();
                SetStatus("That isn't a Backpacking trip.");
                done?.Invoke(false);
                return;
            }
            if (found.GetData("version") != Application.version)
            {
                found.Leave();
                SetStatus($"Your friend is on version {found.GetData("version")}, you're on {Application.version}.");
                done?.Invoke(false);
                return;
            }
            lobby = found;
            steamTransport.targetSteamId = found.Owner.Id.Value;
            done?.Invoke(StartClient(steamTransport, $"Joining {found.Owner.Name}'s trip…"));
        }

        /// <summary>Joins by address ("192.168.1.5" or "192.168.1.5:7777").</summary>
        public bool JoinDirect(string address)
        {
            if (Active || directTransport == null)
                return false;
            string host = address.Trim();
            ushort port = DefaultPort;
            int colon = host.LastIndexOf(':');
            if (colon > 0 && ushort.TryParse(host.Substring(colon + 1), out ushort parsed))
            {
                port = parsed;
                host = host.Substring(0, colon);
            }
            if (host.Length == 0)
                host = "127.0.0.1";
            directTransport.SetConnectionData(host, port);
            return StartClient(directTransport, $"Connecting to {host}:{port}…");
        }

        bool StartClient(NetworkTransport transport, string status)
        {
            leaving = false;
            network.NetworkConfig.NetworkTransport = transport;
            Application.runInBackground = true;
            if (!network.StartClient())
            {
                SetStatus("Couldn't start connecting.");
                return false;
            }
            SetStatus(status);
            return true;
        }

        void OnLobbyJoinRequested(Lobby requested, SteamId friend) => RequestJoin(requested.Id.Value);

        void OnRichPresenceJoinRequested(Friend friend, string connect)
        {
            string[] parts = connect.Split(' ');
            for (int i = 0; i < parts.Length - 1; i++)
                if (parts[i] == "+connect_lobby" && ulong.TryParse(parts[i + 1], out ulong id))
                    RequestJoin(id);
        }

        static void RequestJoin(ulong lobbyId)
        {
            if (Active)
                return;
            PendingJoin = lobbyId;
            JoinRequested?.Invoke(lobbyId);
        }

        // ---------- Leaving ----------

        /// <summary>
        /// Ends the session, then does <paramref name="then"/> once it has fully shut down (a frame or two): say, reloading
        /// the scene, which mustn't destroy the session's objects before the network has let go of them.
        /// </summary>
        public void Leave(Action then)
        {
            bool running = network != null && network.IsListening;
            Leave();
            if (running)
                afterLeaving = then;
            else
                then?.Invoke();
        }

        /// <summary>Ends the session: a guest leaves the trip, a host carries on alone (everyone else is dropped).</summary>
        public void Leave()
        {
            if (lobby.HasValue)
            {
                lobby.Value.Leave();
                lobby = null;
            }
            if (SteamClient.IsValid)
                SteamFriends.ClearRichPresence();
            if (network != null && network.IsListening)
            {
                // The messages already queued (a guest's hiker, say) still go before the connection closes.
                if (!network.IsServer && !leaving)
                    Leaving?.Invoke();
                leaving = true;
                network.Shutdown();
            }
            CoopHiker.ClearAll();
            SetStatus("");
        }

        void EndAsGuest(string reason)
        {
            if (leaving)
                return;
            bool wasGuest = network != null && !network.IsServer;
            // Tell the game once the network has let go (it goes back to the title, reloading the scene).
            Leave(() =>
            {
                SetStatus(reason);
                if (wasGuest)
                    Ended?.Invoke(reason);
            });
        }

        void SetStatus(string text)
        {
            Status = text;
            StatusChanged?.Invoke();
        }
    }
}
