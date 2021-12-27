#region License (GPL v3)
/*
    DESCRIPTION
    Copyright (c) 2020 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License Information (GPL v3)
using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using UnityEngine;
using Oxide.Core.Libraries.Covalence;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Electro Lock", "RFC1920", "1.1.3")]
    [Description("Lock electrical switches and generators with a code lock")]
    internal class ElectroLock : RustPlugin
    {
        #region vars
        private ConfigData configData;
        private const string codeLockPrefab = "assets/prefabs/locks/keypad/lock.code.prefab";
        private const string keyLockPrefab = "assets/prefabs/locks/keylock/lock.key.prefab";
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
        #endregion

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));
        #endregion

        #region init
        private void Init()
        {
            AddCovalenceCommand("el", "cmdElectroLock");

            permission.RegisterPermission(permElectrolockUse, this);
            permission.RegisterPermission(permElectrolockAdmin, this);

            LoadData();
        }

        private void Loaded() => LoadConfigVariables();

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new ConfigData
            {
                Settings = new Settings()
                {
                    ownerBypass = false,
                    useKeyLock = false,
                    debug = false
                },
                Version = Version
            };
            SaveConfig(config);
        }

        public class ConfigData
        {
            public Settings Settings;
            public VersionNumber Version;
        }

        public class Settings
        {
            [JsonProperty(PropertyName = "Owner can bypass lock")]
            public bool ownerBypass;

            [JsonProperty(PropertyName = "Use key lock instead of code lock")]
            public bool useKeyLock;

            public bool debug;
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            configData.Version = Version;
            SaveConfig(configData);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        private void DoLog(string message)
        {
            if (configData.Settings.debug)
            {
                Interface.Oxide.LogInfo(message);
            }
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

        private void Unload()
        {
            SaveData();
        }

        private void LoadData()
        {
            userenabled = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, bool>>(Name + "/electrolock_user");
            switchpairs = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<int, SwitchPair>>(Name + "/electrolock_data");
            foreach (KeyValuePair<int, SwitchPair> switchdata in switchpairs)
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
            if (eswitch != null)
            {
                BasePlayer player = FindOwner(eswitch.OwnerID);
                if (player == null)
                {
                    DoLog("Could not find owner of this generator.");
                    return;
                }

                if (!permission.UserHasPermission(player.UserIDString, permElectrolockUse))
                {
                    DoLog($"Player {player.displayName} denied permission.");
                    return;
                }
                if (!userenabled.ContainsKey(player.userID))
                {
                    DoLog($"Player {player.displayName} has never enabled ElectroLock.");
                    Message(player.IPlayer, "disabled");
                    return;
                }
                if (!userenabled[player.userID])
                {
                    DoLog($"Player {player.displayName} has ElectroLock disabled.");
                    Message(player.IPlayer, "disabled");
                    return;
                }

                if (eswitch)
                {
                    if (AddLock(eswitch, true))
                    {
                        switches.Add(eswitch.net.ID);
                        Message(player.IPlayer, "gspawned");
                        SaveData();
                        DoLog("Spawned generator with lock");
                    }
                    else
                    {
                        DoLog("Failed to spawn generator with lock");
                        Message(player.IPlayer, "gfailed");
                    }
                }
                player = null;
            }
        }

        private void OnEntitySpawned(ElectricSwitch eswitch)
        {
            if (eswitch != null)
            {
                if (eswitch.name.Contains("fluid")) return;
                BasePlayer player = FindOwner(eswitch.OwnerID);
                if (player == null)
                {
                    DoLog($"Could not find owner of this switch.");
                    return;
                }

                if (!permission.UserHasPermission(player.UserIDString, permElectrolockUse))
                {
                    DoLog($"Player {player.displayName} denied permission.");
                    return;
                }
                if (!userenabled.ContainsKey(player.userID))
                {
                    DoLog($"Player {player.displayName} has never enabled ElectroLock.");
                    Message(player.IPlayer, "disabled");
                    return;
                }
                if (!userenabled[player.userID])
                {
                    DoLog($"Player {player.displayName} has ElectroLock disabled.");
                    Message(player.IPlayer, "disabled");
                    return;
                }

                if (eswitch)
                {
                    if (AddLock(eswitch))
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
            if (eswitch == null) return null;
            if (player == null) return null;

            if (switches.Contains(eswitch.net.ID))
            {
                DoLog("OnSwitchToggle called for one of our generators!");
                if (IsLocked(eswitch.net.ID))
                {
                    if (eswitch.OwnerID == player.userID && configData.Settings.ownerBypass)
                    {
                        DoLog("OnSwitchToggle: Per config, owner can bypass");
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
            if (eswitch == null) return null;
            if (player == null) return null;

            if (switches.Contains(eswitch.net.ID))
            {
                DoLog("OnSwitchToggle called for one of our switches!");
                if (IsLocked(eswitch.net.ID))
                {
                    if (eswitch.OwnerID == player.userID && configData.Settings.ownerBypass)
                    {
                        DoLog("OnSwitchToggle: Per config, owner can bypass");
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
            if (myent == null) return null;
            if (player == null) return null;

            if (myent.name.Contains("switch") && IsOurSwitch(myent.net.ID))
            {
                if (IsLocked(myent.net.ID))
                {
                    DoLog("CanPickupEntity: player trying to pickup our locked switch!");
                    Message(player.IPlayer, "locked");
                    return false;
                }
                else
                {
                    DoLog("CanPickupEntity: player picking up our unlocked switch!");
                    switches.Remove(myent.net.ID);
                    int myswitch = switchpairs.FirstOrDefault(x => x.Value.switchid == myent.net.ID).Key;
                    switchpairs.Remove(myswitch);
                    SaveData();
                    return null;
                }
            }
            else if (myent.name.Contains("fuel_gen"))
            {
                if (IsLocked(myent.net.ID))
                {
                    DoLog("CanPickupEntity: player trying to pickup our locked generator!");
                    Message(player.IPlayer, "locked");
                    return false;
                }
                else
                {
                    DoLog("CanPickupEntity: player picking up our unlocked generator!");
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
            if (baseLock == null) return null;
            if (player == null) return null;

            BaseEntity eswitch = baseLock.GetParentEntity();
            if (eswitch == null) return null;

            if (eswitch.name.Contains("switch") && IsOurSwitch(eswitch.net.ID))
            {
                DoLog("CanPickupLock: player trying to remove lock from a locked switch!");
                Message(player.IPlayer, "cannotdo");
                return false;
            }
            if (eswitch.name.Contains("fuel_gen") && IsOurSwitch(eswitch.net.ID))
            {
                DoLog("CanPickupLock: player trying to remove lock from a locked generator!");
                Message(player.IPlayer, "cannotdo");
                return false;
            }
            return null;
        }

        private void OnNewSave(string strFilename)
        {
            // Wipe the dict of switch pairs.  But, player prefs are maintained.
            switchpairs = new Dictionary<int,SwitchPair>();
            SaveData();
        }
        #endregion

        #region Main
        [Command("el")]
        private void cmdElectroLock(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permElectrolockUse)) { Message(player, "notauthorized"); return; }
            ulong playerID = ulong.Parse(player.Id);

            if (args.Length == 0)
            {
                if (!userenabled.ContainsKey(playerID))
                {
                    Message(player, "disabled");
                    Message(player, "instructions");
                }
                else if (!userenabled[playerID])
                {
                    Message(player, "disabled");
                    Message(player, "instructions");
                }
                else if (userenabled[playerID])
                {
                    Message(player, "enabled");
                    Message(player, "instructions");
                }
                return;
            }
            if (args[0] == "on" || args[0] == "1")
            {
                if (!userenabled.ContainsKey(playerID))
                {
                    userenabled.Add(playerID, true);
                }
                else if (!userenabled[playerID])
                {
                    userenabled[playerID] = true;
                }
                Message(player, "enabled");
            }
            else if (args[0] == "off" || args[0] == "0")
            {
                if (!userenabled.ContainsKey(playerID))
                {
                    userenabled.Add(playerID, false);
                }
                else if (userenabled[playerID])
                {
                    userenabled[playerID] = false;
                }
                Message(player, "disabled");
            }
            else if (args[0] == "who" && player.HasPermission(permElectrolockAdmin))
            {
                RaycastHit hit;
                BasePlayer basePlayer = player.Object as BasePlayer;
                if (Physics.Raycast(basePlayer.eyes.HeadRay(), out hit, 2.2f))
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
            string prefab = codeLockPrefab;
            if (configData.Settings.useKeyLock)
            {
                prefab = keyLockPrefab;
            }
            if (newlock = GameManager.server.CreateEntity(prefab, entitypos, entityrot, true))
            {
                if (gen)
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
            if (playerID == 0) return null;
            IPlayer iplayer = covalence.Players.FindPlayer(playerID.ToString());

            if (iplayer != null)
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
            if (switchid == 0) return false;
            foreach (KeyValuePair<int, SwitchPair> switchdata in switchpairs)
            {
                if (switchdata.Value.switchid == switchid)
                {
                    DoLog("This is one of our switches!");
                    return true;
                }
            }
            return false;
        }

        // Check whether this switch has an associated lock, and whether or not it is locked
        private bool IsLocked(ulong switchid)
        {
            if (switchid == 0) return false;
            foreach (KeyValuePair<int, SwitchPair> switchdata in switchpairs)
            {
                if (switchdata.Value.switchid == switchid)
                {
                    uint mylockid =  Convert.ToUInt32(switchdata.Value.lockid);
                    BaseNetworkable bent = BaseNetworkable.serverEntities.Find(mylockid);
                    BaseEntity lockent = bent as BaseEntity;
                    if (lockent.IsLocked())
                    {
                        DoLog("Found an associated lock!");
                        return true;
                    }
                }
            }
            return false;
        }
        #endregion
    }
}
