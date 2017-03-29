﻿/*
 *  File: VehicleManager.cs
 *  Author: Chenko
 *  Date: 12/24/2016
 * 
 * 
 *  Purpose: Loads vehicles from the database and provides functions to manage them
 * 
 * 
 * */


using System.Collections.Generic;
using GTANetworkServer;
using GTANetworkShared;
using MongoDB.Driver;
using RoleplayServer.resources.core;
using RoleplayServer.resources.database_manager;
using RoleplayServer.resources.job_manager;
using RoleplayServer.resources.player_manager;

namespace RoleplayServer.resources.vehicle_manager
{
    public class VehicleManager : Script
    {
        public static List<Vehicle> Vehicles = new List<Vehicle>();
       
        /*
        * 
        * ========== CONSTRUCTOR =========
        * 
        */

        public VehicleManager()
        {
            DebugManager.DebugMessage("[VehicleM] Initilizing vehicle manager...");

            // Register callbacks
            API.onPlayerEnterVehicle += OnPlayerEnterVehicle;
            API.onVehicleDeath += OnVehicleDeath;
            API.onPlayerExitVehicle += OnPlayerExitVehicle;

            // Create vehicle table + 
            load_all_unowned_vehicles();

            DebugManager.DebugMessage("[VehicleM] Vehicle Manager initalized!");
        }

        /*
        * 
        * ========== COMMANDS =========
        * 
        */

        [Command("spawnveh")]
        public void spawnveh_cmd(Client player, VehicleHash model, int color1 = 0, int color2 = 0, int dimension = 0)
        {
            var pos = player.position;
            var rot = player.rotation;

            var veh = CreateUnownedVehicle(model, pos, rot, color1, color2, dimension);
            spawn_vehicle(veh);
            
            API.setPlayerIntoVehicle(player, veh.NetHandle, -1);
            API.setVehicleEngineStatus(veh.NetHandle, true);

            API.sendChatMessageToPlayer(player, "You have spawned a " + model);
            API.sendChatMessageToPlayer(player, "This vehicle is unsaved and may behave incorrectly.");
        }

        [Command("savevehicle")]
        public void savevehicle_cmd(Client player)
        {
            var vehHandle = API.getPlayerVehicle(player);
            var veh = GetVehFromNetHandle(vehHandle);

            if(veh == null)
            {
                API.sendNotificationToPlayer(player, "~r~ ERROR: You are not inside a vehicle.");
                return;
            }

            if(veh.VehType != Vehicle.VehTypeTemp)
            {
                API.sendNotificationToPlayer(player, "~r~ ERROR: You must be inside a temporary vehicle to save it.");
                return;
            }

            if(veh.is_saved())
            {
                API.sendNotificationToPlayer(player, "~r~ ERROR: This vehicle is already saved into the database.");
                return;
            }

            
            veh.Insert();

            veh.VehType = Vehicle.VehTypePerm;
            veh.Save();

            API.sendChatMessageToPlayer(player, "You have saved vehicle " + Vehicles.IndexOf(veh) + ". (SQL: " + veh.Id + ")");
        }

        [Command("savepos")]
        public void savepos_cmd(Client player, int i)
        {
            var pos = player.position;
            var rot = player.rotation;

            API.consoleOutput(i + " " + pos + " " + rot);
            API.sendNotificationToPlayer(player, "Saved");
            API.sendChatMessageToPlayer(player,"Position: " + pos + "Rotation: " + rot);

        }

        [Command("tele")]
        public void Tele(Client player)
        {
            API.setEntityPosition(player.handle, new Vector3(403, -996, -99));
            API.sendChatMessageToPlayer(player, "teleported");
        }


        /*
        * 
        * ========== CALLBACKS =========
        * 
        */

        private void OnPlayerEnterVehicle(Client player, NetHandle vehicleHandle)
        {
            // Admin check in future

            var veh = GetVehFromNetHandle(vehicleHandle);
            API.setBlipTransparency(veh.Blip, 0);


            Character character = API.getEntityData(player.handle, "Character");

            API.sendChatMessageToPlayer(player, "~w~[VehicleM] You have entered vehicle ~r~" + Vehicles.IndexOf(veh) + "(Owned by: " + veh.OwnerName + ")");

            API.sendChatMessageToPlayer(player, "~y~ Press \"N\" on your keyboard to access the vehicle menu.");

            //Vehicle Interaction Menu Setup
            var vehInfo = API.getVehicleDisplayName(veh.VehModel) + " - " + veh.LicensePlate;
            API.setEntitySyncedData(player.handle, "CurrentVehicleInfo", vehInfo);
            API.setEntitySyncedData(player.handle, "OwnsVehicle", DoesPlayerHaveVehicleAccess(player, veh));

            if (API.getPlayerVehicleSeat(player) == -1)
            {
                veh.Driver = character;
            }
        }

        public void OnPlayerExitVehicle(Client player, NetHandle vehicleHandle)
        {
            var veh = GetVehFromNetHandle(vehicleHandle);

            if (veh == null)
            {
                DebugManager.DebugMessage("[VehicleVM] OnPlayerExitVehicle received null Vehicle.");
                return;
            }

            API.setBlipTransparency(veh.Blip, 100);

            if (veh.VehType == Vehicle.VehTypeTemp)
            {
                despawn_vehicle(veh);
                delete_vehicle(veh);

                API.sendNotificationToPlayer(player, "Your vehicle was deleted on exit because it was temporary.");
            }

            if (veh.Driver == API.getEntityData(player, "Character"))
                veh.Driver = null;

            Character character = API.getEntityData(player.handle, "Character");
            character.LastVehicle = veh;
        }

        public void OnVehicleDeath(NetHandle vehicleHandle)
        {
            var veh = GetVehFromNetHandle(vehicleHandle);
            API.consoleOutput("Vehicle " + vehicleHandle + " died");
            API.delay(veh.RespawnDelay, true, () =>
            {
                respawn_vehicle(veh);
            });
        }

        /*
        * 
        * ========== FUNCTIONS =========
        * 
        */

        public Vehicle CreateUnownedVehicle(VehicleHash model, Vector3 pos, Vector3 rot, int color1 = 0, int color2 = 0, int dimension = 0)
        {
            var veh = new Vehicle();

            veh.VehModel = model;
            veh.SpawnPos = pos;
            veh.SpawnRot = rot;
            veh.SpawnColors[0] = color1;
            veh.SpawnColors[1] = color2;
            veh.SpawnDimension = dimension;
            veh.LicensePlate = "ABC123";

            Vehicles.Add(veh);

            return veh;
        }

        public void delete_vehicle(Vehicle veh)
        {
            Vehicles.Remove(veh);
        }

        public static Vehicle GetVehFromNetHandle(NetHandle handle)
        {
            return API.shared.getEntityData(handle, "Vehicle");
        }

        public static int spawn_vehicle(Vehicle veh, Vector3 pos)
        {
            var returnCode = veh.Spawn(pos);

            if (returnCode == 1)
            {
                API.shared.setEntityData(veh.NetHandle, "Vehicle", veh);
            }
            
            API.shared.setVehicleEngineStatus(veh.NetHandle, false);
            return returnCode;
        }

        public static int spawn_vehicle(Vehicle veh)
        {
            return spawn_vehicle(veh, veh.SpawnPos);
        }

        public static int despawn_vehicle(Vehicle veh)
        {
            var returnCode = veh.Despawn();

            if (returnCode == 1)
            {
                API.shared.resetEntityData(veh.NetHandle, "Vehicle");
            }

            return returnCode;
        }

        public static int respawn_vehicle(Vehicle veh, Vector3 pos)
        {
            if (API.shared.hasEntityData(veh.NetHandle, "Vehicle"))
            {
                despawn_vehicle(veh);
            }
            return spawn_vehicle(veh, pos);
        }

        public static int respawn_vehicle(Vehicle veh)
        {
            return respawn_vehicle(veh, veh.SpawnPos);
        }

        public static bool DoesPlayerHaveVehicleAccess(Client player, Vehicle vehicle)
        {
            Account account = API.shared.getEntityData(player.handle, "Account");
            Character character = API.shared.getEntityData(player.handle, "Character");

            if (account.AdminLevel >= 3) { return true; }
            if (character.Id == vehicle.OwnerId) { return true; }
            //faction check
            if(character.JobOne == vehicle.Job) { return true; }
            //gang check

            return false;
        }

        public void load_all_unowned_vehicles()
        {
            var filter = Builders<Vehicle>.Filter.Eq("OwnerName", "None");
            var unownedVehicles = DatabaseManager.VehicleTable.Find(filter).ToList();

            foreach (var v in unownedVehicles)
            {
                v.Job = JobManager.GetJobById(v.JobId);
                spawn_vehicle(v);
                Vehicles.Add(v);
            }

            DebugManager.DebugMessage("Loaded " + unownedVehicles.Count + " unowned vehicles from the database.");
        }

    }
}
