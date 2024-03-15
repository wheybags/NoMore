﻿
// See https://pdx.tools/blog/a-tour-of-pds-clausewitz-syntax

// What is not supported?
// 1: operators as key names: =="bar"
// here '=' "should" be the key and 'bar' the value
//
// 2: objects with implicit operator: foo{bar=qux}
// this should be equivalent to foo={bar=qux}
//
// 3: this [[]] syntax is not supported at all (I think it's not in ck3 though)
// generate_advisor = {
//   [[scaled_skill]
//   $scaled_skill$
//       ]
//   [[!skill] if = {} ]
// }
//
// 4: Missing / extra closing braces are not supported
// Apparently they "should" be
//
// 5: semicolons
// Apparently you can put semicolons in certain places and they should be ignored. This isn't supported
//
// 6: this "list" syntax:
// simple_cross_flag = {
//     pattern = list "christian_emblems_list"
//     color1 = list "normal_colors"
// }
// because I haven't seen a real example, and I can't tell from this one how it should work


public class Parser
{
    // This is a simple handwritten recursive descent parser
    //
    // Grammar:
    // Root                 = ObjectBody $End
    // ObjectBody           = Item ObjectBody | Nil
    // Item                 = $String ItemRest | "{" ObjectBody "}"
    // ItemRest             = Operator AfterOperator | Nil
    // Operator             = "=" | ">=" | "?=" | "<=" | "==" | "<" | ">"
    // AfterOperator        = $String MaybeObject | "{" ObjectBody "}"
    // MaybeObject          = "{" ObjectBody "}" | Nil


    public static CkObject parse(string inputString)
    {
        Parser parser = new Parser();
        parser.tokens = Tokeniser.tokenise(inputString);
        return parser.parseRoot();
    }

    public TokeniserOutput tokens;
    private int tokenIndex = 0;

    Token peek()
    {
        return tokens.tokens[tokenIndex];
    }

    Token pop()
    {
        Token retval = tokens.tokens[tokenIndex];
        tokenIndex++;
        return retval;
    }

    public CkObjectRoot parseRoot()
    {
        CkObjectRoot root = new CkObjectRoot();
        parseObjectBody(root);
        if (peek().type != Token.Type.FileEnd)
            throw new Exception("expected file end");

        root.linesHaveCarriageReturns = tokens.linesHaveCarriageReturn;
        return root;
    }

    private void parseObjectBody(CkObject obj)
    {
        if (peek().type == Token.Type.String || peek().type == Token.Type.OpenBrace)
        {
            parseItem(obj);
            parseObjectBody(obj);
        }
        else if (peek().type == Token.Type.FileEnd || peek().type == Token.Type.CloseBrace)
        {
            obj.whitespaceAfterLastValue = peek().ignoredTextBeforeToken;
        }
        else
        {
            throw new Exception("unexpected");
        }
    }

    private void parseItem(CkObject obj)
    {
        if (peek().type == Token.Type.String)
        {
            Token keyToken = pop();
            parseItemRest(obj, keyToken);
        }
        else if (peek().type == Token.Type.OpenBrace)
        {
            CkKeyValuePair pair = new CkKeyValuePair();
            obj.valuesList.Add(pair);

            pair.whitespaceBeforeValue = peek().ignoredTextBeforeToken;
            pop();
            pair.valueObject = new CkObject();
            parseObjectBody(pair.valueObject);
            if (pop().type != Token.Type.CloseBrace)
                throw new Exception("expected }");
        }
        else
        {
            throw new Exception("unexpected");
        }
    }

    private void parseItemRest(CkObject obj, Token keyToken)
    {
        CkKeyValuePair pair = new CkKeyValuePair();

        if (peek().type == Token.Type.Less ||
            peek().type == Token.Type.Greater ||
            peek().type == Token.Type.LessOrEqual ||
            peek().type == Token.Type.GreaterOrEqual ||
            peek().type == Token.Type.QuestionEqual ||
            peek().type == Token.Type.Assign ||
            peek().type == Token.Type.Equals)
        {
            pair.whitespaceBeforeKeyName = keyToken.ignoredTextBeforeToken;
            pair.key = keyToken.stringValue;

            parseOperator(pair);
            parseAfterOperator(pair);
        }
        else // TODO: assert
        {
            pair.whitespaceBeforeValue = keyToken.ignoredTextBeforeToken;
            pair.valueString = keyToken.stringValue;
            pair.operatorString = null;
        }

        obj.valuesList.Add(pair);
    }

    private void parseOperator(CkKeyValuePair pair)
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

    private void parseAfterOperator(CkKeyValuePair pair)
    {
        if (peek().type == Token.Type.String)
        {
            pair.whitespaceBeforeValue = peek().ignoredTextBeforeToken;

            Token startToken = pop();
            parseMaybeObject(startToken, pair);
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

    private void parseMaybeObject(Token startToken, CkKeyValuePair pair)
    {
        if (peek().type == Token.Type.OpenBrace)
        {
            pair.whitespaceBeforeTypeTag = startToken.ignoredTextBeforeToken;
            pair.typeTag = startToken.stringValue;

            pair.whitespaceBeforeValue = peek().ignoredTextBeforeToken;
            if (pop().type != Token.Type.OpenBrace)
                throw new Exception("expected {");

            pair.valueObject = new CkObject();
            parseObjectBody(pair.valueObject);

            if (pop().type != Token.Type.CloseBrace)
                throw new Exception("expected }");
        }
        else // todo: assert
        {
            pair.whitespaceBeforeValue = startToken.ignoredTextBeforeToken;
            pair.valueString = startToken.stringValue;
        }
    }
}