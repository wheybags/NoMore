﻿using Microsoft.Data.Sqlite;
using Microsoft.Win32;

public class Mod
{
    public string name;
    public string path;
}

public class Playset
{
    public string id;
    public string name;
    public List<Mod> mods = new List<Mod>();
}

public static class GamePaths
{
    public static string getGameInstallFolder()
    {
        string steamPath = Registry.GetValue("HKEY_CURRENT_USER\\SOFTWARE\\Valve\\Steam", "SteamPath", null) as string;
        string vdfPath = steamPath.Replace("/", "\\") + "\\steamapps\\libraryfolders.vdf";


        foreach (string line in File.ReadAllLines(vdfPath))
        {
            string trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("\"path\""))
            {
                // line is something like:
                // "path"		"C:\\Program Files (x86)\\Steam"
                string libraryPath = trimmedLine.Substring("\"path\"".Length).Trim();
                libraryPath = libraryPath.Substring(1, libraryPath.Length - 2) + "\\steamapps\\common\\Crusader Kings III";

                if (Directory.Exists(libraryPath))
                    return libraryPath;
            }
        }

        return null;
    }

    public static List<Playset> fetchPlaysets(string gameInstallPath)
    {
        string sqlitePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\Paradox Interactive\\Crusader Kings III\\launcher-v2.sqlite";
        using SqliteConnection connection = new SqliteConnection("Data Source=" + sqlitePath + ";Mode=ReadOnly");
        connection.Open();

        List<Playset> playsets = new List<Playset>();
        {
            using SqliteCommand selectPlaysetsCommand = connection.CreateCommand();
            selectPlaysetsCommand.CommandText = @"
        SELECT id, name
        FROM playsets
    ";

            using var reader = selectPlaysetsCommand.ExecuteReader();
            while (reader.Read())
            {
                Playset playset = new Playset();
                playset.id = reader.GetString(0);
                playset.name = reader.GetString(1);
                playsets.Add(playset);
            }
        }

        foreach (Playset playset in playsets)
        {
            using SqliteCommand selectPlaysetModsCommand = connection.CreateCommand();
            selectPlaysetModsCommand.CommandText = @"
            SELECT mods.steamId, mods.gameRegistryId, mods.displayName
            FROM playsets_mods
            INNER JOIN mods ON playsets_mods.modId=mods.id
            WHERE playsets_mods.playsetId = $id AND playsets_mods.enabled = 1
            ORDER BY playsets_mods.position
        ";

            selectPlaysetModsCommand.Parameters.AddWithValue("id", playset.id);

            using var reader = selectPlaysetModsCommand.ExecuteReader();
            while (reader.Read())
            {
                string steamId = null;
                if (!reader.IsDBNull(0))
                    steamId = reader.GetString(0);

                string gameRegistryId = reader.GetString(1);
                string displayName = reader.GetString(2);


                Mod mod = new Mod();
                if (steamId != null)
                {
                    string steamappsFolder = new DirectoryInfo(gameInstallPath).Parent.Parent.FullName;
                    mod.path = steamappsFolder + "\\workshop\\content\\1158310\\" + steamId;
                }
                else
                {
                    // gameRegistryId is something like "mod/ugc_2887120253.mod"
                    string relativePath = gameRegistryId.Replace("/", "\\").Substring(0, gameRegistryId.Length - 4);
                    mod.path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\Paradox Interactive\\Crusader Kings III\\" + relativePath;
                }

                mod.name = displayName;

                if (!Directory.Exists(mod.path))
                    throw new Exception("Mod path not found");

                playset.mods.Add(mod);
            }
        }

        return playsets;
    }
}