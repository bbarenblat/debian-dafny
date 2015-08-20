﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Boogie;
using System.Diagnostics.Contracts;
using System.Diagnostics;

namespace Microsoft.Dafny.Triggers {
  class TriggerTerm {
    internal Expression Expr { get; set; }
    internal Expression OriginalExpr { get; set; }
    internal ISet<IVariable> Variables { get; set; }

    public override string ToString() {
      return Printer.ExprToString(OriginalExpr);
    }

    internal static bool Eq(TriggerTerm t1, TriggerTerm t2) {
      return ExprExtensions.ExpressionEq(t1.Expr, t2.Expr);
    }
  }

  class TriggerCandidate {
    internal List<TriggerTerm> Terms { get; set; }
    internal string Annotation { get; set; }

    internal TriggerCandidate(List<TriggerTerm> terms) {
      this.Terms = terms;
    }

    public TriggerCandidate(TriggerCandidate candidate) {
      this.Terms = candidate.Terms;
    }

    internal bool MentionsAll(List<BoundVar> vars) {
      return vars.All(x => Terms.Any(term => term.Variables.Contains(x)));
    }

    private string Repr { get { return String.Join(", ", Terms); } }

    public override string ToString() {
      return "{" + Repr + "}" + (String.IsNullOrWhiteSpace(Annotation) ? "" : " (" + Annotation + ")");
    }

    internal IEnumerable<TriggerMatch> LoopingSubterms(QuantifierExpr quantifier) {
      Contract.Requires(quantifier.SplitQuantifier == null); // Don't call this on a quantifier with a Split clause: it's not a real quantifier
      var matchingSubterms = MatchingSubterms(quantifier);
      return matchingSubterms.Where(tm => tm.CouldCauseLoops(Terms));
    }

    internal List<TriggerMatch> MatchingSubterms(QuantifierExpr quantifier) {
      Contract.Requires(quantifier.SplitQuantifier == null); // Don't call this on a quantifier with a Split clause: it's not a real quantifier
      return Terms.SelectMany(term => quantifier.SubexpressionsMatchingTrigger(term.Expr)).Deduplicate(TriggerMatch.Eq);
    }

    public String AsDafnyAttributeString() {
      return "{:trigger " + Repr + "}";
    }
  }

  class TriggerAnnotation {
    internal bool IsTriggerKiller;
    internal ISet<IVariable> Variables;
    internal readonly List<TriggerTerm> PrivateTerms;
    internal readonly List<TriggerTerm> ExportedTerms;

    internal TriggerAnnotation(bool IsTriggerKiller, IEnumerable<IVariable> Variables, IEnumerable<TriggerTerm> AllTerms, IEnumerable<TriggerTerm> PrivateTerms = null) {
      this.IsTriggerKiller = IsTriggerKiller;
      this.Variables = new HashSet<IVariable>(Variables);
      this.PrivateTerms = new List<TriggerTerm>(PrivateTerms == null ? Enumerable.Empty<TriggerTerm>() : PrivateTerms);
      this.ExportedTerms = new List<TriggerTerm>(AllTerms == null ? Enumerable.Empty<TriggerTerm>() : AllTerms.Except(this.PrivateTerms));
    }

    public override string ToString() {
      StringBuilder sb = new StringBuilder();
      string indent = "  {0}", nindent = "\n  - {0}", subindent = "\n    * {0}";

      sb.AppendFormat(indent, IsTriggerKiller);

      sb.AppendFormat(nindent, "Variables:");
      foreach (var bv in Variables) {
        sb.AppendFormat(subindent, bv.Name);
      }

      sb.AppendFormat(nindent, "Exported terms:");
      foreach (var term in ExportedTerms) {
        sb.AppendFormat(subindent, term);
      }

      if (PrivateTerms.Any()) {
        sb.AppendFormat(nindent, "Private terms:");
        foreach (var term in PrivateTerms) {
          sb.AppendFormat(subindent, term);
        }
      }

      return sb.ToString();
    }
  }

  public class TriggersCollector {
    Dictionary<Expression, TriggerAnnotation> cache;

    internal TriggersCollector() {
      this.cache = new Dictionary<Expression, TriggerAnnotation>();
    }

    private T ReduceAnnotatedSubExpressions<T>(Expression expr, T seed, Func<TriggerAnnotation, T> map, Func<T, T, T> reduce) {
      return expr.SubExpressions.Select(e => map(Annotate(e)))
                                .Aggregate(seed, (acc, e) => reduce(acc, e));
    }

    private List<TriggerTerm> CollectExportedCandidates(Expression expr) {
      return ReduceAnnotatedSubExpressions<List<TriggerTerm>>(expr, new List<TriggerTerm>(), a => a.ExportedTerms, TriggerUtils.MergeAlterFirst);
    }

    private ISet<IVariable> CollectVariables(Expression expr) {
      return ReduceAnnotatedSubExpressions(expr, new HashSet<IVariable>(), a => a.Variables, TriggerUtils.MergeAlterFirst);
    }

    private bool CollectIsKiller(Expression expr) {
      return ReduceAnnotatedSubExpressions(expr, false, a => a.IsTriggerKiller, (a, b) => a || b);
    }

    private IEnumerable<TriggerTerm> OnlyPrivateCandidates(List<TriggerTerm> terms, IEnumerable<IVariable> privateVars) {
      return terms.Where(c => privateVars.Intersect(c.Variables).Any()); //TODO Check perf
    }

    private TriggerAnnotation Annotate(Expression expr) {
      TriggerAnnotation cached;
      if (cache.TryGetValue(expr, out cached)) {
        return cached;
      }

      expr.SubExpressions.Iter(e => Annotate(e));

      TriggerAnnotation annotation; // TODO: Using ApplySuffix fixes the unresolved members problem in GenericSort
      if (expr is FunctionCallExpr || expr is SeqSelectExpr || expr is MemberSelectExpr || expr is OldExpr || expr is ApplyExpr ||
          (expr is UnaryOpExpr && (((UnaryOpExpr)expr).Op == UnaryOpExpr.Opcode.Cardinality)) || // FIXME || ((UnaryOpExpr)expr).Op == UnaryOpExpr.Opcode.Fresh doesn't work, as fresh is a pretty tricky predicate when it's not about datatypes. See translator.cs:10944
          (expr is BinaryExpr && (((BinaryExpr)expr).Op == BinaryExpr.Opcode.NotIn || ((BinaryExpr)expr).Op == BinaryExpr.Opcode.In))) {
        annotation = AnnotatePotentialCandidate(expr);
      } else if (expr is QuantifierExpr && ((QuantifierExpr)expr).SplitQuantifier == null) {
        annotation = AnnotateQuantifier((QuantifierExpr)expr);
      } else if (expr is LetExpr) {
        annotation = AnnotateLetExpr((LetExpr)expr);
      } else if (expr is IdentifierExpr) {
        annotation = AnnotateIdentifier((IdentifierExpr)expr);
      } else if (expr is ApplySuffix) {
        annotation = AnnotateApplySuffix((ApplySuffix)expr);
      } else if (expr is ConcreteSyntaxExpression ||
                 expr is LiteralExpr ||
                 expr is OldExpr ||
                 expr is ThisExpr ||
                 expr is BoxingCastExpr ||
                 expr is DatatypeValue ||
                 expr is SeqDisplayExpr) { //FIXME what abvout other display expressions?
        annotation = AnnotateOther(expr, false);
      } else {
        annotation = AnnotateOther(expr, true);
      }

      TriggerUtils.DebugTriggers("{0} ({1})\n{2}", Printer.ExprToString(expr), expr.GetType(), annotation);
      cache[expr] = annotation;
      return annotation;
    }

    private TriggerAnnotation AnnotatePotentialCandidate(Expression expr) {
      bool expr_is_killer = false;
      var new_expr = TriggerUtils.CleanupExprForInclusionInTrigger(expr, out expr_is_killer);
      var new_term = new TriggerTerm { Expr = new_expr, OriginalExpr = expr, Variables = CollectVariables(expr) };

      List<TriggerTerm> collected_terms = CollectExportedCandidates(expr);
      var children_contain_killers = CollectIsKiller(expr);

      if (!children_contain_killers) {
        // Add only if the children are not killers; the head has been cleaned up into non-killer form
        collected_terms.Add(new_term);
      }

      // This new node is a killer if its children were killers, or if it's non-cleaned-up head is a killer
      return new TriggerAnnotation(children_contain_killers || expr_is_killer, new_term.Variables, collected_terms);
    }

    private TriggerAnnotation AnnotateApplySuffix(ApplySuffix expr) {
      // This is a bit tricky. A funcall node is generally meaningful as a trigger candidate, 
      // but when it's part of an ApplySuffix the function call itself may not resolve properly
      // when the second round of resolving is done after modules are duplicated.
      // Thus first we annotate expr and create a trigger candidate, and then we remove the 
      // candidate matching its direct subexpression if needed. Note that function calls are not the 
      // only possible child here; there can be DatatypeValue nodes, for example (see vstte2012/Combinators.dfy).
      var annotation = AnnotatePotentialCandidate(expr);
      // Comparing by reference is fine here. Using sets could yield a small speedup
      annotation.ExportedTerms.RemoveAll(term => expr.SubExpressions.Contains(term.Expr));
      return annotation;
    }

    private TriggerAnnotation AnnotateQuantifierOrLetExpr(Expression expr, IEnumerable<BoundVar> boundVars) {
      var terms = CollectExportedCandidates(expr);
      return new TriggerAnnotation(true, CollectVariables(expr), terms, OnlyPrivateCandidates(terms, boundVars));
    }

    private TriggerAnnotation AnnotateQuantifier(QuantifierExpr expr) {
      return AnnotateQuantifierOrLetExpr(expr, expr.BoundVars);
    }

    private TriggerAnnotation AnnotateLetExpr(LetExpr expr) {
      return AnnotateQuantifierOrLetExpr(expr, expr.BoundVars);
    }

    private TriggerAnnotation AnnotateIdentifier(IdentifierExpr expr) {
      return new TriggerAnnotation(false, Enumerable.Repeat(expr.Var, 1), null);
    }

    private TriggerAnnotation AnnotateOther(Expression expr, bool isTriggerKiller) {
      return new TriggerAnnotation(isTriggerKiller || CollectIsKiller(expr), CollectVariables(expr), CollectExportedCandidates(expr));
    }

    // FIXME document that this will contain duplicates
    internal List<TriggerTerm> CollectTriggers(QuantifierExpr quantifier) {
      Contract.Requires(quantifier.SplitQuantifier == null); // Don't call this on a quantifier with a Split clause: it's not a real quantifier
      // TODO could check for existing triggers and return that instead, but that require a bit of work to extract the expressions
      return Annotate(quantifier).PrivateTerms;
    }

    internal bool IsTriggerKiller(Expression expr) {
      return Annotate(expr).IsTriggerKiller;
    }
  }
}
