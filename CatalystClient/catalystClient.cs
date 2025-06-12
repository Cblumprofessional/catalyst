using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Threading;

public class Client
{
    static Dictionary<string, JsonElement> uiData;

    public static void Main()
    {
        try
        {
            Console.SetWindowSize(100, 35);
            Console.SetBufferSize(100, 35);
            Console.Clear();
            Console.CursorVisible = true;


            TcpClient client = new TcpClient("127.0.0.1", 8001);
            NetworkStream stream = client.GetStream();

            Console.WriteLine("Connected to the Server.");

            string narration = ReceiveNarration(stream);
            DisplayWithTyping(narration);

            Console.WriteLine("Type a command and press Enter:");

            while (true)
            {
                ReadAllServerMessages(stream);

                Console.Write("> ");
                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (input.ToLower() == "exit")
                    break;

                //send message
                byte[] data = Encoding.ASCII.GetBytes(input);
                stream.Write(data, 0, data.Length);

                /* byte[] responseBuffer = new byte[2048];
                int bytesRead = stream.Read(responseBuffer, 0, responseBuffer.Length);

                string response = Encoding.ASCII.GetString(responseBuffer, 0, bytesRead);
                Console.WriteLine(response.Trim()); */

                Thread.Sleep(50);


            }
            stream.Close();
            client.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine("Error: " + e.Message);
        }

    }

    static string ReceiveNarration(NetworkStream stream)
    {
        byte[] buffer = new byte[8192];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);
        return Encoding.ASCII.GetString(buffer, 0, bytesRead);
    }

    static void DisplayWithTyping(string text, int delay = 0)
    {
        foreach (char c in text)
        {
            Console.Write(c);
            Thread.Sleep(delay);
        }

        Console.WriteLine("\n\nPress Enter to Continue...");
        Console.ReadLine();
    }

    static void ReadAllServerMessages(NetworkStream stream)
    {
        while (stream.DataAvailable)
        {
            byte[] buffer = new byte[4096];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            if (response.TrimStart().StartsWith("{"))
            {
                try
                {
                    uiData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(response);
                    DrawUI(uiData);
                }
                catch
                {
                    Console.WriteLine("Invalid UI JSON:\n" + response);
                }
            }
            else
            {
                Console.WriteLine(response.Trim() + "\n");
            }
        }
    }
    static void DrawUI(Dictionary<string, JsonElement> data)
{
    Console.Clear();

    string classId = data.GetValueOrDefault("classId").GetString() ?? "unknown";
    string locationId = data.GetValueOrDefault("location").GetString() ?? "unknown";
    int health = data.GetValueOrDefault("health").GetInt32();
    int mana = data.GetValueOrDefault("mana").GetInt32();
    int gold = data.GetValueOrDefault("playerGold").GetInt32();

    // Placeholder for location name/desc
    string locationName = locationId switch
    {
        "cradle_05" => "Cinder Grove",
        _ => locationId
    };
    string locationDesc = "A scorched village beneath ash...";

    // Header
    Console.WriteLine("+---------------------------+     +-----------------------------------------+");
    Console.WriteLine("|        CHARACTER          |     |               LOCATION                  |");
    Console.WriteLine("+---------------------------+     +-----------------------------------------+");
    Console.WriteLine($"| Class: {classId,-22} |     | {locationName,-39} |");
    Console.WriteLine($"| Health: {health,-21} |     | {locationDesc,-39} |");
    Console.WriteLine($"| Mana:   {mana,-21} |     | Exits: ??? (add later)                |");
    Console.WriteLine($"| Gold:   {gold,-21} |     |                                         |");
    Console.WriteLine("+---------------------------+     +-----------------------------------------+");

    // NPC Section
    Console.WriteLine();
    Console.WriteLine("+---------------------------+");
    Console.WriteLine("|         NPCs              |");
    Console.WriteLine("+---------------------------+");

    if (data.TryGetValue("npcs", out JsonElement npcs) && npcs.ValueKind == JsonValueKind.Array)
    {
        foreach (var npc in npcs.EnumerateArray())
        {
            string name = npc.GetString() ?? "";
            Console.WriteLine($"| {name,-25} |");
        }
    }
    else
    {
        Console.WriteLine("| No NPCs found             |");
    }

    Console.WriteLine("+---------------------------+");
    Console.WriteLine();
}





}

