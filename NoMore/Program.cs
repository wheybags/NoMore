﻿using System.Data;
using System.Text;
using Microsoft.Data.Sqlite;

public class Program
{
    public static void Main(String[] args)
    {
        string gameInstallPath = "D:\\SteamLibrary\\steamapps\\common\\Crusader Kings III";

        List<Playset> playsets = fetchPlaysets(gameInstallPath);

        Console.WriteLine("Playsets:");
        for (int i = 0; i < playsets.Count; i++)
        {
            Playset playset = playsets[i];
            Console.WriteLine("  " +(i+1) + ": " + playset.name);
            foreach (Mod mod in playset.mods)
                Console.WriteLine("    - " + mod.name);
        }
        Console.WriteLine("");

        Playset selectedPlayset = null;

        while (true)
        {
            Console.Write("Choose a playset (1-" + (playsets.Count + 1) + "): ");
            string read = Console.ReadLine();

            if (int.TryParse(read, out int selectedIndex))
            {
                if (selectedIndex >= 1 && selectedIndex <= playsets.Count)
                {
                    selectedPlayset = playsets[selectedIndex-1];
                    break;
                }
            }
        }

        Console.WriteLine("Using playset " + selectedPlayset.name);

        List<string> sourcePaths = new List<string>()
        {
            gameInstallPath + "\\game",
        };

        string outputModFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\Paradox Interactive\\Crusader Kings III\\mod\\nomore";

        foreach (Mod mod in selectedPlayset.mods)
        {
            if (mod.path != outputModFolder)
                sourcePaths.Add(mod.path);
        }

        HashSet<string> dataFileRelativePaths = new HashSet<string>();
        foreach (string sourcePath in sourcePaths)
        {
            string characterInteractionsPath = Path.Join(sourcePath, "common", "character_interactions");

            if (!Directory.Exists(characterInteractionsPath))
                continue;

            foreach (string path in Directory.EnumerateFiles(characterInteractionsPath))
            {
                if (path.EndsWith(".txt"))
                    dataFileRelativePaths.Add(Path.GetRelativePath(sourcePath, path));
            }
        }

        if (Directory.Exists(outputModFolder))
            Directory.Delete(outputModFolder, true);

        foreach (string relativePath in dataFileRelativePaths)
        {
            Console.WriteLine("Generating " + relativePath);

            string sourcePath = null;
            for (int i = sourcePaths.Count - 1; i >= 0; i--)
            {
                string pathInSource = Path.Join(sourcePaths[i], relativePath);
                if (File.Exists(pathInSource))
                {
                    sourcePath = pathInSource;
                    break;
                }
            }

            string fileData = File.ReadAllText(sourcePath);
            // string fileData = File.ReadAllText("D:\\SteamLibrary\\steamapps\\common\\Crusader Kings III\\game\\common\\character_interactions\\00_debug_interactions.txt");


            // string fileData = File.ReadAllText("C:\\users\\wheybags\\desktop\\test.txt");

            // fileData = @"AND = { # Explicit AND to ensure no funny business";
            //        fileData = @"text = ""mystical_ancestors_disinherit""
            // 	}
            // } >= asd";


            //  fileData = @"scope:actor.dynasty = {
            // 	dynasty_prestige >= medium_dynasty_prestige_value
            // }";

            // fileData = "scope:target = { exists = var:relic_religion}\n";


            List<Token> tokens = Tokeniser.tokenise(fileData);

            Parser parser = new Parser()
            {
                tokens = tokens
            };

            CkObject data = parser.parseRoot();

            foreach (CkKeyValuePair interaction in data.valuesList)
            {
                if (interaction.valueIsString)
                    continue;

                CkKeyValuePair? commonInteractionItem = interaction.valueObject.findFirstWithName("common_interaction");

                if (commonInteractionItem == null)
                {
                    commonInteractionItem = new CkKeyValuePair() { key = "common_interaction" };
                    interaction.valueObject.valuesList.Insert(0, commonInteractionItem);
                }

                commonInteractionItem.valueString = "yes";
            }


            string serialised = serialiseCkObject(data);
            string outputPath = outputModFolder + "\\" + relativePath;
            Directory.CreateDirectory(Directory.GetParent(outputPath).FullName);
            File.WriteAllText(outputPath, serialised);
        }

        string dotModData = @"version=""1.0""
tags={
	""Gui""
	""Fixes""
}
name=""No More""".Replace("\r\n", "\n");

        File.WriteAllText(outputModFolder + "\\descriptor.mod", dotModData);
        File.WriteAllText(Directory.GetParent(outputModFolder).FullName + "\\nomore.mod", dotModData + "\npath=\"" + outputModFolder.Replace("\\", "/") + "\"");

        Console.WriteLine("done!");
        Console.WriteLine("");
        Console.WriteLine("Now, in the game launcher, edit the \"" + selectedPlayset.name + "\" playset, and enable the mod \"No More\"");
        Console.WriteLine("Make sure it is always last in the load order!");
        Console.WriteLine("");
        Console.WriteLine("Press enter to close");
        Console.ReadLine();
    }

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

    public static string serialiseCkObject(CkObject obj)
    {
        StringBuilder stringBuilder = new StringBuilder();
        serialiseCkObject(stringBuilder, obj);
        return stringBuilder.ToString();
    }

    public static void serialiseCkObject(StringBuilder stringBuilder, CkObject obj)
    {
        foreach (CkKeyValuePair pair in obj.valuesList)
        {
            if (pair.key != null)
            {
                stringBuilder.Append(pair.whitespaceBeforeKeyName);
                stringBuilder.Append(pair.key);
                stringBuilder.Append(pair.whitespaceBeforeOperator);
                stringBuilder.Append(pair.operatorString);
            }

            stringBuilder.Append(pair.whitespaceBeforeValue);
            if (pair.valueIsString)
                stringBuilder.Append(pair.valueString);
            else
            {
                stringBuilder.Append("{");
                serialiseCkObject(stringBuilder, pair.valueObject);
                stringBuilder.Append("}");
            }
        }

        stringBuilder.Append(obj.whitespaceAfterLastValue);
    }

}

public class Token
{
    public enum Type
    {
        String,
        GreaterOrEqual,
        LessOrEqual,
        Less,
        Greater,
        QuestionEqual,
        Equals,
        Assign,
        OpenBrace,
        CloseBrace,
        FileEnd,
    }

    public Type type;
    public string ignoredTextBeforeToken;

    public string stringValue;

    public int startRow;
    public int startColumn;
}

public class Tokeniser
{
    public static List<Token> tokenise(string input)
    {
        List<Token> tokens = new List<Token>();

        string accumulatedJunk = "";
        string accumulator = "";

        bool inComment = false;
        bool inBareString = false;
        bool inQuotedString = false;

        int startColumn = -1;
        int startRow = -1;

        List<Tuple<string, Token.Type>> keywordMapping = new List<Tuple<string, Token.Type>>()
        {
            new ("<=", Token.Type.LessOrEqual),
            new (">=", Token.Type.GreaterOrEqual),
            new ("?=", Token.Type.QuestionEqual),
            new ("==", Token.Type.Equals),
            new ("<", Token.Type.Less),
            new (">", Token.Type.Greater),
            new ("=", Token.Type.Assign),
            new ("{", Token.Type.OpenBrace),
            new ("}", Token.Type.CloseBrace),
        };

        void breakToken()
        {
            if (accumulator.Length == 0)
                return;

            Token newToken = new Token();
            newToken.ignoredTextBeforeToken = accumulatedJunk;
            newToken.startRow = startRow;
            newToken.startColumn = startColumn;

            if (inQuotedString || inBareString)
            {
                newToken.type = Token.Type.String;
                newToken.stringValue = accumulator;
            }
            else
            {
                bool found = false;
                foreach (var pair in keywordMapping)
                {
                    if (pair.Item1 == accumulator)
                    {
                        newToken.type = pair.Item2;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    throw new Exception("invalid keyword");
            }

            tokens.Add(newToken);

            accumulatedJunk = "";
            accumulator = "";
        }

        int column = 1;
        int row = 1;

        int characterIndex = 0;

        bool inputStartsWith(string s)
        {
            if (characterIndex + s.Length > input.Length)
                return false;

            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != input[characterIndex + i])
                    return false;
            }

            return true;
        }

        for (; characterIndex < input.Length; characterIndex++)
        {
            char c = input[characterIndex];
            if (c == '\n')
            {
                row++;
                column = 0;
            }
            else
            {
                column++;
            }


            if (inComment)
            {
                accumulatedJunk += c;
                if (c == '\n')
                    inComment = false;

                continue;
            }

            if (inBareString)
            {
                if (isWhitespace(c) || c == '{' || c == '}' || inputStartsWith("<") || inputStartsWith(">") || inputStartsWith("=") || inputStartsWith("!=") || inputStartsWith("?="))
                {
                    breakToken();
                    inBareString = false;
                    // no continue here, so we will process the character as (potentially) the start of a new token
                }
                else
                {
                    accumulator += c;
                    continue;
                }
            }

            if (inQuotedString)
            {
                accumulator += c;

                if (c == '"')
                {
                    breakToken();
                    inQuotedString = false;
                }
                continue;
            }


            if (accumulator.Length == 0)
            {
                bool foundKeyword = false;
                foreach (var pair in keywordMapping)
                {
                    if (inputStartsWith(pair.Item1))
                    {
                        startColumn = column;
                        startRow = row;
                        accumulator += pair.Item1;
                        breakToken();
                        characterIndex += pair.Item1.Length - 1;
                        foundKeyword = true;
                        break;
                    }
                }

                if (foundKeyword)
                    continue;

                if (c == '"')
                {
                    startColumn = column;
                    startRow = row;
                    accumulator += c;
                    inQuotedString = true;
                }
                else if (c == '#')
                {
                    breakToken();
                    inComment = true;
                    accumulatedJunk += c;
                }
                else if (isWhitespace(c))
                {
                    accumulatedJunk += c;
                }
                else
                {
                    startColumn = column;
                    startRow = row;
                    inBareString = true;
                    accumulator += c;
                }
            }
            else
            {
                throw new Exception();
            }
        }

        breakToken();

        Token endToken = new Token();
        endToken.type = Token.Type.FileEnd;
        endToken.ignoredTextBeforeToken = accumulatedJunk;
        tokens.Add(endToken);

        return tokens;
    }

    private static bool isWhitespace(char c)
    {
        return c == '\t' || c == '\r' || c == '\n' || c == ' ';
    }
}


public class CkObject
{
    public List<CkKeyValuePair> valuesList = new List<CkKeyValuePair>();
    public string whitespaceAfterLastValue = " ";

    public CkKeyValuePair? findFirstWithName(string name)
    {
        foreach (CkKeyValuePair item in valuesList)
        {
            if (item.key == name)
                return item;
        }

        return null;
    }
}

public class CkKeyValuePair
{
    public string whitespaceBeforeKeyName = " ";
    public string whitespaceBeforeOperator = " ";
    public string whitespaceBeforeValue = " ";

    public string key = null;
    public string operatorString = "=";

    public bool valueIsString { get; private set; }

    private string? _valueString;
    public string valueString {
        get
        {
            if (!valueIsString)
                throw new Exception("not a string");
            return _valueString!;
        }
        set
        {
            valueIsString = true;
            _valueString = value;
        }
    }

    private CkObject? _valueObject;
    public CkObject valueObject {
        get
        {
            if (valueIsString)
                throw new Exception("not a CkObject");
            return _valueObject!;
        }
        set
        {
            valueIsString = false;
            _valueObject = value;
        }
    }
}


// Root                 = ObjectBody $End
// ObjectBody           = ObjectBody2 | Nil
// ObjectBody2          = Assignment ObjectBody
// Item                 = $String ItemRest
// ItemRest             = Operator AfterOperator | Nil
// Operator             = "=" | ">=" | "?=" | "<=" | "<" | ">"
// AfterOperator        = $String | "{" ObjectBody "}"

public class Parser
{
    public List<Token> tokens;
    private int tokenIndex = 0;

    private string tempBefore = "";

    Token peek()
    {
        return tokens[tokenIndex];
    }

    Token pop()
    {
        Token retval = tokens[tokenIndex];
        tokenIndex++;
        return retval;
    }

    public CkObject parseRoot()
    {
        CkObject root = new CkObject();
        parseObjectBody(root);
        if (peek().type != Token.Type.FileEnd)
            throw new Exception("expected file end");
        return root;
    }

    public void parseObjectBody(CkObject obj)
    {
        if (peek().type == Token.Type.String)
            parseObjectBody2(obj);
        else if (peek().type == Token.Type.FileEnd || peek().type == Token.Type.CloseBrace)
            obj.whitespaceAfterLastValue = peek().ignoredTextBeforeToken;
        else
            throw new Exception("unexpected");
    }

    public void parseObjectBody2(CkObject obj)
    {
        if (peek().type == Token.Type.String)
        {
            parseItem(obj);
            parseObjectBody(obj);
        }
        else
        {
            throw new Exception("expected String");
        }
    }

    public void parseItem(CkObject obj)
    {
        if (peek().type != Token.Type.String)
            throw new Exception("expected BareString");

        Token keyToken = pop();
        parseItemRest(obj, keyToken);
    }

    public void parseItemRest(CkObject obj, Token keyToken)
    {
        CkKeyValuePair pair = new CkKeyValuePair();
        pair.whitespaceBeforeKeyName = keyToken.ignoredTextBeforeToken;

        if (peek().type == Token.Type.Less ||
            peek().type == Token.Type.Greater ||
            peek().type == Token.Type.LessOrEqual ||
            peek().type == Token.Type.GreaterOrEqual ||
            peek().type == Token.Type.QuestionEqual ||
            peek().type == Token.Type.Assign ||
            peek().type == Token.Type.Equals)
        {
            pair.key = keyToken.stringValue;

            parseOperator(pair);
            parseAfterOperator(pair);
        }
        else // TODO: assert
        {
            pair.valueString = keyToken.stringValue;
            pair.operatorString = null;
        }

        obj.valuesList.Add(pair);
    }

    public void parseOperator(CkKeyValuePair pair)
    {
        if (peek().type == Token.Type.Assign)
        {
            pair.operatorString = "=";
            pair.whitespaceBeforeOperator = peek().ignoredTextBeforeToken;
            pop();
        }
        else if (peek().type == Token.Type.GreaterOrEqual)
        {
            pair.operatorString = ">=";
            pair.whitespaceBeforeOperator = peek().ignoredTextBeforeToken;
            pop();
        }
        else if (peek().type == Token.Type.LessOrEqual)
        {
            pair.operatorString = "<=";
            pair.whitespaceBeforeOperator = peek().ignoredTextBeforeToken;
            pop();
        }
        else if (peek().type == Token.Type.QuestionEqual)
        {
            pair.operatorString = "?=";
            pair.whitespaceBeforeOperator = peek().ignoredTextBeforeToken;
            pop();
        }
        else if (peek().type == Token.Type.Less)
        {
            pair.operatorString = "<";
            pair.whitespaceBeforeOperator = peek().ignoredTextBeforeToken;
            pop();
        }
        else if (peek().type == Token.Type.Greater)
        {
            pair.operatorString = ">";
            pair.whitespaceBeforeOperator = peek().ignoredTextBeforeToken;
            pop();
        }
        else if (peek().type == Token.Type.Equals)
        {
            pair.operatorString = "==";
            pair.whitespaceBeforeOperator = peek().ignoredTextBeforeToken;
            pop();
        }
        else
        {
            throw new Exception("expected valid operator");
        }
    }

    public void parseAfterOperator(CkKeyValuePair pair)
    {
        if (peek().type == Token.Type.String)
        {
            pair.whitespaceBeforeValue = peek().ignoredTextBeforeToken;
            pair.valueString = pop().stringValue;
        }
        else if (peek().type == Token.Type.OpenBrace)
        {
            pair.whitespaceBeforeValue = peek().ignoredTextBeforeToken;
            pop();
            pair.valueObject = new CkObject();
            parseObjectBody(pair.valueObject);
            if (pop().type != Token.Type.CloseBrace)
                throw new Exception("expected }");
        }
        else
        {
            throw new Exception("expected String or {");
        }
    }
}