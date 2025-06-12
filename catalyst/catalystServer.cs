

/* Server Program */
using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Dynamic;
using System.Xml.Serialization;
using System.Net.WebSockets;
using Microsoft.VisualBasic;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.ComponentModel.Design;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

[Flags]
public enum RoomFlags
{
    none = 0,
    IsLooted = 0b_0000_0001,
    isVisited = 0b_0000_0010
    
}

[Flags]
public enum PlayerStatus
{
    None = 0,
    IsLoggedIn = 0b_0000_0001,
    IsAdmin = 0b_0000_0010,
    IsBanned = 0b_0000_0100,
    IsInCombat = 0b_0000_1000,
    IsAlive = 0b_0001_0000,
}




public class PasswordUtils
{
    public static string HashPassword(string password)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}



public class NarrationSet
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Narration { get; set; }
}
    

public class NarrationLoader
{
    public static List<NarrationSet> LoadNarration(string path)
    {
        string json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        return JsonSerializer.Deserialize<List<NarrationSet>>(json, options);
    }

    public static void SendNarration(Socket client, string narration)
    {
        byte[] buffer = Encoding.ASCII.GetBytes(narration + "\n");
        client.Send(buffer);
    }

    public static void printHelp(Socket client)
    {
        var narrations = NarrationLoader.LoadNarration("narration.json");
        var help = narrations.FirstOrDefault(n => n.Name == "menu_help");
        if (help != null)
        {
            for (int i = 0; i < 100; i++)
            {
                CharacterCreator.SendToClient(client, "|\nV");
            }
            byte[] buffer = Encoding.ASCII.GetBytes(help.Narration + "\n");
            client.Send(buffer);
        }
        else
        {
            byte[] fallback = Encoding.ASCII.GetBytes("Help Menu Not Found.\n");
            client.Send(fallback);
        }
    }
}

public class Room
{
    public string Id { get; set; }
    public string RoomName { get; set; }
    public string Description { get; set; }
    public Dictionary<string, int> Loot { get; set; }
    public List<string> Connections { get; set; }
    public int X { get; set; }  // Column on map
    public int Y { get; set; }  // Row on map
}



public class SavedPlayer
{
    public string Name { get; set; }
    public string PasswordHash { get; set; }
    public string ClassId { get; set; }
    public int gold { get; set; }
    public int maxHealth{ get; set; }
    public int healthPoints { get; set; }
    public int maxMana { get; set; }
    public int manaPoints { get; set; }
    public Dictionary<string, int> Stats { get; set; }
    public string Location { get; set; }
    public List<string> Inventory { get; set; }
    public List<string> Skills { get; set; }
    public int Status { get; set; }
    public Dictionary<string, RoomFlags> RoomStates { get; set; } = new();
}

public class Npc
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Location { get; set; }
    public bool IsHostile { get; set; }
    public bool IsMerchant { get; set; }
    public string Faction { get; set; }
    public Dictionary<string, int> Inventory { get; set; }
    public Dictionary<string, int> MerchantStock { get; set; }
    public List<string> Quests { get; set; }
    public List<DialogueEntry> Dialogue { get; set; }
    public Dictionary<string, int> stats { get; set; }
    public string Behavior { get; set; }
    public List<string> Flags { get; set; }

}



public class DialogueOption
{
    public string Option { get; set; }
    public string Response { get; set; }
}

public class DialogueEntry
{
    public string text { get; set; }
    public List<DialogueOption> Options{ get; set; }
}

public static class RoomLoader
{
    public static Dictionary<string, Room> LoadRooms(string path)
    {
        string json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var rooms = JsonSerializer.Deserialize<List<Room>>(json, options);
        foreach (var key in rooms.Select(r => r.Id))
        {
            Console.WriteLine($"- {key}");
        }
        return rooms
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .ToDictionary(r => r.Id!, r => r);
    }

    public static void describeRoom(Socket client, Room room, SavedPlayer player)
    {
        if (room == null)
        {
            CharacterCreator.SendToClient(client, $"Your current location is unknown (ID: {player.Location}).");
            Console.WriteLine($"Room not found for ID: {player.Location}");
            return;
        }

        CharacterCreator.SendToClient(client, $"You are in: {room.RoomName}");
        CharacterCreator.SendToClient(client, room.Description);

        // Display exits
        var exitNames = room.Connections
            .Select(connId => RoomLoader.LoadRooms("locations.json").TryGetValue(connId, out var connectedRoom)
                ? connectedRoom.RoomName
                : connId);
        CharacterCreator.SendToClient(client, $"Exits: {string.Join(", ", exitNames)}");

        // Display loot (if any and not already looted)
        if (room.Loot != null &&
            (!player.RoomStates.TryGetValue(room.Id, out var flags) || !flags.HasFlag(RoomFlags.IsLooted)))
        {
            var lootDisplay = string.Join(", ", room.Loot.Select(kvp => $"{kvp.Key} x{kvp.Value}"));
            CharacterCreator.SendToClient(client, $"You see: {lootDisplay}");
        }

    }
    public static void RenderMap(Socket client, SavedPlayer player, Dictionary<string, Room> allRooms)
{
    const int mapWidth = 6;
    const int mapHeight = 6;
    const int cellWidth = 22;

    string[,] grid = new string[mapHeight, mapWidth];
    Dictionary<(int x, int y), Room> coordToRoom = new();

    foreach (var room in allRooms.Values)
    {
        if (!player.RoomStates.TryGetValue(room.Id, out var flags) || !flags.HasFlag(RoomFlags.isVisited))
            continue;

        if (room.X >= 0 && room.X < mapWidth && room.Y >= 0 && room.Y < mapHeight)
        {
            grid[room.Y, room.X] = room.RoomName;
            coordToRoom[(room.X, room.Y)] = room;
        }
    }

    for (int y = 0; y < mapHeight; y++)
    {
        StringBuilder top = new();
        StringBuilder nameLine = new();
        StringBuilder bottom = new();
        StringBuilder connector = new();
        StringBuilder verticalArrows = new();

        for (int x = 0; x < mapWidth; x++)
        {
            bool hasRoom = grid[y, x] != null;
            string cellSpace = new string(' ', cellWidth);

            top.Append(hasRoom ? "+--------------------+" : cellSpace);

            if (hasRoom)
            {
                var isCurrent = coordToRoom[(x, y)].Id == player.Location;
                string name = grid[y, x];
                if (isCurrent)
                    name = "(*) " + name;

                name = name.PadRight(20).Substring(0, 20);
                nameLine.Append($"| {name} |");
            }
            else
            {
                nameLine.Append(cellSpace);
            }

            bottom.Append(hasRoom ? "+--------------------+" : cellSpace);

            if (hasRoom && coordToRoom.TryGetValue((x, y), out var currentRoom))
            {
                bool connectsRight = currentRoom.Connections.Any(id =>
                    allRooms.TryGetValue(id, out var otherRoom) &&
                    otherRoom.X == x + 1 && otherRoom.Y == y &&
                    player.RoomStates.TryGetValue(otherRoom.Id, out var f) &&
                    f.HasFlag(RoomFlags.isVisited));

                connector.Append(connectsRight ? "         --->         " : cellSpace);

                bool connectsDown = currentRoom.Connections.Any(id =>
                    allRooms.TryGetValue(id, out var downRoom) &&
                    downRoom.X == x && downRoom.Y == y + 1 &&
                    player.RoomStates.TryGetValue(downRoom.Id, out var f2) &&
                    f2.HasFlag(RoomFlags.isVisited));

                verticalArrows.Append(connectsDown ? "          ↓           " : cellSpace);
            }
            else
            {
                connector.Append(cellSpace);
                verticalArrows.Append(cellSpace);
            }
        }

        CharacterCreator.SendToClient(client, top.ToString());
        CharacterCreator.SendToClient(client, nameLine.ToString());
        CharacterCreator.SendToClient(client, bottom.ToString());
        CharacterCreator.SendToClient(client, connector.ToString());
        CharacterCreator.SendToClient(client, verticalArrows.ToString());
    }
}


}


public static class NpcLoader
{
    public static Dictionary<string, Npc> LoadNpc(string path)
    {
        string json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var npcs = JsonSerializer.Deserialize<List<Npc>>(json, options);
        foreach (var key in npcs.Select(r => r.Id))
        {
            Console.WriteLine($"- {key}");
        }
        return npcs
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .ToDictionary(r => r.Id!, r => r);
    }
    public static void describeNpc(Socket client, string playerLocation, Dictionary<string, Npc> allNpcs)
    {
        var npcsInRoom = allNpcs.Values.Where(n => n.Location == playerLocation).ToList(); // Load once at start

        if (!npcsInRoom.Any())
        {
            CharacterCreator.SendToClient(client, "There are no NPCs nearby");
            return;
        }
        CharacterCreator.SendToClient(client, "You see the following NPCs");
        foreach (var npc in npcsInRoom)
        {
            CharacterCreator.SendToClient(client, $"- {npc.Name}: {npc.Description}");
        }
    }

    public static void loadDialogue(Socket client, SavedPlayer player, string talkCommand, Dictionary<string, Npc> allNpcs)
    {
        if (player == null)
        {
            CharacterCreator.SendToClient(client, "You must create or load a character first.");
            return;
        }

        string npcName = talkCommand.Substring(5).Trim().ToLower();
        var npcsInRoom = allNpcs.Values
            .Where(n => n.Location == player.Location)
            .ToList();

        var targetNpc = npcsInRoom.FirstOrDefault(n => n.Name.Equals(npcName, StringComparison.OrdinalIgnoreCase));

        if (targetNpc == null)
        {
            CharacterCreator.SendToClient(client, $"No NPC named '{npcName}' is here.");
            return;

        }

        if (targetNpc.Dialogue == null || targetNpc.Dialogue.Count == 0)
        {
            CharacterCreator.SendToClient(client, $"{targetNpc.Name} has nothing to say.");
            return;

        }

        foreach (var entry in targetNpc.Dialogue)
{
    CharacterCreator.SendToClient(client, $"{targetNpc.Name}: {entry.text}");

    if (entry.Options != null && entry.Options.Count > 0)
    {
        for (int i = 0; i < entry.Options.Count; i++)
        {
            CharacterCreator.SendToClient(client, $"{i + 1}. {entry.Options[i].Option}");
        }

        CharacterCreator.SendToClient(client, "Choose an option (enter number):");
        string input = CharacterCreator.RecieveFromClient(client).Trim();

        if (int.TryParse(input, out int optionIndex) && optionIndex > 0 && optionIndex <= entry.Options.Count)
        {
            var selected = entry.Options[optionIndex - 1];
            CharacterCreator.SendToClient(client, $"{targetNpc.Name} replies: {selected.Response}");

            // 🛍 Merchant logic
            if (selected.Option.Equals("Browse wares", StringComparison.OrdinalIgnoreCase) && targetNpc.IsMerchant)
            {
                CharacterCreator.SendToClient(client, $"{targetNpc.Name} offers:");

                int index = 1;
                foreach (var item in targetNpc.MerchantStock)
                {
                    CharacterCreator.SendToClient(client, $"{index}. {item.Key} - {item.Value} gold");
                    index++;
                }

                CharacterCreator.SendToClient(client, "Type the name of the item to buy or 'exit' to cancel:");
                string buyInput = CharacterCreator.RecieveFromClient(client).Trim().ToLower();

                if (buyInput != "exit" && targetNpc.MerchantStock.TryGetValue(buyInput, out int price))
                {
                    CharacterCreator.SendToClient(client, $"Are you sure you want to buy '{buyInput}' for {price} gold? (y/n)");
                    string confirmBuy = CharacterCreator.RecieveFromClient(client).Trim().ToLower();

                    if (confirmBuy == "y")
                    {
                        if (player.gold >= price)
                        {
                            player.gold -= price;
                            player.Inventory.Add(buyInput);
                            CharacterCreator.SendToClient(client, $"You bought a {buyInput} for {price} gold.");
                            CharacterCreator.SavePlayer(player);
                        }
                        else
                        {
                            CharacterCreator.SendToClient(client, "You don’t have enough gold.");
                        }
                    }
                    else
                    {
                        CharacterCreator.SendToClient(client, "Purchase canceled.");
                    }
                }
                else if (buyInput != "exit")
                {
                    CharacterCreator.SendToClient(client, "That item is not available.");
                }
            }
        }
        else
        {
            CharacterCreator.SendToClient(client, "Invalid choice.");
        }
    }

        }
        
    }
    
}

public class PlayerClass
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int health { get; set; }
    public int mana { get; set; }
    public string Description { get; set; }
    public int StatPointsToDistribute { get; set; }
    public List<string> AvailableStats { get; set; }
    public Dictionary<string, int> RecommendedBuild { get; set; }
    public List<string> StartingSkills { get; set; }
    public string StartingWeapon { get; set; }
    public string ResourceType { get; set; }
    public List<string> Tags { get; set; }
}


public class CharacterCreator {

    public string name { get; set; }
    public PlayerClass Class { get; set; }
    public int maxHealth { get; set; }
    public int maxMana { get; set; }
    public int health { get; set; }
    public int mana { get; set; }
    public Dictionary<string, int> Stats { get; set; }
    
    public List<string> Inventory { get; set; }
    public List<string> Skills { get; set; }
    public int Status { get; set; }

    public static int CalculateMaxHP(int baseHP, int constitution)
    {
        return baseHP + (int)(10 * Math.Pow(constitution, 0.1));
    }

    public static int CalculateMaxMana(int baseMana, int intelligence)
    {
        return baseMana + (int)(10 * Math.Pow(intelligence, 0.1));
    }

    public void PlayerStatsDistribute(Socket client, PlayerClass selectedClass)
    {


        this.Class = selectedClass;
        Console.WriteLine("Inside PlayerStatsDistributed");
        var classes = LoadClasses("classes.json");
        Stats = new Dictionary<string, int>();
        Inventory = new List<string>();
        Skills = new List<string>();

        PlayerStatus Status = PlayerStatus.None;

        CharacterCreator creator = new CharacterCreator();


        SendToClient(client, "Enter your character's name: ");

        this.name = RecieveFromClient(client).Trim();
        Console.WriteLine($"Name Received: {this.name}");

        string startWeapon = Class.StartingWeapon;

  

        Inventory.Add(startWeapon);

        Skills.Add($"{string.Join(", ", Class.StartingSkills)}");




        SendToClient(client, $"Recommended Stats for {selectedClass}\n");
        string recommendStats = $"Stats - {string.Join(", ", Class.RecommendedBuild.Select(kv => $"{kv.Key}: {kv.Value}"))}";
        SendToClient(client, recommendStats);

        SendToClient(client, "Do you want to alter the stats? (y/n)");
        string choice = RecieveFromClient(client).Trim();

        string confirmation = "n";

        do
        {
            int remaining = Class.StatPointsToDistribute;


            if (choice.ToLower() == "y")
            {


                foreach (var stat in Class.AvailableStats)
                {
                    Stats[stat] = 1;
                }
                while (remaining > 0)
                {
                    SendToClient(client, $"You have {remaining} points remaining.");
                    SendToClient(client, $"Available Stats: {string.Join(", ", Class.AvailableStats)}");
                    SendToClient(client, "Type the stat you want to increase. (All have been set to 1)");

                    string statChoice = RecieveFromClient(client).Trim().ToLower();

                    if (!Stats.ContainsKey(statChoice))
                    {
                        SendToClient(client, "Invalid stat name. Try again.");
                        continue;
                    }

                    SendToClient(client, $"How many points to add to {statChoice}? (0 to cancel)");

                    string input = RecieveFromClient(client).Trim();

                    if (!int.TryParse(input, out int points) || points < 0 || points > remaining)
                    {
                        SendToClient(client, $"Invalid amount. Must be between 0 and {remaining}.");
                        continue;
                    }
                    Stats[statChoice] += points;
                    remaining -= points;
                }
                // Display the final distribution for confirmation
                SendToClient(client, "Final stat distribution:");
                foreach (var kvp in Stats)
                {
                    SendToClient(client, $"{kvp.Key}: {kvp.Value}");
                }

                SendToClient(client, "Are you happy with this distribution? (y/n)");
                confirmation = RecieveFromClient(client).Trim().ToLower();
            }
            else
            {
                confirmation = "y"; // if user didn't want to alter stats at all, exit loop
                Stats = new Dictionary<string, int>(Class.RecommendedBuild);

            }
        } while (confirmation.ToLower() == "n");

        int con = Stats.ContainsKey("constitution") ? Stats["constitution"] : 0;


        int hp = CalculateMaxHP(Class.health, con);
        int intelligence = Stats.ContainsKey("intelligence") ? Stats["intelligence"] : 0;


        int mp = CalculateMaxMana(Class.mana, intelligence);


        SendToClient(client, "Create a password: (there is no way to reset password so do not forget it!)");
        string playerPassword = RecieveFromClient(client).Trim();
        Console.WriteLine($"Password Received {playerPassword}");
        string hashed = PasswordUtils.HashPassword(playerPassword);
        Console.WriteLine($"Hashed Password {hashed}");


        SavedPlayer player = new SavedPlayer
        {
            Name = this.name,
            ClassId = this.Class.Id,
            gold = 10,
            maxHealth = hp,
            healthPoints = hp,
            maxMana = mp,
            manaPoints = mp,
            Stats = new Dictionary<string, int>(this.Stats),
            PasswordHash = PasswordUtils.HashPassword(playerPassword),
            Location = "cradle_01",
            Inventory = new List<string>(this.Inventory),
            Skills = new List<string>(this.Skills),
            Status = (int)(PlayerStatus.IsAlive | PlayerStatus.IsLoggedIn)

        };

        SavePlayer(player);



    }

    static public void SavePlayer (SavedPlayer newPlayer){
        List<SavedPlayer> allPlayers = LoadPlayers("pcs.json");
        var existing = allPlayers.FirstOrDefault(p => p.Name.Equals(newPlayer.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            allPlayers.Remove(existing);
        }
        allPlayers.Add(newPlayer);

        File.WriteAllText("pcs.json", JsonSerializer.Serialize(allPlayers, new JsonSerializerOptions { WriteIndented = true }));
    }

    static public List<SavedPlayer> LoadPlayers(string path)
    {
        if (!File.Exists(path))
            return new List<SavedPlayer>();

        string json = File.ReadAllText(path);

        if (string.IsNullOrWhiteSpace(json))
            return new List<SavedPlayer>();

        return JsonSerializer.Deserialize<List<SavedPlayer>>(json) ?? new List<SavedPlayer>();
    }



    

    public List<PlayerClass> LoadClasses(string path)
    {
        string json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        return JsonSerializer.Deserialize<List<PlayerClass>>(json, options);
    }


    public void ShowAvailableClasses(List<PlayerClass> classes, Socket client)
    {
        StringBuilder sb = new StringBuilder("Choose Your Class\n");

        for (int i = 0; i < classes.Count; i++)
        {
            //Console.WriteLine($"DEBUG: Class[{i}] = {classes[i].Name}, {classes[i].Description}");

            sb.AppendLine($"\n{i + 1}. {classes[i].Name} - {classes[i].Description} \n\tResource Type - {classes[i].ResourceType}\n\tStarting weapon - {classes[i].StartingWeapon}\n\tStarting Skills - {string.Join(", ", classes[i].StartingSkills)}");

        }

        SendToClient(client, sb.ToString());
    }

    public PlayerClass SelectClass(Socket client, List<PlayerClass> classes)
    {
        while(true){
            string input = RecieveFromClient(client).Trim();
            if(int.TryParse(input, out int choice) && choice >= 1 && choice <= classes.Count)
            {
                return classes[choice - 1];
                
            }
            SendToClient(client, "Invalid Selection. Try Again:");
        }
    }

    


    static public void SendToClient(Socket client, string message)
    {
        byte[] buffer = Encoding.ASCII.GetBytes(message + "\n");
        client.Send(buffer);
    }

    
    static public string RecieveFromClient(Socket client)
    {
        byte[] buffer = new byte[1024];
        int length = client.Receive(buffer);
        return Encoding.ASCII.GetString(buffer, 0, length);
    }

}



public class Server
{


    private static void HandleClient(Socket clientSocket, string narration)
    {
        PlayerStatus status = PlayerStatus.IsLoggedIn;
        var allRooms = RoomLoader.LoadRooms("locations.json"); // Load once at start
        Console.WriteLine("Loaded rooms:");
        var allNpcs = NpcLoader.LoadNpc("npcs.json");

        RoomFlags roomFlags;



        SavedPlayer player = null!;  // <-- Declare it here


        // Check if player is logged in



        try
        {
            Console.WriteLine($"Trying to Send: {narration}");
            if (!string.IsNullOrEmpty(narration))
            {
                NarrationLoader.SendNarration(clientSocket, narration);
            }
            byte[] buffer = new byte[1024];
            while (true)
            {
                int received = clientSocket.Receive(buffer);
                string message = Encoding.ASCII.GetString(buffer, 0, received);
                Console.WriteLine("Received: " + message);

                switch (message)
                {
                    case "help":
                        NarrationLoader.printHelp(clientSocket);
                        break;

                    case "load_character":
                        CharacterCreator.SendToClient(clientSocket, "Enter your Character's name:");
                        string enteredName = CharacterCreator.RecieveFromClient(clientSocket).Trim();

                        var players = CharacterCreator.LoadPlayers("pcs.json");
                        var matched = players.FirstOrDefault(p => p.Name.Equals(enteredName, StringComparison.OrdinalIgnoreCase));

                        if (matched == null)
                        {
                            CharacterCreator.SendToClient(clientSocket, "No character found with that name.");
                            break;
                        }

                        CharacterCreator.SendToClient(clientSocket, "Enter your Password:");
                        string enteredPassword = CharacterCreator.RecieveFromClient(clientSocket).Trim();

                        string hashed = PasswordUtils.HashPassword(enteredPassword);

                        if (hashed != matched.PasswordHash)
                        {
                            CharacterCreator.SendToClient(clientSocket, "Incorrect password.");
                            break;
                        }

                        CharacterCreator.SendToClient(clientSocket, $"Welcome back, {matched.Name}!");

                        player = matched;

                        // You can now access the player's data
                        Dictionary<string, int> stats = player.Stats;
                        CharacterCreator.SendToClient(clientSocket, "Your stats:");
                        foreach (var kvp in stats)
                        {
                            CharacterCreator.SendToClient(clientSocket, $"{kvp.Key}: {kvp.Value}");
                        }
                        allRooms.TryGetValue(player.Location, out var loadRoom);
                        RoomLoader.describeRoom(clientSocket, loadRoom, player);
                        NpcLoader.describeNpc(clientSocket, player.Location, allNpcs);


                        break;

                    case "create_character":
                        var creator = new CharacterCreator();
                        var classes = creator.LoadClasses("classes.json");
                        creator.ShowAvailableClasses(classes, clientSocket);
                        var selected = creator.SelectClass(clientSocket, classes);
                        string confirm = $"You have chosen: {selected.Name}.";
                        NarrationLoader.SendNarration(clientSocket, confirm);
                        creator.PlayerStatsDistribute(clientSocket, selected);
                        var createdPlayer = CharacterCreator.LoadPlayers("pcs.json");

                        var matchedCharacter = createdPlayer.FirstOrDefault(p => p.Name.Equals(creator.name, StringComparison.OrdinalIgnoreCase));
                        if (matchedCharacter != null)
                        {
                            player = matchedCharacter;
                            /*                             CharacterCreator.SendToClient(clientSocket, $"Character {player.Name} created and loaded.");
                                                         if (allRooms.TryGetValue(player.Location, out var startingRoom))
                                                        {
                                                            CharacterCreator.SendToClient(clientSocket, $"You are in: {startingRoom.RoomName}");
                                                            CharacterCreator.SendToClient(clientSocket, startingRoom.Description);
                                                        } */
                        }
                        else
                        {
                            CharacterCreator.SendToClient(clientSocket, "Failed to load the newly created character.");
                        }
                        Dictionary<string, int> stat = player.Stats;
                        CharacterCreator.SendToClient(clientSocket, "Your stats:");
                        foreach (var kvp in stat)
                        {
                            CharacterCreator.SendToClient(clientSocket, $"{kvp.Key}: {kvp.Value}");
                        }

                        // Optional: assign current location, status flags, etc.
                        string StartLocation = player.Location;
                        Console.WriteLine($"Current Location {player.Location}");

                        allRooms.TryGetValue(player.Location, out var room);

                        RoomLoader.describeRoom(clientSocket, room, player);


                        /* if (allRooms.TryGetValue(player.Location, out var room))
                        /* {
                             CharacterCreator.SendToClient(clientSocket, $"You are in: {room.RoomName}");
                             CharacterCreator.SendToClient(clientSocket, room.Description);
                             CharacterCreator.SendToClient(clientSocket, $"Exits: {string.Join(", ", room.Connections)}");
                         }
                         else
                         {
                             CharacterCreator.SendToClient(clientSocket, $"Your current location is unknown (ID: {player.Location}).");
                             Console.WriteLine($"Room not found for ID: {player.Location}");
                         }
  */

                        break;

                    case string moveCommand when moveCommand.StartsWith("move "):
                        if (player == null)
                        {
                            CharacterCreator.SendToClient(clientSocket, "You must create or load character first");
                            break;
                        }

                        if (!allRooms.TryGetValue(player.Location, out var currentRoom))
                        {
                            CharacterCreator.SendToClient(clientSocket, "You are in an unknown location");
                            break;
                        }

                        string targetName = moveCommand.Substring(5).Trim();

                        // Try to match room name to a connected room
                        string? matchedRoomId = currentRoom.Connections
                            .Select(id => allRooms.GetValueOrDefault(id))
                            .Where(r => r != null && r.RoomName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                            .Select(r => r.Id)
                            .FirstOrDefault();

                        if (matchedRoomId == null)
                        {
                            CharacterCreator.SendToClient(clientSocket, $"No connected room named '{targetName}' found.");
                            break;
                        }

                        var newRoom = allRooms[matchedRoomId];

                        // Mark the current room as visited
                        player.RoomStates ??= new Dictionary<string, RoomFlags>();
                        player.RoomStates[player.Location] = player.RoomStates.GetValueOrDefault(player.Location) | RoomFlags.isVisited;

                        // Move the player
                        player.Location = newRoom.Id;


                        // Save and show new room
                        CharacterCreator.SavePlayer(player);
                        RoomLoader.describeRoom(clientSocket, newRoom, player);
                        NpcLoader.describeNpc(clientSocket, player.Location, allNpcs);
                        break;
                    
                    case string talkCommand when talkCommand.StartsWith("talk "):


                        NpcLoader.loadDialogue(clientSocket, player, talkCommand, allNpcs);

                        break;

                    case "loot_room":
                        int goldAmount = 0;

                        if (player == null)
                        {
                            CharacterCreator.SendToClient(clientSocket, "You must create or load a character first");
                            break;
                        }

                        if (!allRooms.TryGetValue(player.Location, out var currRoom))
                        {
                            CharacterCreator.SendToClient(clientSocket, "You're in an unknown locaton");
                            break;
                        }

                        if (currRoom.Loot == null || currRoom.Loot.Count == 0)
                        {
                            CharacterCreator.SendToClient(clientSocket, "There's nothing to loot here");
                            break;
                        }
                        
              // OPTIONAL: Check if player already looted this room
                        if (player.RoomStates != null &&
                            player.RoomStates.TryGetValue(player.Location, out var flags) &&
                            flags.HasFlag(RoomFlags.IsLooted))
                        {
                            CharacterCreator.SendToClient(clientSocket, "You already looted this room.");
                            break;
                        }
                        foreach (var kvp in currRoom.Loot)
                        {
                            for (int i = 0; i < kvp.Value; i++)
                            {
                                if (kvp.Key != "gold")
                                {
                                    CharacterCreator.SendToClient(clientSocket, $"Looted: {kvp.Key}");
                                    player.Inventory.Add(kvp.Key);
                                    CharacterCreator.SendToClient(clientSocket, $"You Looted: {kvp.Key} x{kvp.Value}");

                                }
                                else
                                {
                                    CharacterCreator.SendToClient(clientSocket, $"Looted: gold");

                                    goldAmount += 1;

                                }
                            }
                            CharacterCreator.SendToClient(clientSocket, $"Looted: gold X {goldAmount}");

                        }
                        player.gold += goldAmount;


                        player.RoomStates ??= new Dictionary<string, RoomFlags>();
                        player.RoomStates[player.Location] = player.RoomStates.GetValueOrDefault(player.Location) | RoomFlags.IsLooted;

                        CharacterCreator.SavePlayer(player);


                        break;


                    case "inventory":
                     if (player == null)
                        {
                            CharacterCreator.SendToClient(clientSocket, "You must create or load a character first");
                            break;
                        }
                        int gold = player.gold;

                        var inventoryGrouped = player.Inventory
                            .GroupBy(item => item)
                            .Select(group => $"{group.Key} x{group.Count()}");

                        string inventory = inventoryGrouped.Any()
                            ? string.Join(", ", inventoryGrouped)
                            : "Your inventory is empty.";

                        CharacterCreator.SendToClient(clientSocket, $"Inventory: gold x{gold} {inventory}");

                        break;

                    case "stats":
                     if (player == null)
                        {
                            CharacterCreator.SendToClient(clientSocket, "You must create or load a character first");
                            break;
                        }

                       

                        Dictionary<string, int> playerStat = player.Stats;
                        CharacterCreator.SendToClient(clientSocket, $"Health: {player.healthPoints}");
                        CharacterCreator.SendToClient(clientSocket, $"Mana: {player.manaPoints}");

                        foreach (var kvp in playerStat)
                        {
                            CharacterCreator.SendToClient(clientSocket, $"{kvp.Key}: {kvp.Value}");
                        }
                        break;
                    
                    case "map":
                     if (player == null)
                        {
                            CharacterCreator.SendToClient(clientSocket, "You must create or load a character first");
                            break;
                        }
                        RoomLoader.RenderMap(clientSocket, player, allRooms);
                        break;

                    case "where":
                     if (player == null)
                        {
                            CharacterCreator.SendToClient(clientSocket, "You must create or load a character first");
                            break;
                        }
                        if (allRooms.TryGetValue(player.Location, out var locRoom))
                        {
                            RoomLoader.describeRoom(clientSocket, locRoom, player);
                            NpcLoader.describeNpc(clientSocket, player.Location, allNpcs);

                        }
                        else
                        {
                            CharacterCreator.SendToClient(clientSocket, $"Unknown room ID: {player.Location}");
                        }
                        break;

                        case "get_ui_data":
                            if (player == null)
                            {
                                CharacterCreator.SendToClient(clientSocket, "{\"error\":\"No character loaded\"}");
                                break;
                            }
                               // Get NPCs at the player's current location
                        List<string> npcNames = new();
                        foreach (var npc in allNpcs.Values)
                        {
                            if (npc.Location == player.Location)
                            {
                                npcNames.Add(npc.Name); // Or npc.DisplayName if you have one
                            }
                        }



                        var uiData = new
                        {
                            classId = player.ClassId,
                            location = player.Location,
                            health = player.healthPoints,
                            mana = player.manaPoints,
                            playerGold = player.gold,
                            stats = player.Stats,
                            skills = player.Skills,
                            inventory = player.Inventory,
                            npcs = npcNames

                            };
                            string json = JsonSerializer.Serialize(uiData); // simplified name
                            CharacterCreator.SendToClient(clientSocket, json);
                            break;


                    default:
                        string response = " You said: " + message;
                        byte[] responseBytes = Encoding.ASCII.GetBytes(response);
                        clientSocket.Send(responseBytes);
                        break;
                }


            }

        }
        catch (Exception e)
        {
            Console.WriteLine("Client error: " + e.Message);
        }
        finally
        {
            clientSocket.Close();
        }
    }


    public static void Main()
    {

        var narrations = NarrationLoader.LoadNarration("narration.json");
        var opening = narrations.FirstOrDefault(n => n.Name == "scene_opening");

        Console.WriteLine($"Narration Set to: {opening?.Narration ?? "null"}");

        try
        {
            IPAddress ipAd = IPAddress.Parse("127.0.0.1");
            TcpListener myList = new TcpListener(ipAd, 8001);
            myList.Start();

            Console.WriteLine("Server is running on port 8001...");
            Console.WriteLine("Waiting for connections...");

            while (true)
            {
                Socket clientSocket = myList.AcceptSocket();
                Console.WriteLine("Connection accepted from " + clientSocket.RemoteEndPoint);

                Thread clientThread = new Thread(() => HandleClient(clientSocket, opening?.Narration));
                clientThread.Start();
            }

        }
        catch (Exception e)
        {
            Console.WriteLine("Error: " + e.Message);
        }
    }
}



    