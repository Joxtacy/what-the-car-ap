using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;

namespace WtcArchipelago;

/// <summary>
/// Owns the Archipelago session: connect/login, send location checks, receive
/// items.
///
/// The threading rules here are load-bearing and were learned the hard way on
/// the WHAT THE GOLF? mod -- do not "simplify" them:
///   * AP callbacks arrive OFF Unity's main thread, so anything that touches the
///     game is queued and drained by Tick() from the main-thread pump.
///   * Sends go OUT on the ThreadPool, never synchronously from the game thread.
///     After a socket closes, the Session object lingers but its socket is dead,
///     and sending into a dead socket BLOCKS the caller -- which, on the main
///     thread, freezes the game.
/// </summary>
public class ArchipelagoClient
{
    public enum ConnState { Disconnected, Connecting, Connected, Failed }

    public ArchipelagoData Data { get; } = new();
    public ArchipelagoSession Session { get; private set; }

    /// <summary>Current connection state (drives the passive-until-connected gate).</summary>
    public ConnState State { get; private set; } = ConnState.Disconnected;
    /// <summary>Human-readable status, for the connection UI when it exists.</summary>
    public string StatusMessage { get; private set; } = "Not connected";
    public bool Connected => State == ConnState.Connected;

    private readonly ConcurrentQueue<Action> _mainThread = new();

    /// <summary>Drain queued game-side effects. Call ONLY from the main thread.</summary>
    public void Tick()
    {
        while (_mainThread.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Plugin.Log.LogError($"main-thread action failed: {e}"); }
        }
    }

    public void Connect(string host, int port, string slot, string password = null)
    {
        if (State == ConnState.Connecting || State == ConnState.Connected)
        {
            Plugin.Log.LogWarning("AP already connected/connecting -- disconnect first.");
            return;
        }
        Data.Host = host; Data.Port = port; Data.SlotName = slot;
        Data.Password = string.IsNullOrEmpty(password) ? null : password;

        State = ConnState.Connecting;
        StatusMessage = $"Connecting to {host}:{port} as {slot}...";

        // Connect off-thread so we never block Unity's main loop.
        ThreadPool.QueueUserWorkItem(_ => ConnectImpl());
    }

    /// <summary>Drop the AP session and return the mod to passive/vanilla behaviour.</summary>
    public void Disconnect()
    {
        try
        {
            if (Session != null)
                try { Session.Socket.DisconnectAsync(); } catch { }
        }
        finally
        {
            State = ConnState.Disconnected;
            StatusMessage = "Disconnected";
            Plugin.Log.LogInfo("AP disconnected -- mod is passive (vanilla) until you reconnect.");
        }
    }

    private void ConnectImpl()
    {
        try
        {
            Session = ArchipelagoSessionFactory.CreateSession(Data.Host, Data.Port);

            // Wire events BEFORE logging in.
            Session.Items.ItemReceived += OnItemReceived;
            Session.MessageLog.OnMessageReceived += m => Plugin.Log.LogInfo(m.ToString());
            Session.Socket.ErrorReceived += (e, msg) => Plugin.Log.LogError($"AP socket: {msg}");
            Session.Socket.SocketClosed += reason =>
            {
                State = ConnState.Disconnected;
                StatusMessage = $"Disconnected: {reason}";
                Plugin.Log.LogWarning($"AP closed: {reason}");
            };

            LoginResult result = Session.TryConnectAndLogin(
                Plugin.GameName,
                Data.SlotName,
                ItemsHandlingFlags.AllItems,
                new Version(0, 6, 7),
                password: Data.Password,
                requestSlotData: true);

            if (result is LoginFailure failure)
            {
                string errs = string.Join("; ", failure.Errors);
                State = ConnState.Failed;
                StatusMessage = "Login failed: " + errs;
                Plugin.Log.LogError("AP login failed: " + errs);
                return;
            }

            ReadSlotData(((LoginSuccessful)result).SlotData);

            State = ConnState.Connected;
            StatusMessage = $"Connected as {Data.SlotName}";
            Plugin.Log.LogInfo($"AP connected as {Data.SlotName}.");
        }
        catch (Exception e)
        {
            State = ConnState.Failed;
            StatusMessage = "Connect error: " + e.Message;
            Plugin.Log.LogError($"AP connect error: {e}");
        }
    }

    private void ReadSlotData(Dictionary<string, object> slotData)
    {
        if (slotData == null) return;
        if (slotData.TryGetValue("goal", out var g)) Data.Goal = Convert.ToInt32(g);
        if (slotData.TryGetValue("death_link", out var dl)) Data.DeathLinkEnabled = Convert.ToBoolean(dl);
        // Gate configuration lands here as the apworld grows options.
    }

    // --- Location checks -----------------------------------------------------

    /// <summary>Report an AP location as checked (safe to call from any thread).</summary>
    public void SendCheck(long locationId)
    {
        // Gate on Connected, NOT just a non-null Session: after a socket close the
        // Session lingers but its socket is dead, and a send into a dead socket can
        // BLOCK the caller. This is reached from game postfixes on Unity's main
        // thread, so that would freeze the game. (Dedup happens on this thread.)
        if (Session == null || !Connected || locationId < 0) return;
        if (!Data.CheckedLocations.Add(locationId)) return;   // already sent

        var session = Session;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                session.Locations.CompleteLocationChecks(locationId);
                Plugin.Log.LogInfo($"AP check sent: {locationId}");
            }
            catch (Exception e) { Plugin.Log.LogError($"AP check {locationId} not sent: {e.Message}"); }
        });
    }

    /// <summary>Tell the server this slot reached its goal.</summary>
    public void SendVictory()
    {
        if (Session == null || !Connected) return;
        var session = Session;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                session.Socket.SendPacket(new StatusUpdatePacket { Status = ArchipelagoClientState.ClientGoal });
                Plugin.Log.LogInfo("AP goal reported.");
            }
            catch (Exception e) { Plugin.Log.LogError($"AP victory not sent: {e.Message}"); }
        });
    }

    // --- Item receipt --------------------------------------------------------

    private void OnItemReceived(ReceivedItemsHelper helper)
    {
        // Replayed-history guard: the server resends everything on connect.
        ItemInfo item = helper.DequeueItem();
        if (helper.Index <= Data.ItemIndex) return;
        Data.ItemIndex = helper.Index;

        // Apply on the main thread. ItemApplier is a stub until the apworld
        // defines an item pool.
        _mainThread.Enqueue(() => Mapping.ItemApplier.Apply(item));
    }
}
