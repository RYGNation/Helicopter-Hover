using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using Newtonsoft.Json;
using ProtoBuf;
using UnityEngine.Assertions;

namespace Oxide.Plugins
{
    [Info("Helicopter Hover", "0x89A", "2.0.6")]
    [Description("Allows minicopters to hover without driver on command")]
    class HelicopterHover : RustPlugin
    {
        #region -Fields-

        private static HelicopterHover _plugin;
        private static Configuration _config;

        private Dictionary<int, HoveringComponent> _helicopters = new Dictionary<int, HoveringComponent>();

        private const string CanHover = "helicopterhover.canhover";

        #endregion -Fields-

        #region -Init-

        void Init()
        {
            _plugin = this;

            if (!_config.EnterBroadcast) Unsubscribe(nameof(OnEntityMounted));
            if (!_config.Hovering.DisableHoverOnDismount) Unsubscribe(nameof(OnEntityDismounted));

            permission.RegisterPermission(CanHover, this);
        }

        void Unload()
        {
            _plugin = null;
            _config = null;

            foreach (KeyValuePair<int, HoveringComponent> pair in _helicopters) UnityEngine.Object.Destroy(pair.Value);
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

            if (permission.UserHasPermission(player.UserIDString, CanHover) && helicopter != null && _helicopters.ContainsKey(helicopter.GetInstanceID()) && (_config.Permission.PassengerToggle || helicopter.GetDriver() == player) && (_config.Permission.EnableHoverWithTwoOccupants || helicopter.NumMounted() <= 1))
            {
                if (helicopter.IsEngineOn() || helicopter.GetDriver() != player) _helicopters[helicopter.GetInstanceID()]?.ToggleHover();
                else PrintToChat(player, lang.GetMessage("NotFlying", this, player.UserIDString));
            }
            else if (!permission.UserHasPermission(player.UserIDString, CanHover)) 
                PrintToChat(player, lang.GetMessage("NoPermission", this, player.UserIDString));
            else if (helicopter == null) 
                PrintToChat(player, lang.GetMessage("NotInHelicopter", this, player.UserIDString));
            else if (!_config.Permission.PassengerToggle || helicopter.GetDriver() != player) 
                PrintToChat(player, lang.GetMessage("NoPassengerToggle", this, player.UserIDString));
            else if (!_config.Permission.EnableHoverWithTwoOccupants && helicopter.NumMounted() > 1) 
                PrintToChat(player, lang.GetMessage("CantHoverTwoOccupants", this, player.UserIDString));
        }
        
        #endregion -Commands-

        #region -Hooks-

        void OnEntitySpawned(BaseHelicopterVehicle helicopter) //Apply custom script when helicopters spawn
        {
            if (_helicopters.ContainsKey(helicopter.GetInstanceID()) || (helicopter is ScrapTransportHelicopter && !_config.Permission.ScrapheliCanHover) || (helicopter is MiniCopter && !_config.Permission.MiniCanHover) || (helicopter is CH47Helicopter && !_config.Permission.ChinookCanHover)) return;

            _helicopters.Add(helicopter.GetInstanceID(), helicopter.gameObject.AddComponent<HoveringComponent>());
        }

        void OnEntityMounted(BaseMountable mount, BasePlayer player) //Broadcast message when mounting helicopter.
        {
            BaseEntity parentEntity = mount.GetParentEntity();

            //Make sure that chat message only sends if the vehicle is allowed to hover (set in config)
            if (parentEntity != null && permission.UserHasPermission(player.UserIDString, CanHover) && ((parentEntity is ScrapTransportHelicopter && _config.Permission.ScrapheliCanHover) || (parentEntity is MiniCopter && _config.Permission.MiniCanHover) || (parentEntity is CH47Helicopter && _config.Permission.ChinookCanHover)))
                PrintToChat(player, lang.GetMessage("Mounted", this, player.UserIDString));
        }

        void OnEntityDismounted(BaseMountable mount, BasePlayer player) //Handle disabling hover on dismount
        {
            BaseHelicopterVehicle parent = mount?.GetParentEntity() as BaseHelicopterVehicle;

            //If is not helicopter or "helicopters" does not contain key, return
            if (parent == null || !_helicopters.ContainsKey(parent.GetInstanceID())) return;

            if (_config.Hovering.DisableHoverOnDismount) _helicopters[parent.GetInstanceID()]?.StopHover();
        }

        void OnServerCommand(ConsoleSystem.Arg args)
        {
            BaseHelicopterVehicle vehicle = args.Player()?.GetMountedVehicle() as BaseHelicopterVehicle;
            if (args.cmd.FullName != "vehicle.swapseats" || vehicle == null || vehicle.GetDriver() != args.Player() || !_helicopters.ContainsKey(vehicle.GetInstanceID())) return;

            HoveringComponent hover = _helicopters[vehicle.GetInstanceID()];

            if (_config.Hovering.DisableHoverOnSeat && hover.IsHovering) hover.StopHover();
            else if (_config.Hovering.HoverOnSeatSwitch && !hover.IsHovering) hover.StartHover();
        }

        #endregion -Hooks-

        private class HoveringComponent : MonoBehaviour
        {
            private BaseHelicopterVehicle _helicopter;
            MiniCopter _minicopter;
            Rigidbody _rb;

            Timer _timedHoverTimer;
            Timer _fuelUseTimer;

            Coroutine _hoverCoroutine;

            VehicleEngineController<MiniCopter> _engineController;

            public bool IsHovering => _rb.constraints == RigidbodyConstraints.FreezePositionY;

            void Awake()
            {
                if (!TryGetComponent(out _helicopter) || !TryGetComponent(out _rb))
                {
                    _plugin._helicopters.Remove(_helicopter?.GetInstanceID() ?? 0);
                    DestroyImmediate(this);
                    return;
                }

                _minicopter = GetComponent<MiniCopter>();

                _engineController = _minicopter?.engineController;
            }

            public void ToggleHover()
            {
                if (IsHovering) StopHover();
                else StartHover();

                foreach (BaseVehicle.MountPointInfo info in _helicopter.mountPoints)
                {
                    BasePlayer player = info.mountable.GetMounted();
                    if (player != null) _plugin.PrintToChat(player, _plugin.lang.GetMessage(IsHovering ? "HelicopterEnabled" : "HelicopterDisabled", _plugin, player.UserIDString));
                }
            }

            public void StartHover()
            {
                _rb.constraints = RigidbodyConstraints.FreezePositionY;
                if (!_config.Hovering.EnableRotationOnHover) _rb.freezeRotation = true;

                _engineController?.FinishStartingEngine();

                if (_config.Hovering.KeepEngineOnHover && _helicopter != null) _hoverCoroutine = ServerMgr.Instance.StartCoroutine(HoveringCoroutine());
            }

            public void StopHover()
            {
                _rb.constraints = RigidbodyConstraints.None;
                _rb.freezeRotation = false;

                if (_hoverCoroutine != null) ServerMgr.Instance.StopCoroutine(_hoverCoroutine);
                if (_timedHoverTimer != null) _timedHoverTimer.Destroy();
                if (_fuelUseTimer != null) _fuelUseTimer.Destroy();
            }

            IEnumerator HoveringCoroutine() //Keep engine running and manage fuel
            {
                if (_config.Hovering.TimedHover) _timedHoverTimer = _plugin.timer.Once(_config.Hovering.HoverDuration, () => StopHover());

                EntityFuelSystem fuelSystem = _minicopter?.GetFuelSystem();
                
                if (fuelSystem != null)
                {
                    if (_config.Hovering.UseFuelOnHover) _fuelUseTimer = _plugin.timer.Every(1f, () =>
                    {
                        if (fuelSystem.HasFuel() && _minicopter.GetDriver() == null) fuelSystem.TryUseFuel(1f, _minicopter.fuelPerSec);
                        else if (!fuelSystem.HasFuel()) _fuelUseTimer.Destroy();
                    });
                }

                //Keep engine on
                
                while (IsHovering)
                {
                    if (!(_engineController?.IsOn ?? false) && (_helicopter.AnyMounted() || !_config.Hovering.DisableHoverOnDismount)) _engineController?.FinishStartingEngine();

                    if (fuelSystem != null)
                    {
                        if (!fuelSystem.HasFuel()) //If no fuel, stop hovering
                        {
                            StopHover();
                            _engineController?.StopEngine();

                            yield break;
                        }
                    }

                    yield return null;
                }
            }

            void OnDestroy() //Stop any timers or coroutines persisting after destruction or plugin unload
            {
                if (_hoverCoroutine != null) ServerMgr.Instance.StopCoroutine(_hoverCoroutine);
                _timedHoverTimer?.Destroy();
                _fuelUseTimer?.Destroy();
            }
        }

        #region -Configuration-

        class Configuration
        {
            [JsonProperty(PropertyName = "Broadcast message on mounted")]
            public bool EnterBroadcast = true;

            [JsonProperty(PropertyName = "Permissions")]
            public PermissionClass Permission = new PermissionClass();

            [JsonProperty(PropertyName = "Hovering")]
            public HoveringClass Hovering = new HoveringClass();

            public class PermissionClass
            {
                [JsonProperty(PropertyName = "Minicopter can hover")]
                public bool MiniCanHover = true;
                
                [JsonProperty(PropertyName = "Scrap Transport Helicopter can hover")]
                public bool ScrapheliCanHover = true;

                [JsonProperty(PropertyName = "Chinook can hover")]
                public bool ChinookCanHover = true;

                [JsonProperty(PropertyName = "Enable hover with two occupants")]
                public bool EnableHoverWithTwoOccupants = true;

                [JsonProperty(PropertyName = "Passenger can toggle hover")]
                public bool PassengerToggle = true;
            }

            public class HoveringClass
            {
                [JsonProperty(PropertyName = "Disable hover on dismount")]
                public bool DisableHoverOnDismount = true;

                [JsonProperty(PropertyName = "Use fuel while hovering")]
                public bool UseFuelOnHover = true;

                [JsonProperty(PropertyName = "Keep engine on when hovering")]
                public bool KeepEngineOnHover = true;

                [JsonProperty(PropertyName = "Enable helicopter rotation on hover")]
                public bool EnableRotationOnHover = true;

                [JsonProperty(PropertyName = "Disable hover on change seats")]
                public bool DisableHoverOnSeat = false;

                [JsonProperty(PropertyName = "Hover on seat change")]
                public bool HoverOnSeatSwitch = true;

                [JsonProperty(PropertyName = "Timed hover")]
                public bool TimedHover = false;

                [JsonProperty(PropertyName = "Timed hover duration")]
                public float HoverDuration = 60;
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
