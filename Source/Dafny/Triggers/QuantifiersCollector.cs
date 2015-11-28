﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Boogie;
using System.Collections.ObjectModel;
using System.Diagnostics.Contracts;

namespace Microsoft.Dafny.Triggers {
  internal class QuantifierCollector : TopDownVisitor<bool> {
    readonly ErrorReporter reporter;
    private readonly HashSet<Expression> quantifiers = new HashSet<Expression>();
    internal readonly HashSet<Expression> exprsInOldContext = new HashSet<Expression>();
    internal readonly List<QuantifiersCollection> quantifierCollections = new List<QuantifiersCollection>();

    public QuantifierCollector(ErrorReporter reporter) {
      Contract.Requires(reporter != null);
      this.reporter = reporter;
    }

    protected override bool VisitOneExpr(Expression expr, ref bool inOldContext) {
      var quantifier = expr as QuantifierExpr;

      if (quantifier != null && !quantifiers.Contains(quantifier)) {
        quantifiers.Add(quantifier);
        if (quantifier.SplitQuantifier != null) {
          var collection = quantifier.SplitQuantifier.Select(q => q as QuantifierExpr).Where(q => q != null);
          quantifierCollections.Add(new QuantifiersCollection(collection, reporter));
          quantifiers.UnionWith(quantifier.SplitQuantifier);
        } else {
          quantifierCollections.Add(new QuantifiersCollection(Enumerable.Repeat(quantifier, 1), reporter));
        }
      }

      if (expr is OldExpr) {
        inOldContext = true;
      } else if (inOldContext) { // FIXME be more restrctive on the type of stuff that we annotate
        exprsInOldContext.Add(expr);
      }

      return true;
    }

    protected override bool VisitOneStmt(Statement stmt, ref bool st) {
      Contract.Requires(stmt != null);
      if (stmt is ForallStmt) {
        ForallStmt s = (ForallStmt)stmt;
        if (s.ForallExpressions != null) {
          foreach (Expression expr in s.ForallExpressions) {
            VisitOneExpr(expr, ref st);
          }
        }
      }
      return true;
    }
  }
}
