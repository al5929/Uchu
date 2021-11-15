// Thanks to Simon Nitzsche for his research into racing
// https://github.com/SimonNitzsche/OpCrux-Server/
// https://www.youtube.com/watch?v=X5qvEDmtE5U

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using InfectedRose.Luz;
using RakDotNet.IO;
using Uchu.Core;
using Uchu.Physics;

namespace Uchu.World
{
    public class RacingControlComponent : ScriptedActivityComponent
    {
        public override ComponentId Id => ComponentId.RacingControlComponent;

        private List<RacingPlayerInfo> _players = new List<RacingPlayerInfo>();

        private RaceInfo _raceInfo = new RaceInfo();

        private RacingStatus _racingStatus = RacingStatus.None;

        private LuzPathData _path;

        public RacingControlComponent()
        {
            _raceInfo.LapCount = 3;
            _raceInfo.PathName = "MainPath"; // MainPath
            Listen(OnStart, async () =>
            {
                await LoadAsync();
            });
        }

        private async Task LoadAsync()
        {
            _path = Zone.ZoneInfo.LuzFile.PathData.FirstOrDefault(path => path.PathName == "MainPath");

            if (_path == default)
                throw new Exception("Path not found");

            Listen(this.GameObject.OnMessageBoxRespond, OnMessageBoxRespond);

            Listen(Zone.OnPlayerLoad, player =>
            {
                Listen(player.OnWorldLoad, () => OnPlayerLoad(player));
                Listen(player.OnRequestDie, (msg) => OnPlayerRequestDie(player));
                Listen(player.OnRacingPlayerInfoResetFinished, () => OnRacingPlayerInfoResetFinished(player));
                Listen(player.OnAcknowledgePossession, vehicle => OnAcknowledgePossession(player, vehicle));
            });
        }

        /// <summary>
        /// Message box response handler
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        private void OnMessageBoxRespond(Player player, MessageBoxRespondMessage message)
        {
            Logger.Information($"Button - {message.Button} {message.Identifier} {message.UserData}");
            if (message.Identifier == "ACT_RACE_EXIT_THE_RACE?" && message.Button == 1)
            {
                _players.RemoveAll(info => info.Player == player);
                // TODO: use zone corresponding to this racing world
                player.SendToWorldAsync(1200, new Vector3(248.8f, 287.4f, 186.9f), new Quaternion(0, 0.7f, 0, 0.7f));
            }
        }

        /// <summary>
        /// This runs when the player loads into the world, it registers the player
        /// </summary>
        /// <param name="player"></param>
        private void OnPlayerLoad(Player player)
        {
            Logger.Information("Player loaded into racing");

            // Register player
            this._players.Add(new RacingPlayerInfo
            {
                Player = player,
                PlayerLoaded = true,
                PlayerIndex = (uint) _players.Count + 1,
                NoSmashOnReload = true,
            });

            LoadPlayerCar(player);
            Zone.Schedule(InitRace, 10000);
        }

        /// <summary>
        /// Set up the player's car
        /// </summary>
        /// <param name="player"></param>
        /// <exception cref="Exception"></exception>
        private void LoadPlayerCar(Player player)
        {
            // Get position and rotation
            var startPosition = _path.Waypoints.First().Position + Vector3.UnitY * 3;
            var spacing = 15;
            var range = _players.Count * spacing;
            startPosition += Vector3.UnitZ * range;

            var startRotation = Quaternion.CreateFromYawPitchRoll(((float) Math.PI) * -0.5f, 0 , 0);

            // Create car
            player.Teleport(startPosition, startRotation);
            GameObject car = GameObject.Instantiate(this.GameObject.Zone.ZoneControlObject, 8092, startPosition, startRotation);

            // Setup imagination
            var destroyableComponent = car.GetComponent<DestroyableComponent>();
            destroyableComponent.MaxImagination = 60;
            destroyableComponent.Imagination = 0;

            // Let the player posses the car
            // Listen(player.OnAcknowledgePossession, this.OnAcknowledgePossession);
            var possessableComponent = car.GetComponent<PossessableComponent>();
            possessableComponent.Driver = player;
            var characterComponent = player.GetComponent<CharacterComponent>();
            characterComponent.VehicleObject = car;

            // Get custom parts
            var inventory = player.GetComponent<InventoryManagerComponent>();
            var carItem = inventory.FindItem(Lot.ModularCar);
            var moduleAssemblyComponent = car.GetComponent<ModuleAssemblyComponent>();
            if (carItem == null)
                moduleAssemblyComponent.SetAssembly("1:8129;1:8130;1:13513;1:13512;1:13515;1:13516;1:13514;"); // Fallback
            else
                moduleAssemblyComponent.SetAssembly(carItem.Settings["assemblyPartLOTs"].ToString());

            Start(car);
            GameObject.Construct(car);
            GameObject.Serialize(player);

            AddPlayer(player);

            Zone.BroadcastMessage(new RacingSetPlayerResetInfoMessage
            {
                Associate = this.GameObject,
                CurrentLap = 0,
                FurthestResetPlane = 0,
                PlayerId = player,
                RespawnPos = startPosition,
                UpcomingPlane = 1,
            });

            // TODO Set Jetpack mode?

            Zone.BroadcastMessage(new NotifyVehicleOfRacingObjectMessage
            {
                Associate = car,
                RacingObjectId = this.GameObject, // zonecontrol
            });

            Zone.BroadcastMessage(new VehicleSetWheelLockStateMessage
            {
                Associate = car,
                ExtraFriction = false,
                Locked = true
            });

            Zone.BroadcastMessage(new RacingPlayerLoadedMessage
            {
                Associate = this.GameObject,
                PlayerId = player,
                VehicleId = car,
            });

            // Register car for player
            for (var i = 0; i < _players.Count; i++)
            {
                RacingPlayerInfo info = _players[i];
                if (info.Player == player)
                {
                    info.Vehicle = car;
                    _players[i] = info;
                }
            }
        }

        private void InitRace()
        {
            if (_racingStatus == RacingStatus.Loaded)
                return;

            _racingStatus = RacingStatus.Loaded;

            Zone.BroadcastMessage(new NotifyRacingClientMessage
            {
                Associate = this.GameObject,
                EventType = RacingClientNotificationType.ActivityStart,
            });

            // Start after 6 seconds
            Zone.Schedule(StartRace, 6000);
        }

        private void StartRace()
        {
            if (_racingStatus == RacingStatus.Started)
                return;

            _racingStatus = RacingStatus.Started;

            // Start imagination spawners
            var minSpawner = Zone.SpawnerNetworks.FirstOrDefault(gameObject => gameObject.Name == "ImaginationSpawn_Min");
            var medSpawner = Zone.SpawnerNetworks.FirstOrDefault(gameObject => gameObject.Name == "ImaginationSpawn_Med");
            var maxSpawner = Zone.SpawnerNetworks.FirstOrDefault(gameObject => gameObject.Name == "ImaginationSpawn_Max");

            minSpawner?.Activate();
            minSpawner?.SpawnAll();

            if (_players.Count > 2)
            {
                medSpawner?.Activate();
                medSpawner?.SpawnAll();
            }

            if (_players.Count > 4)
            {
                maxSpawner?.Activate();
                maxSpawner?.SpawnAll();
            }

            // Respawn points
            // This is not the most efficient way to do this.
            // This checks the distance to every respawn point every physics tick,
            // but we only really need to check the next one (or nearest few).
            for (var i = 0; i < _path.Waypoints.Length; i++)
            {
                var proximityObject = GameObject.Instantiate(GameObject.Zone);
                var physics = proximityObject.AddComponent<PhysicsComponent>();
                var physicsObject = SphereBody.Create(
                    GameObject.Zone.Simulation,
                    _path.Waypoints[i].Position,
                    50f);
                physics.SetPhysics(physicsObject);

                // Listen for players entering and leaving.
                var playersInPhysicsObject = new List<Player>();
                var index = i;
                this.Listen(physics.OnEnter, component =>
                {
                    if (!(component.GameObject is Player player)) return;
                    if (playersInPhysicsObject.Contains(player)) return;
                    playersInPhysicsObject.Add(player);

                    this.PlayerReachedCheckpoint(player, index);
                });
                this.Listen(physics.OnLeave, component =>
                {
                    if (!(component.GameObject is Player player)) return;
                    if (!playersInPhysicsObject.Contains(player)) return;
                    playersInPhysicsObject.Remove(player);
                });
            }

            // Go!
            foreach (var info in _players)
            {
                Zone.BroadcastMessage(new VehicleUnlockInputMessage
                {
                    Associate = info.Vehicle,
                    LockWheels = false,
                });
            }

            Zone.BroadcastMessage(new ActivityStartMessage
            {
                Associate = this.GameObject,
            });

            GameObject.Serialize(this.GameObject);
        }

        // todo
        // RacingClientReady client->server
        // RacingPlayerLoaded server->client
        // RacingResetPlayerToLastReset server->client --> this is like player reached respawn checkpoint
        // RacingSetPlayerResetInfo server->client
        // RacingPlayerInfoResetFinished client->server

        // in capture even before acknowledge poss:
        // teleport msg [player]
        // bIgnoreY = False
        // bSetRotation = True
        // bSkipAllChecks = False
        // pos = (-1457.711669921875, 794.0, -351.61419677734375)
        // useNavmesh = False
        // w = 0.5037656426429749
        // x = 0.0
        // y = 0.8638404011726379
        // z = 0.0

        public void OnAcknowledgePossession(Player player, GameObject vehicle)
        {
        }

        /// <summary>
        /// Respawns the player's car after a crash
        /// </summary>
        /// <param name="player"></param>
        private void OnRacingPlayerInfoResetFinished(Player player)
        {
            var playerInfoIndex = _players.FindIndex(x => x.Player == player);
            var playerInfo = _players[playerInfoIndex];
            var car = playerInfo.Vehicle;

            if (!playerInfo.NoSmashOnReload)
            {
                car.GetComponent<DestructibleComponent>().SmashAsync(player);

                player.Message(new VehicleUnlockInputMessage
                {
                    Associate = car,
                    LockWheels = false,
                });

                player.Message(new VehicleSetWheelLockStateMessage
                {
                    Associate = car,
                    ExtraFriction = false,
                    Locked = false,
                });

                car.GetComponent<DestructibleComponent>().ResurrectAsync();

                Zone.BroadcastMessage(new ResurrectMessage
                {
                    Associate = car,
                });
            }

            playerInfo.NoSmashOnReload = false;
            _players[playerInfoIndex] = playerInfo;
        }

        /// <summary>
        /// Handler for when the client tells the server that the car crashed
        /// </summary>
        /// <param name="player"></param>
        public void OnPlayerRequestDie(Player player)
        {
            var racingPlayer = _players.Find(info => info.Player == player);

            Logger.Information($"{player} requested death - respawning to {racingPlayer.RespawnPosition}");

            player.Message(new RacingSetPlayerResetInfoMessage
            {
                Associate = GameObject,
                CurrentLap = (int) racingPlayer.Lap,
                FurthestResetPlane = racingPlayer.RespawnIndex,
                PlayerId = player,
                RespawnPos = racingPlayer.RespawnPosition,
                UpcomingPlane = racingPlayer.RespawnIndex + 1,
            });

            player.Message(new RacingResetPlayerToLastResetMessage
            {
                Associate = GameObject,
                PlayerId = player,
            });
        }

        private void PlayerReachedCheckpoint(Player player, int index)
        {
            var waypoint = _path.Waypoints[index];
            var playerInfoIndex = _players.FindIndex(x => x.Player == player);
            var playerInfo = _players[playerInfoIndex];
            playerInfo.RespawnIndex = (uint) index;
            playerInfo.RespawnPosition = waypoint.Position;
            playerInfo.RespawnRotation = player.Transform.Rotation;
            _players[playerInfoIndex] = playerInfo;

            Logger.Information($"Player reached checkpoint: {index}");
        }

        public override void Construct(BitWriter writer)
        {
            this.Serialize(writer);
        }

        // override to be able to use ScriptedActivityComponent as base
        public override void Serialize(BitWriter writer)
        {
            for (var i = 0; i < this.Parameters.Count; i++)
            {
                var parameters = Parameters[i];
                parameters[0] = 1;
                parameters[1] = 265f; // doesn't appear on client
                parameters[2] = 87f; // doesn't appear on client
            }

            base.Serialize(writer);
            StructPacketParser.WritePacket(this.GetSerializePacket(), writer);
        }

        public new RacingControlSerialization GetSerializePacket()
        {
            var packet = this.GetPacket<RacingControlSerialization>();

            packet.ExpectedPlayerCount = (ushort)this.Participants.Count;

            packet.PreRacePlayerInfos = this._players.Select(info => new PreRacePlayerInfo
            {
                Player = info.Player,
                Vehicle = info.Vehicle,
                StartingPosition = info.PlayerIndex,
                IsReady = info.PlayerLoaded,
            }).ToArray();

            packet.RaceInfo = _raceInfo;

            packet.DuringRacePlayerInfos = this._players.Select(info => new DuringRacePlayerInfo
            {
                Player = info.Player,
                BestLapTime = (float) info.BestLapTime.TotalSeconds,
                RaceTime = (float) info.RaceTime.TotalSeconds
            }).ToArray();


            // TODO

            return packet;
        }

        private enum RacingStatus {
            None,
            Loaded,
            Started,
        }

        struct RacingPlayerInfo {
            public GameObject Player { get; set; }
            public GameObject Vehicle { get; set; }
            public uint PlayerIndex { get; set; }
            public bool PlayerLoaded { get; set; }
            public float[] Data { get; set; }
            public Vector3 RespawnPosition { get; set; }
            public Quaternion RespawnRotation { get; set; }
            public uint RespawnIndex { get; set; }
            public uint Lap { get; set; }
            public uint Finished { get; set; }
            public uint ReachedPoints { get; set; }
            public TimeSpan BestLapTime { get; set; }
            public TimeSpan LapTime { get; set; }
            public uint SmashedTimes { get; set; }
            public bool NoSmashOnReload { get; set; }
            public bool CollectedRewards { get; set; }
            public TimeSpan RaceTime { get; set; }
        };
    }
}
