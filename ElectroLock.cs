//#define DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using Rust;
using UnityEngine;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("ElectroLock", "RFC1920", "1.0.0")]
    [Description("Lock electrical switches")]
    class ElectroLock : RustPlugin
    {
        #region vars
        const string codeLockPrefab = "assets/prefabs/locks/keypad/lock.code.prefab";
        public Quaternion entityrot;
        public Vector3 entitypos;
        public BaseEntity newlock;

        public Dictionary<ulong,bool> userenabled = new Dictionary<ulong,bool>();
        public Dictionary<int,SwitchPair> switchpairs = new Dictionary<int,SwitchPair>();
        public List<ulong> switches = new List<ulong>();

        public class SwitchPair
        {
            public ulong owner;
            public ulong switchid;
            public ulong lockid;
        }
        #endregion

        #region Message
        private string _(string msgId, BasePlayer player, params object[] args)
        {
            var msg = lang.GetMessage(msgId, this, player?.UserIDString);
            return args.Length > 0 ? string.Format(msg, args) : msg;
        }

        private void PrintMsgL(BasePlayer player, string msgId, params object[] args)
        {
            if(player == null) return;
            PrintMsg(player, _(msgId, player, args));
        }

        private void PrintMsg(BasePlayer player, string msg)
        {
            if(player == null) return;
            SendReply(player, $"{msg}");
        }
        #endregion

        #region init
        void Init()
        {
            AddCovalenceCommand("el", "cmdElectroLock");

            permission.RegisterPermission("electrolock.use", this);
            permission.RegisterPermission("electrolock.admin", this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["cannotdo"] = "You cannot remove a lock which is part of a switch pair.",
                ["notauthorized"] = "You don't have permission to use this command.",
                ["instructions"] = "/el on to enable, /el off to disable.",
                ["spawned"] = "ElectroLock spawned a new lockable switch!",
                ["failed"] = "ElectroLock failed to spawn a new lockable switch!",
                ["locked"] = "This ElectroLock is locked!",
                ["unlocked"] = "This ElectroLock is unlocked!",
                ["disabled"] = "ElectroLock is disabled.",
                ["enabled"] = "ElectroLock is enabled."
            }, this);
        }

        void Loaded()
        {
            LoadData();
        }

        void Unload()
        {
            SaveData();
        }

        private void LoadData()
        {
            userenabled = Interface.GetMod().DataFileSystem.ReadObject<Dictionary<ulong, bool>>(this.Title + "/electrolock_user");
            switchpairs = Interface.GetMod().DataFileSystem.ReadObject<Dictionary<int, SwitchPair>>(this.Title + "/electrolock_data");
            foreach(KeyValuePair<int, SwitchPair> switchdata in switchpairs)
            {
                switches.Add(switchdata.Value.switchid);
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(this.Title + "/electrolock_user", userenabled);
            Interface.Oxide.DataFileSystem.WriteObject(this.Title + "/electrolock_data", switchpairs);
        }
        #endregion

        #region Rust_Hooks
        private void OnEntitySpawned(BaseNetworkable networkObject)
        {
            ElectricSwitch eswitch = networkObject.GetComponent<ElectricSwitch>();

            if(eswitch)
            {
                BasePlayer player = new BasePlayer();
                try
                {
                    player = FindOwner(eswitch.OwnerID) as BasePlayer;
                }
                catch {}

                if(player == null)
                {
#if DEBUG
                    Puts($"Could not find owner of this switch.");
#endif
                    return;
                }
                if(!isAllowed(player, "electrolock.use"))
                {
#if DEBUG
                    Puts($"Player {player.displayName} denied permission.");
#endif
                    return;
                }
                if(!userenabled.ContainsKey(player.userID))
                {
#if DEBUG
                    Puts($"Player {player.displayName} has never enabled ElectroLock.");
#endif
                    PrintMsgL(player, "disabled");
                    return;
                }
                if(userenabled[player.userID] == false || userenabled[player.userID] == null)
                {
#if DEBUG
                    Puts($"Player {player.displayName} has ElectroLock disabled.");
#endif
                    PrintMsgL(player, "disabled");
                    return;
                }

                if(eswitch)
                {
                    Vector3 spawnpos = eswitch.transform.position + new Vector3(0, 0.6f, 0);
#if DEBUG
                    Puts($"Found switch {eswitch.ShortPrefabName}, pos {eswitch.transform.position.ToString()}");
                    Puts($"Spawning lock at {spawnpos.ToString()}");
#endif
                    if(AddLock(eswitch))
                    {
                        switches.Add(eswitch.net.ID);
                        PrintMsgL(player, "spawned");
                        SaveData();
                    }
                    else
                    {
                        PrintMsgL(player, "failed");
                    }
                }
                player = null;
            }
        }

        private object OnSwitchToggle(ElectricSwitch eswitch, BasePlayer player)
        {
            if(switches.Contains(eswitch.net.ID))
            {
#if DEBUG
                Puts("OnSwitchToggle called for one of our switches!");
#endif
                if(IsLocked(eswitch.net.ID))
                {
                    PrintMsgL(player, "locked");
                    return true;
                }
            }
            return null;
        }

        // Check for our switch, currently only log
        private object CanPickupEntity(BasePlayer player, BaseCombatEntity entity)
        {
            if(entity == null || player == null) return null;

            BaseEntity myent = entity as BaseEntity;

            if(myent.name.Contains("switch"))
            {
                if(myent.name.Contains("switch") && IsOurSwitch(myent.net.ID))
                {
#if DEBUG
                    Puts("CanPickupEntity: player trying to pickup our locked switch!");
#endif
                }
            }
            return null;
        }

        // Check for our switch lock, block pickup
        private object CanPickupLock(BasePlayer player, BaseLock baseLock)
        {
            if(baseLock == null) return null;
            if(player == null) return null;

            BaseEntity myent = baseLock as BaseEntity;
            BaseEntity myparent = null;
            try
            {
                myparent = myent.GetParentEntity();
            }
            catch {}

            if(myparent.name.Contains("switch") && IsOurSwitch(myparent.net.ID))
            {
#if DEBUG
                Puts("CanPickupLock: player trying to remove lock from a locked switch!");
#endif
                PrintMsgL(player, "cannotdo");
                return false;
            }
            return null;
        }
        #endregion

        #region Main
        bool isAllowed(BasePlayer player, string perm) => permission.UserHasPermission(player.UserIDString, perm);

        [Command("el")]
        void cmdElectroLock(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if(!iplayer.HasPermission("electrolock.use")) { PrintMsgL(player, "notauthorized"); return; }
            if(args.Length == 0)
            {
                if(!userenabled.ContainsKey(player.userID))
                {
                    PrintMsgL(player, "disabled");
                    PrintMsgL(player, "instructions");
                }
                else if(userenabled[player.userID] == false)
                {
                    PrintMsgL(player, "disabled");
                    PrintMsgL(player, "instructions");
                }
                else if(userenabled[player.userID] == true)
                {
                    PrintMsgL(player, "enabled");
                    PrintMsgL(player, "instructions");
                }
                return;
            }
            if(args[0] == "on" || args[0] == "1")
            {
                if(!userenabled.ContainsKey(player.userID))
                {
                    userenabled.Add(player.userID, true);
                }
                else if(userenabled[player.userID] == false)
                {
                    userenabled[player.userID] = true;
                }
                PrintMsgL(player, "enabled");
            }
            else if(args[0] == "off" || args[0] == "0")
            {
                if(!userenabled.ContainsKey(player.userID))
                {
                    userenabled.Add(player.userID, false);
                }
                else if(userenabled[player.userID] == true)
                {
                    userenabled[player.userID] = false;
                }
                PrintMsgL(player, "disabled");
            }
            SaveData();
        }

        // Lock entity spawner
        private bool AddLock(BaseEntity eswitch)
        {
            try
            {
                newlock = new BaseEntity();
                newlock = GameManager.server.CreateEntity(codeLockPrefab, entitypos, entityrot, true);
                newlock.transform.localEulerAngles = new Vector3(0, 90, 0);
                newlock.transform.localPosition = new Vector3(0, 0.65f, 0);
                newlock.SetParent(eswitch, 0);
                newlock?.Spawn();
                newlock.OwnerID = eswitch.OwnerID;

                int id = UnityEngine.Random.Range(1, 99999999);
                switchpairs.Add(id, new SwitchPair
                {
                    owner = eswitch.OwnerID,
                    switchid = eswitch.net.ID,
                    lockid = newlock.net.ID
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Used to find the owner of a switch
        private object FindOwner(ulong playerID)
        {
            IPlayer iplayer = covalence.Players.FindPlayer(playerID.ToString());
            if(iplayer != null)
            {
                return iplayer.Object as BasePlayer;
            }
            else
            {
                return null;
            }
        }

        private bool IsOurSwitch(ulong switchid)
        {
            foreach(KeyValuePair<int, SwitchPair> switchdata in switchpairs)
            {
                if(switchdata.Value.switchid == switchid)
                {
#if DEBUG
                    Puts("This is one of our switches!");
#endif
                    return true;
                }
            }
            return false;
        }

        // Check whether this switch has an associated lock, and whether or not it is locked
        private bool IsLocked(ulong switchid)
        {
            foreach(KeyValuePair<int, SwitchPair> switchdata in switchpairs)
            {
                if(switchdata.Value.switchid == switchid)
                {
                    var mylockid =  Convert.ToUInt32(switchdata.Value.lockid);
                    var bent = BaseNetworkable.serverEntities.Find(mylockid);
                    var lockent = bent as BaseEntity;
                    if(lockent.IsLocked())
                    {
#if DEBUG
                        Puts("Found an associated lock!");
#endif
                        return true;
                    }
                }
            }
            return false;
        }
        #endregion
    }
}
