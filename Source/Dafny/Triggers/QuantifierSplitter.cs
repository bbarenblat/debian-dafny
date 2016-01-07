﻿using Microsoft.Boogie;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;

namespace Microsoft.Dafny.Triggers {
  class QuantifierSplitter : BottomUpVisitor {
    /// This cache was introduced because some statements (notably calc) return the same SubExpression multiple times.
    /// This ended up causing an inconsistent situation when the calc statement's subexpressions contained the same quantifier 
    /// twice: on the first pass that quantifier got its SplitQuantifiers generated, and on the the second pass these 
    /// split quantifiers got re-split, creating a situation where the direct children of a split quantifier were 
    /// also split quantifiers.
    private Dictionary<QuantifierExpr, List<Expression>> splits = new Dictionary<QuantifierExpr, List<Expression>>();

    private static BinaryExpr.Opcode FlipOpcode(BinaryExpr.Opcode opCode) {
      if (opCode == BinaryExpr.Opcode.And) {
        return BinaryExpr.Opcode.Or;
      } else if (opCode == BinaryExpr.Opcode.Or) {
        return BinaryExpr.Opcode.And;
      } else {
        throw new ArgumentException();
      } 
    }

    // NOTE: If we wanted to split quantifiers as far as possible, it would be
    // enough to put the formulas in DNF (for foralls) or CNF (for exists).
    // Unfortunately, this would cause ill-behaved quantifiers to produce
    // exponentially many smaller quantifiers. Thus we only do one step of 
    // distributing, which takes care of the usual precondition case:
    //   forall x :: P(x) ==> (Q(x) && R(x))

    private static UnaryOpExpr Not(Expression expr) {
      return new UnaryOpExpr(expr.tok, UnaryOpExpr.Opcode.Not, expr) { Type = expr.Type };
    }

    private static Attributes CopyAttributes(Attributes source) {
      if (source == null) {
        return null;
      } else {
        return new Attributes(source.Name, source.Args, CopyAttributes(source.Prev));
      }
    }

    internal static IEnumerable<Expression> SplitExpr(Expression expr, BinaryExpr.Opcode separator) {
      expr = expr.Resolved;
      var unary = expr as UnaryOpExpr;
      var binary = expr as BinaryExpr;

      if (unary != null && unary.Op == UnaryOpExpr.Opcode.Not) {
        foreach (var e in SplitExpr(unary.E, FlipOpcode(separator))) { yield return Not(e); }
      } else if (binary != null && binary.Op == separator) {
        foreach (var e in SplitExpr(binary.E0, separator)) { yield return e; }
        foreach (var e in SplitExpr(binary.E1, separator)) { yield return e; }
      } else if (binary != null && binary.Op == BinaryExpr.Opcode.Imp && separator == BinaryExpr.Opcode.Or) {
        foreach (var e in SplitExpr(Not(binary.E0), separator)) { yield return e; }
        foreach (var e in SplitExpr(binary.E1, separator)) { yield return e; }
      } else {
        yield return expr;
      }
    }

    internal static IEnumerable<Expression> SplitAndStich(BinaryExpr pair, BinaryExpr.Opcode separator) {
      foreach (var e1 in SplitExpr(pair.E1, separator)) {
        // Notice the token. This makes triggers/splitting-picks-the-right-tokens.dfy possible
        yield return new BinaryExpr(e1.tok, pair.Op, pair.E0, e1) { ResolvedOp = pair.ResolvedOp, Type = pair.Type };
      }
    }

    internal static IEnumerable<Expression> SplitQuantifier(QuantifierExpr quantifier) {
      var body = quantifier.Term;
      var binary = body as BinaryExpr;

      if (quantifier is ForallExpr) {
        IEnumerable<Expression> stream;
        if (binary != null && (binary.Op == BinaryExpr.Opcode.Imp || binary.Op == BinaryExpr.Opcode.Or)) {
          stream = SplitAndStich(binary, BinaryExpr.Opcode.And);
        } else {
          stream = SplitExpr(body, BinaryExpr.Opcode.And);
        }
        foreach (var e in stream) {
          var tok = new NestedToken(quantifier.tok, e.tok);
          yield return new ForallExpr(tok, quantifier.TypeArgs, quantifier.BoundVars, quantifier.Range, e, CopyAttributes(quantifier.Attributes)) { Type = quantifier.Type };
        }
      } else if (quantifier is ExistsExpr) {
        IEnumerable<Expression> stream;
        if (binary != null && binary.Op == BinaryExpr.Opcode.And) {
          stream = SplitAndStich(binary, BinaryExpr.Opcode.Or);
        } else {
          stream = SplitExpr(body, BinaryExpr.Opcode.Or);
        }
        foreach (var e in stream) {
          var tok = new NestedToken(quantifier.tok, e.tok);
          yield return new ExistsExpr(tok, quantifier.TypeArgs, quantifier.BoundVars, quantifier.Range, e, CopyAttributes(quantifier.Attributes)) { Type = quantifier.Type };
        }
      } else {
        yield return quantifier;
      }
    }
    
    private static bool AllowsSplitting(QuantifierExpr quantifier) {
      bool splitAttr = true;
      return !Attributes.ContainsBool(quantifier.Attributes, "split", ref splitAttr) || splitAttr;
    }

    protected override void VisitOneExpr(Expression expr) {
      var quantifier = expr as QuantifierExpr;
      if (quantifier != null) {
        Contract.Assert(quantifier.SplitQuantifier == null);
        if (!splits.ContainsKey(quantifier) && AllowsSplitting(quantifier)) {
          splits[quantifier] = SplitQuantifier(quantifier).ToList();
        }
      }
    }

    protected override void VisitOneStmt(Statement stmt) {
      if (stmt is ForallStmt) {
        ForallStmt s = (ForallStmt)stmt;
        if (s.ForallExpressions != null) {
          foreach (Expression expr in s.ForallExpressions) {
            VisitOneExpr(expr);
          }
        }
      }
    }

    /// <summary>
    /// See comments above definition of splits for reason why this method exists
    /// </summary>
    internal void Commit() {
      foreach (var quantifier in splits.Keys) {
        quantifier.SplitQuantifier = splits[quantifier];
      }
    }
  }
}
