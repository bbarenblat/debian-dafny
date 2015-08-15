﻿using Microsoft.Boogie;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Dafny.Triggers {
  class QuantifierSplitter : BottomUpVisitor {
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
    // exponentially many smaller quantifiers.

    private static UnaryOpExpr Not(Expression expr) {
      var not = new UnaryOpExpr(expr.tok, UnaryOpExpr.Opcode.Not, expr) { Type = expr.Type };
      return not;
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
        yield return new BinaryExpr(pair.tok, pair.Op, pair.E0, e1) { ResolvedOp = pair.ResolvedOp, Type = pair.Type };
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
          yield return new ForallExpr(quantifier.tok, quantifier.BoundVars, quantifier.Range, e, quantifier.Attributes) { Type = quantifier.Type };
        }
      } else if (quantifier is ExistsExpr) {
        IEnumerable<Expression> stream;
        if (binary != null && binary.Op == BinaryExpr.Opcode.And) {
          stream = SplitAndStich(binary, BinaryExpr.Opcode.Or);
        } else {
          stream = SplitExpr(body, BinaryExpr.Opcode.Or);
        }
        foreach (var e in stream) {
          yield return new ExistsExpr(quantifier.tok, quantifier.BoundVars, quantifier.Range, e, quantifier.Attributes) { Type = quantifier.Type };
        }
      } else {
        yield return quantifier;
      }
    }
        
    protected override void VisitOneExpr(Expression expr) {
      var quantifier = expr as QuantifierExpr;
      if (quantifier != null) {
        var split = SplitQuantifier(quantifier).ToList();
        quantifier.SplitQuantifier = split;
        //Console.WriteLine("!!! {0} => {1}", Printer.ExprToString(expr), rew.MapConcat(Printer.ExprToString, " ||| "));
      }
    }
  }
}
