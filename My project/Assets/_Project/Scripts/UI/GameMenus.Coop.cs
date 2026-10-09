using System;
using System.Net;
using System.Net.Sockets;
using Backpacking.Character;
using Backpacking.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backpacking.UI
{
    /// <summary>
    /// Co-op in the menus: Join a friend on the title screen (friends hosting on Steam, or an address), and in the
    /// pause menu, inviting friends, hosting by address, and leaving. Accepting a Steam invite comes here too.
    /// </summary>
    public partial class GameMenus
    {
        VisualElement joinPage, friendsList;
        Label titleCoopStatus, coopStatus, joinStatus;
        Button saveButton, inviteButton, hostDirectButton, leaveButton;
        TextField addressField;

        // ---------- Pause menu ----------

        VisualElement BuildCoopPauseSection()
        {
            coopStatus = UIBuild.Text("", "coop-status");
            inviteButton = UIBuild.Button("Invite friends (Steam)", InviteFriends, "menu");
            hostDirectButton = UIBuild.Button("Host by address", HostDirect, "menu", "quiet");
            leaveButton = UIBuild.Button("Leave co-op", LeaveCoop, "menu", "quiet");
            return UIBuild.Box().With(coopStatus, inviteButton, hostDirectButton, leaveButton);
        }

        void RefreshCoop()
        {
            if (coopStatus == null)
                return;
            CoopSession session = CoopSession.Instance;
            bool active = CoopSession.Active;
            coopStatus.text = active ? session.Status
                : session.SteamReady ? $"Hiking alone. Invite up to {CoopSession.MaxPlayers - 1} friends to hike with you."
                : "Hiking alone. Steam isn't running, so there are no invites; hosting by address still works.";
            inviteButton.SetVisible(!CoopSession.IsGuest);
            inviteButton.SetEnabled(session.SteamReady);
            inviteButton.text = session.HasLobby ? "Invite more friends (Steam)" : "Invite friends (Steam)";
            hostDirectButton.SetVisible(!active);
            leaveButton.SetVisible(active);
            leaveButton.text = CoopSession.IsGuest ? "Leave this trip" : "Stop hosting";
            if (titleCoopStatus != null)
            {
                titleCoopStatus.text = active ? "" : session.Status;
                titleCoopStatus.SetVisible(!string.IsNullOrEmpty(titleCoopStatus.text));
            }
        }

        void InviteFriends()
        {
            if (CoopSession.IsGuest)
                return;
            CoopSession.Instance.HostWithSteam(ok =>
            {
                RefreshCoop();
                if (!ok)
                {
                    Notifications.Post(CoopSession.Instance.Status, 6f);
                    return;
                }
                if (!CoopSession.Instance.OpenInviteOverlay())
                    Notifications.Post("Your trip is open to your Steam friends. Steam's invite window only opens when the game is started from Steam, "
                                       + "so ask them to choose Join a friend on the title screen: you'll be listed there.", 12f);
            });
        }

        void HostDirect()
        {
            if (!CoopSession.Instance.HostDirect())
            {
                Notifications.Post(CoopSession.Instance.Status, 5f);
                return;
            }
            Notifications.Post($"Hosting on port {CoopSession.DefaultPort}. Friends join with your address, {LocalAddress()}, "
                               + "from Join a friend on the title screen (over the internet, the port has to be forwarded to this PC).", 12f);
            RefreshCoop();
        }

        void LeaveCoop()
        {
            if (CoopSession.IsGuest)
                Confirm("Leave your friends' trip and go back to the title screen? The host keeps your hiker and gear in their trip "
                        + "(once they save), and you'll pick up where you left off when you rejoin.", saves.ReturnToTitle);
            else
                Confirm("Stop hosting? Everyone else leaves the trip; you carry on alone.", () =>
                {
                    CoopSession.Instance.Leave();
                    Notifications.Post("You're hiking alone again.", 4f);
                });
        }

        static string LocalAddress()
        {
            try
            {
                foreach (IPAddress address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                        return address.ToString();
            }
            catch (SocketException)
            {
            }
            return "127.0.0.1";
        }

        // ---------- Join a friend ----------

        VisualElement BuildJoinPage()
        {
            friendsList = UIBuild.Box();
            joinStatus = UIBuild.Text("", "coop-status");
            addressField = new TextField { value = "127.0.0.1" };
            addressField.AddToClassList("address-field");
            VisualElement panel = UIBuild.Box("panel", "menu-panel").With(
                UIBuild.Text("Join a friend", "title"),
                UIBuild.Text("Friends on Steam who are hosting a trip:", "text"),
                friendsList,
                UIBuild.Button("Refresh", RefreshFriends, "menu", "quiet"),
                UIBuild.Text("Or join by address (on the same network, or a second copy of the game on this PC):", "text"),
                UIBuild.Box("friend-row").With(addressField, UIBuild.Button("Join", () => JoinByAddress(addressField.value), "primary")),
                joinStatus,
                UIBuild.Box("footer").With(UIBuild.Button("Back", Back)));
            panel.style.width = 640f;
            return panel;
        }

        void ShowJoin()
        {
            ShowPage(Page.Join);
            RefreshFriends();
        }

        void RefreshFriends()
        {
            friendsList.Clear();
            CoopSession session = CoopSession.Instance;
            joinStatus.text = session.Status;
            if (!session.SteamReady)
            {
                friendsList.Add(UIBuild.Text("Steam isn't running. Start Steam, then restart the game to join friends there.", "small"));
                return;
            }
            var friends = session.JoinableFriends();
            if (friends.Count == 0)
                friendsList.Add(UIBuild.Text("No friends are hosting right now. When one opens their trip (pause menu, Invite friends), "
                                             + "they'll show up here; or accept their invite in Steam.", "small"));
            foreach (JoinableFriend friend in friends)
            {
                ulong lobbyId = friend.lobby;
                friendsList.Add(UIBuild.Box("friend-row").With(
                    UIBuild.Text(friend.name, "text"),
                    UIBuild.Button("Join", () => JoinLobby(lobbyId), "primary")));
            }
        }

        void JoinLobby(ulong lobbyId)
        {
            CoopSession.PendingJoin = 0;
            StartAsGuest(() => CoopSession.Instance.JoinSteamLobby(lobbyId, ok =>
            {
                if (!ok)
                    Notifications.Post($"{CoopSession.Instance.Status} You're on a trip of your own for now.", 8f);
            }));
        }

        void JoinByAddress(string address)
        {
            StartAsGuest(() =>
            {
                if (!CoopSession.Instance.JoinDirect(address))
                    Notifications.Post($"{CoopSession.Instance.Status} You're on a trip of your own for now.", 8f);
            });
        }

        /// <summary>Make (or pick) a hiker, start fresh at home with your own money, then connect to the host.</summary>
        void StartAsGuest(Action connect)
        {
            void Begin(CharacterProfile hiker)
            {
                BeginTrip(hiker, freshStart: true, guest: true);
                connect();
            }
            if (creator == null)
            {
                Begin(avatar != null ? avatar.Profile : new CharacterProfile());
                return;
            }
            ShowPage(Page.Creator);
            creator.Open(Begin, ShowJoin);
        }

        /// <summary>A friend's invite was accepted in Steam while the game is running.</summary>
        void OnJoinRequested(ulong lobbyId)
        {
            if (onTitle)
            {
                JoinLobby(lobbyId);
                return;
            }
            Confirm("Join your friend's trip? You'll leave this one, and progress since your last save will be lost.", () =>
            {
                // The title screen picks the invite up after the reload.
                CoopSession.PendingJoin = lobbyId;
                saves.ReturnToTitle();
            });
        }

        /// <summary>The host left (or the connection dropped): back to the title, which says why.</summary>
        void OnCoopEnded(string reason)
        {
            Notifications.Post(reason, 6f);
            saves.ReturnToTitle();
        }
    }
}
