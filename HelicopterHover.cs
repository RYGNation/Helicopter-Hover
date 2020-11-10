using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Helicopter Hover", "0x89A", "2.0.0")]
    [Description("Allows minicopters to hover without driver on command")]
    class HelicopterHover : RustPlugin
    {
        #region -Fields-

        private static HelicopterHover plugin;
        private static Configuration _config;

        private Dictionary<int, HelicopterHovering> helicopters = new Dictionary<int, HelicopterHovering>();

        private const string canHover = "helicopterhover.canhover";

        #endregion -Fields-

        #region -Init-

        void Init()
        {
            plugin = this;

            if (!_config.enterBroadcast) Unsubscribe(nameof(OnEntityMounted));
            if (!_config.Hovering.disableHoverOnDismount) Unsubscribe(nameof(OnEntityDismounted));

            permission.RegisterPermission(canHover, this);
        }

        void Unload()
        {
            plugin = null;
            _config = null;

            foreach (KeyValuePair<int, HelicopterHovering> pair in helicopters) UnityEngine.Object.Destroy(pair.Value);
        }

        void OnServerInitialized()
        {
            foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
            {
                if (networkable is BaseHelicopterVehicle) OnEntitySpawned(networkable as BaseHelicopterVehicle);
            }
        }

        #endregion -Init-

        #region -Commands-

        [ConsoleCommand("helicopterhover.hover")]
        void ConsoleHover(ConsoleSystem.Arg args)
        {
            if (args.Player() != null) Hover(args.Player());
        }

        [ChatCommand("hover")]
        void Hover(BasePlayer player)
        {
            if (player == null) return;
            BaseHelicopterVehicle helicopter = player.GetMountedVehicle() as BaseHelicopterVehicle;

            if (permission.UserHasPermission(player.UserIDString, canHover) && helicopter != null && helicopters.ContainsKey(helicopter.GetInstanceID()) && (_config.Permission.passengerToggle || helicopter.GetDriver() == player) && (_config.Permission.enableHoverWithTwoOccupants || helicopter.NumMounted() <= 1))
            {
                if (helicopter.IsEngineOn() && helicopter.isMobile || helicopter.isMobile && helicopter.GetDriver() != player) helicopters[helicopter.GetInstanceID()].ToggleHover();
                else PrintToChat(player, lang.GetMessage("NotFlying", this, player.UserIDString));
            }
            else if (!permission.UserHasPermission(player.UserIDString, canHover)) 
                PrintToChat(player, lang.GetMessage("NoPermission", this, player.UserIDString));
            else if (helicopter == null) 
                PrintToChat(player, lang.GetMessage("NotInHelicopter", this, player.UserIDString));
            else if (!_config.Permission.passengerToggle || helicopter.GetDriver() != player) 
                PrintToChat(player, lang.GetMessage("NoPassengerToggle", this, player.UserIDString));
            else if (!_config.Permission.enableHoverWithTwoOccupants && helicopter.NumMounted() > 1) 
                PrintToChat(player, lang.GetMessage("CantHoverTwoOccupants", this, player.UserIDString));
        }
        
        #endregion -Commands-

        #region -Hooks-

        void OnEntitySpawned(BaseHelicopterVehicle helicopter) //Apply custom script when helicopters spawn
        {
            if (helicopters.ContainsKey(helicopter.GetInstanceID()) || (helicopter is ScrapTransportHelicopter && !_config.Permission.scrapheliCanHover) || (helicopter is MiniCopter && !_config.Permission.miniCanHover) || (helicopter is CH47Helicopter && !_config.Permission.chinookCanHover)) return;

            helicopters.Add(helicopter.GetInstanceID(), helicopter.gameObject.AddComponent<HelicopterHovering>());
        }

        void OnEntityMounted(BaseMountable mount, BasePlayer player) //Broadcast message when mounting helicopter.
        {
            BaseEntity parentEntity = mount.GetParentEntity();

            //Make sure that chat message only sends if the vehicle is allowed to hover (set in config)
            if (parentEntity != null && permission.UserHasPermission(player.UserIDString, canHover) && ((parentEntity is ScrapTransportHelicopter && _config.Permission.scrapheliCanHover) || (parentEntity is MiniCopter && _config.Permission.miniCanHover) || (parentEntity is CH47Helicopter && _config.Permission.chinookCanHover)))
                PrintToChat(player, lang.GetMessage("Mounted", this, player.UserIDString));
        }

        void OnEntityDismounted(BaseMountable mount, BasePlayer player) //Handle disabling hover on dismount
        {
            BaseHelicopterVehicle parent = mount.GetParentEntity() as BaseHelicopterVehicle;

            //If is not helicopter or "helicopters" does not contain key, return
            if (parent == null || !helicopters.ContainsKey(parent.GetInstanceID())) return;

            HelicopterHovering hover = helicopters[parent.GetInstanceID()];

            if (_config.Hovering.disableHoverOnDismount) hover.StopHover();
        }

        void OnServerCommand(ConsoleSystem.Arg args)
        {
            BaseHelicopterVehicle vehicle = args.Player()?.GetMountedVehicle() as BaseHelicopterVehicle;
            if (args.cmd.FullName != "vehicle.swapseats" || vehicle == null || vehicle.GetDriver() != args.Player() || !helicopters.ContainsKey(vehicle.GetInstanceID())) return;

            HelicopterHovering hover = helicopters[vehicle.GetInstanceID()];

            if (_config.Hovering.disableHoverOnSeat && hover.IsHovering) hover.StopHover();
            else if (_config.Hovering.hoverOnSeatSwitch && !hover.IsHovering) hover.StartHover();
        }

        #endregion -Hooks-

        private class HelicopterHovering : MonoBehaviour
        {
            BaseHelicopterVehicle helicopter;
            MiniCopter minicopter;
            Rigidbody rb;

            Timer timedHoverTimer;
            Timer fuelUseTimer;

            Coroutine hoverCoroutine;

            public bool IsHovering => rb.constraints == RigidbodyConstraints.FreezePositionY;

            void Awake()
            {
                helicopter = gameObject.GetComponent<BaseHelicopterVehicle>();
                minicopter = gameObject.GetComponent<MiniCopter>();
                rb = gameObject.GetComponent<Rigidbody>();
            }

            public void ToggleHover()
            {
                if (IsHovering) StopHover();
                else StartHover();

                foreach (BaseVehicle.MountPointInfo info in helicopter.mountPoints)
                {
                    BasePlayer player = info.mountable.GetMounted();
                    if (player != null) plugin.PrintToChat(player, plugin.lang.GetMessage(IsHovering ? "HelicopterEnabled" : "HelicopterDisabled", plugin, player.UserIDString));
                }
            }

            public void StartHover()
            {
                rb.constraints = RigidbodyConstraints.FreezePositionY;
                if (!_config.Hovering.enableRotationOnHover) rb.freezeRotation = true;

                if (_config.Hovering.keepEngineOnHover && minicopter != null) hoverCoroutine = ServerMgr.Instance.StartCoroutine(HoveringCoroutine());
            }

            public void StopHover()
            {
                rb.constraints = RigidbodyConstraints.None;
                rb.freezeRotation = false;

                if (hoverCoroutine != null) ServerMgr.Instance.StopCoroutine(hoverCoroutine);
                if (timedHoverTimer != null) timedHoverTimer.Destroy();
                if (fuelUseTimer != null) fuelUseTimer.Destroy();
            }


            IEnumerator HoveringCoroutine() //Keep engine running and manage fuel
            {
                if (_config.Hovering.timedHover) timedHoverTimer = plugin.timer.Once(_config.Hovering.hoverDuration, () => StopHover());

                EntityFuelSystem fuelSystem = minicopter.GetFuelSystem();

                if (_config.Hovering.useFuelOnHover) fuelUseTimer = plugin.timer.Every(1f, () =>
                {
                    if (fuelSystem.HasFuel() && minicopter.GetDriver() == null) fuelSystem.TryUseFuel(1f, minicopter.fuelPerSec);
                    else if (!fuelSystem.HasFuel()) fuelUseTimer.Destroy();
                });

                while (IsHovering)
                {
                    if (!minicopter.IsEngineOn() && fuelSystem.HasFuel() && (minicopter.HasAnyPassengers() || !_config.Hovering.disableHoverOnDismount))
                        minicopter.EngineStartup();

                    if (minicopter.HasDriver() && fuelSystem.HasFuel()) minicopter.EngineOn(); //Keep engine on when player in seat

                    if (!minicopter.GetFuelSystem().HasFuel()) //If no fuel, stop hovering
                    {
                        StopHover();
                        minicopter.EngineOff();

                        yield break;
                    }

                    yield return null;
                }
            }

            void OnDestroy() //Stop any timers or coroutines persisting after destruction or plugin unload
            {
                if (hoverCoroutine != null) ServerMgr.Instance.StopCoroutine(hoverCoroutine);
                if (timedHoverTimer != null) timedHoverTimer.Destroy();
                if (fuelUseTimer != null) fuelUseTimer.Destroy();
            }
        }

        #region -Configuration-

        class Configuration
        {
            [JsonProperty(PropertyName = "Broadcast message on mounted")]
            public bool enterBroadcast = true;

            [JsonProperty(PropertyName = "Permissions")]
            public PermissionClass Permission = new PermissionClass();

            [JsonProperty(PropertyName = "Hovering")]
            public HoveringClass Hovering = new HoveringClass();

            public class PermissionClass
            {
                [JsonProperty(PropertyName = "Minicopter can hover")]
                public bool miniCanHover = true;
                
                [JsonProperty(PropertyName = "Scrap Transport Helicopter can hover")]
                public bool scrapheliCanHover = true;

                [JsonProperty(PropertyName = "Chinook can hover")]
                public bool chinookCanHover = true;

                [JsonProperty(PropertyName = "Enable hover with two occupants")]
                public bool enableHoverWithTwoOccupants = true;

                [JsonProperty(PropertyName = "Passenger can toggle hover")]
                public bool passengerToggle = true;
            }

            public class HoveringClass
            {
                [JsonProperty(PropertyName = "Disable hover on dismount")]
                public bool disableHoverOnDismount = true;

                [JsonProperty(PropertyName = "Use fuel while hovering")]
                public bool useFuelOnHover = true;

                [JsonProperty(PropertyName = "Keep engine on when hovering")]
                public bool keepEngineOnHover = true;

                [JsonProperty(PropertyName = "Enable helicopter rotation on hover")]
                public bool enableRotationOnHover = true;

                [JsonProperty(PropertyName = "Disable hover on change seats")]
                public bool disableHoverOnSeat = false;

                [JsonProperty(PropertyName = "Hover on seat change")]
                public bool hoverOnSeatSwitch = true;

                [JsonProperty(PropertyName = "Timed hover")]
                public bool timedHover = false;

                [JsonProperty(PropertyName = "Timed hover duration")]
                public float hoverDuration = 60;
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
                SaveConfig();
            }
            catch
            {
                PrintError("Failed to load config, using default values");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion -Configuration-s

        #region -Localisation-

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["OnlyAdmins"] = "This command is exclusive to admins",
                ["NotFlying"] = "The helicopter is not flying",
                ["Mounted"] = "Use '/hover' to toggle hover",
                ["NoPermission"] = "You do not have permission to hover",
                ["CantHoverTwoOccupants"] = "Cannot hover with two occupants",
                ["HelicopterEnabled"] = "Helicopter hover: enabled",
                ["HelicopterDisabled"] = "Helicopter hover: disabled",
                ["NotInHelicopter"] = "You are not in a helicopter",
                ["NoPassengerToggle"] = "Passengers cannot toggle hover"
            }
            , this);
        }

        #endregion
    }
}
