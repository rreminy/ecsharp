﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Loyc;
using Loyc.Utilities;
using Loyc.Syntax.Lexing;
using Loyc.LLParserGenerator;
using Loyc.Collections;
using Loyc.Syntax;

namespace Loyc.Syntax.Les
{
	using TT = TokenType;
	using S = CodeSymbols;
	using P = LesPrecedence;

	/// <summary>Parses LES (Loyc Expression Syntax) code into a sequence of Loyc 
	/// trees (<see cref="LNode"/>), one per top-level statement.</summary>
	/// <remarks>
	/// You can use the static <see cref="Parse"/> method to easily parse a text 
	/// string (holding zero or more LES statements) into a Loyc tree.
	/// </remarks>
	public partial class LesParser : BaseParser<Token>
	{
		protected IMessageSink _messages;
		protected LNodeFactory F;
		protected ISourceFile _sourceFile;
		protected IListSource<Token> _tokenTree;
		protected IListSource<Token> _tokens;
		// index into source text of the first token at the current depth (inside 
		// parenthesis, etc.). Used if we need to print an error inside empty {} [] ()
		protected int _startTextIndex = 0;
		protected LNode _missingExpr = null; // used by MissingExpr
		public IListSource<Token> TokenTree { get { return _tokenTree; } }
		public ISourceFile SourceFile { get { return _sourceFile; } }

		static readonly Symbol _Error = Utilities.MessageSink.Error;

		public LesParser(IListSource<Token> tokens, ISourceFile file, IMessageSink messageSink)
		{
			MessageSink = messageSink;
			Reset(tokens, file);
		}

		public void Reset(IListSource<Token> tokens, ISourceFile file)
		{
			_tokenTree = _tokens = tokens;
			_sourceFile = file;
			F = new LNodeFactory(file);
			InputPosition = 0; // reads LT(0)
			_missingExpr = null;
		}

		public IMessageSink MessageSink
		{
			get { return _messages; } 
			set { _messages = value ?? Loyc.Utilities.MessageSink.Current; }
		}

		protected LNode MissingExpr { get { return _missingExpr = _missingExpr ?? F.Id(S.Missing); } }
		protected sealed override int EofInt() { return 0; }
		protected sealed override int LA0Int { get { return _lt0.TypeInt; } }
		protected TT LA0 { get { return _lt0.Type(); } }
		protected TT LA(int i)
		{
			bool fail = false;
			return _tokens.TryGet(InputPosition + i, ref fail).Type();
		}
		protected sealed override Token LT(int i)
		{
			bool fail = false;
			return _tokens.TryGet(InputPosition + i, ref fail);
		}
		protected override void Error(int inputPosition, string message)
		{
			int iPos = GetTextPosition(inputPosition);
			SourcePos pos = _sourceFile.IndexToLine(iPos);
			_messages.Write(_Error, pos, message);
		}
		protected int GetTextPosition(int tokenPosition)
		{
			bool fail = false;
			var token = _tokens.TryGet(tokenPosition, ref fail);
			if (!fail)
				return token.StartIndex;
			else if (_tokens.Count == 0 || tokenPosition < 0)
				return _startTextIndex;
			else
				return _tokens[_tokens.Count - 1].EndIndex;
		}
		protected override string ToString(int type_)
		{
			var type = (TokenType)type_;
			return type.ToString();
		}

		const TokenType EOF = TT.EOF;
		static readonly int MinPrec = Precedence.MinValue.Lo;
		public static readonly Precedence StartStmt = new Precedence(MinPrec, MinPrec, MinPrec);
		
		Stack<Pair<IListSource<Token>, int>> _parents = new Stack<Pair<IListSource<Token>, int>>();

		protected bool Down(int li)
		{
			return Down(LT(li).Children);
		}
		protected bool Down(IListSource<Token> children)
		{
			if (children != null) {
				_parents.Push(Pair.Create(_tokens, InputPosition));
				_tokens = children;
				InputPosition = 0;
				return true;
			}
			return false;
		}
		protected T Up<T>(T value)
		{
			Up();
			return value;
		}
		protected void Up()
		{
			Debug.Assert(_parents.Count > 0);
			var pair = _parents.Pop();
			_tokens = pair.A;
			InputPosition = pair.B;
		}

		protected virtual RWList<LNode> ParseAttributes(Token group, RWList<LNode> list)
		{
			return AppendExprsInside(group, list);
		}
		protected RWList<LNode> AppendExprsInside(Token group, RWList<LNode> list)
		{
			if (Down(group.Children)) {
				ExprList(ref list);
				return Up(list);
			}
			return list;
		}
		protected RWList<LNode> ExprListInside(Token t)
		{
			return AppendExprsInside(t, new RWList<LNode>());
		}
		protected virtual LNode ParseBraces(Token t, int endIndex)
		{
			RWList<LNode> list = new RWList<LNode>();
			if (Down(t.Children)) {
				StmtList(ref list);
				Up();
			}
			return F.Braces(list.ToRVList(), t.StartIndex, endIndex - t.StartIndex);
		}
		protected virtual LNode ParseParens(Token t, int endIndex)
		{
			var list = ExprListInside(t);
			if (list.Count == 1)
				return list[0];
			if (list.Count == 2 && (object)list[1] == MissingExpr)
				return F.Call(S.Tuple, list[0]);
			return F.Call(S.Tuple, list.ToRVList(), t.StartIndex, endIndex - t.StartIndex);
		}
		protected virtual LNode ParseCall(Token target, Token paren, int endIndex)
		{
			return F.Call((Symbol)target.Value, ExprListInside(paren).ToRVList(), target.StartIndex, endIndex - target.StartIndex);
		}
		protected virtual LNode ParseCall(LNode target, Token paren, int endIndex)
		{
			return F.Call(target, ExprListInside(paren).ToRVList(), target.Range.StartIndex, endIndex - target.Range.StartIndex);
		}
		
		// All the keys are Symbols, but we use object as the key type to avoid casting Token.Value
		protected static readonly Map<object, Precedence> PredefinedPrefixPrecedence = 
			new MMap<object, Precedence>() {
				{ S.Substitute,  P.Substitute }, // #$
				{ S.Dot,         P.Substitute }, // hmm, I might repurpose '.' with lower precedence to remove the spacing rule
				{ S.Colon,       P.Substitute }, // #:
				{ S.NotBits,     P.Prefix     }, // #~
				{ S.Not,         P.Prefix     }, // #!
				{ S.Mod,         P.Prefix     }, // #%
				{ S.XorBits,     P.Prefix     }, // #^
				{ S._AddressOf,  P.Prefix     }, // #&
				{ S._Dereference,P.Prefix     }, // #*
				{ S._UnaryPlus,  P.Prefix     }, // #+
				{ S._Negate,     P.Prefix     }, // #-
				{ S.DotDot,      P.PrefixDots }, // #..
				{ S.OrBits,      P.PrefixOr   }, // #|
				{ S.Div,         P.Reserved   }, // #/
				{ S.Backslash,   P.Reserved   }, // #\
				{ S.LT,          P.Reserved   }, // #<
				{ S.GT,          P.Reserved   }, // #>
				{ S.QuestionMark,P.Reserved   }, // #?
				{ S.Set,         P.Reserved   }, // #=
			}.AsImmutable();

		protected static readonly Map<object, Precedence> PredefinedSuffixPrecedence =
			new MMap<object, Precedence>() {
				{ S.PreInc,     P.Primary   }, // #++, never mind that it's called "pre"inc
				{ S.PreDec,     P.Primary   }, // #--
			}.AsImmutable();

		protected static readonly Map<object, Precedence> PredefinedInfixPrecedence =
			new MMap<object, Precedence>() {
				{ S.Dot,        P.Primary   }, // #.
				{ S.QuickBind,  P.Primary   }, // #=:
				{ S.Not,        P.Primary   }, // #!
				{ S.NullDot,    P.NullDot   }, // #?.
				{ S.ColonColon, P.NullDot   }, // #::
				{ S.DoubleBang, P.DoubleBang}, // #!!
				{ S.Exp,        P.Power     }, // #**
				{ S.Mul,        P.Multiply  }, // #*
				{ S.Div,        P.Multiply  }, // #/
				{ S.Mod,        P.Multiply  }, // #%
				{ S.Backslash,  P.Multiply  }, // #\
				{ S.Shr,        P.Multiply  }, // #>>
				{ S.Shl,        P.Multiply  }, // #<<
				{ S.Add,        P.Add       }, // #+
				{ S.Sub,        P.Add       }, // #-
				{ S._RightArrow,P.Arrow     }, // #->
				{ S.LeftArrow,  P.Arrow     }, // #<-
				{ S.AndBits,    P.AndBits   }, // #&
				{ S.OrBits,     P.OrBits    }, // #|
				{ S.XorBits,    P.OrBits    }, // #^
				{ S.NullCoalesce,P.OrIfNull }, // #??
				{ S.DotDot,     P.Range     }, // #..
				{ S.GT,         P.Compare   }, // #>
				{ S.LT,         P.Compare   }, // #<
				{ S.LE,         P.Compare   }, // #<=
				{ S.GE,         P.Compare   }, // #>=
				{ S.Eq,         P.Compare   }, // #==
				{ S.Neq,        P.Compare   }, // #!=
				{ S.And,        P.And       }, // #&&
				{ S.Or,         P.Or        }, // #||
				{ S.Xor,        P.Or        }, // #^^
				{ S.QuestionMark,P.IfElse   }, // #?
				{ S.Colon,      P.Reserved  }, // #:
				{ S.Set,        P.Assign    }, // #=
				{ S.Lambda,     P.Lambda    }, // #=>
				{ S.NotBits,    P.Reserved  }, // #~
			}.AsImmutable();
		
		// All the keys are Symbols, but we use object as the key type to avoid casting Token.Value
		protected MMap<object, Precedence> _prefixPrecedence = PredefinedPrefixPrecedence.AsMutable();
		protected MMap<object, Precedence> _suffixPrecedence = PredefinedSuffixPrecedence.AsMutable();
		protected MMap<object, Precedence> _infixPrecedence = PredefinedInfixPrecedence.AsMutable();
		protected Precedence P_SuperExpr = P.SuperExpr; // allow derived class to override

		Precedence FindPrecedence(MMap<object,Precedence> table, object symbol, Precedence @default)
		{
			// You can see the official rules in the LesPrecedence documentation.
			// Rule 1 is covered by the pre-populated contents of the table, and
			// the pre-populated table helps interpret rules 3-4 also.
			Precedence prec;
			if (table.TryGetValue(symbol, out prec))
				return prec;
			
			string sym = symbol.ToString();
			char first = sym[0], last = sym[sym.Length - 1];
			// All one-character operators should be found in the table
			Debug.Assert(sym.Length > (first == '#' ? 2 : 1) || char.IsLetter(first));

			if (table == _infixPrecedence && last == '=')
				return table[symbol] = P.Assign;
			if (first == '#')
				first = sym[1];
			
			var twoCharOp = GSymbol.Get("#" + first + last);
			if (table.TryGetValue(twoCharOp, out prec))
				return table[symbol] = prec;

			var oneCharOp = GSymbol.Get("#" + last);
			if (table.TryGetValue(oneCharOp, out prec))
				return table[symbol] = prec;

			// Default precedence is used for 
			return table[symbol] = @default;
		}
		private Precedence PrefixPrecedenceOf(Token t)
		{
			if (t.TypeInt == (int)TT.BQString)
				return LesPrecedence.Prefix;
			return FindPrecedence(_prefixPrecedence, t.Value, P.BackslashWord);
		}
		private Precedence SuffixPrecedenceOf(Token t)
		{
			return FindPrecedence(_suffixPrecedence, t.Value, P.Suffix2);
		}
		private Precedence InfixPrecedenceOf(Token t)
		{
			if (t.TypeInt == (int)TT.BQString)
				return LesPrecedence.Backtick;
			return FindPrecedence(_infixPrecedence, t.Value, P.BackslashWord);
		}
		
		Dictionary<object, Symbol> _suffixOpNames;

		private Symbol ToSuffixOpName(object symbol)
		{
			_suffixOpNames = _suffixOpNames ?? new Dictionary<object, Symbol>();
			Symbol name;
			if (_suffixOpNames.TryGetValue(symbol.ToString(), out name))
				return name;

			var was = symbol.ToString();
			if (was.StartsWith("#"))
				return _suffixOpNames[symbol] = GSymbol.Get(@"#suf" + symbol.ToString().Substring(1));
			else
				return _suffixOpNames[symbol] = (Symbol)symbol;
		}

		//protected virtual LNode MakeSuperExpr(LNode firstExpr, LNode primary, RVList<LNode> otherExprs)
		//{
		//    if (primary == null)
		//        return firstExpr; // an error should have been printed already

		//    if (otherExprs.IsEmpty)
		//        return firstExpr;
		//    if (firstExpr == primary) {
		//        Debug.Assert(firstExpr.BaseStyle == NodeStyle.Special);
		//        return firstExpr.WithArgs(firstExpr.Args.AddRange(otherExprs));
		//    } else {
		//        Debug.Assert(firstExpr.BaseStyle != NodeStyle.Special);
		//        Debug.Assert(firstExpr != null && firstExpr.IsCall && firstExpr.ArgCount > 0);
		//        int c = firstExpr.ArgCount-1;
		//        LNode ce = MakeSuperExpr(firstExpr.Args[c], primary, otherExprs);
		//        return firstExpr.WithArgChanged(c, ce);
		//    }
		//}
		protected virtual LNode MakeSuperExpr(LNode lhs, ref LNode primary, RVList<LNode> rhs)
		{
			if (primary == null)
				return lhs; // an error should have been printed already

			if (lhs == primary) {
				if (primary.BaseStyle == NodeStyle.Operator)
					primary = F.Call(primary, rhs);
				else
					primary = lhs.WithArgs(lhs.Args.AddRange(rhs));
				primary.BaseStyle = NodeStyle.Special;
				return primary;
			} else {
				Debug.Assert(lhs != null && lhs.IsCall && lhs.ArgCount > 0);
				Debug.Assert(lhs.BaseStyle != NodeStyle.Special);
				int c = lhs.ArgCount-1;
				LNode ce = MakeSuperExpr(lhs.Args[c], ref primary, rhs);
				return lhs.WithArgChanged(c, ce);
			}
		}
		public IEnumerator<LNode> ParseStmtsUntilEnd()
		{
			TT la0;
			var next = SuperExprOptUntil(TT.Semicolon);
			for (;;) {
				la0 = LA0;
				if (la0 == TT.Semicolon) {
					yield return next;
					Skip();
					next = SuperExprOptUntil(TT.Semicolon);
				} else
					break;
			}
			if (next != (object)MissingExpr) yield return next;
		}
	}
}