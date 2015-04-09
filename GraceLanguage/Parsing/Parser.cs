using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Globalization;
using Grace.Unicode;

namespace Grace.Parsing
{
    public class Parser
    {
        private Lexer lexer;
        private string code;
        private int indentColumn = 0;

        private string moduleName = "source code";

        private List<ParseNode> comments;

        private bool doNotAcceptDelimitedBlock = false;

        public Parser(string module, string code)
        {
            this.moduleName = module;
            this.code = code;
        }

        public Parser(string code)
        {
            this.code = code;
        }

        public ParseNode Parse()
        {
            ObjectParseNode module = new ObjectParseNode(
                    new UnknownToken(moduleName, 0, 0));
            if (code.Length == 0)
                return module;
            List<ParseNode> body = module.body;
            lexer = new Lexer(moduleName, this.code);
            Token was = lexer.current;
            while (!lexer.Done())
            {
                consumeBlankLines();
                indentColumn = lexer.current.column;
                ParseNode n = parseStatement();
                body.Add(n);
                if (lexer.current == was)
                {
                    reportError("P1000", lexer.current, "Unknown construct");
                    break;
                }
                while (lexer.current is NewLineToken)
                    lexer.NextToken();
                was = lexer.current;
            }
            return module;
        }

        private void reportError(string code, Dictionary<string, string> vars,
                string localDescription)
        {
            ErrorReporting.ReportStaticError(moduleName, lexer.current.line,
                    code,
                    vars,
                    localDescription);
        }

        private void reportError(string code, Token t1, string localDescription)
        {
            ErrorReporting.ReportStaticError(moduleName, lexer.current.line,
                    code,
                    new Dictionary<string, string>() {
                        {"token", t1.ToString()}
                    },
                    localDescription);
        }

        private void reportError(string code, string localDescription)
        {
            ErrorReporting.ReportStaticError(moduleName, lexer.current.line, code, localDescription);
        }

        private bool awaiting<T>(Token start) where T : Token
        {
            if (lexer.current is T)
                return false;
            if (lexer.current is EndToken)
                ErrorReporting.ReportStaticError(moduleName, start.line,
                        "P1001", "Unexpected end of file");
            return true;
        }

        private void expect<T>() where T : Token
        {
            if (lexer.current is T)
                return;
            ErrorReporting.ReportStaticError(moduleName, lexer.current.line,
                    "P1002",
                    new Dictionary<string, string>() {
                        { "expected", typeof(T).Name },
                        { "found", lexer.current.ToString() }
                    },
                    "Expected something else, got " + lexer.current);
        }

        private Token nextToken()
        {
            lexer.NextToken();
            if (lexer.current is CommentToken)
            {
                takeComments();
            }
            if (lexer.current is NewLineToken)
            {
                // Check for continuation lines
                Token t = lexer.Peek();
                if (t.column > indentColumn)
                    lexer.NextToken();
            }
            if (lexer.current is CommentToken)
            {
                takeComments();
            }
            return lexer.current;
        }

        private void consumeBlankLines()
        {
            while (lexer.current is NewLineToken)
            {
                lexer.NextToken();
            }
        }

        private void skipSpaces()
        {
            while (lexer.current is SpaceToken || lexer.current is NewLineToken)
                lexer.NextToken();
        }

        private T attachComment<T>(T to)
            where T : ParseNode
        {
            if (lexer.current is CommentToken)
                comments.Add(parseComment());
            return to;
        }

        private void attachComments(ParseNode node, List<ParseNode> comments)
        {
            if (comments.Count == 0)
            {
                return;
            }
            int startAt = 0;
            if (node.comment == null)
            {
                ParseNode dest = comments.First();
                node.comment = dest;
                startAt = 1;
            }
            ParseNode append = node.comment;
            while (append.comment != null)
                append = append.comment;
            for (int i = startAt; i < comments.Count; i++)
            {
                ParseNode cur = comments[i];
                append.comment = cur;
                while (append.comment != null)
                    append = append.comment;
            }
        }

        private List<ParseNode> prepareComments()
        {
            List<ParseNode> orig = comments;
            comments = new List<ParseNode>();
            return orig;
        }

        private void restoreComments(List<ParseNode> orig)
        {
            comments = orig;
        }

        private ParseNode parseStatement()
        {
            List<ParseNode> origComments = comments;
            comments = new List<ParseNode>();
            takeLineComments();
            Token start = lexer.current;
            ParseNode ret;
            if (lexer.current is NewLineToken || lexer.current is EndToken
                    || lexer.current is RBraceToken)
            {
                // Took line comments, followed by a blank
                ret = collapseComments(comments);
                comments = new List<ParseNode>();
            }
            else if (lexer.current is CommentToken)
                ret = parseComment();
            else if (lexer.current is VarKeywordToken)
                ret = parseVarDeclaration();
            else if (lexer.current is DefKeywordToken)
                ret = parseDefDeclaration();
            else if (lexer.current is MethodKeywordToken)
                ret = parseMethodDeclaration();
            else if (lexer.current is ClassKeywordToken)
                ret = parseClassDeclaration();
            else if (lexer.current is InheritsKeywordToken)
                ret = parseInherits();
            else if (lexer.current is ImportKeywordToken)
                ret = parseImport();
            else if (lexer.current is DialectKeywordToken)
                ret = parseDialect();
            else if (lexer.current is ReturnKeywordToken)
                ret = parseReturn();
            else if (lexer.current is TypeKeywordToken)
                ret = parseTypeStatement();
            else
            {
                ret = parseExpression();
                if (lexer.current is BindToken)
                {
                    nextToken();
                    ParseNode expr = parseExpression();
                    ret = new BindParseNode(start, ret, expr);
                }
            }
            if (lexer.current is SemicolonToken)
            {
                lexer.NextToken();
                if (!(lexer.current is NewLineToken
                            || lexer.current is CommentToken
                            || lexer.current is EndToken
                            || lexer.current is RBraceToken))
                    reportError("P1003", "Other code cannot follow a semicolon on the same line.");
            }
            if (!(lexer.current is NewLineToken
                        || lexer.current is CommentToken
                        || lexer.current is EndToken
                        || lexer.current is RBraceToken))
                reportError("P1004", lexer.current,
                        "Unexpected token after statement.");
            while (lexer.current is NewLineToken)
                lexer.NextToken();
            attachComments(ret, comments);
            comments = origComments;
            return ret;
        }

        private ParseNode parseVarDeclaration()
        {
            Token start = lexer.current;
            nextToken();
            expect<IdentifierToken>();
            ParseNode name = parseIdentifier();
            ParseNode type = null;
            if (lexer.current is ColonToken)
            {
                type = parseTypeAnnotation();
            }
            var annotations = parseAnnotations();
            ParseNode val = null;
            if (lexer.current is BindToken)
            {
                nextToken();
                val = parseExpression();
            }
            else if (lexer.current is SingleEqualsToken)
            {
                reportError("P1005", "var declarations use ':='.");
            }
            return new VarDeclarationParseNode(start, name, val,
                        type, annotations);
        }

        private ParseNode parseDefDeclaration()
        {
            Token start = lexer.current;
            nextToken();
            expect<IdentifierToken>();
            ParseNode name = parseIdentifier();
            ParseNode type = null;
            if (lexer.current is ColonToken)
            {
                type = parseTypeAnnotation();
            }
            var annotations = parseAnnotations();
            if (lexer.current is BindToken)
            {
                reportError("P1006", "def declarations use '='.");
            }
            expect<SingleEqualsToken>();
            nextToken();
            ParseNode val = parseExpression();
            return new DefDeclarationParseNode(start, name, val,
                        type, annotations);
        }

        private AnnotationsParseNode parseAnnotations()
        {
            if (!(lexer.current is IsKeywordToken))
                return null;
            AnnotationsParseNode ret = new AnnotationsParseNode(lexer.current);
            nextToken();
            while (lexer.current is IdentifierToken)
            {
                doNotAcceptDelimitedBlock = true;
                ret.AddAnnotation(parseExpression());
                doNotAcceptDelimitedBlock = false;
                if (lexer.current is CommaToken)
                    nextToken();
                else
                    break;
            }
            return ret;
        }

        private void parseMethodHeader(Token start, MethodHeader ret)
        {
            OperatorToken op = lexer.current as OperatorToken;
            if (op != null)
            {
                ParseNode partName = new IdentifierParseNode(op);
                nextToken();
                PartParameters pp = ret.AddPart(partName);
                List<ParseNode> theseParameters = pp.Ordinary;
                if (lexer.current is LParenToken)
                {
                    Token lp = lexer.current;
                    nextToken();
                    parseParameterList<RParenToken>(lp, theseParameters);
                    expect<RParenToken>();
                    nextToken();
                }
            }
            else
            {
                expect<IdentifierToken>();
                IdentifierParseNode partName;
                bool first = true;
                while (lexer.current is IdentifierToken)
                {
                    partName = parseIdentifier();
                    if (lexer.current is BindToken)
                    {
                        partName.name += ":=";
                        nextToken();
                    }
                    else if ("prefix" == partName.name && first
                          && lexer.current is OperatorToken)
                    {
                        op = lexer.current as OperatorToken;
                        partName.name += op.name;
                        nextToken();
                    }
                    first = false;
                    PartParameters pp = ret.AddPart(partName);
                    List<ParseNode> theseParameters = pp.Ordinary;
                    List<ParseNode> theseGenerics = pp.Generics;
                    if (lexer.current is LGenericToken)
                    {
                        Token lp = lexer.current;
                        nextToken();
                        parseParameterList<RGenericToken>(lp, theseGenerics);
                        expect<RGenericToken>();
                        nextToken();
                    }
                    if (lexer.current is LParenToken)
                    {
                        Token lp = lexer.current;
                        nextToken();
                        parseParameterList<RParenToken>(lp, theseParameters);
                        expect<RParenToken>();
                        nextToken();
                    }
                }
            }
            if (lexer.current is ArrowToken)
            {
                nextToken();
                doNotAcceptDelimitedBlock = true;
                ret.returnType = parseExpression();
                doNotAcceptDelimitedBlock = false;
            }
            ret.annotations = parseAnnotations();
        }


        private ParseNode parseMethodDeclaration()
        {
            Token start = lexer.current;
            nextToken();
            MethodDeclarationParseNode ret = new MethodDeclarationParseNode(start);
            parseMethodHeader(start, ret);
            expect<LBraceToken>();
            List<ParseNode> origComments = prepareComments();
            parseBraceDelimitedBlock(ret.body);
            attachComments(ret, comments);
            restoreComments(origComments);
            return ret;
        }

        private ParseNode parseClassDeclaration()
        {
            Token start = lexer.current;
            nextToken();
            expect<IdentifierToken>();
            ParseNode baseName = parseIdentifier();
            expect<DotToken>();
            nextToken();
            ClassDeclarationParseNode ret = new ClassDeclarationParseNode(start, baseName);
            parseMethodHeader(start, ret);
            expect<LBraceToken>();
            List<ParseNode> origComments = prepareComments();
            parseBraceDelimitedBlock(ret.body);
            attachComments(ret, comments);
            restoreComments(origComments);
            return ret;
        }

        private ParseNode parseTypeStatement()
        {
            Token start = lexer.current;
            Token peeked = lexer.Peek();
            if (!(peeked is IdentifierToken))
                return parseType();
            nextToken();
            expect<IdentifierToken>();
            ParseNode name = parseIdentifier();
            List<ParseNode> genericParameters = new List<ParseNode>();
            if (lexer.current is LGenericToken)
            {
                nextToken();
                while (lexer.current is IdentifierToken)
                {
                    genericParameters.Add(parseIdentifier());
                    if (lexer.current is CommaToken)
                        nextToken();
                }
                if (!(lexer.current is RGenericToken))
                    reportError("P1007", "Unterminated generic type parameter list.");
                nextToken();
            }
            else if (lexer.current is OperatorToken)
            {
                OperatorToken op = lexer.current as OperatorToken;
                if (op.name == "<")
                    reportError("P1008", "Generic '<' must not have spaces around it.");
                else
                    reportError("P1009", "Unexpected operator in type name, expected '<'.");
            }
            expect<SingleEqualsToken>();
            nextToken();
            Token ts = lexer.current;
            ParseNode type = null;
            if (lexer.current is TypeKeywordToken)
            {
                nextToken();
            }
            else if (lexer.current is IdentifierToken)
            {
                type = parseIdentifier();
            }
            if (type == null)
            {
                List<ParseNode> origComments = prepareComments();
                List<ParseNode> body = parseTypeBody();
                type = new TypeParseNode(ts, body);
                attachComments(type, comments);
                restoreComments(origComments);
            }
            type = expressionRest(type);
            return new TypeStatementParseNode(start, name, type, genericParameters);
        }

        private ParseNode parseType()
        {
            Token start = lexer.current;
            expect<TypeKeywordToken>();
            nextToken();
            expect<LBraceToken>();
            List<ParseNode> origComments = prepareComments();
            List<ParseNode> body = parseTypeBody();
            ParseNode ret = new TypeParseNode(start, body);
            attachComments(ret, comments);
            restoreComments(origComments);
            return ret;
        }

        private List<ParseNode> parseTypeBody()
        {
            expect<LBraceToken>();
            int indentBefore = indentColumn;
            Token start = lexer.current;
            nextToken();
            takeLineComments();
            consumeBlankLines();
            if (lexer.current is RBraceToken)
            {
                nextToken();
                return new List<ParseNode>();
            }
            indentColumn = lexer.current.column;
            if (indentColumn <= indentBefore)
                reportError("P1010", new Dictionary<string, string>() {
                        { "previous indent", "" + (indentBefore - 1) },
                        { "new indent", "" + (indentColumn - 1) }
                    },
                    "Indentation must increase inside {}.");
            List<ParseNode> ret = new List<ParseNode>();
            while (awaiting<RBraceToken>(start))
            {
                List<ParseNode> origComments = prepareComments();
                takeLineComments();
                TypeMethodParseNode tmn = new TypeMethodParseNode(lexer.current);
                parseMethodHeader(lexer.current, tmn);
                ret.Add(tmn);
                attachComments(tmn, comments);
                restoreComments(origComments);
                consumeBlankLines();
            }
            lexer.NextToken();
            indentColumn = indentBefore;
            return ret;
        }

        private void parseParameterList<Terminator>(Token start,
                List<ParseNode> parameters)
            where Terminator : Token
        {
            while (awaiting<Terminator>(start))
            {
                ParseNode param = null;
                if (lexer.current is IdentifierToken
                        || lexer.current is NumberToken
                        || lexer.current is StringToken)
                {
                    Token after = lexer.Peek();
                    if (after is ColonToken)
                    {
                        ParseNode id = parseTerm();
                        ParseNode type = parseTypeAnnotation();
                        param = new TypedParameterParseNode(id, type);
                    }
                    else if (after is CommaToken || after is Terminator)
                    {
                        param = parseTerm();
                    }
                    else
                    {
                        // TODO destructuring patterns
                    }
                }
                else if (lexer.current is NumberToken)
                {
                    // TODO number patterns
                }
                else if (lexer.current is StringToken)
                {
                    // TODO string patterns
                }
                else if (lexer.current is OperatorToken)
                {
                    // This must be varargs
                    OperatorToken op = lexer.current as OperatorToken;
                    if ("*" != op.name)
                        reportError("P1012", new Dictionary<string, string>() { { "operator", op.name } },
                                "Unexpected operator in parameter list.");
                    nextToken();
                    expect<IdentifierToken>();
                    ParseNode id = parseIdentifier();
                    param = id;
                    if (lexer.current is ColonToken)
                    {
                        ParseNode type = parseTypeAnnotation();
                        param = new TypedParameterParseNode(param, type);
                    }
                    param = new VarArgsParameterParseNode(param);
                }
                if (param != null)
                    parameters.Add(param);
                else
                    reportError("TEMP0001", "Other sorts of parameter not yet supported.");
                if (lexer.current is CommaToken)
                    nextToken();
                else if (!(lexer.current is Terminator))
                {
                    reportError("P1013", lexer.current,
                            "In parameter list, expected "
                            + " ',' or end of list.");
                    break;
                }
            }
        }

        private ParseNode parseTypeAnnotation()
        {
            expect<ColonToken>();
            nextToken();
            return parseExpression();
        }

        private ParseNode parseInherits()
        {
            Token start = lexer.current;
            nextToken();
            ParseNode val = parseExpression();
            return new InheritsParseNode(start, val);
        }

        private ParseNode parseImport()
        {
            Token start = lexer.current;
            nextToken();
            expect<StringToken>();
            if ((lexer.current as StringToken).beginsInterpolation)
                reportError("P1014", "Import path uses string interpolation.");
            ParseNode path = parseString();
            expect<AsToken>();
            nextToken();
            expect<IdentifierToken>();
            ParseNode name = parseIdentifier();
            ParseNode type = null;
            if (lexer.current is ColonToken)
            {
                type = parseTypeAnnotation();
            }
            return new ImportParseNode(start, path, name, type);
        }

        private ParseNode parseDialect()
        {
            Token start = lexer.current;
            nextToken();
            expect<StringToken>();
            if ((lexer.current as StringToken).beginsInterpolation)
                reportError("P1015", "Dialect path uses string interpolation.");
            ParseNode path = parseString();
            return new DialectParseNode(start, path);
        }

        private ParseNode parseReturn()
        {
            Token start = lexer.current;
            nextToken();
            if (lexer.current is NewLineToken || lexer.current is CommentToken)
            {
                // Void return
                return new ReturnParseNode(start, null);
            }
            ParseNode val = parseExpression();
            return new ReturnParseNode(start, val);
        }

        private ParseNode expressionRestNoOp(ParseNode ex)
        {
            ParseNode lhs = ex;
            while (lexer.current is DotToken)
            {
                lhs = parseDotRequest(lhs);
            }
            return lhs;
        }

        private ParseNode maybeParseOperator(ParseNode lhs)
        {
            if (lexer.current is OperatorToken)
            {
                lhs = parseOperator(lhs);
            }
            return lhs;
        }

        private ParseNode expressionRest(ParseNode lhs)
        {
            lhs = expressionRestNoOp(lhs);
            return maybeParseOperator(lhs);
        }

        private ParseNode parseExpressionNoOp()
        {
            ParseNode lhs;
            if (lexer.current is LParenToken)
            {
                nextToken();
                lhs = parseExpression();
                consumeBlankLines();
                if (lexer.current is RParenToken)
                {
                    nextToken();
                }
                else
                {
                    reportError("P1017", "Parenthesised expression does not have closing parenthesis");
                }
            }
            else
            {
                lhs = parseTerm();
            }
            if (lhs is IdentifierParseNode)
            {
                if (lexer.current is LParenToken)
                {
                    lhs = parseImplicitReceiverRequest(lhs);
                }
                else if (hasDelimitedTerm())
                {
                    lhs = parseImplicitReceiverRequest(lhs);
                }
                else if (lexer.current is LGenericToken)
                {
                    lhs = parseImplicitReceiverRequest(lhs);
                }
            }
            lhs = expressionRestNoOp(lhs);
            return lhs;
        }

        private ParseNode parseExpression()
        {
            ParseNode lhs = parseExpressionNoOp();
            lhs = maybeParseOperator(lhs);
            return lhs;
        }

        private bool hasDelimitedTerm()
        {
            if (lexer.current is NumberToken)
                return true;
            if (lexer.current is StringToken)
                return true;
            if (lexer.current is LBraceToken && !doNotAcceptDelimitedBlock)
                return true;
            return false;
        }

        private bool hasTermStart()
        {
            if (lexer.current is IdentifierToken)
                return true;
            if (lexer.current is NumberToken)
                return true;
            if (lexer.current is StringToken)
                return true;
            if (lexer.current is LBraceToken)
                return true;
            if (lexer.current is TypeKeywordToken)
                return true;
            return false;
        }

        private ParseNode parseTerm()
        {
            ParseNode ret = null;
            if (lexer.current is IdentifierToken)
            {
                ret = parseIdentifier();
            }
            else if (lexer.current is NumberToken)
            {
                ret = parseNumber();
            }
            else if (lexer.current is StringToken)
            {
                ret = parseString();
            }
            else if (lexer.current is LBraceToken)
            {
                ret = parseBlock();
            }
            else if (lexer.current is ObjectKeywordToken)
            {
                ret = parseObject();
            }
            else if (lexer.current is TypeKeywordToken)
            {
                ret = parseType();
            }
            else if (lexer.current is OperatorToken)
            {
                ret = parsePrefixOperator();
            }
            if (ret == null)
            {
                reportError("P1018", lexer.current, "Expected term.");
            }
            return ret;
        }

        private ParseNode parsePrefixOperator()
        {
            OperatorToken op = lexer.current as OperatorToken;
            nextToken();
            ParseNode expr;
            if (lexer.current is LParenToken)
            {
                nextToken();
                expr = parseExpression();
                if (!(lexer.current is RParenToken))
                {
                    reportError("P1017", "Parenthesised expression does not have closing parenthesis");
                }
                nextToken();
            }
            else
            {
                expr = parseTerm();
                expr = expressionRestNoOp(expr);
            }
            return new PrefixOperatorParseNode(op, expr);
        }

        private ParseNode parseString()
        {
            StringToken tok = lexer.current as StringToken;
            if (tok.beginsInterpolation)
            {
                InterpolatedStringParseNode ret = new InterpolatedStringParseNode(tok);
                StringToken lastTok = tok;
                lexer.NextToken();
                while (lastTok.beginsInterpolation)
                {
                    ret.parts.Add(new StringLiteralParseNode(lastTok));
                    lexer.NextToken();
                    ParseNode expr = parseExpression();
                    ret.parts.Add(expr);
                    if (lexer.current is RBraceToken)
                    {
                        lexer.TreatAsString();
                        lastTok = lexer.current as StringToken;
                    }
                    else
                    {
                        reportError("P1019",
                                "Interpolation not terminated by }");
                        throw new Exception();
                    }
                    lexer.NextToken();
                }
                ret.parts.Add(new StringLiteralParseNode(lastTok));
                return ret;
            }
            else
            {
                nextToken();
                return new StringLiteralParseNode(tok);
            }
        }

        private ParseNode parseNumber()
        {
            ParseNode ret = new NumberParseNode(lexer.current);
            nextToken();
            return ret;
        }

        private IdentifierParseNode parseIdentifier()
        {
            IdentifierParseNode ret = new IdentifierParseNode(lexer.current);
            nextToken();
            return ret;
        }

        private ParseNode parseOperator(ParseNode lhs)
        {
            return parseOperatorStream(lhs);
        }

        private ParseNode oldParseOperator(ParseNode lhs)
        {
            OperatorToken tok = lexer.current as OperatorToken;
            if ((!tok.spaceBefore || !tok.spaceAfter) && (tok.name != ".."))
                reportError("P1020",
                        new Dictionary<string, string>()
                        {
                            { "operator", tok.name }
                        },
                        "Infix operators must be surrounded by spaces.");
            nextToken();
            ParseNode rhs = parseExpressionNoOp();
            ParseNode ret = new OperatorParseNode(tok, tok.name, lhs, rhs);
            tok = lexer.current as OperatorToken;
            while (tok != null)
            {
                if ((!tok.spaceBefore || !tok.spaceAfter) && (tok.name != ".."))
                    reportError("P1020",
                            new Dictionary<string, string>()
                            {
                                { "operator", tok.name }
                            },
                            "Infix operators must be surrounded by spaces.");
                nextToken();
                ParseNode comment = null;
                if (lexer.current is CommentToken)
                {
                    comment = parseComment();
                }
                rhs = parseExpressionNoOp();
                ret = new OperatorParseNode(tok, tok.name, ret, rhs);
                ret.comment = comment;
                tok = lexer.current as OperatorToken;
            }
            return ret;
        }

        private static int precedence(string op)
        {
            if (op == "*")
                return 10;
            if (op == "/")
                return 10;
            return 0;
        }

        private ParseNode parseOperatorStream(ParseNode lhs)
        {
            var opstack = new Stack<OperatorToken>();
            var valstack = new Stack<ParseNode>();
            valstack.Push(lhs);
            OperatorToken tok = lexer.current as OperatorToken;
            string firstOp = null;
            bool allArith = true;
            while (tok != null)
            {
                if ((!tok.spaceBefore || !tok.spaceAfter) && (tok.name != ".."))
                    reportError("P1020",
                            new Dictionary<string, string>()
                            {
                                { "operator", tok.name }
                            },
                            "Infix operators must be surrounded by spaces.");
                nextToken();
                if (lexer.current is CommentToken)
                {
                    parseComment();
                }
                switch (tok.name)
                {
                    case "*":
                    case "-":
                    case "/":
                    case "+":
                        break;
                    default:
                        allArith = false;
                        break;
                }
                if (firstOp != null && !allArith && firstOp != tok.name)
                {
                    reportError("P1026",
                            new Dictionary<string, string>()
                            {
                                { "operator", tok.name }
                            },
                            "Mixed operators without parentheses");
                }
                else if (firstOp == null)
                {
                    firstOp = tok.name;
                }
                int myprec = precedence(tok.name);
                while (opstack.Count > 0
                        && myprec <= precedence(opstack.Peek().name))
                {
                    var o2 = opstack.Pop();
                    var tmp2 = valstack.Pop();
                    var tmp1 = valstack.Pop();
                    valstack.Push(
                            new OperatorParseNode(o2, o2.name,
                                tmp1, tmp2));
                }
                opstack.Push(tok);
                ParseNode rhs = parseExpressionNoOp();
                valstack.Push(rhs);
                tok = lexer.current as OperatorToken;
            }
            while (opstack.Count > 0)
            {
                var o = opstack.Pop();
                var tmp2 = valstack.Pop();
                var tmp1 = valstack.Pop();
                valstack.Push(
                        new OperatorParseNode(o, o.name, tmp1, tmp2));
            }
            return valstack.Pop();
        }

        private void parseBraceDelimitedBlock(List<ParseNode> body)
        {
            int indentBefore = indentColumn;
            Token start = lexer.current;
            // Skip the {
            lexer.NextToken();
            if (lexer.current is CommentToken)
            {
                comments.Add(parseComment());
            }
            consumeBlankLines();
            if (lexer.current is RBraceToken)
            {
                nextToken();
                return;
            }
            consumeBlankLines();
            takeLineComments();
            consumeBlankLines();
            if (lexer.current is RBraceToken)
            {
                nextToken();
                return;
            }
            indentColumn = lexer.current.column;
            if (indentColumn <= indentBefore)
                reportError("P1011", new Dictionary<string, string>() {
                        { "previous indent", "" + (indentBefore - 1) },
                        { "new indent", "" + (indentColumn - 1) }
                    },
                    "Indentation must increase inside {}.");
            Token lastToken = lexer.current;
            while (awaiting<RBraceToken>(start))
            {
                if (lexer.current.column != indentColumn)
                {
                    reportError("P1016", new Dictionary<string, string>() 
                            {
                                { "required indentation", "" + (indentColumn - 1) },
                                { "given indentation", "" + (lexer.current.column - 1) }
                            },
                            "Indentation mismatch; is "
                            + (lexer.current.column - 1) + ", should be "
                            + (indentColumn - 1) + ".");
                }
                body.Add(parseStatement());
                if (lexer.current == lastToken)
                {
                    reportError("P1000", lexer.current,
                            "Nothing consumed in {} body.");
                    break;
                }
            }
            nextToken();
            indentColumn = indentBefore;
        }
        private ParseNode parseObject()
        {
            ObjectParseNode ret = new ObjectParseNode(lexer.current);
            lexer.NextToken();
            if (!(lexer.current is LBraceToken))
            {
                reportError("P1021", "object must have '{' after.");
            }
            List<ParseNode> origComments = prepareComments();
            parseBraceDelimitedBlock(ret.body);
            attachComments(ret, comments);
            restoreComments(origComments);
            return ret;
        }

        private ParseNode parseBlock()
        {
            int indentStart = indentColumn;
            BlockParseNode ret = new BlockParseNode(lexer.current);
            Token start = lexer.current;
            lexer.NextToken();
            consumeBlankLines();
            Token firstBodyToken = lexer.current;
            indentColumn = firstBodyToken.column;
            // TODO fix to handle indentation properly
            // does not at all now. must recalculate after params list too
            if (lexer.current is IdentifierToken
                    || lexer.current is NumberToken
                    || lexer.current is StringToken)
            {
                // It *might* be a parameter.
                ParseNode expr = parseExpression();
                if (lexer.current is BindToken)
                {
                    // Definitely not a parameter
                    nextToken();
                    ParseNode val = parseExpression();
                    ret.body.Add(new BindParseNode(start, expr, val));
                    if (lexer.current is CommaToken
                        || lexer.current is ArrowToken)
                        reportError("P1022", lexer.current, "Block parameter list contained invalid symbol.");
                }
                else if (lexer.current is SemicolonToken)
                {
                    // Definitely not a parameter
                    lexer.NextToken();
                    if (!(lexer.current is NewLineToken
                                || lexer.current is CommentToken
                                || lexer.current is EndToken
                                || lexer.current is RBraceToken))
                        reportError("P1003", "Other code cannot follow a semicolon on the same line.");
                    ret.body.Add(expr);
                }
                else if (lexer.current is ColonToken)
                {
                    // Definitely a parameter of some sort, has a type.
                    ParseNode type = parseTypeAnnotation();
                    ret.parameters.Add(new TypedParameterParseNode(expr, type));
                }
                else if (lexer.current is CommaToken)
                {
                    // Can only be a parameter.
                    ret.parameters.Add(expr);
                }
                else if (lexer.current is ArrowToken)
                {
                    // End of parameter list
                    ret.parameters.Add(expr);
                }
                else
                {
                    ret.body.Add(expr);
                }
                if (lexer.current is CommaToken)
                {
                    nextToken();
                    parseParameterList<ArrowToken>(start, ret.parameters);
                }
            }
            if (lexer.current is ArrowToken)
            {
                lexer.NextToken();
                consumeBlankLines();
                firstBodyToken = lexer.current;
            }
            else
            {
                consumeBlankLines();
            }
            Token lastToken = lexer.current;
            indentColumn = firstBodyToken.column;
            while (!(lexer.current is RBraceToken))
            {
                ret.body.Add(parseStatement());
                if (lexer.current == lastToken)
                {
                    reportError("P1000", lexer.current,
                            "Nothing consumed in block body.");
                    break;
                }
            }
            indentColumn = indentStart;
            nextToken();
            return ret;
        }

        private void parseArgumentList(List<ParseNode> arguments)
        {
            if (lexer.current is LParenToken)
            {
                Token start = lexer.current;
                nextToken();
                while (awaiting<RParenToken>(start))
                {
                    ParseNode expr = parseExpression();
                    if (lexer.current is ColonToken)
                    {
                        ParseNode type = parseTypeAnnotation();
                        expr = new TypedParameterParseNode(expr, type);
                    }
                    arguments.Add(expr);
                    consumeBlankLines();
                    if (lexer.current is CommaToken)
                        nextToken();
                    else if (!(lexer.current is RParenToken))
                    {
                        reportError("P1023", lexer.current,
                                "In argument list of request, expected "
                                + " ',' or ')'.");
                        break;
                    }
                }
                nextToken();
            }
            else if (hasDelimitedTerm())
            {
                arguments.Add(parseTerm());
            }
        }

        private void parseGenericArgumentList(List<ParseNode> arguments)
        {
            if (lexer.current is LGenericToken)
            {
                Token start = lexer.current;
                nextToken();
                while (awaiting<RGenericToken>(start))
                {
                    ParseNode expr = parseExpression();
                    arguments.Add(expr);
                    consumeBlankLines();
                    if (lexer.current is CommaToken)
                        nextToken();
                    else if (!(lexer.current is RGenericToken))
                    {
                        reportError("P1024", lexer.current,
                                "In generic argument list of request, expected "
                                + " ',' or '>'.");
                        break;
                    }
                }
                nextToken();
            }
        }

        private ParseNode parseImplicitReceiverRequest(ParseNode lhs)
        {
            ImplicitReceiverRequestParseNode ret = new ImplicitReceiverRequestParseNode(lhs);
            parseGenericArgumentList(ret.genericArguments[0]);
            parseArgumentList(ret.arguments[0]);
            while (lexer.current is IdentifierToken)
            {
                // This is a multi-part method name
                ret.AddPart(parseIdentifier());
                parseArgumentList(ret.arguments.Last());
            }
            return ret;
        }

        private ParseNode parseDotRequest(ParseNode lhs)
        {
            ExplicitReceiverRequestParseNode ret = new ExplicitReceiverRequestParseNode(lhs);
            nextToken();
            bool named = false;
            while (lexer.current is IdentifierToken)
            {
                // Add this part of the method name
                ret.AddPart(parseIdentifier());
                parseGenericArgumentList(ret.genericArguments.Last());
                parseArgumentList(ret.arguments.Last());
                named = true;
            }
            if (!named)
            {
                reportError("P1025", lexer.current,
                        "Expected identifier after '.'.");
            }
            return ret;
        }

        private ParseNode parseComment()
        {
            ParseNode ret = new CommentParseNode(lexer.current);
            nextToken();
            return ret;
        }

        private ParseNode collapseComments(List<ParseNode> comments)
        {
            ParseNode first = comments[0];
            ParseNode last = first;
            for (int i = 1; i < comments.Count; i++)
            {
                last.comment = comments[i];
                last = comments[i];
            }
            return first;
        }

        private void takeLineComments()
        {
            if (!(lexer.current is CommentToken))
                return;
            ParseNode ret = new CommentParseNode(lexer.current);
            comments.Add(ret);
            lexer.NextToken();
            if (lexer.current is NewLineToken)
            {
                lexer.NextToken();
                if (lexer.current is CommentToken)
                    takeLineComments();
            }
        }

        private void takeComments()
        {
            if (!(lexer.current is CommentToken))
                return;
            ParseNode ret = new CommentParseNode(lexer.current);
            comments.Add(ret);
            lexer.NextToken();
            if (lexer.current is NewLineToken)
            {
                // Check for continuation lines
                Token t = lexer.Peek();
                if (t.column > indentColumn)
                    lexer.NextToken();
            }
            if (lexer.current is CommentToken)
                takeComments();
        }

    }

}
