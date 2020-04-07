#define DEBUG
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
    [Info("Electro Lock", "RFC1920", "1.1.0")]
    [Description("Lock electrical switches and generators with a code lock")]
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
        private const string permElectrolockUse = "electrolock.use";
        private const string permElectrolockAdmin = "electrolock.admin";

        public class SwitchPair
        {
            public ulong owner;
            public ulong switchid;
            public ulong lockid;
        }

        private bool g_configChanged;
        private bool ownerBypass = false;
        #endregion

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));
        #endregion

        #region init
        void Init()
        {
            AddCovalenceCommand("el", "cmdElectroLock");

            permission.RegisterPermission(permElectrolockUse, this);
            permission.RegisterPermission(permElectrolockAdmin, this);

            LoadData();
        }

        void Loaded() => LoadConfigValues();

        protected override void LoadDefaultConfig() => Puts("New configuration file created.");

        void LoadConfigValues()
        {
            ownerBypass = Convert.ToBoolean(GetConfigValue("Settings", "Owner can bypass lock", false));

            if(g_configChanged)
            {
                Puts("Configuration file updated.");
                SaveConfig();
            }
        }

        object GetConfigValue(string category, string setting, object defaultValue)
        {
            Dictionary<string, object> data = Config[category] as Dictionary<string, object>;
            object value;

            if(data == null)
            {
                data = new Dictionary<string, object>();
                Config[category] = data;
                g_configChanged = true;
            }

            if(data.TryGetValue(setting, out value)) return value;
            value = defaultValue;
            data[setting] = value;
            g_configChanged = true;
            return value;
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["cannotdo"] = "You cannot remove a lock which is part of a switch pair.",
                ["notauthorized"] = "You don't have permission to use this command.",
                ["instructions"] = "/el on to enable, /el off to disable.",
                ["spawned"] = "ElectroLock spawned a new lockable switch!",
                ["gspawned"] = "ElectroLock spawned a new lockable generator!",
                ["failed"] = "ElectroLock failed to spawn a new lockable switch!",
                ["gfailed"] = "ElectroLock failed to spawn a new lockable generator!",
                ["locked"] = "This ElectroLock is locked!",
                ["unlocked"] = "This ElectroLock is unlocked!",
                ["disabled"] = "ElectroLock is disabled.",
                ["enabled"] = "ElectroLock is enabled.",
                ["owner"] = "ElectroLock owned by {0}.",
                ["noswitch"] = "Could not find an ElectroLock in front of you."
            }, this);
        }

        void Unload()
        {
            SaveData();
        }

        private void LoadData()
        {
            userenabled = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, bool>>(Name + "/electrolock_user");
            switchpairs = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<int, SwitchPair>>(Name + "/electrolock_data");
            foreach(KeyValuePair<int, SwitchPair> switchdata in switchpairs)
            {
                switches.Add(switchdata.Value.switchid);
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name + "/electrolock_user", userenabled);
            Interface.Oxide.DataFileSystem.WriteObject(Name + "/electrolock_data", switchpairs);
        }
        #endregion

        #region Rust_Hooks
        private void OnEntitySpawned(FuelGenerator eswitch)
        {
            if(eswitch != null)
            {
                BasePlayer player = FindOwner(eswitch.OwnerID);
                if(player == null)
                {
#if DEBUG
                    Puts($"Could not find owner of this generator.");
#endif
                    return;
                }

                if(!player.IPlayer.HasPermission(permElectrolockUse))
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
                    Message(player.IPlayer, "disabled");
                    return;
                }
                if(userenabled[player.userID] == false || userenabled[player.userID] == null)
                {
#if DEBUG
                    Puts($"Player {player.displayName} has ElectroLock disabled.");
#endif
                    Message(player.IPlayer, "disabled");
                    return;
                }

                if(eswitch)
                {
                    if(AddLock(eswitch, true))
                    {
                        switches.Add(eswitch.net.ID);
                        Message(player.IPlayer, "gspawned");
                        SaveData();
#if DEBUG
                        Puts($"Spawned generator with lock");
#endif
                    }
                    else
                    {
#if DEBUG
                        Puts($"Failed to spawn generator with lock");
#endif
                        Message(player.IPlayer, "gfailed");
                    }
                }
                player = null;
            }
        }

        private void OnEntitySpawned(ElectricSwitch eswitch)
        {
            if(eswitch != null)
            {
                BasePlayer player = FindOwner(eswitch.OwnerID);
                if(player == null)
                {
#if DEBUG
                    Puts($"Could not find owner of this switch.");
#endif
                    return;
                }

                if(!player.IPlayer.HasPermission(permElectrolockUse))
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
                    Message(player.IPlayer, "disabled");
                    return;
                }
                if(userenabled[player.userID] == false || userenabled[player.userID] == null)
                {
#if DEBUG
                    Puts($"Player {player.displayName} has ElectroLock disabled.");
#endif
                    Message(player.IPlayer, "disabled");
                    return;
                }

                if(eswitch)
                {
                    if(AddLock(eswitch))
                    {
                        switches.Add(eswitch.net.ID);
                        Message(player.IPlayer, "spawned");
                        SaveData();
                    }
                    else
                    {
                        Message(player.IPlayer, "failed");
                    }
                }
                player = null;
            }
        }

        private object OnSwitchToggle(FuelGenerator eswitch, BasePlayer player)
        {
            if(eswitch == null) return null;
            if(player == null) return null;

            if(switches.Contains(eswitch.net.ID))
            {
#if DEBUG
                Puts("OnSwitchToggle called for one of our generators!");
#endif
                if(IsLocked(eswitch.net.ID))
                {
                    if(eswitch.OwnerID == player.userID && ownerBypass)
                    {
#if DEBUG
                        Puts("OnSwitchToggle: Per config, owner can bypass");
#endif
                        return null;
                    }
                    Message(player.IPlayer, "locked");
                    return true;
                }
            }
            return null;
        }

        private object OnSwitchToggle(ElectricSwitch eswitch, BasePlayer player)
        {
            if(eswitch == null) return null;
            if(player == null) return null;

            if(switches.Contains(eswitch.net.ID))
            {
#if DEBUG
                Puts("OnSwitchToggle called for one of our switches!");
#endif
                if(IsLocked(eswitch.net.ID))
                {
                    if(eswitch.OwnerID == player.userID && ownerBypass)
                    {
#if DEBUG
                        Puts("OnSwitchToggle: Per config, owner can bypass");
#endif
                        return null;
                    }
                    Message(player.IPlayer, "locked");
                    return true;
                }
            }
            return null;
        }

        // Check for our switch, currently only log
        private object CanPickupEntity(BasePlayer player, BaseEntity myent)
        {
            if(myent == null) return null;
            if(player == null) return null;

            if(myent.name.Contains("switch") && IsOurSwitch(myent.net.ID))
            {
                if(IsLocked(myent.net.ID))
                {
#if DEBUG
                    Puts("CanPickupEntity: player trying to pickup our locked switch!");
#endif
                    Message(player.IPlayer, "locked");
                    return false;
                }
                else
                {
#if DEBUG
                    Puts("CanPickupEntity: player picking up our unlocked switch!");
#endif
                    switches.Remove(myent.net.ID);
                    int myswitch = switchpairs.FirstOrDefault(x => x.Value.switchid == myent.net.ID).Key;
                    switchpairs.Remove(myswitch);
                    SaveData();
                    return null;
                }
            }
            else if(myent.name.Contains("fuel_gen"))
            {
                if(IsLocked(myent.net.ID))
                {
#if DEBUG
                    Puts("CanPickupEntity: player trying to pickup our locked generator!");
#endif
                    Message(player.IPlayer, "locked");
                    return false;
                }
                else
                {
#if DEBUG
                    Puts("CanPickupEntity: player picking up our unlocked generator!");
#endif
                    switches.Remove(myent.net.ID);
                    int myswitch = switchpairs.FirstOrDefault(x => x.Value.switchid == myent.net.ID).Key;
                    switchpairs.Remove(myswitch);
                    SaveData();
                    return null;
                }
            }
            return null;
        }

        // Check for our switch lock, block pickup
        private object CanPickupLock(BasePlayer player, BaseLock baseLock)
        {
            if(baseLock == null) return null;
            if(player == null) return null;

            BaseEntity eswitch = baseLock.GetParentEntity();
            if(eswitch == null) return null;

            if(eswitch.name.Contains("switch") && IsOurSwitch(eswitch.net.ID))
            {
#if DEBUG
                Puts("CanPickupLock: player trying to remove lock from a locked switch!");
#endif
                Message(player.IPlayer, "cannotdo");
                return false;
            }
            if(eswitch.name.Contains("fuel_gen") && IsOurSwitch(eswitch.net.ID))
            {
#if DEBUG
                Puts("CanPickupLock: player trying to remove lock from a locked generator!");
#endif
                Message(player.IPlayer, "cannotdo");
                return false;
            }
            return null;
        }

        void OnNewSave(string strFilename)
        {
            // Wipe the dict of switch pairs.  But, player prefs are maintained.
            switchpairs = new Dictionary<int,SwitchPair>();
            SaveData();
        }
        #endregion

        #region Main
        [Command("el")]
        void cmdElectroLock(IPlayer player, string command, string[] args)
        {
            if(!player.HasPermission(permElectrolockUse)) { Message(player, "notauthorized"); return; }
            ulong playerID = ulong.Parse(player.Id);

            if(args.Length == 0)
            {
                if(!userenabled.ContainsKey(playerID))
                {
                    Message(player, "disabled");
                    Message(player, "instructions");
                }
                else if(userenabled[playerID] == false)
                {
                    Message(player, "disabled");
                    Message(player, "instructions");
                }
                else if(userenabled[playerID] == true)
                {
                    Message(player, "enabled");
                    Message(player, "instructions");
                }
                return;
            }
            if(args[0] == "on" || args[0] == "1")
            {
                if(!userenabled.ContainsKey(playerID))
                {
                    userenabled.Add(playerID, true);
                }
                else if(userenabled[playerID] == false)
                {
                    userenabled[playerID] = true;
                }
                Message(player, "enabled");
            }
            else if(args[0] == "off" || args[0] == "0")
            {
                if(!userenabled.ContainsKey(playerID))
                {
                    userenabled.Add(playerID, false);
                }
                else if(userenabled[playerID] == true)
                {
                    userenabled[playerID] = false;
                }
                Message(player, "disabled");
            }
            else if(args[0] == "who" && player.HasPermission(permElectrolockAdmin))
            {
                RaycastHit hit;
                BasePlayer basePlayer = player.Object as BasePlayer;
                if(Physics.Raycast(basePlayer.eyes.HeadRay(), out hit, 2.2f))
                {
                    BaseEntity eswitch = hit.GetEntity();
                    BasePlayer owner = FindOwner(eswitch.OwnerID);
                    Message(player, "owner", owner.displayName);
                    eswitch = null;
                    owner = null;
                }
                else
                {
                    Message(player, "noswitch");
                }
            }
            SaveData();
        }

        // Lock entity spawner
        private bool AddLock(BaseEntity eswitch, bool gen = false)
        {
            newlock = new BaseEntity();
            if(newlock = GameManager.server.CreateEntity(codeLockPrefab, entitypos, entityrot, true))
            {
                if(gen)
                {
                    newlock.transform.localEulerAngles = new Vector3(0, 90, 90);
                    newlock.transform.localPosition = new Vector3(0, 0.65f, 0.1f);
                }
                else
                {
                    newlock.transform.localEulerAngles = new Vector3(0, 90, 0);
                    newlock.transform.localPosition = new Vector3(0, 0.65f, 0);
                }
                newlock.SetParent(eswitch, 0);
                newlock.Spawn();
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
            return false;
        }

        // Used to find the owner of a switch
        private BasePlayer FindOwner(ulong playerID)
        {
            if(playerID == null) return null;
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
            if(switchid == null) return false;
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
            if(switchid == null) return false;
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
