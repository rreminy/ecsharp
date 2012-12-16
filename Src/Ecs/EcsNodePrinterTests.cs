﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Loyc.CompilerCore;
using S = Loyc.CompilerCore.CodeSymbols;
using Loyc.Utilities;
using Loyc.Essentials;

namespace ecs
{
	// A main goal of these tests should be to make the EcsNodePrinter print 
	// something that doesn't round-trip perfectly. But before I can do that, 
	// I need to build and test the parser. So for now, a bunch of basic tests...
	[TestFixture]
	class EcsNodePrinterTests : Assert
	{
		int _testNum;
		void CheckIsComplexIdentifier(bool? result, GreenNode expr)
		{
			var np = new EcsNodePrinter(expr, null);
			_testNum++;
			var is1 = np.IsComplexIdentifier(expr);
			var is2 = np.IsComplexIdentifierOrNull(expr.Head);
			if (result == null && !is1 && is2)
				return;
			else if (result == is1 && result == is2)
				return;

			Assert.Fail(string.Format(
				"IsComplexIdentifier: fail on test #{0} '{1}'. Expected {2}, got {3}/{4}",
				_testNum, expr.ToString(), result, is1, is2));
		}

		[Test]
		public void IsComplexIdentifierTests()
		{
			_testNum = 0;
			CheckIsComplexIdentifier(true, a);                             // a
			//CheckIsComplexIdentifier(null, F.InParens(a));                 // (a)
			CheckIsComplexIdentifier(true, F.Dot(a, b));                   // a.b
			CheckIsComplexIdentifier(true, F.Dot(a, b, c));                // a.b.c
			CheckIsComplexIdentifier(null, F.Dot(F.Dot(a, b), c));         // #.(a.b, c)
			CheckIsComplexIdentifier(null, F.Dot(a, F.Dot(b, c)));         // #.(a, b.c)
			CheckIsComplexIdentifier(true, F.Of(a, b));                    // a<b>        == #of(a,b)          ==> true
			CheckIsComplexIdentifier(true, F.Of(_(S.Bracks), b));          // a[]         == #of(#[],a)        ==> true
			CheckIsComplexIdentifier(true, F.Of(F.Dot(a,b),F.Dot(c,x)));   // a.b<c.x>    == #of(#.(a,b),#.(c,x)) ==> true
			CheckIsComplexIdentifier(null, F.Call(a, x));                  // a(x)                             ==> true for Head
			CheckIsComplexIdentifier(null, F.Call(F.Dot(a,b), x));         // a.b(x)      == #.(a,b)(x)        ==> true for Head
			CheckIsComplexIdentifier(null, F.Call(F.Of(F.Dot(a,b),c), c)); // a.b<c>(x)   == #of(#.(a,b),c)(x) ==> true for Head
			CheckIsComplexIdentifier(false, F.Call(F.InParens(a), x));     // (a)(x)                           ==> false
			CheckIsComplexIdentifier(false, F.Call(F.InParens(F.Dot(a,b)),x));// (a.b)(x) == (#.(a,b))(x)      ==> false
			CheckIsComplexIdentifier(null, F.Of(F.Of(a,b),c));             // #of(a<b>,c) == #of(#of(a,b),c)   ==> false
		}

		static GreenFactory F = new GreenFactory(EmptySourceFile.Unknown);
		GreenNode a = F.Symbol("a"), b = F.Symbol("b"), c = F.Symbol("c"), x = F.Symbol("x");
		GreenNode Foo = F.Symbol("Foo"), IFoo = F.Symbol("IFoo"), T = F.Symbol("T");
		GreenNode @class = F.Symbol(S.Class), @partial = F.Symbol("#partial"), one = F.Literal(1);
		GreenNode @public = F.Symbol(S.Public), @static = F.Symbol(S.Static), fooKW = F.Symbol("#foo");
		GreenNode @lock = F.Symbol(S.Lock), @if = F.Symbol(S.If), @out = F.Symbol(S.Out), @new = F.Symbol(S.New);
		GreenNode style_macroCall = F.Symbol(S.StyleMacroCall);
		GreenNode _(string name) { return F.Symbol(name); }
		GreenNode _(Symbol name) { return F.Symbol(name); }

		[Test]
		public void SimpleCallsAndVarDecls()
		{
			Expr("a",        a);
			Expr("(a)",      F.InParens(a));
			Expr("((a))",    F.InParens(F.InParens(a)));
			Expr("#public",  @public);
			Expr("@public",  _("public"));
			Expr("a.b.c",    F.Dot(a, b, c));
			Expr("a<b>.c",   F.Dot(F.Of(a, b), c));
			Expr("a.b<c>",   F.Of(F.Dot(a, b), c));
			Expr("a<b.c>",   F.Of(a, F.Dot(b, c)));
			Expr("a<b,c>",   F.Of(a, b, c));
			Expr("a(b)",     F.Call(a, b));
			Expr("a<b>(c)",  F.Call(F.Of(a, b), c));
			Expr("a.b(c)",   F.Call(F.Dot(a, b), c));
			Stmt("Foo a;",   F.Var(Foo, a));
			Stmt("int a;",   F.Var(F.Int32, a));
			Stmt("int[] a;", F.Var(F.Of(S.Bracks, S.Int32), a));
			Stmt(@"\Foo x;", F.Var(F.Call(S.Substitute, Foo), x));
			Stmt(@"\(a(b)) x;", F.Var(F.Call(S.Substitute, F.Call(a, b)), x));
		}

		protected virtual void Expr(string result, GreenNode input, Action<EcsNodePrinter> configure = null)
		{
			Stmt(result, input, configure, true);
		}
		protected virtual void Stmt(string result, GreenNode input, Action<EcsNodePrinter> configure = null, bool exprMode = false)
		{
			var sb = new StringBuilder();
			var printer = input.NewEcsPrinter(sb);
			printer.NewlineOptions &= ~(NewlineOpt.AfterOpenBraceInNewExpr | NewlineOpt.BeforeCloseBraceInNewExpr);
			if (configure != null)
				configure(printer);
			if (exprMode)
				printer.PrintExpr();
			else
				printer.PrintStmt();
			AreEqual(result, sb.ToString());
		}
		protected void Option(string before, string after, GreenNode input, Action<EcsNodePrinter> configure = null, bool exprMode = false)
		{
			Stmt(before, input, null, exprMode);
			Stmt(after, input, configure, exprMode);
		}
		
		GreenNode Attr(GreenNode attr, GreenNode node)
		{
			node = node.Unfrozen();
			node.Attrs.Insert(0, attr);
			return node;
		}
		GreenNode Attr(params GreenNode[] attrsAndNode)
		{
			var node = attrsAndNode[attrsAndNode.Length - 1].Unfrozen();
			for (int i = 0; i < attrsAndNode.Length - 1; i++)
				node.Attrs.Insert(i, attrsAndNode[i]);
			return node;
		}

		[Test]
		public void SimpleCallsAndAttributes()
		{
			Stmt("[Foo] a;",              Attr(Foo, a));
			Stmt("[Foo] a.b.c;",          Attr(Foo, F.Dot(a, b, c)));
			Stmt("[Foo] a<b,c>;",         Attr(Foo, F.Of(a, b, c)));
			Stmt("#.([Foo] a, b, c);",    F.Dot(Attr(Foo, a), b, c));
			Stmt("#.(a, b, [Foo] c);",    F.Dot(a, b, Attr(Foo, c)));
			Stmt("#of([Foo] a, b, c);",   F.Of(Attr(Foo, a), b, c));
			Stmt("#of(a, b, [Foo] c);",   F.Of(a, b, Attr(Foo, c)));
			Stmt("#of(Foo<a>, b);",       F.Of(F.Of(Foo, a), b));
			Stmt("public a;",             Attr(@public, a));
			Stmt("[Foo] public a(b);",    Attr(Foo, Attr(@public, F.Call(a, b))));
			Stmt("[Foo] static int a;",   Attr(Foo, Attr(@static, F.Var(F.Int32, a))));
			Stmt("partial public int a;", Attr(partial, Attr(@public, F.Var(F.Int32, a))));
			Stmt("[#lock] static int a;", Attr(@lock, Attr(@static, F.Var(F.Int32, a))));
			Stmt("[#public, Foo] int a;", Attr(@public, Attr(Foo, F.Var(F.Int32, a))));
		}

		GreenNode Alternate(GreenNode node)
		{
			node = node.Unfrozen();
			node.Style |= NodeStyle.Alternate;
			return node;
		}
		GreenNode AsStyle(GreenNode node, NodeStyle s)
		{
			node = node.Unfrozen();
			node.BaseStyle = s;
			return node;
		}
		GreenNode Operator(GreenNode node) { return AsStyle(node, NodeStyle.Operator); }
		GreenNode StmtStyle(GreenNode node) { return AsStyle(node, NodeStyle.Statement); }
		GreenNode ExprStyle(GreenNode node) { return AsStyle(node, NodeStyle.Expression); }

		[Test]
		public void Literals()
		{
			Expr("6",        F.Literal(6));
			Expr("5m",       F.Literal(5m));
			Expr("4L",       F.Literal(4L));
			Expr("3.5d",     F.Literal(3.5d));
			Expr("3d",       F.Literal(3d));
			Expr("2.5f",     F.Literal(2.5f));
			Expr("2f",       F.Literal(2f));
			Expr("1u",       F.Literal(1u));
			Expr("0uL",      F.Literal(0uL));
			Expr("-1",       F.Literal(-1));
			Expr("0xff",     Alternate(F.Literal(0xFF)));
			Expr("null",     F.Literal(null));
			Expr("false",    F.Literal(false));
			Expr("true",     F.Literal(true));
			Expr("'$'",      F.Literal('$'));
			Expr(@"'\0'",    F.Literal('\0'));
			Expr(@"""hi""",  F.Literal("hi"));
			Expr(@"@""hi""", Alternate(F.Literal("hi")));
			Expr("@\"\n\"",  Alternate(F.Literal("\n")));
			Expr("@@\"\n\"", Attr(_(S.StyleDoubleVerbatim), F.Literal("\n")));
			Expr("void",     F.Literal(@void.Value));
			Expr("$hello",   F.Literal(GSymbol.Get("hello")));
			Expr("$int",     F.Literal(GSymbol.Get("int")));
			Expr("$'#int'",  F.Literal(GSymbol.Get("#int")));
			Expr("$'1+1'",   F.Literal(GSymbol.Get("1+1")));
			Expr("$'1'",     F.Literal(GSymbol.Get("1")));
			Expr("123456789123456789uL", F.Literal(123456789123456789uL));
			Expr("0xffffffffffffffffuL", Alternate(F.Literal(0xFFFFFFFFFFFFFFFFuL)));
		}

		[Test]
		public void SimpleExpressions()
		{
			Expr("a + b",        F.Call(S.Add, a, b));
			Expr("a + b + c",    F.Call(S.Add, F.Call(S.Add, a, b), c));
			Expr("+a",           F.Call(S.Add, a));
			Expr("#+(a, b, c)",  F.Call(S.Add, a, b, c));
			Expr("a >> b",       F.Call(S.Shr, a, b));
			Expr("a = b + c",    F.Call(S.Set, a, F.Call(S.Add, b, c)));
			Expr("a(b)(c)",      F.Call(F.Call(a, b), c));
			Expr("a++--",        F.Call(S.PostDec, F.Call(S.PostInc, a)));
			Expr("x => x + 1",   F.Call(S.Lambda, x, F.Call(S.Add, x, one)));
			Stmt("[Foo] a = b;", Attr(Foo, F.Call(S.Set, a, b)));
		}

		[Test]
		public void TuplesAndVarDeclsInExpressions()
		{
			Stmt("Foo a, b, c;",          F.Var(Foo, a, b, c));
			Stmt("Foo? a, b, c;",         F.Var(F.Of(_(S.QuestionMark), Foo), a, b, c));
			Stmt("(#var(Foo, a, b, c));", F.InParens(F.Var(Foo, a, b, c)));
			Stmt("(Foo a) = x;",          F.Call(S.Set, F.InParens(F.Var(Foo, a)), x));
			Stmt("(Foo a) => a;",         F.Call(S.Lambda, F.InParens(F.Var(Foo, a)), a));
			Stmt("(#var(Foo, a)) + x;",   F.Call(S.Add, F.InParens(F.Var(Foo, a)), x));
			var x_1 = F.Call(S.Tuple, x, one);
			Stmt("(a, b) = (x, 1);",      F.Call(S.Set, F.Call(S.Tuple, a, b), x_1));
			Stmt("(a,) = (x,);",          F.Call(S.Set, F.Call(S.Tuple, a), F.Call(S.Tuple, x)));
			Stmt("(a, Foo b) = (x, 1);",  F.Call(S.Set, F.Call(S.Tuple, a, F.Var(Foo, b)), x_1));
			Stmt("(Foo a, b) = (x, 1);",  F.Call(S.Set, F.Call(S.Tuple, F.Var(Foo, a), b), x_1));
			Stmt("(#var(Foo, a) + 1, b) = (x, 1);", F.Call(S.Set, F.Call(S.Tuple, F.Call(S.Add, F.Var(Foo, a), one), b), x_1));
			Stmt("(Foo a,) = (x,);",      F.Call(S.Set, F.Call(S.Tuple, F.Var(Foo, a)), F.Call(S.Tuple, x)));
		}

		[Test]
		public void SpecialOperators()
		{
			Expr("c ? Foo(x) : a + b", F.Call(S.QuestionMark, c, F.Call(Foo, x), F.Call(S.Add, a, b)));
			Expr("Foo[x]",           F.Call(S.Bracks, Foo, x));
			Expr("Foo[a, b]",        F.Call(S.Bracks, Foo, a, b));
			Expr("Foo[]",            F.Call(S.Bracks, Foo)); // "Foo[]" means #of(#[], Foo) only in a type context
			Expr("#[]()",            F.Call(S.Bracks));
			Expr("(Foo) x",          F.Call(S.Cast, x, Foo));
			Expr("x(->Foo)",         Alternate(F.Call(S.Cast, x, Foo)));
			Expr("x(->a + b)",       F.Call(S.Cast, x, F.Call(S.Add, a, b)));
			Expr("x as Foo",         F.Call(S.As, x, Foo));
			Expr("x using Foo",      F.Call(S.UsingCast, x, Foo));
			Expr("x(as Foo)",        Alternate(F.Call(S.As, x, Foo)));
			Expr("x(using Foo)",     Alternate(F.Call(S.UsingCast, x, Foo)));
			Expr("x++",              F.Call(S.PostInc, x));
			Expr("x--",              F.Call(S.PostDec, x));
			Expr("#postInc(a, b)",   F.Call(S.PostInc, a, b));
			Expr("#postDec()",       F.Call(S.PostDec));
			Expr("@(a = b)",         F.Call(S.CodeQuote, F.Call(S.Set, a, b)));
			Expr("@(a = b, Foo())",  F.Call(S.CodeQuote, F.Call(S.Set, a, b), F.Call(Foo)));
			Expr("@@(a = b)",        F.Call(S.CodeQuoteSubstituting, F.Call(S.Set, a, b)));
			Expr("@@(a = b, Foo())", F.Call(S.CodeQuoteSubstituting, F.Call(S.Set, a, b), F.Call(Foo)));
		}

		[Test]
		public void ExpressionsAndAttrs()
		{
			// The printer must use prefix notation if the arguments passed to an 
			// operator have attributes.
			Expr("#+([Foo] a, b)",    F.Call(S.Add, Attr(Foo, a), b));
			Expr("#+(a, [Foo] b)",    F.Call(S.Add, a, Attr(Foo, b)));
			Expr("#[]([Foo] a, b)",   F.Call(S.Bracks, Attr(Foo, a), b));
			Expr("a[[Foo] b]",        F.Call(S.Bracks, a, Attr(Foo, b)));
			Expr("#?([Foo] c, a, b)", F.Call(S.QuestionMark, Attr(Foo, c), a, b));
			Expr("#?(c, [Foo] a, b)", F.Call(S.QuestionMark, c, Attr(Foo, a), b));
			Expr("#?(c, a, [Foo] b)", F.Call(S.QuestionMark, c, a, Attr(Foo, b)));
		}

		[Test]
		public void BugFixes()
		{
			Stmt("#+(a, b)(c, 1);", F.Call(F.Call(S.Add, a, b), c, one)); // was: "c+1"
			// was "partial #var(Foo, a);" which would be parsed as a method declaration
			Stmt("([#partial] #var(Foo, a));", F.InParens(Attr(@partial, F.Var(Foo, a))));
		}

		[Test]
		public void BracesInExpr()
		{
			var stmt1 = F.Call(S.QuickBind, F.Dot(Foo, x), a);
			var stmt2 = F.Call(S.Add, F.Call(S.Mul, a, a), a);
			Expr("b + #(Foo.x:::a, a * a + a)",           F.Call(S.Add, b, F.List(stmt1, stmt2)));
			Expr("b + #{\n\tFoo.x:::a;\n\ta * a + a;\n}", F.Call(S.Add, b, StmtStyle(F.List(stmt1, stmt2))));
			Expr("b + {\n\tFoo.x:::a;\n\ta * a + a;\n}",  F.Call(S.Add, b, F.Braces(stmt1, stmt2)));
			Expr("b + #{}(Foo.x:::a, a * a + a)",         F.Call(S.Add, b, AsStyle(F.Braces(stmt1, stmt2), NodeStyle.PrefixNotation)));
		}

		[Test]
		public void PrecedenceChallenges()
		{
			Expr(@"#.(a, -b)",      F.Dot(a, F.Call(S._Negate, b)));     // a.-b.c would be ideal, but this will do
			Expr(@"#.(a, -b, c)",   F.Dot(a, F.Call(S._Negate, b), c));  // a.-b.c would be ideal, but this will do
			Expr(@"#.(a, -b.c)",    F.Dot(a, F.Call(S._Negate, F.Dot(b, c))));
			Expr(@"a.(b)(c)",       F.Call(F.Dot(a, F.InParens(b)), c));
			// The printer should revert to prefix notation in certain cases in 
			// order to faithfully represent the original tree.
			Expr(@"a * b + c",      F.Call(S.Add, F.Call(S.Mul, a, b), c));
			Expr(@"(a + b) * c",    F.Call(S.Mul, F.InParens(F.Call(S.Add, a, b)), c));
			Expr(@"#+(a, b) * c",   F.Call(S.Mul, F.Call(S.Add, a, b), c));
			Expr(@"--a++",          F.Call(S.PreDec, F.Call(S.PostInc, a)));
			Expr(@"(--a)++",        F.Call(S.PostInc, F.InParens(F.Call(S.PreDec, a))));
			Expr(@"#--(a)++",       F.Call(S.PostInc, F.Call(S.PreDec, a)));
			GreenNode a_b = F.Dot(a, b), a_b__c = F.Call(S.NullDot, F.Dot(a, b), c);
			Expr(@"a.b??.c.x",      F.Call(S.NullDot, a_b, F.Dot(c, x)));
			Expr(@"(a.b??.c).x",    F.Dot(F.InParens(a_b__c), x));
			Expr(@"#??.(a.b, c).x", F.Dot(a_b__c, x));
			Expr(@"++\x",           F.Call(S.PreInc, F.Call(S.Substitute, x)));
			Expr(@"++\([Foo] x)",   F.Call(S.PreInc, F.Call(S.Substitute, Attr(Foo, x))));
			Expr(@"a ? b : c",      F.Call(S.QuestionMark, a, b, c));
			Expr(@"a ? b + x : c + x",  F.Call(S.QuestionMark, a, F.Call(S.Add, b, x), F.Call(S.Add, c, x)));
			Expr(@"a ? b = x : (c = x)",F.Call(S.QuestionMark, a, F.Call(S.Set, b, x), F.InParens(F.Call(S.Set, c, x))));
			// A prefix operator can appear on the right-hand side of any infix/
			// prefix operator regardless of the precedence of the two operators.
			Expr(@"++\x",           F.Call(S.PreInc, F.Call(S.Substitute, x))); // easy
			Expr(@"++--x",          F.Call(S.PreInc, F.Call(S.PreDec, x)));     // easy
			Expr(@"\++x",           F.Call(S.Substitute, F.Call(S.PreInc, x)));
			Expr(@"#.(~x)",         F.Call(S.Dot, F.Call(S.NotBits, x))); // a.~x would be ideal, but this will do
			// Note: an analagous rule does NOT exist for suffix operators because 
			// (1) x++ and x-- do not need this rule to work in expressions 
			//     like "x++--.Foo" because ++, --, and . have the same precedence
			//     and can be used together already with no special rule.
			// (2) The other suffix operator, the `backtick`, does not use this rule
			//     because input like "a `foo`.x" would be ambiguous: it could be 
			//     parsed as "(a `foo`).x" or as "a `foo` (.x)"
			Expr(@"x++.Foo",        F.Dot(F.Call(S.PostInc, x), Foo));
			Expr(@"x++.Foo()",      F.Call(F.Dot(F.Call(S.PostInc, x), Foo)));
			Expr(@"x++--.Foo",      F.Dot(F.Call(S.PostDec, F.Call(S.PostInc, x)), Foo));
			// Due to its high precedence, the argument of a the \ operator must 
			// be in parens unless it is trivial.
			Expr(@"\x",             F.Call(S.Substitute, x));
			Expr(@"\(x++)",         F.Call(S.Substitute, F.Call(S.PostInc, x)));
			Expr(@"\(Foo(x))",      F.Call(S.Substitute, F.Call(Foo, x)));
			Expr(@"\(a.b)",         F.Call(S.Substitute, F.Dot(a, b)));
			Expr(@"\(a.b<c>)",      F.Call(S.Substitute, F.Of(F.Dot(a, b), c)));
			Expr(@"\((Foo) x)",     F.Call(S.Substitute, F.Call(S.Cast, x, Foo)));
			Expr(@"\(x(->Foo))",    F.Call(S.Substitute, Alternate(F.Call(S.Cast, x, Foo))));
		}

		[Test]
		public void SpecialEcsChallenges()
		{
			Expr("Foo x = a",            F.Var(Foo, x.Name, a));
			Expr("(Foo x = a) + 1",      F.Call(S.Add, F.InParens(F.Var(Foo, x.Name, a)), one));
			Expr("#var(Foo, x(a)) + 1",  F.Call(S.Add, F.Var(Foo, x.Name, a), one));
			Expr("#var(Foo, a) = x",     F.Call(S.Set, F.Var(Foo, a), x));
			Expr("#var(Foo, a) + x",     F.Call(S.Add, F.Var(Foo, a), x));
			Expr("x + #var(Foo, a)",     F.Call(S.Add, x, F.Var(Foo, a)));
			Expr("#label(Foo)",          F.Call(S.Label, Foo));
			Stmt("Foo:",                 F.Call(S.Label, Foo));
			GreenNode Foo_a = F.Call(S.NamedArg, Foo, a);
			Expr("Foo: a",               Foo_a);
			Stmt("#namedArg(Foo, a);",   Foo_a);
			Expr("#namedArg(Foo(x), a)", F.Call(S.NamedArg, F.Call(Foo, x), a));
			Expr("b + (Foo: a)",         F.Call(S.Add, b, F.InParens(Foo_a)));
			Expr("b + #namedArg(Foo, a)",F.Call(S.Add, b, Foo_a));
			// Ambiguity between multiplication and pointer declarations:
			// - multiplication at stmt level => prefix notation, except in #result or when lhs is not a complex identifier
			// - pointer declaration inside expr => generic, not pointer, notation
			Expr("a * b",                F.Call(S.Mul, a, b));
			Stmt("#*(a, b);",            F.Call(S.Mul, a, b));
			Stmt("a() * b;",             F.Call(S.Mul, F.Call(a), b));
			Expr("#result(a * b)",       F.Call(S.Result, F.Call(S.Mul, a, b)));
			Stmt("{\n\ta * b\n}",        F.Braces(F.Call(S.Result, F.Call(S.Mul, a, b))));
			Stmt("Foo* a = x;",          F.Var(F.Of(_(S._Pointer), Foo), F.Call(a, x)));
			Expr("#*<Foo> a = x",        F.Var(F.Of(_(S._Pointer), Foo), F.Call(a, x)));
			// Ambiguity between bitwise not and destructor declarations
			Expr("~Foo()",               F.Call(S.NotBits, F.Call(Foo)));
			Stmt("#~(Foo());",           F.Call(S.NotBits, F.Call(Foo)));
			Stmt("~Foo;",                F.Call(S.NotBits, Foo));
		}

		[Test]
		public void TypeContext()
		{
			// Certain syntax trees can print differently in a "type context" than elsewhere.
			var FooBracks = F.Call(S.Bracks, Foo);
			var FooArray = F.Of(_(S.Bracks), Foo);
			var FooNullable = F.Of(_(S.QuestionMark), Foo);
			var FooPointer = F.Of(_(S._Pointer), Foo);
			Expr("Foo[]",            FooBracks);
			Expr("#[]<Foo>",         FooArray);
			Expr("#?<Foo>",          FooNullable);
			Expr("#*<Foo>",          FooPointer);
			Stmt("#var(Foo[], a);",  F.Var(FooBracks, a));
			Stmt("Foo[] a;",         F.Var(FooArray, a));
			Stmt("typeof(Foo?);",    F.Call(S.Typeof, FooNullable));
			Stmt("default(Foo*);",   F.Call(S.Default, FooPointer));
			Stmt("(Foo[]) a;",       F.Call(S.Cast, a, FooArray));
			Stmt("a(->Foo?);",       Alternate(F.Call(S.Cast, a, FooNullable)));
			Stmt("a(as Foo*);",      Alternate(F.Call(S.As, a, FooPointer)));
			Stmt("Foo<#(Foo[])>;",   F.Of(Foo, F.List(FooBracks)));
			Stmt("Foo<#(#*<Foo>)>;", F.Of(Foo, F.List(FooPointer)));
			Expr("checked(Foo[])",   F.Call(S.Checked, FooBracks));
			Stmt("Foo<a*> x;",       F.Var(F.Of(Foo, F.Of(_(S._Pointer), a)), x));
		}

		[Test]
		public void OptionsTest()
		{
			// MixImmiscibleOperators is tested elsewhere
			Action<EcsNodePrinter> parens = p => p.AllowExtraParenthesis = true;
			Action<EcsNodePrinter> oldCasts = p => p.PreferOldStyleCasts = true;
			//Action<EcsNodePrinter> allowPtrs = p => p.AllowPointers = true;
			Action<EcsNodePrinter> dropAttrs = p => p.DropNonDeclarationAttributes = true;
			Stmt(@"(Foo) b;",          Alternate(F.Call(S.Cast, b, Foo)), oldCasts);
			Stmt(@"b(->Foo)(x);",      F.Call(F.Call(S.Cast, b, Foo), x));
			Stmt(@"#cast(b, Foo)(x);", F.Call(F.Call(S.Cast, b, Foo), x), oldCasts);
			Stmt(@"((Foo) b)(x);",     F.Call(F.Call(S.Cast, b, Foo), x), p => p.SetPlainCSharpMode());
			Option(@"#+(a, b) / c;", @"(a + b) / c;", F.Call(S.Div, F.Call(S.Add, a, b), c), parens);
			Option(@"#-(a)++;",      @"(-a)++;",      F.Call(S.PostInc, F.Call(S._Negate, a)), parens);
			Option(@"b(x)(->Foo);",  @"(Foo) b(x);",  Alternate(F.Call(S.Cast, F.Call(b, x), Foo)), oldCasts);
			
			// Put attributes in various locations and watch them all disappear
			Option(@"[Foo] a + b;",        @"a + b;",     Attr(Foo, F.Call(S.Add, a, b)), dropAttrs);
			Option(@"public a(x);",        @"a(x);",      Attr(@public, F.Call(a, x)), dropAttrs);
			Option(@"a([#foo] x);",        @"a(x);",      F.Call(a, Attr(fooKW, x)), dropAttrs);
			Option(@"##([Foo] a)(x);",     @"a(x);",      F.Call(Attr(Foo, a), x), dropAttrs);
			Option(@"x[[Foo] a];",         @"x[a];",      F.Call(S.Bracks, x, Attr(Foo, a)), dropAttrs);
			Option(@"#[](static x, a);",   @"x[a];",      F.Call(S.Bracks, Attr(@static, x), a), dropAttrs);
			Option(@"#+([Foo] a, 1);",     @"a + 1;",     F.Call(S.Add, Attr(Foo, a), one), dropAttrs);
			Option(@"#+(a, [Foo] 1);",     @"a + 1;",     F.Call(S.Add, a, Attr(Foo, one)), dropAttrs);
			Option(@"#?(a, [#foo] b, c);", @"a ? b : c;", F.Call(S.QuestionMark, a, Attr(fooKW, b), c), dropAttrs);
			Option(@"#?(a, b, public c);", @"a ? b : c;", F.Call(S.QuestionMark, a, b, Attr(@public, c)), dropAttrs);
			Option(@"#++([Foo] x);",       @"++x;",       F.Call(S.PreInc, Attr(Foo, x)), dropAttrs);
			Option(@"#postInc([Foo] x);",  @"x++;",       F.Call(S.PostInc, Attr(Foo, x)), dropAttrs);
			Option(@"x(->static Foo);",    @"(Foo) x;",   F.Call(S.Cast, x, Attr(@static, Foo)), dropAttrs);
			Option(@"#var(static Foo, x);", @"Foo x;",    F.Var(Attr(@static, Foo), x), dropAttrs);
			Option(@"#var(Foo, static x);", @"Foo x;",    F.Var(Foo, Attr(@static, x)), dropAttrs);
			Option(@"#var(Foo<a>, [#foo] b, c(1));", @"Foo<a> b, c = 1;", F.Var(F.Of(Foo, a), Attr(fooKW, b), F.Call(c, one)), dropAttrs);
			Option(@"#var(#of(Foo, static a), b);", @"Foo<a> b;",         F.Var(F.Of(Foo, Attr(@static, a)), b), dropAttrs);
			Option(@"#var(#of(static Foo, a), b);", @"Foo<a> b;",         F.Var(F.Of(Attr(@static, Foo), a), b), dropAttrs);
		}

		[Test]
		public void ExprSpacingTests()
		{
			// TODO
		}

		public void CastComplications()
		{
			Expr(@"(a using Foo)(x)",F.Call(F.InParens(F.Call(S.UsingCast, a, Foo)), x));
			Expr(@"a(using Foo)(x)", F.Call(F.Call(S.UsingCast, a, Foo), x));
			Expr(@"(a) b(x)",        F.Call(S.Cast, F.Call(b, x), a));
			Expr(@"b(->a)(x)",       F.Call(F.Call(S.Cast, b, a), x));
			Expr(@"a(as Foo).b",     F.Dot(F.Call(S.As, a, Foo), b));
			Expr(@"\(a as Foo)",     F.Call(S.Substitute, F.Call(S.UsingCast, a, Foo)));
			Expr(@"\(a(as Foo))",    F.Call(S.Substitute, Alternate(F.Call(S.UsingCast, a, Foo))));
		}

		[Test]
		public void SpecialCSharpChallenges()
		{
			var neg_a = F.Call(S._Negate, a);
			Expr("(Foo) - a",         F.Call(S.Sub, F.InParens(Foo), a));
			Expr("(Foo) (-a)",        F.Call(S.Cast, F.InParens(neg_a), Foo));
			Expr("(Foo) #-(a)",       F.Call(S.Cast, neg_a, Foo));
			Expr("(Foo) #+(a)",       F.Call(S.Cast, F.Call(S._UnaryPlus, a), Foo));
			var Foo_a = F.Of(Foo, a); 
			Expr("(Foo<a>) (-a)",     F.Call(S.Cast, F.InParens(neg_a), Foo_a));
			Expr("([ ] Foo)(-a)",     F.Call(F.InParens(Foo), neg_a));   // [] certifies "this is not a cast!";
			Expr("([ ] Foo<a>)(-a)",  F.Call(F.InParens(Foo_a), neg_a)); // extra parenthesis would also work
			Expr("((Foo<a>))(-a)",    F.Call(F.InParens(Foo_a), neg_a), p => p.AllowExtraParenthesis = true);
			//TODO: traditional cast style should only be printed if target could be a data type
			Expr("(a.b<c>) x",        F.Call(S.Cast, x, F.Of(F.Dot(a, b), c)));
			Expr("(a.b<#(c > 1)>) x", F.Call(S.Cast, x, F.Of(F.Dot(a, b), F.Call(S.List, F.Call(S.GT, c, one)))));
			Expr("x(->#of(a.b, c + 1))", F.Call(S.Cast, x, F.Of(F.Dot(a, b), F.Call(S.Add, c, one))));
		}

		[Test]
		public void AttrInHead()
		{
			// I needed a new syntax to support this case specifically... oops! hadn't planned for this.
			Stmt("[a] ##([b] c)(x);",   Attr(a, F.Call(Attr(b, c), x)));
			Stmt("[a] ##([b] c())(x);", Attr(a, F.Call(Attr(b, F.Call(c)), x)));
		}

		[Test]
		public void Backtick()
		{
			GreenNode foo_a = F.Call(fooKW, a), foo_a_b = F.Call(fooKW, a, b);
			Expr("a `Foo` b", Operator(F.Call(Foo, a, b)));
			Expr("#foo(a, b)", foo_a_b);
			Expr("a `#foo` b", Operator(foo_a_b));
			Expr("#foo(a)", foo_a);
			Stmt("a = b `Foo` c;", F.Call(S.Set, a, Operator(F.Call(Foo, b, c))));
			Stmt("a = b `Foo` c**x;", F.Call(S.Set, a, Operator(F.Call(Foo, b, F.Call(S.Exp, c, x)))));
		}

		[Test]
		public void Immiscibility()
		{
			Action<EcsNodePrinter> parens = p => p.AllowExtraParenthesis = true;
			Action<EcsNodePrinter> mixImm = p => p.MixImmiscibleOperators = true;
			// Of course, operators can be mixed with themselves.
			Stmt("a + b + c;", F.Call(S.Add, F.Call(S.Add, a, b), c), parens);
			Stmt("a = b = c;", F.Call(S.Set, a, F.Call(S.Set, b, c)), parens);
			// But some cannot be mixed with each other, unless requested (with mixImm).
			Option("#<<(a, b) + 1;",    "(a << b) + 1;",    F.Call(S.Add, F.Call(S.Shl, a, b), one), parens);
			Option("#+(a, b) << 1;",    "(a + b) << 1;",    F.Call(S.Shl, F.Call(S.Add, a, b), one), parens);
			Option("#+(a, b) << 1;",    "a + b << 1;",      F.Call(S.Shl, F.Call(S.Add, a, b), one), mixImm);
			// "#&(a, b) == 1;" would also be acceptable output on the left:
			Option("a `#&` b == 1;",    "(a & b) == 1;",    F.Call(S.Eq, F.Call(S.AndBits, a, b), one), parens);
			Option("#==(a, b) & 1;",    "(a == b) & 1;",    F.Call(S.AndBits, F.Call(S.Eq, a, b), one), parens);
			Option("#==(a, b) & 1;",    "a == b & 1;",      F.Call(S.AndBits, F.Call(S.Eq, a, b), one), mixImm);
			Option("Foo(a, b) + 1;",    "(a `Foo` b) + 1;", F.Call(S.Add, Operator(F.Call(Foo, a, b)), one), parens);
			// #+(a, b) `foo` 1; would also be acceptable output on the left:
			Option("a `#+` b `Foo` 1;", "(a + b) `Foo` 1;", Operator(F.Call(Foo, F.Call(S.Add, a, b), one)), parens);
		}

		[Test]
		public void CallStyleOperatorsAndNew()
		{
			Expr("checked(a + b)",       F.Call(S.Checked, F.Call(S.Add, a, b)));
			Expr("unchecked(a << b)",    F.Call(S.Unchecked, F.Call(S.Shl, a, b)));
			Expr("default(Foo)",         F.Call(S.Default, Foo));
			Expr("default(int)",         F.Call(S.Default, F.Int32));
			Expr("typeof(Foo)",          F.Call(S.Typeof, Foo));
			Expr("typeof(int)",          F.Call(S.Typeof, F.Int32));
			Expr("typeof(Foo<int>)",     F.Call(S.Typeof, F.Call(S.Of, Foo, F.Int32)));
			Expr("new Foo(x)",           F.Call(S.New, F.Call(Foo, x)));
			Expr("new Foo(x) { a }",     F.Call(S.New, F.Call(Foo, x), a));
			Expr("new Foo(x) { [a] b = c }", 
			                             F.Call(S.New, F.Call(Foo, x), Attr(a, F.Call(S.Set, b, c))));
			Option("#new([#foo] Foo(x), a);", "new Foo(x) { a };", 
			                             F.Call(S.New, Attr(fooKW, F.Call(Foo, x)), a), p => p.DropNonDeclarationAttributes = true);
			Expr("new Foo()",            F.Call(S.New, F.Call(Foo)));
			Expr("new Foo() { a }",      F.Call(S.New, F.Call(Foo), a));
			Expr("new Foo { a }",        F.Call(S.New, Foo, a));
			Expr("#new(Foo)",            F.Call(S.New, Foo));
			Expr("new #+(a, b)",         F.Call(S.New, F.Call(S.Add, a, b))); // #new(#+(a, b)) would also be ok
			Expr("new int[] { a, b }",   F.Call(S.New, F.Of(S.Bracks, S.Int32), a, b));
			Expr("new [] { a, b }",      F.Call(S.New, _(S.Bracks), a, b));
			Expr("new [] { }",           F.Call(S.New, _(S.Bracks)));
			Expr("#new(Foo()(), a)",     F.Call(S.New, F.Call(F.Call(Foo)), a));
		}

		[Test]
		public void PreprocessorConflicts()
		{
			Stmt("@#error(\"FAIL!\");", F.Call(S.Error, F.Literal("FAIL!")));
			Stmt("@#if(c, Foo());",     ExprStyle(F.Call(S.If, c, F.Call(Foo))));
			Stmt("@#region(57);",       ExprStyle(F.Call(GSymbol.Get("#region"), F.Literal(57))));
		}

		// TODO : new expressions: new object() { ... }, new int[] { ... }, new [] { ... }

		//protected static string Lines(params string[] lines)
		//{
		//    return string.Join("\n", lines);
		//}

		//public class A { 
		//    public A() {  } 
		//    public readonly B b;
		//}
		//public class B { public int x; }
		//void Weird() { var gds = new A { b = { x = 1 } }; } // NullReferenceException.

		[Test]
		public void SpaceStmts()
		{
			// Spaces: S.Struct, S.Class, S.Trait, S.Enum, S.Alias, S.Interface, S.Namespace
			var public_x = Attr(@public, F.Var(F.Int32, x));
			Stmt("struct Foo;",        F.Call(S.Struct, Foo, F._Missing));
			Stmt("struct Foo : IFoo;", F.Call(S.Struct, Foo, F.List(IFoo)));
			Stmt("struct Foo\n{\n}",   F.Call(S.Struct, Foo, F._Missing, F.Braces()));
			Stmt("struct Foo\n{\n" +
				"\tpublic int x;\n}",  F.Call(S.Struct, Foo, F._Missing, F.Braces(public_x)));
			Stmt("class Foo : IFoo\n{\n}", F.Call(S.Class, Foo, F.List(IFoo), F.Braces()));
			var a_where = Attr(F.Call(S.Where, @class), a);
			var b_where = Attr(F.Call(S.Where, a), b);
			var stmt = F.Call(S.Class, F.Of(Foo, a_where, b_where), F.List(IFoo), F.Braces());
			Stmt("class Foo<a,b> : IFoo where a: class where b: a\n{\n}", stmt);
			stmt = F.Call(S.Class, F.Of(Foo, Attr(_(S.Out), a_where)), F._Missing, F.Braces());
			Stmt("class Foo<out a> where a: class\n{\n}", stmt);
			stmt = F.Call(S.Class, F.Of(Foo, Attr(_(S.In), T)), F._Missing, F.Braces());
			Stmt("class Foo<in T>\n{\n}", stmt);
			stmt = Attr(F.Call(S.If, F.Call(S.Eq, F.Call(S.Add, a, b), c)),
			            F.Call(S.Trait, Foo, F.List(IFoo), F.Braces(public_x)));
			Stmt("trait Foo : IFoo if a + b == c\n{\n"+
				 "\tpublic int x;\n}", stmt);

			stmt = Attr(F.Call(S.If, F.Call(S.IsLegal, F.Call(S.Add, F.Call(S.Default, T), one))),
			            F.Call(S.Struct, F.Of(Foo, F.Call(S.Substitute, T)), F._Missing));
			Stmt(@"struct Foo<\T> if default(T) + 1 is legal;", stmt);
			Expr(@"[@#if(default(T) + 1 is legal)] #struct(Foo<\T>, #missing)", stmt);

			stmt = F.Call(S.Enum, Foo, F.List(F.UInt8), F.Braces(F.Call(S.Set, a, one), b, c, F.Call(S.Set, x, F.Literal(24))));
			Stmt("enum Foo : byte\n{\n\ta = 1, b, c, x = 24\n}", stmt);
			Expr("#enum(Foo, #(#byte), {\n\ta = 1;\n\tb;\n\tc;\n\tx = 24;\n})", stmt);

			stmt = F.Call(S.Interface, F.Of(Foo, Attr(@out, T)), F.List(F.Of(_("IEnumerable"), T)), F.Braces(public_x));
			Stmt("interface Foo<out T> : IEnumerable<T>\n{\n\tpublic int x;\n}", stmt);
			Expr("#interface(#of(Foo, [#out] T), #(IEnumerable<T>), {\n\tpublic int x;\n})", stmt);

			stmt = F.Call(S.Namespace, F.Of(Foo, T), F._Missing, F.Braces(public_x));
			Stmt("namespace Foo<T>\n{\n\tpublic int x;\n}", stmt);
			Expr("#namespace(Foo<T>, #missing, {\n\tpublic int x;\n})", stmt);

			stmt = F.Call(S.Alias, F.Call(S.Set, F.Of(_("Map"), a, b), F.Of(_("Dictionary"), a, b)), F._Missing);
			Stmt("alias Map<a,b> = Dictionary<a,b>;", stmt);
			Expr("#alias(Map<a,b> = Dictionary<a,b>, #missing)", stmt);
			stmt = F.Call(S.Alias, F.Call(S.Set, Foo, fooKW), F.List(IFoo), F.Braces(public_x));
			Stmt("alias Foo = #foo : IFoo\n{\n\tpublic int x;\n}", stmt);
			Expr("#alias(Foo = #foo, #(IFoo), {\n\tpublic int x;\n})", stmt);
			
			// An alias must have an #= node as its first argument; other spaces
			// must have type names as their first argument.
			stmt = F.Call(S.Alias, Foo, F.List(IFoo), F.Braces(public_x));
			Stmt("#alias(Foo, #(IFoo), {\n\tpublic int x;\n});", stmt);
			stmt = F.Call(S.Class, F.Call(S.Set, F.Of(_("L"), T), F.Of(_("List"), T)), F._Missing);
			Stmt("#class(L<T> = List<T>, #missing);", stmt);
		}

		[Test]
		public void MethodDefinitionStmts()
		{
			// #def and #delegate
			GreenNode int_x = F.Var(F.Int32, x), list_int_x = F.List(int_x), x_mul_x = F.Call(S.Mul, x, x);
			GreenNode stmt;
			stmt = F.Call(S.Delegate, F.Void, F.Of(Foo, Attr(F.Call(S.Where, _(S.Class), x), T)), F.List(F.Var(T, x)));
			Stmt("delegate void Foo<T>(T x) where T: class, x", stmt);
			Expr("#delegate(#void, #of(Foo, [#where(#class, x)] T), #(T x))", stmt);
			stmt = Attr(@public, @new, @partial, F.Def(F.String, Foo, list_int_x));
			Stmt("public new partial string Foo(int x);", stmt);
			Expr("[#public, #partial, #new] #def(#string, Foo, #(int x))", stmt);
			stmt = F.Def(F.Int32, Foo, list_int_x, F.Braces(F.Call(S.Result, x_mul_x)));
			Stmt("int Foo(int x) {\n\tx * x\n}", stmt);
			Expr("#def(#int, Foo, #(int x), {\n\tx * x\n})", stmt);
			stmt = F.Def(F.Int32, Foo, list_int_x, F.Braces(F.Call(S.Return, x_mul_x)));
			Stmt("int Foo(int x) {\n\treturn x * x;\n}", stmt);
			Expr("#def(#int, Foo, #(int x), {\n\treturn x * x;\n})", stmt);
			stmt = F.Def(_("def"), Foo, list_int_x, F.Call(S.Forward, F.Dot(a, b)));
			Stmt("def Foo(int x) ==> a.b;", stmt);
			Expr("#def(def, Foo, #(int x), ==> a.b)", stmt);
			stmt = F.Def(_("IEnumerator"), F.Dot(_("IEnumerable"), _("GetEnumerator")), F.List(), F.Braces());
			Stmt("IEnumerator IEnumerable.GetEnumerator() { }", stmt);
			Expr("#def(IEnumerator, IEnumerable.GetEnumerator, #(), {\n})", stmt);
			stmt = F.Def(F._Missing, _(S.New), list_int_x, F.Braces(F.Call(_(S.This), x, one), F.Call(S.Set, a, x)));
			Stmt("new (int x) : this(x, 1) {\n\ta = x;\n}", stmt);
			Expr("#def(#missing, #new, #(int x), {\n\t#this(x, 1);\n\ta = x;\n})", stmt);
			stmt = F.Def(F._Missing, Foo, list_int_x, F.Braces(F.Call(_(S.Base), x), F.Call(S.Set, b, x)));
			Stmt("Foo (int x) : base(x) {\n\tb = x;\n}", stmt);
			Expr("#def(#missing, Foo,  #(int x), {\n\t#base(x);\n\tb = x;\n})", stmt);
			stmt = F.Def(F._Missing, F.Call(S._Destruct, Foo), F.List(), F.Braces());
			Stmt("~Foo() {\n}", stmt);
			Expr("#def(#missing, #~(Foo), #(), {\n})", stmt);
			GreenNode @operator = _(S.Operator), cast = _(S.Cast), operator_cast = Attr(@operator, cast);
			GreenNode Foo_a = F.Var(Foo, a), Foo_b = F.Var(Foo, b); 
			stmt = Attr(@static, F.Def(F.Bool, Attr(@operator, _(S.Eq)), F.List(F.Var(T, a), F.Var(T, b)), F.Braces()));
			Stmt("static bool operator==(T a, T b) {\n}", stmt);
			Expr("[#static] #def(#bool, [#operator] #==, #(T a, T b), {\n})", stmt);
			stmt = Attr(@static, _(S.Implicit), F.Def(T, operator_cast, F.List(Foo_a), F.Braces()));
			Stmt("static implicit operator T(Foo a) {\n}", stmt);
			Expr("[#static, #implicit] #def(T, [#operator] #cast, #(Foo a), {\n})", stmt);
			stmt = Attr(@static, _(S.Explicit), 
			            F.Def(F.Of(Foo, T), Attr(@operator, F.Of(cast, F.Call(S.Substitute, T))), 
			                  F.List(F.Var(F.Of(_("Bar"), T), b))));
			Stmt(@"static explicit operator Foo<T><\T>(Bar<T> b);", stmt);
			Expr(@"[#static, #explicit] #def(Foo<T>, [#operator] #of<#cast, \T>, #(Bar<T> b))", stmt);
			stmt = F.Def(F.Bool, Attr(@operator, _("when")), F.List(Foo_a, Foo_b), F.Braces());
			Stmt("bool operator `when`(Foo a, Foo b) {\n}", stmt);
			Expr("#def(#bool, [#operator] when, #(Foo a, Foo b), {\n})", stmt);
		}

		[Test]
		public void EventStmts()
		{
			GreenNode EventHandler = _("EventHandler"), add = _("add"), remove = _("remove");
			var stmt = F.Call(S.Event, F.Of(EventHandler, T), _("Click"));
			Stmt("event EventHandler<T> Click;", stmt);
			Expr("#event(EventHandler<T>, Click)", stmt);
			stmt = F.Call(S.Event, EventHandler, a, b);
			Stmt("event EventHandler a, b;", stmt);
			Expr("#event(EventHandler, a, b)", stmt);
			stmt = F.Call(S.Event, EventHandler, a, F.Braces(F.Call(add, F.Braces()), F.Call(remove, F.Braces())));
			Stmt("event EventHandler a {\n\tadd {\n\t} remove {\n\t}\n}", stmt);
			Expr("#event(EventHandler, a, {\n\tadd({\n});\n\tremove({\n});\n}))", stmt);
			stmt = F.Call(S.Event, F.Call(S.Add, a, b), _("Click"));
			Stmt("#event(a + b, Click);", stmt);
			stmt = F.Call(S.Event, EventHandler, F.Call(S.Add, a, b));
			Expr("#event(EventHandler, a + b)", stmt);
			stmt = F.Call(S.Event, EventHandler, F.Call(S.Add, a, b), F.Braces());
			Expr("#event(EventHandler, a + b, {\n})", stmt);
		}

		[Test]
		public void PropertyStmts()
		{
			GreenNode get = _("get"), set = _("set"), value = _("value");
			GreenNode stmt = F.Property(F.Int32, Foo, F.Braces(get, set));
			Stmt("int Foo {\n\tget;\n\tset;\n}", stmt);
			Expr("#property(int, Foo, {\n\tget;\n\tset;\n}", stmt);
			stmt = Attr(@public, F.Property(F.Int32, Foo, F.Braces(
			                       Attr(style_macroCall, F.Call(get, F.Braces(F.Call(S.Return, x)))),
			                       Attr(style_macroCall, F.Call(set, F.Braces(F.Call(S.Set, x, value)))))));
			Stmt("int Foo {\n\t"
			      +"get {\n\t\treturn x;\n\t}\n\t"
			      +"set {\n\t\tx = value;\n\t}\n}", stmt);
		}

		[Test]
		public void SimpleStmts()
		{
			// S.Break, S.Continue, S.Goto, S.GotoCase, S.Return, S.Throw
			Stmt("break;",             _(S.Break));
			Stmt("break outer;",       F.Call(S.Break, _("outer")));
			Stmt("continue;",          _(S.Continue));
			Stmt("continue outer;",    F.Call(S.Continue, _("outer")));
			Stmt("goto end;",          F.Call(S.Goto, _("end")));
			Stmt("goto case 1;",       F.Call(S.GotoCase, one));
			Stmt("goto case [Foo] 1;", F.Call(S.GotoCase, Attr(Foo, one)));
			Stmt("return;",            _(S.Return));
			Stmt("return 1;",          F.Call(S.Return, one));
			Stmt("return void;",       F.Call(S.Return, F.Literal(@void.Value)));
			Stmt("throw;",             _(S.Throw));
			Stmt("throw new Foo();",   F.Call(S.Throw, F.Call(S.New, F.Call(Foo))));
		}

		[Test]
		public void BlockStmts()
		{
			// S.If, S.Checked, S.DoWhile, S.Fixed, S.For, S.ForEach, S.If, S.Lock, 
			// S.Switch, S.Try, S.Unchecked, S.UsingStmt, S.While
			Stmt("if (Foo) a();",                     F.Call(S.If, Foo, F.Call(a)));
			Stmt("if (Foo) a(); else b();",           F.Call(S.If, Foo, F.Call(a), F.Call(b)));
			var ifStmt = F.Call(S.If, Foo, F.Call(S.Result, a), F.Call(S.Result, b));
			Stmt("{\n\tif (Foo) a else b\n}",         F.Braces(ifStmt));
			Stmt("{\n\tif (Foo) #result(a); else #result(b); c\n}", F.Braces(ifStmt, c));

			Stmt("checked {\n\tx = a();\n\tx * x\n}", F.Call(S.Checked, F.Braces(F.Call(S.Set, x, F.Call(a)), 
			                                                                 F.Call(S.Result, F.Call(S.Mul, x, x)))));
			Stmt("unchecked {\n\t0xBAAD * 0xF00D\n}", F.Call(S.Unchecked, F.Braces(F.Call(S.Result, 
			                                                   F.Call(S.Mul, Alternate(F.Literal(0xBAAD)), Alternate(F.Literal(0xF00D)))))));
			
			Stmt("do a(); while(c);",                 F.Call(S.DoWhile, F.Call(a), c));
			Stmt("do #{ a(); } while(c);",            F.Call(S.DoWhile, F.List(F.Call(a)), c));
			Stmt("do { a(); } while(c);",             F.Call(S.DoWhile, F.Braces(F.Call(a)), c));
			Stmt("do { a } while(c);",                F.Call(S.DoWhile, F.Braces(F.Call(S.Result, a)), c));
			
			var amp_b_c = F.Call(S._AddressOf, F.Call(S.PtrArrow, b, c));
			var int_a_amp_b_c = F.Var(F.Of(_(S._Pointer), F.Int32), F.Call(a, amp_b_c));
			Stmt("fixed (int* a = &b->c) Foo(a);",    F.Call(S.Fixed, int_a_amp_b_c, F.Call(Foo, a)));
			Stmt("fixed (int* a = &b->c) {\n\tFoo(a);\n}", F.Call(S.Fixed, int_a_amp_b_c, F.Braces(F.Call(Foo, a))));
			
			var forArgs = new GreenAtOffs[] {
				F.Var(F.Int32, F.Call(x, F.Literal(0))),
				F.Call(S.LT, x, F.Literal(10)),
				F.Call(S.PostInc, x),
				F.Braces()
			};
			Stmt("for (int x = 0; x < 10; x++) {\n}",   F.Call(S.For, forArgs));
			forArgs[3] = F.List(F.Call(a), F.Call(b));
			Stmt("for (int x = 0; x < 10; x++) #{ a(); b(); }", F.Call(S.For, forArgs));
			forArgs[3] = F.List(F.Call(S.Result, a));
			Stmt("for (int x = 0; x < 10; x++) #{ x }", F.Call(S.For, forArgs));
		}

		[Test]
		public void BlocksOfStmts()
		{
		}

		[Test]
		public void BraceInIfClause()
		{
			// A braced block is not allowed inside an "if" clause. However we 
			// don't have a good way to prevent it.
			var stmt = Attr(F.Call(S.If, F.Call(S.Eq, a, F.Braces(b))),
			                F.Call(S.Namespace, Foo, F._Missing));
			Stmt("namespace Foo if a == #{}(b);", stmt);
			stmt = Attr(F.Call(S.If, F.Call(S.Eq, a, StmtStyle(F.List(b)))),
			            F.Call(S.Class, Foo));
			Stmt("class Foo if a == #(b);", stmt);
		}
	}
}
